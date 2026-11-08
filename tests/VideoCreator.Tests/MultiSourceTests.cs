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
}
