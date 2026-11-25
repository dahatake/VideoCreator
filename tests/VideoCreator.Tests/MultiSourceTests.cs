using System.Globalization;
using VideoCreator.Audio;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>
/// Multi-source sync (full-length search, timestamp-true PCM, drift) and the P10 screen + presenter layout, verified against ground truth built into
/// synthetic media: known offsets / drift, and beeps paired with flashes at known times that are measured again in the rendered file.
/// </summary>
public class MultiSourceTests : IAsyncLifetime
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-ms-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    static string F(double x) => x.ToString("0.###", IC);

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static SourceAsset Asset(string id, string path, double dur, int w, int h, AssetRole role = AssetRole.CamB, double offset = 0, double ppm = 0) => new()
    {
        Id = id, ProjectId = "p", Path = path, Sha256 = "x", Role = role, SyncOffset = offset, SyncDriftPpm = ppm, SyncMethod = "audio",
        Media = new MediaInfo { Duration = dur, Width = w, Height = h, Fps = 30, AudioChannels = 1 },
    };

    string P(string name) => Path.Combine(_dir, name);
    Task Run(params string[] args) => Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-y", .. args], Ct);

    /// <summary>Pink-noise "room" shared by every recording of the scene.</summary>
    async Task<string> WorldAsync(double dur, int seed = 3)
    {
        var f = P($"world{seed}_{F(dur)}.wav");
        if (!File.Exists(f)) await Run("-f", "lavfi", "-i", $"anoisesrc=c=pink:r=48000:d={F(dur)}:seed={seed},volume=0.5", "-ac", "1", f);
        return f;
    }

    // ---------- sync ----------

    [Fact] // the audio stream of the master starts 0.28 s after its video: PCM must follow timestamps, or the offset is off by 0.28 s while looking reliable
    public async Task Sync_uses_timestamps_when_an_audio_stream_starts_late()
    {
        var world = await WorldAsync(160);
        await Run("-f", "lavfi", "-i", "color=c=red:s=320x240:r=30:d=150", "-itsoffset", "0.3", "-ss", "0.3", "-t", "149.7", "-i", world, "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("late.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=blue:s=320x240:r=30:d=150", "-i", world, "-af", "adelay=2000:all=1", "-t", "150", "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("cam.mp4"));
        var probe = await Ff.RunProbeAsync(["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=start_time", "-of", "csv=p=0", P("late.mp4")], Ct);
        Assert.True(double.Parse(probe.StdOut.Trim(), IC) > 0.2, "fixture: the audio stream must start late");

        var fit = await AudioSync.AlignAsync(Ff, P("late.mp4"), 150, P("cam.mp4"), 150, Ct);
        Assert.True(fit.Reliable, $"{fit}");
        Assert.InRange(fit.At(0), 1.998, 2.002);

        // the analysis proxy (transcript timeline) is on the same timeline: the PCM at file time t is world time t
        var (_, audio) = await new MediaProbe(Ff).MakeProxyAsync(P("late.mp4"), new MediaInfo { Duration = 150, Width = 320, Height = 240, AudioChannels = 1 }, P("cache"), Ct);
        var a = await AudioSync.LoadAsync(Ff, audio, 8000, 0, Ct, 40, 10);
        var w = await AudioSync.LoadAsync(Ff, world, 8000, 0, Ct, 39, 12);
        Assert.InRange(AudioSync.Gcc(a, w, 8000, 2).Delay, 0.998, 1.002);
    }

    [Fact] // a camera started 35 s after the master: outside any fixed search window; found by the full-length envelope search
    public async Task Sync_finds_a_camera_that_started_much_later()
    {
        var world = await WorldAsync(160);
        await Run("-f", "lavfi", "-i", "color=c=red:s=320x240:r=30:d=150", "-t", "150", "-i", world, "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("m.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=blue:s=320x240:r=30:d=100", "-ss", "35", "-t", "100", "-i", world, "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("late.mp4"));
        var fit = await AudioSync.AlignAsync(Ff, P("m.mp4"), 150, P("late.mp4"), 100, Ct);
        Assert.True(fit.Reliable, $"{fit}");
        Assert.InRange(fit.At(0), -35.002, -34.998);

        await Run("-f", "lavfi", "-i", "color=c=green:s=320x240:r=30:d=120", "-f", "lavfi", "-i", "anoisesrc=c=brown:r=48000:d=120:seed=99", "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("other.mp4"));
        Assert.False((await AudioSync.AlignAsync(Ff, P("m.mp4"), 150, P("other.mp4"), 120, Ct)).Reliable);
    }

    [Fact] // a camera clock 200 ppm fast: the drift is measured and the offset is right at both ends (a single offset would be 30 ms off at the end)
    public async Task Sync_measures_clock_drift()
    {
        var world = await WorldAsync(160);
        await Run("-f", "lavfi", "-i", "color=c=red:s=320x240:r=30:d=150", "-t", "150", "-i", world, "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("m.mp4"));
        // other(t) = world((t − 1.5)·(1 + 200e−6))  ⇒  otherTime = 1.5 + masterTime / (1 + 200e−6)
        await Run("-f", "lavfi", "-i", "color=c=blue:s=320x240:r=30:d=150", "-i", world, "-af", "asetrate=48009.6,aresample=48000,adelay=1500:all=1", "-t", "150", "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("d.mp4"));
        var fit = await AudioSync.AlignAsync(Ff, P("m.mp4"), 150, P("d.mp4"), 150, Ct);
        Assert.True(fit.Reliable, $"{fit}");
        double truePpm = -200 / (1 + 200e-6);
        Assert.InRange(fit.DriftPpm, truePpm - 30, truePpm + 30);
        foreach (var t in new[] { 0.0, 75, 145 }) Assert.InRange(fit.At(t) - (1.5 + truePpm * 1e-6 * t), -0.004, 0.004);
    }

    [Fact] // Theil–Sen: windows with a wrong peak do not bend the line
    public void Drift_fit_ignores_outlier_windows()
    {
        var pts = Enumerable.Range(0, 10).Select(i => (T: i * 60.0, D: 2.0 + 50e-6 * i * 60, Conf: 100.0)).ToList();
        pts[3] = (pts[3].T, 2.4, 100); pts[7] = (pts[7].T, 1.7, 100);
        var (off, ppm) = AudioSync.Fit(pts);
        Assert.InRange(off, 1.9995, 2.0005);
        Assert.InRange(ppm, 49, 51);
    }

    [Fact] // a long clip on a drifting camera is split so that every piece re-anchors on the drift line (≤ half a frame off)
    public void Drifting_camera_clips_are_reanchored()
    {
        var cam = Asset("b", "b.mp4", 4000, 1280, 720, offset: 3.0, ppm: 60);
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "b", SrcIn = cam.CameraTime(100, "m"), SrcOut = cam.CameraTime(100, "m") + 900, AudioIn = 100 } };
        var res = PlanBuilder.ApplyDrift(clips, [cam, Asset("m", "m.mp4", 4000, 1280, 720, AssetRole.Main)], "m", 30);
        Assert.True(res.Count >= 4, $"{res.Count} pieces");
        Assert.Equal(900, res.Sum(c => c.MasterOut - c.MasterIn), 3);
        foreach (var c in res)
        {
            Assert.InRange(c.SrcIn - cam.CameraTime(c.MasterIn, "m"), -0.001, 0.001);
            Assert.True(Math.Abs(c.SrcOut - cam.CameraTime(c.MasterOut, "m")) <= 0.5 / 30 + 0.001, $"{c.ClipId} ends {(c.SrcOut - cam.CameraTime(c.MasterOut, "m")) * 1000:F1} ms off");
        }
    }

    // ---------- screen + presenter (P10) ----------

    static readonly double[] Beeps = [6, 14, 22, 31, 39, 47];      // world time: beep + white flash on the presenter camera
    static readonly double[] Marks = [12, 18, 24];                  // world time: beep + magenta block on the screen
    const double ScreenStart = 2.5;                                 // the screen recording started 2.5 s after the presenter camera
    const double TypeFrom = 10, TypeTo = 26;                        // world time: someone types in the top-right of the screen

    static string Between(IEnumerable<double> ts, double len) => string.Join("+", ts.Select(t => $"between(t,{F(t)},{F(t + len)})"));

    async Task<(string Presenter, string Screen)> SceneAsync()
    {
        // world audio: quiet pink room tone + 1 kHz beeps (sample-accurate) at Beeps ∪ Marks
        var beeps = Beeps.Concat(Marks).OrderBy(x => x).ToArray();
        await Run("-f", "lavfi", "-i", "anoisesrc=c=pink:r=48000:d=66:seed=21,volume=0.05", "-f", "lavfi", "-i", $"aevalsrc='0.5*sin(2*PI*1000*t)*({Between(beeps, 0.1)})':s=48000:d=66",
            "-filter_complex", "[0:a][1:a]amix=inputs=2:normalize=0", "-ac", "1", P("world.wav"));
        // presenter: noisy (like a real sensor) dark blue frame, a white flash on every beep
        var presenter = P("presenter.mp4");
        await Run("-f", "lavfi", "-i", "color=c=0x2040C0:s=1280x720:r=30:d=60", "-i", P("world.wav"),
            "-vf", $"noise=alls=14:allf=t,drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:enable='{Between(beeps, 0.09)}'", "-t", "60",
            "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-c:a", "aac", presenter);
        // screen: light-grey desktop; one typed "character" every 0.5 s (top-right, two lines); a magenta block on the screen-side beeps.
        // Its audio (same room, quieter, low-passed) starts 0.3 s after its video, as some recorders write it.
        var chars = new List<string>();
        for (int k = 0; k < 32; k++)
        {
            double t = TypeFrom - ScreenStart + k * 0.5;
            chars.Add($"drawbox=x={1300 + (k % 16) * 30}:y={170 + (k / 16) * 60}:w=24:h=40:color=black:t=fill:enable='gte(t,{F(t)})*lte(t,{F(TypeTo - ScreenStart + 6)})'");
        }
        var marks = Between(Marks.Select(m => m - ScreenStart), 0.09);
        var screen = P("screen.mp4");
        await Run("-f", "lavfi", "-i", "color=c=0xE0E0E0:s=1920x1080:r=30:d=60", "-itsoffset", "0.3", "-ss", F(ScreenStart + 0.3), "-t", "59.7", "-i", P("world.wav"),
            "-vf", string.Join(",", chars) + $",drawbox=x=1300:y=320:w=480:h=180:color=magenta:t=fill:enable='{marks}'",
            "-af", "volume=0.6,lowpass=f=4000,aformat=channel_layouts=mono", "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-c:a", "aac", screen);
        return (presenter, screen);
    }

    [Fact] // AT: P10 — roles found automatically, synced from audio, screen while typing / presenter while still, A/V sync measured in the output
    public async Task Screen_and_presenter_are_synced_switched_and_composited()
    {
        var (presenterFile, screenFile) = await SceneAsync();

        // 1. which is which: activity + (no) faces
        var actP = (await ScreenActivity.AnalyzeAsync(Ff, presenterFile, P("actP.json"), Ct)).Samples;
        var anS = await ScreenActivity.AnalyzeAsync(Ff, screenFile, P("actS.json"), Ct); var actS = anS.Samples;
        var pres0 = Asset("a1", presenterFile, 60, 1280, 720, AssetRole.Main);
        var scr0 = Asset("a2", screenFile, 60, 1920, 1080);
        var (scrId, presId, why) = ScreenRoles.Classify([pres0, scr0], new Dictionary<string, SourceProfile>
        {
            ["a1"] = new("a1", ScreenActivity.StaticRatio(actP), -1), ["a2"] = new("a2", ScreenActivity.StaticRatio(actS), -1),
        });
        Assert.True(scrId == "a2" && presId == "a1", why);
        // typing shows up where and when it happens (screen time)
        var typing = actS.Where(s => s.Active && s.T > TypeFrom - ScreenStart + 0.6 && s.T < TypeTo - ScreenStart - 0.5).ToList();
        Assert.True(typing.Count >= 25, $"{typing.Count} active steps while typing");
        Assert.All(typing, s => Assert.True(s.X0 >= 0.6 && s.Y1 <= 0.5, $"t={s.T} box {s.X0:F2},{s.Y0:F2}-{s.X1:F2},{s.Y1:F2}"));
        Assert.DoesNotContain(actS, s => s.Active && s.T < TypeFrom - ScreenStart - 0.5);

        // 2. sync (the screen's audio stream starts 0.3 s late)
        var fit = await AudioSync.AlignAsync(Ff, presenterFile, 60, screenFile, 59.7, Ct);
        Assert.True(fit.Reliable, $"{fit}");
        Assert.InRange(fit.At(0), -ScreenStart - 0.002, -ScreenStart + 0.002);
        var scr = scr0 with { SyncOffset = fit.Offset, SyncDriftPpm = fit.DriftPpm };
        var assets = new List<SourceAsset> { pres0, scr };

        // 3. plan: three jump-cut speech pieces (master time)
        var p = Fixtures.Presets["P10"];
        Clip Piece(string id, double a, double b) => new() { ClipId = id, AssetId = "a1", SrcIn = a, SrcOut = b, AudioIn = a };
        var speech = new List<Clip> { Piece("s1", 4, 20), Piece("s2", 21, 34), Piece("s3", 36, 50) };
        var notes = new List<string>();
        var tr = new Transcript { Provider = "test", Duration = 60 };
        var timeline = ScreenPlanner.Plan(speech, tr, assets, pres0, new ScreenContext("a2", "a1", actS, anS.Details), new Dictionary<string, List<FaceSample>>(), p, new Canvas(), notes);
        string Dump() => string.Join(" ", timeline.Select(c => $"[{c.AssetId} {c.Reframe.Mode} m{c.MasterIn:F1}-{c.MasterOut:F1}{(c.Pip != null ? " pip:" + c.Pip.Corner : "")}]"));
        Assert.Equal(speech.Sum(c => c.Length), timeline.Sum(c => c.Length), 2);
        Assert.Equal("a1", timeline[0].AssetId);                                                     // hook: the presenter's face first
        Assert.All(timeline, c => Assert.True(c.Length >= p.Screen!.MinHoldSec - 0.01 || c == timeline[^1], $"{c.ClipId} {c.Length:F2}s: {Dump()}"));
        foreach (var t in new[] { 11.0, 15, 19, 22, 25 })                                            // typing → screen with the presenter inset
        {
            var c = timeline.Single(c => c.MasterIn <= t && c.MasterOut > t);
            Assert.True(c.AssetId == "a2" && c.Reframe.Mode == "screen" && c.Pip?.AssetId == "a1", $"m{t}: {Dump()}");
            Assert.InRange(c.SrcIn - c.MasterIn, -ScreenStart - 0.002, -ScreenStart + 0.002);
            Assert.InRange(c.Pip!.SrcIn - c.MasterIn, -0.001, 0.001);
            Assert.True(c.Reframe.CropW is > 0 and < 1920, $"zoomed in on the typing: crop {c.Reframe.CropW}");
            Assert.NotEqual("tr", c.Pip.Corner);
        }
        foreach (var t in new[] { 30.0, 40, 48 })                                                    // still screen → presenter
            Assert.Equal("a1", timeline.Single(c => c.MasterIn <= t && c.MasterOut > t).AssetId);

        // 4. render and measure the output (decoded pixels / PCM): A/V sync and the composition
        var plan = new EditPlan { EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Timeline = timeline, Audio = new AudioSpec { Master = "a1" } };
        var ctx = new QaContext(plan, p, tr, ["a1", "a2"], [], assets);
        Assert.Equal(QaStatus.Pass, QaEngine.PlanChecks(ctx).First(q => q.CheckId == "H-09").Status);
        Assert.Equal(QaStatus.Fail, QaEngine.PlanChecks(ctx with { RightsAssetIds = ["a2"] }).First(q => q.CheckId == "H-09").Status);   // the inset's rights count too
        var outp = await new Renderer(Ff).RenderAsync(plan, p, assets.ToDictionary(a => a.Id), P("p10.mp4"), preview: true, Ct);

        const int vw = 135, vh = 240, sc = 8;                 // decoded at 1/8 of the 1080×1920 canvas
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-i", outp.Path, "-vf", $"scale={vw}:{vh}:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], Ct, stdout: ms);
        var frames = ms.ToArray(); int fsz = vw * vh * 3, nf = frames.Length / fsz;
        Assert.InRange(nf / 30.0, plan.Duration - 0.1, plan.Duration + 0.1);
        (int R, int G, int B) Px(int f, int x, int y) { int o = f * fsz + (y * vw + x) * 3; return (frames[o], frames[o + 1], frames[o + 2]); }
        Clip At(double t) { double acc = 0; foreach (var c in timeline) { if (t < acc + c.Length) return c; acc += c.Length; } return timeline[^1]; }
        bool White((int R, int G, int B) c) => c.R > 200 && c.G > 200 && c.B > 200;
        bool Blue((int R, int G, int B) c) => c.B > 140 && c.R < 110;
        bool Magenta((int R, int G, int B) c) => c.R > 180 && c.G < 90 && c.B > 180;

        var flashes = new List<double>(); var magenta = new List<double>(); bool prevF = false, prevM = false;
        int layoutChecked = 0;
        for (int f = 0; f < nf; f++)
        {
            double t = f / 30.0; var c = At(t);
            var probe = c.Pip is { } pip ? ((pip.X + pip.Border + pip.D / 2) / sc, (pip.Y + pip.Border + pip.D / 2) / sc) : (540 / sc, 840 / sc);
            bool fl = White(Px(f, probe.Item1, probe.Item2));
            if (fl && !prevF) flashes.Add(t); prevF = fl;
            bool mg = false;
            if (c.Reframe.Mode == "screen") for (int y = c.Reframe.BoxY / sc; y < (c.Reframe.BoxY + c.Reframe.BoxH) / sc && !mg; y++) for (int x = 0; x < vw && !mg; x++) mg = Magenta(Px(f, x, y));
            if (mg && !prevM) magenta.Add(t); prevM = mg;
            // composition, away from flashes: screen layout = grey band + blue inset; presenter = blue
            if (!fl && !mg && f % 15 == 0)
            {
                if (c.Pip is { } pp)
                {
                    Assert.True(Blue(Px(f, probe.Item1, probe.Item2)), $"t={t:F2} inset {Px(f, probe.Item1, probe.Item2)}");
                    var band = Px(f, (pp.Corner.EndsWith('l') ? 1000 : 80) / sc, (c.Reframe.BoxY + c.Reframe.BoxH / 2) / sc);
                    Assert.True(band.R > 180 && band.G > 180, $"t={t:F2} screen band {band}");
                }
                else Assert.True(Blue(Px(f, probe.Item1, probe.Item2)), $"t={t:F2} presenter {Px(f, probe.Item1, probe.Item2)}");
                layoutChecked++;
            }
        }
        Assert.True(layoutChecked > 50);

        // beep onsets in the output audio
        using var am = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-i", outp.Path, "-vn", "-af", "highpass=f=700", "-ac", "1", "-ar", "48000", "-f", "s16le", "-"], Ct, stdout: am);
        var pcm = am.ToArray(); var onsets = new List<double>(); int quiet = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            int v = Math.Abs((short)(pcm[i] | pcm[i + 1] << 8));
            if (v > 6000) { if (quiet > 48000 / 5) onsets.Add(i / 2 / 48000.0); quiet = 0; } else quiet++;
        }
        // every beep the plan keeps is heard where expected, and every one has its flash within one frame
        var expected = Beeps.Concat(Marks).Where(b => speech.Any(s => b >= s.MasterIn && b + 0.1 <= s.MasterOut)).Select(b => OutTime(timeline, b)).OrderBy(x => x).ToList();
        Assert.Equal(expected.Count, onsets.Count);
        for (int i = 0; i < expected.Count; i++) Assert.InRange(onsets[i] - expected[i], -0.02, 0.02);
        foreach (var b in Beeps.Where(b => speech.Any(s => b >= s.MasterIn && b + 0.1 <= s.MasterOut)))
        {
            double tb = OutTime(timeline, b);
            Assert.True(flashes.Any(x => Math.Abs(x - tb) <= 1.0 / 30 + 0.005), $"beep at out {tb:F3}: flashes {string.Join(",", flashes.Select(x => x.ToString("F3", IC)))}");
        }
        foreach (var m in Marks)
        {
            double tb = OutTime(timeline, m);
            Assert.True(magenta.Any(x => Math.Abs(x - tb) <= 1.0 / 30 + 0.005), $"screen mark at out {tb:F3}: seen {string.Join(",", magenta.Select(x => x.ToString("F3", IC)))}");
        }
    }

    static double OutTime(List<Clip> tl, double master)
    {
        double acc = 0;
        foreach (var c in tl) { if (master >= c.MasterIn && master < c.MasterOut) return acc + (master - c.MasterIn) / c.Speed; acc += c.Length; }
        return double.NaN;
    }

    [Fact]
    public async Task News_preset_renders_the_anchor_with_a_timed_landscape_inset()
    {
        await Run("-f", "lavfi", "-i", "color=c=red:s=640x360:r=30:d=8", "-f", "lavfi", "-i", "sine=f=440:r=48000:d=8",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("anchor.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=blue:s=640x360:r=30:d=8", "-c:v", "libx264", "-preset", "ultrafast", P("visual.mp4"));
        var main = Asset("m", P("anchor.mp4"), 8, 640, 360, AssetRole.Main);
        var screen = Asset("s", P("visual.mp4"), 8, 640, 360, AssetRole.Screen) with { PipScenes = [new(2, 6)] };
        var speech = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 8 } };
        var tr = new Transcript
        {
            Provider = "test", Sentences = [new(0, 0, 8, "新しいニュース", 1, 0, 1)],
            Words = [new("w1", 0.5, 1.3, "新しい", 1), new("w2", 1.3, 1.9, "ニュース", 1)],
        };
        var faces = Enumerable.Range(0, 17).Select(i => new FaceSample(i * 0.5, [new(0.72, 0.2, 0.15, 0.2, 1)])).ToList();
        var preset = Fixtures.Presets["P13"];
        var canvas = Canvas.Landscape();
        var timeline = PipPlanner.Plan(speech, tr, main, screen, preset, canvas, [], faces);
        var inset = Assert.Single(timeline, c => c.Pip != null).Pip!;
        Assert.Equal("rectangle", inset.Shape);
        Assert.True(inset.X + inset.Width < canvas.Width * 0.72 && inset.Y < canvas.Height / 2);

        var plan = new EditPlan
        {
            EditplanId = "news", ProjectId = "p", Preset = preset.Ref, Canvas = canvas, Timeline = timeline,
            Telops = TelopBuilder.Build(timeline, tr, preset, "", "新しいニュース", [], []),
            TelopFaces = new() { ["m"] = new(main.Media, faces) },
            Audio = new AudioSpec { Master = "m", SfxEnabled = false },
        };
        var headline = Assert.Single(plan.Telops, t => t.Type == TelopType.Headline);
        Assert.Null(TelopLayout.Resolve(plan, preset)[headline.Id].Bounds);
        Assert.DoesNotContain(",Headline,,0,0,0,,", AssWriter.Write(plan, preset));
        var clearPlan = plan with
        {
            Timeline = speech,
            Telops = TelopBuilder.Build(speech, tr, preset, "", "新しいニュース", [], []),
        };
        var clearHeadline = Assert.Single(clearPlan.Telops, t => t.Type == TelopType.Headline);
        var titleBounds = Assert.IsType<TelopLayout.Rect>(TelopLayout.Resolve(clearPlan, preset)[clearHeadline.Id].Bounds);
        Assert.Contains(",Headline,,0,0,0,,", AssWriter.Write(clearPlan, preset));
        var output = await new Renderer(Ff).RenderAsync(plan, preset, new[] { main, screen }.ToDictionary(a => a.Id), P("news.mp4"), preview: true, Ct);
        var clearOutput = await new Renderer(Ff).RenderAsync(clearPlan, preset, new[] { main, screen }.ToDictionary(a => a.Id), P("news-title.mp4"), preview: true, Ct);
        var media = await new MediaProbe(Ff).ProbeAsync(output.Path, Ct);
        Assert.True(media.Width > media.Height);
        async Task<byte[]> Frame(string path, string time)
        {
            using var buffer = new MemoryStream();
            await Ff.RunFfmpegAsync(["-v", "error", "-ss", time, "-i", path, "-frames:v", "1",
                "-vf", "scale=192:108:flags=area", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: buffer);
            return buffer.ToArray();
        }
        var before = await Frame(output.Path, "1");
        var during = await Frame(output.Path, "3");
        var after = await Frame(output.Path, "7");
        var withTitle = await Frame(clearOutput.Path, "1");
        int changed = 0;
        for (int row = Math.Max(0, (int)titleBounds.Top / 10); row < Math.Min(108, (int)Math.Ceiling(titleBounds.Bottom / 10)); row++)
        for (int col = 0; col < 192; col++)
        {
            int i = (row * 192 + col) * 3;
            if (Math.Abs(withTitle[i] - before[i]) + Math.Abs(withTitle[i + 1] - before[i + 1]) + Math.Abs(withTitle[i + 2] - before[i + 2]) > 50) changed++;
        }
        Assert.True(changed > 20, $"headline must be visible in the rendered frame: {changed} pixels changed");
        int x = (inset.X + inset.Width / 2) / 10, y = (inset.Y + inset.Height / 2) / 10;
        int pixel = (y * 192 + x) * 3;
        Assert.True(before[pixel] > 180 && before[pixel + 2] < 70);
        Assert.True(during[pixel + 2] > 180 && during[pixel] < 70);
        Assert.True(after[pixel] > 180 && after[pixel + 2] < 70);
        int anchor = (30 * 192 + 170) * 3;
        Assert.True(during[anchor] > 180 && during[anchor + 2] < 70);
    }

    [Fact]
    public async Task Background_first_pip_keeps_the_rotated_main_and_shows_only_eligible_speech()
    {
        await Run("-f", "lavfi", "-i", "color=c=red:s=640x360:r=30:d=12", "-f", "lavfi", "-i", "sine=f=440:r=48000:d=12",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("base.mp4"));
        await Run("-display_rotation", "-90", "-i", P("base.mp4"), "-c", "copy", P("portrait.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=blue:s=640x360:r=30:d=14", "-vf",
            "drawbox=x=0:y=0:w=iw:h=ih:color=lime:t=fill:enable='between(t,6,6.2)'", "-c:v", "libx264", "-preset", "ultrafast", P("slides.mp4"));
        var media = await new MediaProbe(Ff).ProbeAsync(P("portrait.mp4"), Ct);
        Assert.Equal((360, 640), (media.Width, media.Height));
        var main = Asset("m", P("portrait.mp4"), 12, 360, 640, AssetRole.Main) with { Media = media };
        var screen = Asset("s", P("slides.mp4"), 14, 640, 360, AssetRole.Screen, offset: 2) with { PipScenes = [new(5, 9)] };
        var speech = new List<Clip>
        {
            new() { ClipId = "c1", AssetId = "m", SrcIn = 0, SrcOut = 6 },
            new() { ClipId = "c2", AssetId = "m", SrcIn = 8, SrcOut = 12 },
        };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 2, 6, "このスライド", 1, 0, 0)] };
        var p = Fixtures.Presets["P11"];
        var faces = Enumerable.Range(0, 25).Select(i => new FaceSample(i * 0.5, [new(0.3, 0.2, 0.4, 0.25, 1)])).ToList();
        var timeline = PipPlanner.Plan(speech, tr, main, screen, p, new Canvas(), [], faces);
        Assert.Equal(10, timeline.Sum(c => c.Length), 6);
        Assert.All(timeline, c => Assert.Equal("m", c.AssetId));
        var inset = Assert.Single(timeline, c => c.Pip != null);
        Assert.Equal((3.0, 6.0, 5.0), (inset.MasterIn, inset.MasterOut, inset.Pip!.SrcIn));
        Assert.Equal("rectangle", inset.Pip.Shape);
        var plan = new EditPlan
        {
            EditplanId = "pip", ProjectId = "p", Preset = p.Ref, Timeline = timeline,
            Audio = new AudioSpec { Master = "m", SfxEnabled = false },
        };
        var output = await new Renderer(Ff).RenderAsync(plan, p, new[] { main, screen }.ToDictionary(a => a.Id), P("pip.mp4"), preview: true, Ct);
        var rendered = await new MediaProbe(Ff).ProbeAsync(output.Path, Ct);
        Assert.InRange(rendered.Duration, 9.95, 10.1);
        using var buffer = new MemoryStream();
        await Ff.RunFfmpegAsync(["-v", "error", "-i", output.Path, "-vf", "scale=108:192:flags=area", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: buffer);
        var frames = buffer.ToArray();
        (int R, int G, int B) Pixel(double t, int x, int y)
        {
            int i = ((int)(t * 30) * 108 * 192 + y * 108 + x) * 3;
            return (frames[i], frames[i + 1], frames[i + 2]);
        }
        bool Red((int R, int G, int B) c) => c.R > 180 && c.G < 70 && c.B < 70;
        // Rotation must fill the portrait canvas, and the original camera remains behind the slide at every time.
        foreach (var t in new[] { 1.0, 3.5, 4.1, 6.5, 9.0 })
            Assert.True(Red(Pixel(t, 54, 25)), $"main background at {t}");
        Assert.True(Red(Pixel(1, 54, 155)));
        Assert.True(Red(Pixel(6.5, 54, 155)));
        Assert.True(Pixel(3.5, 54, 155).B > 180);
        Assert.True(Pixel(4.1, 54, 155).G > 180, "screen source 6.1 must appear at master/output 4.1");
    }

    [Fact] // the renderer places a shrunk (top-anchored) main exactly where layout projects it
    public async Task Shrunk_main_renders_where_the_layout_projects_it()
    {
        await Run("-f", "lavfi", "-i", "color=c=blue:s=360x640:r=30:d=2,drawbox=x=90:y=320:w=180:h=160:color=red:t=fill", "-f", "lavfi", "-i", "sine=r=48000:d=2",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("shrink.mp4"));
        var main = Asset("m", P("shrink.mp4"), 2, 360, 640, AssetRole.Main);
        var p = Fixtures.Presets["P11"];
        var clip = new Clip { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 2, Reframe = new() { Mode = "blur_bg", FgScale = 0.72, FgOffsetY = 0 } };
        var plan = new EditPlan { EditplanId = "sh", ProjectId = "p", Preset = p.Ref, Timeline = [clip], Audio = new AudioSpec { Master = "m", SfxEnabled = false } };
        var output = await new Renderer(Ff).RenderAsync(plan, p, new Dictionary<string, SourceAsset> { ["m"] = main }, P("sh.mp4"), preview: true, Ct);
        using var buffer = new MemoryStream();
        await Ff.RunFfmpegAsync(["-v", "error", "-ss", "1", "-i", output.Path, "-frames:v", "1", "-vf", "scale=108:192:flags=area", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: buffer);
        var f = buffer.ToArray();
        (int R, int G, int B) Px(double x, double y) { int i = ((int)(y / 10) * 108 + (int)(x / 10)) * 3; return (f[i], f[i + 1], f[i + 2]); }
        var box = TelopLayout.Project(new FaceBox(0.25, 0.5, 0.5, 0.25, 1), clip, main.Media, new Canvas(), 0);
        double cx = (box.Left + box.Right) / 2, cy = (box.Top + box.Bottom) / 2;
        Assert.True(Px(cx, cy) is { R: > 180, G: < 80, B: < 80 }, $"{Px(cx, cy)} at {cx},{cy}");
        Assert.True(Px(cx, box.Top - 40) is { B: > 150, R: < 80 }, "above the box is the main's blue");
        double pad = TelopLayout.FacePadding, l = box.Left + pad, r = box.Right - pad;
        Assert.True(Px(l + 25, cy).R > 180 && Px(l - 25, cy).R < 80 && Px(r - 25, cy).R > 180 && Px(r + 25, cy).R < 80, "x scale/offset");
        Assert.True(Px(540, (int)(1080 * 0.72) * 16 / 9 - 40).B > 150, "the fg reaches down to where it is projected");
    }

    [Fact] // with a deck, matched stretches show the slide image (sharp, original) instead of the recording, in the inset and in a screen-only switch
    public async Task Pip_shows_the_matched_slide_image_instead_of_the_recording()
    {
        await Run("-f", "lavfi", "-i", "color=c=red:s=360x640:r=30:d=10", "-f", "lavfi", "-i", "sine=f=440:r=48000:d=10",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("main.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=blue:s=640x360:r=30:d=12", "-c:v", "libx264", "-preset", "ultrafast", P("rec.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=yellow:s=1280x720", "-frames:v", "1", P("slide-001.png"));
        var main = Asset("m", P("main.mp4"), 10, 360, 640, AssetRole.Main);
        var deck = new SlideDeck { Path = P("deck.pptx"), Sha256 = "d", Source = "powerpoint", Slides = [new(1, P("slide-001.png"), 1280, 720)] };
        var screen = Asset("s", P("rec.mp4"), 12, 640, 360, AssetRole.Screen) with
        {
            PipScenes = [new(0, 12)], Deck = deck, SlideSpans = [new(3, 6, 1, 0.97)], PipContent = new PixelRect(40, 20, 560, 315),
        };
        var speech = new List<Clip> { new() { ClipId = "c1", AssetId = "m", SrcIn = 0, SrcOut = 10 } };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 0, 10, "このスライド", 1, 0, 0)] };
        var p = Fixtures.Presets["P11"];
        var faces = Enumerable.Range(0, 21).Select(i => new FaceSample(i * 0.5, [new(0.3, 0.1, 0.4, 0.2, 1)])).ToList();
        var notes = new List<string>();
        var timeline = PipPlanner.Plan(speech, tr, main, screen, p, new Canvas(), notes, faces);
        Assert.Equal(10, timeline.Sum(c => c.Length), 6);
        Assert.DoesNotContain(timeline, c => c.MasterIn < 3 - 1e-6 && c.MasterOut > 3 + 1e-6 || c.MasterIn < 6 - 1e-6 && c.MasterOut > 6 + 1e-6);
        foreach (var c in timeline.Where(c => c.Pip != null))
        {
            bool slide = c.MasterIn >= 3 && c.MasterOut <= 6;
            Assert.Equal(slide ? deck.Slides[0].Path : null, c.Pip!.Image);
            Assert.Equal(slide ? 0 : 560, c.Pip.CropW);
            Assert.InRange(c.Pip.Width / (double)c.Pip.Height, (slide ? 16 / 9.0 : 560 / 315.0) - 0.01, (slide ? 16 / 9.0 : 560 / 315.0) + 0.01);
        }
        Assert.Contains(timeline, c => c.Pip?.Image != null);
        Assert.Contains(notes, n => n.Contains("高解像度スライド"));
        // without face data the same stretch switches to the screen, still showing the slide
        var switched = PipPlanner.Plan(speech, tr, main, screen, p, new Canvas(), [], null);
        Assert.Contains(switched, c => c.AssetId == "s" && c.Reframe.Image == deck.Slides[0].Path && c.Reframe.CropW == 0);

        var assets = new[] { main, screen }.ToDictionary(a => a.Id);
        async Task<Func<double, int, int, (int R, int G, int B)>> Render(List<Clip> tl, string name)
        {
            var plan = new EditPlan { EditplanId = name, ProjectId = "p", Preset = p.Ref, Timeline = tl, Audio = new AudioSpec { Master = "m", SfxEnabled = false } };
            var output = await new Renderer(Ff).RenderAsync(plan, p, assets, P(name + ".mp4"), preview: true, Ct);
            Assert.InRange((await new MediaProbe(Ff).ProbeAsync(output.Path, Ct)).Duration, 9.95, 10.1);
            using var buffer = new MemoryStream();
            await Ff.RunFfmpegAsync(["-v", "error", "-i", output.Path, "-vf", "scale=108:192:flags=area", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: buffer);
            var frames = buffer.ToArray();
            return (t, x, y) => { int i = ((int)(t * 30) * 108 * 192 + y * 108 + x) * 3; return (frames[i], frames[i + 1], frames[i + 2]); };
        }
        bool Yellow((int R, int G, int B) c) => c.R > 180 && c.G > 180 && c.B < 90;
        bool Blue((int R, int G, int B) c) => c.B > 160 && c.R < 90 && c.G < 90;
        var px = await Render(timeline, "slides");
        foreach (var t in new[] { 3.5, 5.5 })
        {
            var c = timeline.Single(c => c.MasterIn <= t && c.MasterOut > t);
            Assert.True(Yellow(px(t, (c.Pip!.X + c.Pip.Width / 2) / 10, (c.Pip.Y + c.Pip.Height / 2) / 10)), $"slide image in the inset at {t}");
        }
        var rec = timeline.First(c => c.Pip != null && c.MasterIn >= 6);
        Assert.True(Blue(px((rec.MasterIn + rec.MasterOut) / 2, (rec.Pip!.X + rec.Pip.Width / 2) / 10, (rec.Pip.Y + rec.Pip.Height / 2) / 10)), "recording outside the slide span");
        var sw = await Render(switched, "switched");
        var sc = switched.First(c => c.Reframe.Image != null);
        Assert.True(Yellow(sw((sc.MasterIn + sc.MasterOut) / 2, 54, (sc.Reframe.BoxY + sc.Reframe.BoxH / 2) / 10)), "slide image in the screen-only switch");
    }

    [Fact] // the screen-only switch shows a cropped content box at its own aspect (the screen layout stretches a crop to the box)
    public void Pip_switch_frame_keeps_the_content_aspect_centred_above_the_captions()
    {
        var p = Fixtures.Presets["P11"];
        var canvas = new Canvas();
        var screen = Asset("s", "s.mp4", 20, 1920, 1080, AssetRole.Screen) with { PipContent = new PixelRect(60, 120, 1560, 870) };
        var r = PipPlanner.SwitchFrame(screen, p, canvas);
        var band = AssWriter.Band(TelopType.Caption, p, canvas.Height)!.Value;
        Assert.InRange(r.CropW / (double)r.CropH, canvas.Width / (double)r.BoxH - 0.01, canvas.Width / (double)r.BoxH + 0.01);
        Assert.True(r.BoxY >= 24 && r.BoxY + r.BoxH <= band.Top - 24, $"{r.BoxY}+{r.BoxH}");
        Assert.InRange(r.BoxY - 24 - (band.Top - 24 - r.BoxY - r.BoxH), -4, 4);
        // a tall content box would overflow the area: the crop widens around it (inside the frame) instead of being squeezed
        var tall = PipPlanner.SwitchFrame(screen with { PipContent = new PixelRect(100, 0, 600, 1080) }, p, canvas);
        Assert.InRange(tall.CropW / (double)tall.CropH, canvas.Width / (double)tall.BoxH - 0.01, canvas.Width / (double)tall.BoxH + 0.01);
        Assert.True(tall.Track![0].X <= 100 && tall.Track[0].X + tall.CropW >= 700 && tall.Track[0].X >= 0 && tall.Track[0].X + tall.CropW <= 1920);
        var slide = PipPlanner.SwitchFrame(screen, p, canvas, new SlideImage(1, "s1.png", 3840, 2160));
        Assert.Equal(("s1.png", 0), (slide.Image, slide.CropW));
    }

    [Fact] // 2 fps frame matches: a short unmatched transition is split between its slides; suggestions follow slide (or full-screen) coverage
    public void Slide_spans_bridge_transitions_and_suggest_only_slide_or_full_screen_scenes()
    {
        byte[] Strip(byte v) => Enumerable.Repeat(v, 64).ToArray();
        var frames = Enumerable.Range(0, 20).Select(k =>
        {
            double t = k * 0.5;
            int slide = t < 2 ? 1 : t >= 2.5 && t < 5 ? 2 : 0;
            return new SlideDecks.FrameMatch(t, slide, slide > 0 ? 0.97 : 0.2, Strip(t < 5 ? (byte)0 : (byte)200));
        }).ToList();
        var spans = SlideDecks.Spans(frames, 10);
        Assert.Equal([new SlideSpan(0, 2, 1, 0.97), new SlideSpan(2, 4.75, 2, 0.97)], spans);
        var full = SlideDecks.FullScreen(frames, 10);
        Assert.Equal([new Interval(0, 4.75)], full);
        Assert.Empty(SlideDecks.FullScreen(frames.Select(f => f with { Strip = Strip((byte)(f.T * 25)) }).ToList(), 10));   // no persistent taskbar: no claim
        var scenes = new List<Interval> { new(0, 5), new(5, 10) };
        var withDeck = SlideDecks.Suggest(scenes, new SlideAnalysis(spans, full), true);
        Assert.Equal(("slide", true, 0.0, 4.75), (withDeck[0].Kind, withDeck[0].Suggested, withDeck[0].T0, withDeck[0].T1));
        Assert.Equal([1, 2], withDeck[0].Slides);
        Assert.Equal(("screen", false), (withDeck[1].Kind, withDeck[1].Suggested));
        Assert.Empty(SlideDecks.Suggest([new(4.5, 10)], new SlideAnalysis(spans, full), true)[0].Slides);   // a 0.25 s edge overlap is not "shown"
        var noDeck = SlideDecks.Suggest(scenes, new SlideAnalysis([], full), false);
        Assert.Equal(("fullscreen", true, 0.0, 5.0), (noDeck[0].Kind, noDeck[0].Suggested, noDeck[0].T0, noDeck[0].T1));
        Assert.False(noDeck[1].Suggested);
        // a thumbnail correlates with itself, not with a different layout
        var a = new byte[640 * 360]; var b = new byte[640 * 360];
        for (int i = 0; i < a.Length; i++) { a[i] = (byte)(i % 640 < 320 ? 30 : 220); b[i] = (byte)(i / 640 < 180 ? 30 : 220); }
        var sa = SlideDecks.Signature(a, 0, 0, 640, 360)!; var sb = SlideDecks.Signature(b, 0, 0, 640, 360)!;
        Assert.InRange(sa.Zip(sa).Sum(x => x.First * x.Second), 0.999, 1.001);
        Assert.InRange(Math.Abs(sa.Zip(sb).Sum(x => x.First * x.Second)), 0, 0.1);
        Assert.Null(SlideDecks.Signature(new byte[640 * 360], 0, 0, 640, 360));
    }

    [Fact] // click builds: the step images of one slide are no rivals of each other; the recording maps to the right step and At() shows its image
    public async Task Slide_build_steps_match_their_own_step_in_the_recording()
    {
        // slide 1: a title bar plus 0..2 text bars (steps 1..3); slide 2: a different layout
        string Bars(int n) => "drawbox=x=80:y=60:w=1100:h=90:color=0x8080a0:t=fill" + string.Concat(Enumerable.Range(0, n).Select(i => $",drawbox=x=120:y={220 + i * 140}:w=700:h=70:color=0x404040:t=fill"));
        for (int k = 0; k < 3; k++) await Run("-f", "lavfi", "-i", "color=c=white:s=1280x720", "-vf", Bars(k), "-frames:v", "1", P($"slide-001-s{k + 1:D2}.png"));
        await Run("-f", "lavfi", "-i", "color=c=white:s=1280x720", "-vf", "drawbox=x=700:y=300:w=500:h=380:color=0x2060c0:t=fill", "-frames:v", "1", P("slide-002.png"));
        var order = new[] { "slide-001-s01", "slide-001-s02", "slide-001-s03", "slide-002" };
        var inputs = order.SelectMany(n => new[] { "-loop", "1", "-t", "3", "-i", P(n + ".png") }).ToList();
        // a meeting recording: the slideshow scaled into a dark window, heavily compressed
        await Run([.. inputs, "-filter_complex", "[0][1][2][3]concat=n=4:v=1:a=0,scale=960:540,pad=1280:720:160:90:color=0x141414,fps=15", "-c:v", "libx264", "-crf", "34", "-preset", "ultrafast", P("deckrec.mp4")]);
        var deck = new SlideDeck
        {
            Path = P("deck.pptx"), Sha256 = "steps0123456789abcdef", Source = "powerpoint",
            Slides = [.. Enumerable.Range(1, 3).Select(k => new SlideImage(1, P($"slide-001-s{k:D2}.png"), 1280, 720, k)), new(2, P("slide-002.png"), 1280, 720)],
        };
        var screen = Asset("s", P("deckrec.mp4"), 12, 1280, 720, AssetRole.Screen) with { Deck = deck };
        var spans = (await SlideDecks.AnalyzeAsync(Ff, screen, P("deckcache"), Ct)).Spans;
        Assert.Equal([(1, 1), (1, 2), (1, 3), (2, 0)], spans.Select(s => (s.Number, s.Step)));
        for (int i = 0; i < 4; i++) Assert.InRange(spans[i].T0, i * 3 - 0.6, i * 3 + 0.6);
        Assert.Equal(P("slide-001-s02.png"), SlideDecks.At(screen with { SlideSpans = spans }, 4.5)!.Path);
    }

    [Theory] // the state after each click (entrance by paragraph, appear, exit), not the editing view with every shape — by PowerPoint and by LibreOffice
    [InlineData("powerpoint")]
    [InlineData("libreoffice")]
    public async Task Click_build_steps_render_as_in_the_slideshow(string renderer)
    {
        if (!SlideDecks.PowerPointAvailable) Assert.Skip("Needs PowerPoint on Windows to author the test deck");
        if (renderer == "libreoffice" && SlideDecks.LibreOffice() == null) Assert.Skip("Needs LibreOffice (or VIDEOCREATOR_SOFFICE)");
        var pptx = P("builds.pptx");
        var make = P("make.ps1");
        await File.WriteAllTextAsync(make, """
            param([string]$Out)
            $ErrorActionPreference = 'Stop'
            $running = @(Get-Process POWERPNT -ErrorAction SilentlyContinue).Count -gt 0
            $app = New-Object -ComObject PowerPoint.Application
            $p = $app.Presentations.Add(0)
            $p.PageSetup.SlideWidth = 960; $p.PageSetup.SlideHeight = 540
            $s = $p.Slides.Add(1, 12)
            $b = $s.Shapes.AddTextbox(1, 40, 110, 520, 380); $b.TextFrame.TextRange.Text = "First`rSecond"; $b.TextFrame.TextRange.Font.Size = 32
            $o = $s.Shapes.AddShape(9, 620, 120, 280, 200); $o.Fill.ForeColor.RGB = 0x20A040
            $r = $s.Shapes.AddShape(1, 620, 360, 280, 140); $r.Fill.ForeColor.RGB = 0x2020D0
            $q = $s.TimeLine.MainSequence
            [void]$q.AddEffect($b, 10, 2, 1); [void]$q.AddEffect($o, 1, 0, 1); [void]$q.AddEffect($o, 61, 0, 2)
            $x = $q.AddEffect($r, 10, 0, 1); $x.Exit = -1
            $p.SaveAs($Out); $p.Close()
            if (-not $running) { $app.Quit() }
            """, System.Text.Encoding.ASCII, Ct);
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", make, "-Out", pptx }) psi.ArgumentList.Add(a);
        using (var proc = System.Diagnostics.Process.Start(psi)!) await proc.WaitForExitAsync(Ct);
        Assert.True(File.Exists(pptx));
        var deck = await SlideDecks.RenderAsync(Ff, pptx, P("builds"), Ct, renderer);
        Assert.Equal(renderer, deck.Source);
        // clicks: paragraph 1, paragraph 2, oval (+ spin with it), rectangle out -> 5 states
        Assert.Equal([1, 2, 3, 4, 5], deck.Slides.Select(s => s.Step));
        async Task<(int R, int G, int B)> Px(int step, double x, double y)
        {
            using var ms = new MemoryStream();
            await Ff.RunFfmpegAsync(["-v", "error", "-i", deck.Slides[step - 1].Path, "-vf", $"crop=1:1:{(int)(x * 4)}:{(int)(y * 4)}", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: ms);
            var b = ms.ToArray(); return (b[0], b[1], b[2]);
        }
        bool Green((int R, int G, int B) c) => c.G > 120 && c.R < 100;
        bool Red((int R, int G, int B) c) => c.R > 150 && c.G < 90;
        bool Dark((int R, int G, int B) c) => c.R + c.G + c.B < 600;
        async Task<bool> Ink(int step, double y)   // any text pixel in the row band of a paragraph
        {
            using var ms = new MemoryStream();
            await Ff.RunFfmpegAsync(["-v", "error", "-i", deck.Slides[step - 1].Path, "-vf", $"crop={400 * 4}:{30 * 4}:{50 * 4}:{(int)(y * 4)},scale=200:15,format=rgb24", "-f", "rawvideo", "-"], Ct, stdout: ms);
            var b = ms.ToArray();
            for (int i = 0; i < b.Length; i += 3) if (Dark((b[i], b[i + 1], b[i + 2]))) return true;
            return false;
        }
        var line1 = await Task.WhenAll(Enumerable.Range(1, 5).Select(k => Ink(k, 113)));
        var line2 = await Task.WhenAll(Enumerable.Range(1, 5).Select(k => Ink(k, 162)));
        var oval = (await Task.WhenAll(Enumerable.Range(1, 5).Select(k => Px(k, 760, 220)))).Select(Green).ToArray();
        var rect = (await Task.WhenAll(Enumerable.Range(1, 5).Select(k => Px(k, 760, 430)))).Select(Red).ToArray();
        Assert.Equal([false, true, true, true, true], line1);
        Assert.Equal([false, false, true, true, true], line2);
        Assert.Equal([false, false, false, true, true], oval);
        Assert.Equal([true, true, true, true, false], rect);
    }

    [Fact] // avoid_faces: a caption whose band a face (or the inset) covers moves to the nearest free height inside the safe zone
    public void Captions_move_off_faces_inside_the_safe_zone()
    {
        var p = Fixtures.Presets["P11"];
        var media = new MediaInfo { Duration = 20, Width = 1080, Height = 1920, Fps = 30 };
        var clip = new Clip { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 10, Reframe = new() { Mode = "fit" } };
        EditPlan Plan(double faceY) => new()
        {
            EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = [clip], Audio = new AudioSpec { Master = "m" },
            Telops = [new Telop { Id = "t1", Type = TelopType.Caption, Text = "字幕です", Start = 1, End = 3, Style = "Caption" }],
            TelopFaces = new() { ["m"] = new(media, Enumerable.Range(0, 21).Select(i => new FaceSample(i * 0.5, [new(0.3, faceY, 0.4, 0.2, 1)])).ToList()) },
        };
        var band = AssWriter.Band(TelopType.Caption, p, 1920)!.Value;
        var free = TelopLayout.Resolve(Plan(0.1), p)["t1"];
        Assert.False(free.Positioned);
        var low = Plan(0.55);
        var moved = TelopLayout.Resolve(low, p)["t1"];
        Assert.True(moved.Positioned);
        Assert.True(moved.Bounds!.Top >= QaEngine.SafeZone.Top && moved.Bounds.Bottom <= 1920 - QaEngine.SafeZone.Bottom, $"{moved.Bounds}");
        Assert.All(TelopLayout.Obstacles(low, low.Telops[0], out _), f => Assert.False(f.Intersects(moved.Bounds)));
        Assert.Equal(band.Bottom - band.Top, moved.Bounds.Bottom - moved.Bounds.Top, 3);
        Assert.Contains(@"\an5\pos(", AssWriter.Write(low, p, TelopLayout.Resolve(low, p)));
        Assert.False(TelopLayout.Resolve(low, p with { Telop = p.Telop with { Caption = p.Telop.Caption with { AvoidFaces = false } } })["t1"].Positioned);
    }

    [Fact]
    public void Pip_eligibility_handles_drift_gaps_and_missing_scenes_without_using_unapproved_frames()
    {
        var main = Asset("m", "m.mov", 100, 1080, 1920, AssetRole.Main);
        var screen = Asset("s", "s.mp4", 100, 1920, 1080, AssetRole.Screen, offset: -3, ppm: 200)
            with { PipScenes = [new(8, 15), new(30, 40)] };
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 50 } };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 5, 18, "スライド", 1, 0, 0), new(1, 34, 39, "説明", 1, 0, 0)] };
        var p = Fixtures.Presets["P11"];
        var result = PipPlanner.Plan(clips, tr, main, screen, p, new Canvas(), []);
        Assert.Equal(50, result.Sum(c => c.Length), 6);
        Assert.Contains(result, c => c.AssetId == "s");
        Assert.DoesNotContain(result, c => c.Pip != null);
        foreach (var c in result.Where(c => c.AssetId == "s"))
        {
            Assert.Contains(screen.PipScenes, s => c.SrcIn >= s.T0 - 1e-6 && screen.CameraTime(c.MasterOut, main.Id) <= s.T1 + 1e-6);
            Assert.Equal(screen.CameraTime(c.MasterIn, main.Id), c.SrcIn, 6);
        }
        Assert.Throws<PlanException>(() => PipPlanner.Plan(clips, tr, main, screen with { PipScenes = [] }, p, new Canvas(), []));
        Assert.Throws<PlanException>(() => PipPlanner.Plan(clips, tr with { Sentences = [] }, main, screen, p, new Canvas(), []));
        Assert.Throws<PlanException>(() => PipPlanner.Plan(clips, tr, main, screen with { PipScenes = [new(80, 90)] }, p, new Canvas(), []));
    }

    [Fact] // a meeting recording's delay steps (VoIP jitter buffer): the map follows the step; unrelated audio yields no map
    public void Sync_map_follows_a_stepped_delay_and_rejects_unrelated_audio()
    {
        const int r = AudioSync.FineRate;
        var rng = new Random(7);
        float[] Noise(double sec) => Enumerable.Range(0, (int)(sec * r)).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
        var master = Noise(30);
        // other = 1.0 s lead-in, master[0..15), 0.4 s of other sound, master[15..30): D = 1.0 before 15 s and 1.4 after
        float[] other = [.. Noise(1.0), .. master[..(15 * r)], .. Noise(0.4), .. master[(15 * r)..], .. Noise(2)];
        var anchors = AudioSync.Anchors(AudioSync.Trace(master, other, r, 0, 1.2), 30);
        Assert.True(anchors.Count >= 20, $"{anchors.Count} anchors");
        Assert.All(anchors.Where(a => a.T < 14), a => Assert.Equal(1.0, a.D, 3));
        Assert.All(anchors.Where(a => a.T > 16), a => Assert.Equal(1.4, a.D, 3));
        var cam = Asset("s", "s.mp4", 33, 1920, 1080, AssetRole.Screen) with { SyncMap = anchors };
        Assert.Equal(5 + 1.0, cam.CameraTime(5, "m"), 3);
        Assert.Equal(25 + 1.4, cam.CameraTime(25, "m"), 3);
        Assert.Single(cam.SyncJumps(0, 30));
        Assert.Empty(AudioSync.Anchors(AudioSync.Trace(master, Noise(33), r, 0, 1.2), 30));
    }

    [Fact] // the conversation picked for a PiP must overlap the chosen slides; with none, one is built around them on sentence bounds
    public void Pip_selection_keeps_scene_overlapping_candidates_or_builds_one_around_the_scenes()
    {
        var main = Asset("m", "m.mov", 34, 1080, 1920, AssetRole.Main);
        var screen = Asset("s", "s.mp4", 65, 1920, 1080, AssetRole.Screen) with { SyncMethod = "audio-map", SyncMap = [new(1, 3.0), new(20, 3.0), new(21, 3.6), new(33, 3.6)], PipScenes = [new(33.5, 39)] };
        var windows = PipPlanner.SceneWindows(main, screen);
        Assert.Equal(29.9, windows.Single().T0, 6);
        Assert.Equal(34, windows.Single().T1, 6);
        var p = Fixtures.Presets["P11"];
        var tr = new Transcript { Provider = "test", Sentences = [.. Enumerable.Range(0, 11).Select(i => new Sentence(i, i * 3 + 0.2, i * 3 + 2.9, $"文{i}", 1, 0, 0))] };
        Candidate Cand(string id, double t0, double t1) => new() { Id = id, Segments = [new(t0, t1)], Rank = 1 };
        var (kept, built) = PipPlanner.PreferScenes([Cand("early", 0, 20), Cand("late", 15, 33)], windows, tr, new SignalSet(), p, 34);
        Assert.False(built);
        Assert.Equal("late", kept.Single().Id);
        (kept, built) = PipPlanner.PreferScenes([Cand("early", 0, 20)], windows, tr, new SignalSet(), p, 34);
        Assert.True(built);
        var seg = kept.Single().Segments.Single();
        Assert.Contains(tr.Sentences, s => s.T0 == seg.T0);
        Assert.True(seg.T0 <= windows[0].T0 - p.Screen!.HookPresenterSec, $"opens on the presenter: {seg}");
        Assert.InRange(seg.Length, p.Target.Min, p.Target.Max);
        Assert.True(seg.T1 >= 32.9);
    }

    [Fact] // a piece must not span a delay step, and every piece starts the screen at its own local offset; the meeting chrome is cropped away
    public void Pip_pieces_split_at_sync_steps_and_show_only_the_content_box()
    {
        var main = Asset("m", "m.mov", 30, 1080, 1920, AssetRole.Main);
        var content = new PixelRect(60, 120, 1560, 870);
        var screen = Asset("s", "s.mp4", 40, 1920, 1080, AssetRole.Screen) with
        {
            SyncMethod = "audio-map", SyncMap = [new(1, 3.0), new(10, 3.0), new(11, 3.6), new(25, 3.6)], PipScenes = [new(0, 40)], PipContent = content,
        };
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 20 } };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 0, 20, "説明", 1, 0, 0)] };
        var result = PipPlanner.Plan(clips, tr, main, screen, Fixtures.Presets["P11"], new Canvas(), []);
        Assert.Equal(20, result.Sum(c => c.Length), 6);
        var shown = result.Where(c => c.AssetId == "s").ToList();
        Assert.NotEmpty(shown);
        Assert.DoesNotContain(shown, c => c.MasterIn < 10.5 - 1e-6 && c.MasterOut > 10.5 + 1e-6);
        foreach (var c in shown)
        {
            Assert.Equal(c.MasterIn + (c.MasterIn < 10.5 ? 3.0 : 3.6), c.SrcIn, 6);
            Assert.Equal((content.W, content.H, content.Y, content.X), (c.Reframe.CropW, c.Reframe.CropH, c.Reframe.CropY, (int)c.Reframe.Track![0].X));
        }
        var faces = Enumerable.Range(0, 60).Select(i => new FaceSample(i * 0.5, [new(0.35, 0.45, 0.3, 0.2, 1)])).ToList();
        var pip = PipPlanner.PlaceInset(clips[0] with { Reframe = new() { Mode = "fit" } }, main, screen, faces, Fixtures.Presets["P11"], new Canvas())?.Pip;
        Assert.NotNull(pip);
        Assert.Equal((content.X, content.Y, content.W, content.H), (pip.CropX, pip.CropY, pip.CropW, pip.CropH));
        Assert.InRange(pip.Width / (double)pip.Height, content.W / (double)content.H - 0.01, content.W / (double)content.H + 0.01);
    }

    [Fact] // Teams-style frame: a slide with dark text lines, black margins, and a participant tile at the side
    public void Screen_content_box_excludes_black_bars_and_the_participant_tile()
    {
        var g = new byte[640 * 360];
        Array.Fill(g, (byte)12);
        for (int y = 40; y < 330; y++) for (int x = 20; x < 540; x++) g[y * 640 + x] = (byte)(y % 30 < 3 ? 15 : 220);
        for (int y = 150; y < 210; y++) for (int x = 560; x < 630; x++) g[y * 640 + x] = 160;
        var b = ScreenScenes.ContentBox(g);
        Assert.NotNull(b);
        Assert.Equal(20 / 640.0, b.Value.X0, 2);
        Assert.Equal(540 / 640.0, b.Value.X1, 2);
        Assert.Equal(40 / 360.0, b.Value.Y0, 2);
        Assert.Equal(330 / 360.0, b.Value.Y1, 2);
        Assert.Null(ScreenScenes.ContentBox(new byte[640 * 360]));
    }

    [Fact]
    public void Screen_scenes_split_global_changes_not_pointer_motion_and_validate_editable_ranges()
    {
        var analysis = new ScreenAnalysis(
            [new(0.5, 0.001, 0, 0, 0.1, 0.1), new(5, 0.8, 0, 0, 1, 1), new(5.5, 0.4, 0, 0, 1, 1), new(12, 0.9, 0, 0, 1, 1)], []);
        Assert.Equal([new Interval(0, 5), new(5, 12), new(12, 20)], ScreenScenes.Detect(analysis, 20));
        Assert.True(ScreenScenes.Valid([new(5, 7), new(10, 15)], 20));
        Assert.False(ScreenScenes.Valid([new(5, 10), new(9, 15)], 20));
        Assert.False(ScreenScenes.Valid([new(5, 21)], 20));
        Assert.False(ScreenScenes.Valid([new(double.NaN, 10)], 20));
    }

    [Fact]
    public void Pip_placement_avoids_the_whole_moving_face_envelope_and_captions_or_switches()
    {
        var main = Asset("m", "m.mov", 20, 1080, 1920, AssetRole.Main);
        var screen = Asset("s", "s.mp4", 20, 1920, 1080, AssetRole.Screen);
        var clip = new Clip { ClipId = "c", AssetId = "m", SrcIn = 2, SrcOut = 8, Reframe = new() { Mode = "fit" } };
        var p = Fixtures.Presets["P11"];
        var canvas = new Canvas();
        var faces = Enumerable.Range(3, 15).Select(i => new FaceSample(i * 0.5, [new(0.1 + i * 0.01, 0.2, 0.4, 0.25, 1)])).ToList();
        var layout = PipPlanner.PlaceInset(clip, main, screen, faces, p, canvas);
        Assert.NotNull(layout);
        var pip = layout.Pip;
        var shown = clip with { Reframe = layout.Main };
        var bounds = new TelopLayout.Rect(pip.X, pip.Y, pip.X + pip.Width, pip.Y + pip.Height);
        foreach (var sample in faces)
        foreach (var face in sample.Boxes)
            Assert.False(bounds.Intersects(TelopLayout.Project(face, shown, main.Media, canvas, sample.T - clip.SrcIn)));
        var band = AssWriter.Band(TelopType.Caption, p, canvas.Height)!.Value;
        Assert.False(bounds.Intersects(new(0, band.Top, canvas.Width, band.Bottom)));
        Assert.InRange(pip.Width / (double)pip.Height, screen.Media.Aspect - 0.01, screen.Media.Aspect + 0.01);
        Assert.True(pip.Width >= canvas.Width * 0.72 - 2);
        Assert.Null(PipPlanner.PlaceInset(clip, main, screen, faces.Where((_, i) => i % 2 == 0).ToList(), p, canvas));
        var fullFrame = faces.Select(s => s with { Boxes = [new(0, 0, 1, 1, 1)] }).ToList();
        Assert.Null(PipPlanner.PlaceInset(clip, main, screen, fullFrame, p with { Screen = p.Screen! with { MainMinScale = 1 } }, canvas));
        var below = PipPlanner.PlaceInset(clip, main, screen, fullFrame, p, canvas);   // only a shrunk main leaves room, below it
        Assert.True(below!.Main.FgScale < 1 && below.Pip.Y >= (int)(canvas.Width * below.Main.FgScale) * 16 / 9);
        Assert.Null(PipPlanner.PlaceInset(clip, main, screen, null, p, canvas));
        // A low face leaves no room below or above at full width, but a smaller inset still fits above it.
        var low = faces.Select(s => s with { Boxes = [new(0.2, 0.3, 0.6, 0.6, 1)] }).ToList();
        var small = PipPlanner.PlaceInset(clip, main, screen, low, p with { Screen = p.Screen! with { MainMinScale = 1 } }, canvas);
        Assert.NotNull(small);
        Assert.True(small.Pip.Width < pip.Width);
        Assert.Equal("fit", small.Main.Mode);
    }

    [Fact] // a face filling the lower half: full size leaves only a small inset, so the main shrinks (top-anchored) and a large inset fits below
    public void Pip_main_shrinks_for_a_low_face_and_the_inset_and_captions_clear_it()
    {
        var main = Asset("m", "m.mov", 20, 1080, 1920, AssetRole.Main);
        var screen = Asset("s", "s.mp4", 20, 1920, 1080, AssetRole.Screen);
        var clip = new Clip { ClipId = "c", AssetId = "m", SrcIn = 2, SrcOut = 8, Reframe = new() { Mode = "fit" } };
        var p = Fixtures.Presets["P11"];
        var canvas = new Canvas();
        var faces = Enumerable.Range(3, 15).Select(i => new FaceSample(i * 0.5, [new(0.25, 0.3, 0.5, 0.42, 1)])).ToList();
        var full = PipPlanner.PlaceInset(clip, main, screen, faces, p with { Screen = p.Screen! with { MainMinScale = 1 } }, canvas);
        var layout = PipPlanner.PlaceInset(clip, main, screen, faces, p, canvas);
        Assert.NotNull(layout);
        Assert.Equal(("blur_bg", 0.72, 0), (layout.Main.Mode, layout.Main.FgScale, layout.Main.FgOffsetY));
        Assert.True(full == null || layout.Pip.Width > full.Pip.Width, $"{full?.Pip.Width} vs {layout.Pip.Width}");
        var shown = clip with { Reframe = layout.Main };
        var box = new TelopLayout.Rect(layout.Pip.X, layout.Pip.Y, layout.Pip.X + layout.Pip.Width, layout.Pip.Y + layout.Pip.Height);
        var band = AssWriter.Band(TelopType.Caption, p, canvas.Height)!.Value;
        foreach (var f in faces.SelectMany(s => s.Boxes.Select(b => TelopLayout.Project(b, shown, main.Media, canvas, s.T - clip.SrcIn))))
        {
            Assert.False(box.Intersects(f));
            Assert.False(f.Intersects(new(0, band.Top, canvas.Width, band.Bottom)), $"face {f} vs caption band");
        }
    }

    [Fact]
    public void Pip_introduction_uses_output_time_even_when_a_teaser_precedes_an_earlier_source_range()
    {
        var main = Asset("m", "main.mp4", 30, 1080, 1920, AssetRole.Main);
        var screen = Asset("s", "screen.mp4", 30, 1920, 1080, AssetRole.Screen, offset: 2) with { PipScenes = [new(2, 8)] };
        var clips = new List<Clip>
        {
            new() { ClipId = "teaser", AssetId = "m", SrcIn = 20, SrcOut = 22 },
            new() { ClipId = "body", AssetId = "m", SrcIn = 0, SrcOut = 10 },
        };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 0, 6, "説明", 1, 0, 0)] };
        var result = PipPlanner.Plan(clips, tr, main, screen, Fixtures.Presets["P11"], new Canvas(), []);
        Assert.Equal("m", result[0].AssetId);
        Assert.Equal(0, result[1].MasterIn);
        Assert.Equal("s", result[1].AssetId);
        Assert.Equal(12, result.Sum(c => c.Length), 6);
    }

    [Fact] // the picture roles and the audio source are separate: a screen recording with the clearest sound can be the time spine while camera A is the speaker
    public void Pip_with_a_screen_and_camera_a_only_uses_the_chosen_audio_source_as_the_time_spine()
    {
        var screen = Asset("s", "s.mp4", 40, 1920, 1080, AssetRole.Screen) with { AudioRef = true };
        var cam = Asset("a", "a.mov", 40, 1080, 1920, AssetRole.CamA, offset: 2);
        var assets = new[] { cam, screen };
        Assert.Equal("s", AssetRoles.Master(assets)!.Id);
        Assert.Equal("a", AssetRoles.Presenter(assets)!.Id);
        Assert.Equal("a", AssetRoles.Master([cam, screen with { AudioRef = false }])!.Id);   // no mark: the first one, as before
        var auto = ScreenScenes.Auto(new ScreenAnalysis(
            [.. Enumerable.Range(0, 60).Where(i => i is >= 10 and < 14 or >= 50 and < 52).Select(i => new ActivitySample(i / 2.0, 0.01, 0, 0, 0.2, 0.2))], []), 40);
        Assert.Equal([new Interval(4.5, 12.5), new Interval(24.5, 31.5)], auto);   // bursts of change, each held 6 s after its last change
        screen = screen with { AutoPipScenes = auto };
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "s", SrcIn = 0, SrcOut = 36 } };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 0, 36, "説明", 1, 0, 0)] };
        var faces = Enumerable.Range(0, 80).Select(i => new FaceSample(i * 0.5, [LayoutPreview.TypicalFace])).ToList();
        var result = PipPlanner.Plan(clips, tr, screen, cam, [screen], Fixtures.Presets["P11"], new Canvas(), [], faces);
        Assert.Equal(36, result.Sum(c => c.Length), 6);
        Assert.All(result.Where(c => c.AssetId == "a"), c => Assert.Equal(c.AudioIn!.Value + 2, c.SrcIn, 6));   // the camera is read at master time + its offset
        Assert.Contains(result, c => c.Pip is { AssetId: "s" });
        Assert.Equal("a", result[0].AssetId);   // the speaker opens the video
        Assert.All(result.Where(c => c.Pip != null), c => Assert.Equal("a", c.AssetId));
        Assert.DoesNotContain(result, c => c.AssetId == "s" || c.Pip != null && c.MasterIn is > 12.6 and < 23.5);   // between the bursts the speaker is shown alone
    }

    [Fact] // any number of screens: each shows in its own scenes, and where they overlap the one already showing keeps the frame
    public void Pip_shows_several_screens_one_at_a_time()
    {
        var main = Asset("m", "m.mov", 60, 1080, 1920, AssetRole.Main);
        var s1 = Asset("s1", "s1.mp4", 60, 1920, 1080, AssetRole.Screen) with { PipScenes = [new(4, 20)] };
        var s2 = Asset("s2", "s2.mp4", 60, 1920, 1080, AssetRole.Screen) with { PipScenes = [new(14, 40)] };
        var resolved = PipPlanner.Resolve([(new Interval(4, 20), 0), (new Interval(14, 40), 1)]);
        Assert.Equal([(new Interval(4, 20), 0), (new Interval(20, 40), 1)], resolved);
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 50 } };
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 0, 50, "説明", 1, 0, 0)] };
        var result = PipPlanner.Plan(clips, tr, main, main, [s1, s2], Fixtures.Presets["P11"], new Canvas(), []);
        var shown = result.Select(c => c.Pip?.AssetId ?? (c.AssetId is "s1" or "s2" ? c.AssetId : null)).Where(x => x != null).Distinct().ToList();
        Assert.Equal(["s1", "s2"], shown);
    }


    [Fact] // one small typed character (3×5 px at analysis size) is activity; a blinking caret (toggling back) is not
    public void Screen_activity_sees_typing_but_not_a_blinking_caret()
    {
        int W = ScreenActivity.W, H = ScreenActivity.H;
        var sink = new ScreenActivity.FrameSink();
        var frame = new byte[W * H];
        for (int k = 0; k < 20; k++)
        {
            Array.Fill(frame, (byte)224);
            if (k % 2 == 1) for (int y = 100; y < 107; y++) for (int x = 50; x < 52; x++) frame[y * W + x] = 0;          // caret, on every other step
            for (int c = 0; c < Math.Max(0, k - 9); c++) for (int y = 200; y < 205; y++) for (int x = 300 + c * 6; x < 303 + c * 6; x++) frame[y * W + x] = 20;   // one char per step from step 10
            sink.Write(frame, 0, frame.Length);
        }
        var s = sink.Samples;
        Assert.All(s.Where(x => x.T is >= 1 and < 5), x => Assert.False(x.Active, $"caret counted at t={x.T}: {x.Changed}"));
        Assert.All(s.Where(x => x.T >= 5), x => Assert.True(x.Active && x.Y0 > 0.5 && x.X0 >= 0.45, $"typing missed at t={x.T}: {x}"));
    }

    [Fact] // a slide change says nothing about where; the PiP goes to the corner with the least detail (it must not cover the slide's text)
    public void Pip_avoids_the_busy_part_of_a_slide()
    {
        var pres = Asset("a1", "pres.mp4", 60, 1280, 720, AssetRole.Main);
        var scr = Asset("a2", "scr.mp4", 60, 1920, 1080);
        var act = Enumerable.Range(1, 120).Select(i => i / 2.0).Select(t => t == 10 ? new ActivitySample(t, 0.9, 0, 0, 1, 1) : new ActivitySample(t, 0, 0, 0, 0, 0)).ToList();
        var grid = new byte[ScreenActivity.GridW * ScreenActivity.GridH];
        for (int gy = 4; gy < 9; gy++) for (int gx = 0; gx < 6; gx++) grid[gy * ScreenActivity.GridW + gx] = 200;   // text in the lower left
        var details = Enumerable.Range(0, 31).Select(i => new DetailSample(i * 2.0, grid)).ToList();
        var notes = new List<string>();
        var tl = ScreenPlanner.Plan([new Clip { ClipId = "s", AssetId = "a1", SrcIn = 5, SrcOut = 20, AudioIn = 5 }],
            new Transcript { Provider = "t", Duration = 60, Sentences = [new Sentence(0, 5, 20, "このスライドを見てください", 1, 0, 0)] },
            [pres, scr], pres, new ScreenContext("a2", "a1", act, details), new Dictionary<string, List<FaceSample>>(), Fixtures.Presets["P10"], new Canvas(), notes);
        var shot = tl.First(c => c.Pip != null);
        Assert.Equal(0, shot.Reframe.CropW);                       // a whole-screen change is shown whole
        Assert.NotEqual("bl", shot.Pip!.Corner);
        Assert.NotEqual("tl", shot.Pip.Corner);
    }

    [Fact] // the screen band sits between the headline and the emphasis telops; the PiP fits in it
    public void Screen_layout_keeps_telops_off_the_screen()
    {
        var p = Fixtures.Presets["P10"];
        var lay = ScreenLayout.For(new Canvas(), p);
        Assert.True(lay.BoxY >= AssWriter.HeadlineMarginV + 60, $"box top {lay.BoxY}");
        int emphTop = 1920 - (p.Telop.Caption.MarginV + AssWriter.EmphLift) - (int)(p.Telop.Caption.FontPx * 1.25);
        Assert.True(lay.BoxY + lay.BoxH <= emphTop, $"box bottom {lay.BoxY + lay.BoxH} vs emphasis {emphTop}");
        foreach (var k in ScreenLayout.Corners)
        {
            var (x, y) = lay.Corner(k);
            Assert.True(x >= 0 && x + lay.Ring <= lay.W && y >= lay.BoxY && y + lay.Ring <= lay.BoxY + lay.BoxH, k);
        }
    }

    [Fact] // series plans give every block of clips its own seeked input; the master-audio and PiP chains must resolve to the real assets
    public void Series_inputs_resolve_audio_and_pip_blocks()
    {
        var a1 = Asset("a1", "pres.mp4", 600, 1280, 720, AssetRole.Main);
        var a2 = Asset("a2", "scr.mp4", 600, 1920, 1080, offset: -2.5);
        var tl = new List<Clip>
        {
            new() { ClipId = "c1", AssetId = "a1", SrcIn = 10, SrcOut = 20, AudioIn = 10 },
            new() { ClipId = "c2", AssetId = "a2", SrcIn = 17.5, SrcOut = 40, AudioIn = 20, Reframe = new ReframeSpec { Mode = "screen", BoxY = 412, BoxH = 652 },
                Pip = new PipSpec { AssetId = "a1", SrcIn = 20, CropS = 600, D = 270, X = 24, Y = 770 } },
        };
        var plan = new EditPlan
        {
            EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P10"].Ref, Timeline = tl, Audio = new AudioSpec { Master = "a1" },
            Series = new SeriesInfo { Label = "前編", Body = new Interval(10, 42) },
        };
        var inp = new Renderer(Ff).ResolveInputs(plan, new Dictionary<string, SourceAsset> { ["a1"] = a1, ["a2"] = a2 });
        Assert.Equal(-1, inp.Pip[0].Index);
        Assert.True(inp.Pip[1].Index >= 0 && inp.Args[inp.Pip[1].Index].Contains("pres.mp4"));
        Assert.Contains("pres.mp4", inp.Args[inp.Audio[1].Index]);
        var graph = Renderer.BuildGraph(plan, inp, null, ".", true, null);
        Assert.Contains("alphamerge", graph);
    }

    [Fact]
    public void Slide_changes_inside_one_inset_are_not_short_shots()
    {
        var p = Fixtures.Presets["P11"];
        var pip = new PipSpec { AssetId = "scr", Shape = "rect", X = 24, Y = 1326, Width = 1014, Height = 570 };
        Clip C(string id, double a, double b, PipSpec? pi) => new() { ClipId = id, AssetId = "m", SrcIn = a, SrcOut = b, AudioIn = a, Pip = pi };
        var tr = new Transcript { Provider = "test", Duration = 40 };
        QaResult S04(params Clip[] t) => QaEngine.PlanChecks(new QaContext(new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = [.. t], Audio = new AudioSpec { Master = "m" } }, p, tr, ["m", "scr"], [])).Single(q => q.CheckId == "S-04");

        // one continuous shot whose inset only changes slide image: 0.7 + 1.5 + 0.9 s
        var slides = S04(C("a", 0, 10, null), C("b", 10, 10.7, pip with { Image = "s1.png" }), C("c", 10.7, 12.2, pip with { Image = "s2.png" }), C("d", 12.2, 13.1, pip with { Image = "s3.png" }));
        Assert.Equal(QaStatus.Pass, slides.Status);
        // a real cut (source jump) or a moved inset is still a new shot
        Assert.Equal(QaStatus.Warn, S04(C("a", 0, 10, pip), C("b", 15, 15.7, pip), C("c", 20, 30, pip)).Status);
        Assert.Equal(QaStatus.Warn, S04(C("a", 0, 10, pip), C("b", 10, 10.7, pip with { Y = 100 }), C("c", 10.7, 20, pip)).Status);
    }

    [Fact] // the UI's layout sketch comes from the planners' own geometry, so it shows where a run puts each video
    public void Layout_preview_uses_the_planner_geometry_and_flags_role_problems()
    {
        var p = Fixtures.Presets["P11"];
        var canvas = new Canvas();
        var main = Asset("m", "m.mov", 20, 1080, 1920, AssetRole.Main);
        var screen = Asset("s", "s.mp4", 20, 1920, 1080, AssetRole.Screen) with { PipScenes = [new(2, 8)] };
        var lp = LayoutPreview.For(p, [main, screen]);
        Assert.Equal(["hook", "pip", "talk", "fallback"], lp.Beats.Select(b => b.Kind));
        Assert.Empty(lp.Issues);
        Assert.Equal("typical", lp.FaceBasis);
        var faces = Enumerable.Range(0, 9).Select(i => new FaceSample(i * 0.5, [LayoutPreview.TypicalFace])).ToList();
        var clip = new Clip { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 4, Reframe = new() { Mode = "fit" } };
        var expected = PipPlanner.PlaceInset(clip, main, screen, faces, p, canvas)!.Pip;
        var inset = lp.Beats[1].Boxes.Single(b => b.Slot == "screen");
        Assert.Equal(("s", expected.X, expected.Y, expected.Width, expected.Height), (inset.AssetId, inset.X, inset.Y, inset.W, inset.H));
        Assert.False(inset.Y < lp.Captions!.Y + lp.Captions.H && inset.Y + inset.H > lp.Captions.Y, "inset overlaps the caption band");
        Assert.Equal("s", lp.Beats[3].Boxes.Last().AssetId);   // a slide the inset cannot hold switches to the screen full-frame

        // measured faces (cached detection) replace the assumption: a low face makes the planner shrink the main video
        var low = Enumerable.Range(0, 9).Select(i => new FaceSample(i * 0.5, [new FaceBox(0.25, 0.3, 0.5, 0.42, 1)])).ToList();
        var measured = LayoutPreview.For(p, [main, screen], low);
        Assert.Equal("measured", measured.FaceBasis);
        var shrunk = measured.Beats[1].Boxes.Last(b => b.Slot == "main" && !b.Backdrop);
        Assert.True(shrunk.W < canvas.Width && measured.Beats[1].Boxes.Any(b => b.Backdrop), $"{shrunk}");

        // role problems the pipeline would hit
        Assert.Contains("need_screen", LayoutPreview.For(p, [main, screen with { Role = AssetRole.CamB }]).Issues);
        Assert.DoesNotContain("no_scenes", LayoutPreview.For(p, [main, screen with { PipScenes = [] }]).Issues);   // scenes are chosen automatically
        Assert.Empty(LayoutPreview.For(p, [main with { Role = AssetRole.CamA }, screen]).Issues);                  // camera A is a speaker too
        Assert.Contains("need_presenter", LayoutPreview.For(p, [main with { Role = AssetRole.Wide }, screen]).Issues);
        Assert.Contains("implicit_main", LayoutPreview.For(Fixtures.Presets["P7"], [main with { Role = AssetRole.CamA }, screen]).Issues);
        Assert.Contains("only_main", LayoutPreview.For(Fixtures.Presets["P1"], [main, screen]).Issues);

        // P10: the screen in its band and the presenter as the bottom-left circle
        var p10 = Fixtures.Presets["P10"];
        var sc = LayoutPreview.For(p10, [main, screen]);
        var lay = ScreenLayout.For(canvas, p10);
        var (cx, cy) = lay.Corner("bl");
        var wipe = sc.Beats.Single(b => b.Kind == "screen").Boxes.Single(b => b.Shape == "circle");
        Assert.Equal(("m", cx, cy, lay.Ring), (wipe.AssetId, wipe.X, wipe.Y, wipe.W));

        // P7 without speakers: the cameras' microphones decide, shown as one beat per camera
        var sw = LayoutPreview.For(Fixtures.Presets["P7"], [main, Asset("b", "b.mov", 20, 1080, 1920)]);
        Assert.Contains("auto_speaker", sw.Issues);
        Assert.Equal(["m", "b"], sw.Beats.Where(b => b.Kind == "cam").Select(b => b.Boxes.Last().AssetId));
    }
}
