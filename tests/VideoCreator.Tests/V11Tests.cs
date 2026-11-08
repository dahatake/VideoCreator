using System.Globalization;
using System.Text.RegularExpressions;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>v1.1 features verified with real FFmpeg / ONNX Runtime on synthetic media: BGM+SFX, face-follow, multi-camera, speed ramps, beat sync, selection scope.</summary>
public class V11Tests : IAsyncLifetime
{
    static readonly Transcript Tr = Fixtures.Talk;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-v11-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static SourceAsset Asset(string id, string path, double dur, AssetRole role, double offset = 0, int w = 640, int h = 360, int? speaker = null) => new()
    {
        Id = id, ProjectId = "p", Path = path, Sha256 = "x", Role = role, SyncOffset = offset, Speaker = speaker,
        Media = new MediaInfo { Duration = dur, Width = w, Height = h, Fps = 30, AudioChannels = 1 },
    };

    async Task<(int r, int g, int b)> PixelAsync(string file, double t)
    {
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", t.ToString("0.###", IC), "-i", file, "-frames:v", "1", "-vf", "scale=1:1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], Ct, stdout: ms);
        var b = ms.ToArray(); return (b[0], b[1], b[2]);
    }

    async Task<double> RmsDbAsync(string file, double t0, double t1, string filter)
    {
        var r = await Ff.RunFfmpegAsync(["-hide_banner", "-ss", t0.ToString("0.###", IC), "-t", (t1 - t0).ToString("0.###", IC), "-i", file, "-af", $"{filter}astats=metadata=0", "-f", "null", "-"], Ct);
        var m = Regex.Matches(r.StdErr, @"RMS level dB:\s*(-?[\d.]+|-inf)");
        return m.Count == 0 ? double.NegativeInfinity : double.Parse(m[^1].Groups[1].Value.Replace("-inf", "-999"), IC);
    }

    async Task<short[]> RenderAudioOnlyAsync(EditPlan plan, Dictionary<string, SourceAsset> assets, AudioLibrary lib, string name)
    {
        await lib.EnsureAsync(Ct);
        var r = new Renderer(Ff) { BgmResolver = id => lib.Find(id)?.Path, SfxResolver = lib.SfxPath };
        var inp = r.ResolveInputs(plan, assets);
        var wav = Path.Combine(_dir, name + ".wav");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", .. inp.Flat, "-filter_complex", Renderer.BuildAudioOnlyGraph(plan, inp, "aresample=48000,aformat=channel_layouts=mono"), "-map", "[m]", "-ar", "48000", "-c:a", "pcm_s16le", wav], Ct);
        var bytes = await File.ReadAllBytesAsync(wav, Ct);
        var pcm = new short[(bytes.Length - 44) / 2]; Buffer.BlockCopy(bytes, 44, pcm, 0, pcm.Length * 2); return pcm;
    }

    static double RmsDb(short[] pcm, double t0, double t1)
    {
        int a = (int)(t0 * 48000), b = Math.Min(pcm.Length, (int)(t1 * 48000)); double s = 0;
        for (int i = a; i < b; i++) { double v = pcm[i] / 32768.0; s += v * v; }
        return 10 * Math.Log10(s / Math.Max(1, b - a) + 1e-12);
    }

    // ---------- audio: BGM library, tempo, ducking, SFX ----------

    [Fact] // FR-AUD-05: the library is synthesised; tempo/phase estimation recovers the known BPM
    public async Task Synthesised_bgm_tempo_is_estimated_within_half_a_bpm()
    {
        var lib = new AudioLibrary(Ff, Path.Combine(_dir, "audio"));
        await lib.EnsureAsync(Ct);
        foreach (var spec in AudioLibrary.TrackSpecs)
        {
            var (bpm, phase) = await TempoEstimator.AnalyzeFileAsync(Ff, lib.TrackPath(spec.Id), Ct);
            Assert.InRange(bpm, spec.Bpm - 0.1, spec.Bpm + 0.1);
            double beat = 60.0 / spec.Bpm, ph = phase % beat;
            Assert.True(Math.Min(ph, beat - ph) <= 0.02, $"{spec.Id}: phase {phase:F3}s");
        }
        // a track with an arbitrary tempo (what a user would bring): 120 BPM sped up by 6.67 % -> 128 BPM
        var odd = Path.Combine(_dir, "odd.wav");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-stream_loop", "3", "-i", lib.TrackPath("upbeat"), "-af", "atempo=1.066667", odd], Ct);
        var (b2, _) = await TempoEstimator.AnalyzeFileAsync(Ff, odd, Ct);
        Assert.InRange(b2, 127.85, 128.15);
    }

    EditPlan DialoguePlan(string asset, double dur, AudioSpec audio, Preset p) => new()
    {
        EditplanId = "e", ProjectId = "p", Preset = p.Ref, Audio = audio,
        Timeline = [new Clip { ClipId = "c1", AssetId = asset, SrcIn = 0, SrcOut = dur }],
    };

    [Fact] // FR-AUD-03: sidechain ducking ≥ 12 dB while dialogue plays, recovers afterwards
    public async Task Bgm_is_ducked_under_dialogue_by_at_least_12_dB()
    {
        var lib = new AudioLibrary(Ff, Path.Combine(_dir, "audio"));
        var src = Path.Combine(_dir, "dlg.mp4");
        // dialogue proxy: 1 kHz tone 0–5 s, silence 5–10 s
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=30:d=10", "-f", "lavfi", "-i", "sine=f=1000:d=5,volume=0.8,apad=whole_dur=10", "-shortest", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", src], Ct);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = Asset("a1", src, 10, AssetRole.Main) };
        var p = Fixtures.Presets["P1"];
        var ducked = await RenderAudioOnlyAsync(DialoguePlan("a1", 10, new AudioSpec { Master = "a1", Bgm = "upbeat", BgmGainDb = -12, Duck = true, SfxEnabled = false }, p), assets, lib, "duck");
        var plain = await RenderAudioOnlyAsync(DialoguePlan("a1", 10, new AudioSpec { Master = "a1", Bgm = "upbeat", BgmGainDb = -12, Duck = false, SfxEnabled = false }, p), assets, lib, "noduck");
        // remove the 1 kHz dialogue tone so only the music bed is compared (window 1.5–4.5 s, after the 350 ms release has settled in the silent half)
        string Notch(string wav) => wav;
        double Bed(short[] pcm, double a, double b) => RmsDb(pcm, a, b);
        var dlgOnly = await RenderAudioOnlyAsync(DialoguePlan("a1", 10, new AudioSpec { Master = "a1", Bgm = null, SfxEnabled = false }, p), assets, lib, "dlg");
        // music-in-dialogue ≈ mixture power minus dialogue power (power subtraction)
        double Music(short[] mix, double a, double b) { double pm = Math.Pow(10, RmsDb(mix, a, b) / 10), pd = Math.Pow(10, RmsDb(dlgOnly, a, b) / 10); return 10 * Math.Log10(Math.Max(1e-12, pm - pd)); }
        double depth = Music(plain, 1.5, 4.5) - Music(ducked, 1.5, 4.5);
        Assert.True(depth >= 12, $"ducking depth {depth:F1} dB");
        // recovery: in the silent half the bed is back to its un-ducked level (±3 dB) after the release
        Assert.InRange(Bed(ducked, 7, 9.5) - Bed(plain, 7, 9.5), -3, 3);
    }

    [Fact] // FR-AUD-06: SFX are placed on the requested time (±10 ms) and BGM mutes work
    public async Task Sfx_lands_on_time_and_bgm_mute_silences_the_bed()
    {
        var lib = new AudioLibrary(Ff, Path.Combine(_dir, "audio"));
        var src = Path.Combine(_dir, "silent.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=30:d=8", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=mono:d=8", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", src], Ct);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = Asset("a1", src, 8, AssetRole.Main) };
        var p = Fixtures.Presets["P1"];
        var plan = DialoguePlan("a1", 8, new AudioSpec { Master = "a1", Sfx = [new SfxEvent("ding", 3.0), new SfxEvent("boom", 6.0)] }, p);
        var pcm = await RenderAudioOnlyAsync(plan, assets, lib, "sfx");
        int FirstLoud(int from) { for (int i = from; i < pcm.Length; i++) if (Math.Abs(pcm[i]) > 800) return i; return -1; }
        Assert.InRange(FirstLoud((int)(2.5 * 48000)) / 48000.0, 3.0, 3.010);
        Assert.InRange(FirstLoud((int)(5.5 * 48000)) / 48000.0, 6.0, 6.010);

        var withBgm = plan with { Audio = plan.Audio with { Bgm = "chill", Sfx = [], BgmMutes = [new Interval(3.0, 5.0)] } };
        var bed = await RenderAudioOnlyAsync(withBgm, assets, lib, "mute");
        Assert.True(RmsDb(bed, 1.0, 2.5) > -50 && RmsDb(bed, 3.2, 4.8) < -80, $"bed {RmsDb(bed, 1.0, 2.5):F1} dB, muted {RmsDb(bed, 3.2, 4.8):F1} dB");
    }

    // ---------- multi-camera sync + switching ----------

    [Fact] // FR-SWT-01: GCC-PHAT recovers a known delay within 1 ms and rejects unrelated audio
    public void Audio_sync_recovers_known_delay_and_rejects_unrelated_audio()
    {
        var rnd = new Random(5); int rate = 8000;
        var a = Enumerable.Range(0, rate * 20).Select(_ => (float)(rnd.NextDouble() * 2 - 1)).ToArray();
        int d = (int)(1.234 * rate);
        var b = new float[a.Length]; Array.Copy(a, 0, b, d, a.Length - d);
        var res = AudioSync.Gcc(a, b, rate, 10);
        Assert.InRange(res.Delay, 1.233, 1.235);
        Assert.True(res.Reliable, $"confidence {res.Confidence:F0}");
        var other = Enumerable.Range(0, rate * 20).Select(_ => (float)(rnd.NextDouble() * 2 - 1)).ToArray();
        Assert.False(AudioSync.Gcc(a, other, rate, 10).Reliable);
    }

    [Fact] // AT: P7 — the camera on screen is the speaker's; sync from audio; output frames come from the right camera
    public async Task Multicam_plan_shows_the_speakers_camera_and_stays_in_sync()
    {
        string Noise(string name, string video, int delayMs)
        {
            var f = Path.Combine(_dir, name + ".mp4");
            Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"color=c={video}:s=640x360:r=30:d=140", "-f", "lavfi", "-i", $"anoisesrc=c=pink:r=48000:d=138:seed=11,volume=0.5,adelay={delayMs}|{delayMs},apad=whole_dur=140",
                "-shortest", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", f], Ct).GetAwaiter().GetResult();
            return f;
        }
        string red = Noise("camA", "0xDC1E1E", 0), blue = Noise("camB", "0x1E1EDC", 2000), green = Noise("wide", "0x1EDC1E", 500);
        var s2 = await AudioSync.EstimateAsync(Ff, red, blue, Ct); var s3 = await AudioSync.EstimateAsync(Ff, red, green, Ct);
        Assert.InRange(s2.Delay, 1.999, 2.001); Assert.True(s2.Reliable);
        Assert.InRange(s3.Delay, 0.499, 0.501);

        var p = Fixtures.Presets["P7"];
        var assets = new List<SourceAsset> { Asset("a1", red, 140, AssetRole.Main), Asset("a2", blue, 140, AssetRole.CamB, s2.Delay), Asset("a3", green, 140, AssetRole.Wide, s3.Delay) };
        var cand = new Candidate { Id = "c1", Segments = [new Interval(0.1, 62)] };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P7"] };
        var (plan, notes) = await PlanBuilder.BuildAsync(new PlanRequest(cand, Tr, Fixtures.Signals(Tr), p, assets, assets[0], "p", job, new Dictionary<string, List<FaceSample>>(), null, null), Ct);
        Assert.Contains(plan.Timeline, c => c.AssetId == "a2");
        Assert.Contains(plan.Timeline, c => c.AssetId == "a1");
        Assert.Contains(plan.Timeline, c => c.AssetId == "a3" && c.Role == "wide");            // opening wide shot
        Assert.All(plan.Timeline, c => Assert.InRange(c.SrcIn - c.MasterIn, c.AssetId == "a2" ? 1.999 : c.AssetId == "a3" ? 0.499 : -0.001, c.AssetId == "a2" ? 2.001 : c.AssetId == "a3" ? 0.501 : 0.001));
        Assert.All(plan.Timeline.Where(c => c.Role == "body"), c => Assert.True(c.Length >= p.Cut.MinShotSec - 0.01, $"{c.ClipId} {c.Length:F2}s; timeline: " + string.Join(" ", plan.Timeline.Select(x => $"{x.ClipId}[{x.AssetId} {x.Role} m{x.MasterIn:F1}-{x.MasterOut:F1}]"))));
        var ctx = new QaContext(plan, p, Tr, ["a1", "a2", "a3"], [], assets);
        var qa = QaEngine.PlanChecks(ctx);
        Assert.Equal(QaStatus.Pass, qa.First(q => q.CheckId == "S-13").Status);
        Assert.All(qa.Where(q => q.Hard), q => Assert.True(q.Status == QaStatus.Pass, $"{q.CheckId} {q.Measured}"));

        var outp = await new Renderer(Ff).RenderAsync(plan, p, assets.ToDictionary(a => a.Id), Path.Combine(_dir, "p7.mp4"), preview: true, Ct);
        double t = 0; int checkedN = 0;
        foreach (var c in plan.Timeline)
        {
            var (r, g, b) = await PixelAsync(outp.Path, t + c.Length / 2);
            (int r, int g, int b) expect = c.AssetId switch { "a1" => (220, 30, 30), "a2" => (30, 30, 220), _ => (30, 220, 30) };
            Assert.True(Math.Abs(r - expect.r) < 40 && Math.Abs(g - expect.g) < 40 && Math.Abs(b - expect.b) < 40, $"clip {c.ClipId} at {t:F1}s showed ({r},{g},{b})");
            t += c.Length; checkedN++;
        }
        Assert.True(checkedN >= 6);
        var probe = await Ff.RunProbeAsync(["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", outp.Path], Ct);
        Assert.InRange(double.Parse(probe.StdOut.Trim(), IC), plan.Duration - 0.15, plan.Duration + 0.15);
        Assert.Contains(plan.Telops, x => x.Type == TelopType.Caption && x.Speaker == 2);
        Assert.Contains("CapS2", AssWriter.Write(plan, p));   // per-speaker caption styles
    }

    // ---------- faces ----------

    [Fact] // FR-SIG-06 / FR-RFM-03: a moving face stays near the centre of the 9:16 output
    public async Task Face_follow_keeps_a_moving_face_near_the_centre()
    {
        var model = Path.Combine(Fixtures.RepoRoot, "tools", "models", "ultraface.onnx");
        var faces = new FaceService(Ff, model);
        Assert.True(faces.Available, "run scripts/get-tools.ps1 (face model)");
        var portrait = Path.Combine(Fixtures.Dir, "faces", "face1.jpg");
        var moving = Path.Combine(_dir, "moving.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=0x303840:s=1920x1080:r=30:d=10", "-loop", "1", "-framerate", "30", "-i", portrait,
            "-f", "lavfi", "-i", "sine=f=300:d=10", "-filter_complex", "[1:v]scale=-2:800[pp];[0:v][pp]overlay=x='200+110*t':y=140:shortest=1[v]", "-map", "[v]", "-map", "2:a", "-shortest", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", moving], Ct);
        var asset = Asset("a1", moving, 10, AssetRole.Main, w: 1920, h: 1080);
        var samples = await faces.AnalyzeAsync(moving, [new Interval(0, 10)], Path.Combine(_dir, "faces.json"), Ct);
        var spec = FaceTrack.Plan(samples, asset.Media, 0, 10, 1.0, out var why);
        Assert.True(spec != null, why);
        Assert.Equal("face", spec!.Mode);
        Assert.True(spec.Track!.Count >= 2, "the crop has to move with the face");
        var p = Fixtures.Presets["P2"];
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Audio = new AudioSpec { Master = "a1", SfxEnabled = false }, Timeline = [new Clip { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 10, Reframe = spec }] };
        var outp = await new Renderer(Ff).RenderAsync(plan, p, new Dictionary<string, SourceAsset> { ["a1"] = asset }, Path.Combine(_dir, "followed.mp4"), preview: false, Ct);
        int inCentre = 0, n = 0;
        for (double t = 0.5; t < 9.6; t += 0.5)
        {
            var f = await faces.DetectAtAsync(outp.Path, t, Ct); n++;
            if (f.Count == 1 && Math.Abs(f[0].Cx - 0.5) <= 0.18) inCentre++;
        }
        Assert.True(inCentre >= n * 0.9, $"face centred in {inCentre}/{n} frames");
        var rt = new Renderer(Ff);
        // a static centre crop would lose the face for much of the clip (the reason the feature exists)
        int staticIn = 0;
        for (double t = 0.5; t < 9.6; t += 0.5) { double cx = 200 + 110 * t + 320 * 0.8; if (Math.Abs(cx - 960) <= 0.18 * 607) staticIn++; }
        Assert.True(staticIn < inCentre, $"static {staticIn} vs followed {inCentre}");
    }

    [Fact] // FR-RFM-03: groups fall back to a blurred full frame; single faces are followed
    public async Task Face_plan_falls_back_for_groups()
    {
        var faces = new FaceService(Ff, Path.Combine(Fixtures.RepoRoot, "tools", "models", "ultraface.onnx"));
        var cache = Path.Combine(_dir, "g.json");
        var group = Path.Combine(_dir, "group.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-loop", "1", "-framerate", "30", "-i", Path.Combine(Fixtures.Dir, "faces", "face3.jpg"), "-t", "6", "-vf", "scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,format=yuv420p", "-c:v", "libx264", "-preset", "ultrafast", group], Ct);
        var media = new MediaInfo { Duration = 6, Width = 1920, Height = 1080, Fps = 30 };
        var samples = await faces.AnalyzeAsync(group, [new Interval(0, 6)], cache, Ct);
        Assert.Null(FaceTrack.Plan(samples, media, 0, 6, 1.0, out var why));
        Assert.Contains("複数", why);
    }

    // ---------- speed ramps and beat sync ----------

    [Fact] // FR-SPD: idle stretches are compressed, speech stays 1x, audio and video lengths agree
    public async Task Speed_plan_compresses_idle_time_and_keeps_av_in_sync()
    {
        var p = Fixtures.Presets["P8"];
        var media = new MediaInfo { Duration = 300, Width = 1280, Height = 720, Fps = 30, AudioChannels = 1 };
        var words = Enumerable.Range(0, 6).Select(i => new Word($"w{i}", 100 + i * 0.5, 100.4 + i * 0.5, "ここです", 1)).ToList();
        var tr = new Transcript { Provider = "t", Duration = 300, Words = words, Sentences = [new Sentence(0, 100, 103, "ここですここですここです", 1, 0, 5)] };
        var notes = new List<string>();
        var clips = SpeedPlanner.Plan(new Interval(0, 240), tr, p, "a1", media, notes);
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = clips, Audio = new AudioSpec { Master = "a1", SfxEnabled = false } };
        Assert.Equal("teaser", clips[0].Role);
        Assert.InRange(plan.Duration, p.Target.Min, p.Target.Max);
        Assert.All(clips, c => Assert.True(c.Speed <= (p.Cut.SpeedUp?.MaxFactor ?? 16) + 0.01));
        Assert.Contains(clips, c => c.Speed > 4);                                            // idle was sped up
        Assert.Contains(clips, c => c.Speed == 1.0 && c.MasterIn <= 100.5 && c.MasterOut >= 102);  // speech kept at 1x
        Assert.Equal(1.0, clips[^1].Speed);                                                   // the finish plays at normal speed
        Assert.All(clips.Where(c => c.Role == "body"), c => Assert.True(c.Length >= p.Cut.MinShotSec - 0.01, $"{c.ClipId} {c.Length:F2}s"));

        // dense speech (the real two-speaker transcript): no cut may fall inside a word (H-06) and no flicker clips
        var dense = SpeedPlanner.Plan(new Interval(0, 125), Tr, p, "a1", Fixtures.Landscape(), []);
        var densePlan = new EditPlan { EditplanId = "e2", ProjectId = "p", Preset = p.Ref, Timeline = dense, Audio = new AudioSpec { Master = "a1", SfxEnabled = false } };
        var qa = QaEngine.PlanChecks(new QaContext(densePlan, p, Tr, ["a1"], []));
        Assert.Equal(QaStatus.Pass, qa.First(q => q.CheckId == "H-06").Status);
        Assert.All(dense.Where(c => c.Role == "body"), c => Assert.True(c.Length >= p.Cut.MinShotSec - 0.01, $"dense {c.ClipId} {c.Length:F2}s"));
        var src = Path.Combine(_dir, "long.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=300", "-f", "lavfi", "-i", "sine=f=330:d=300,volume=0.3", "-shortest", "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-c:a", "aac", src], Ct);
        var outp = await new Renderer(Ff).RenderAsync(plan, p, new Dictionary<string, SourceAsset> { ["a1"] = Asset("a1", src, 300, AssetRole.Main) }, Path.Combine(_dir, "speed.mp4"), preview: true, Ct);
        var probe = await Ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "csv=p=0", outp.Path], Ct);
        var durs = probe.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Split(',')).Where(x => x.Length == 2).ToDictionary(x => x[0], x => double.Parse(x[1], IC));
        Assert.InRange(durs["video"], plan.Duration - 0.2, plan.Duration + 0.2);
        Assert.InRange(Math.Abs(durs["video"] - durs["audio"]), 0, 0.1);
    }

    [Fact] // FR-BGM-03: every cut lies on the music's beat grid (±1 frame)
    public void Beat_plan_cuts_stay_on_the_beat_grid()
    {
        var p = Fixtures.Presets["P5"];
        var media = new MediaInfo { Duration = 200, Width = 1920, Height = 1080, Fps = 30, AudioChannels = 1 };
        double bpm = 140, beat = 60 / bpm;
        var clips = BeatPlanner.Plan([new Interval(0, 200)], Fixtures.Signals(Tr), new Transcript { Provider = "t" }, p, "a1", media, bpm, []);
        Assert.InRange(clips.Sum(c => c.Length), p.Target.Min - 5, p.Target.Max);
        double t = 0;
        foreach (var c in clips)
        {
            Assert.True(Math.Abs(c.Length * 30 - Math.Round(c.Length * 30)) < 0.05, "whole frames");
            t += c.Length;
            double nearest = Math.Round(t / beat) * beat;
            Assert.True(Math.Abs(t - nearest) <= 1.0 / 30 + 1e-6, $"cut at {t:F3}s is {Math.Abs(t - nearest) * 1000:F0} ms from the grid");
        }
        Assert.True(clips.Count >= 8);
    }

    [Fact] // selection scope (FR-SEL-10): economy < balanced < full in text sent to the AI
    public async Task Selection_scope_changes_how_much_text_is_sent()
    {
        var words = new List<Word>(); var sents = new List<Sentence>();
        for (int i = 0; i < 900; i++)
        {
            double t = i * 4; var text = "これはテストの文章です。ここで何かが起きています。";
            words.Add(new Word($"w{i}", t, t + 3.5, text, 0)); sents.Add(new Sentence(i, t, t + 3.5, text, 0, i, i));
        }
        var tr = new Transcript { Provider = "t", Duration = 3600, Words = words, Sentences = sents };
        var sig = new SignalSet { Loudness100Ms = Enumerable.Range(0, 36000).Select(k => (k / 6000) % 2 == 1 ? -18.0 : -30.0).ToList(), MedianLoudness = -24 };
        async Task<SelectionOutcome> Run(string scope)
        {
            var llm = new FakeSelector(_ => new LlmResult([new RawCandidate([new Interval(60, 90)], "", "", [], "r", 7, 7, 7, 7)], "fake", new LlmUsage(1000, 100)));
            return await new SelectionService(llm).SelectAsync(tr, sig, Fixtures.Presets["P4"], 3600, null, [], Ct, scope);
        }
        var eco = await Run("economy"); var bal = await Run("balanced"); var full = await Run("full");
        Assert.True(eco.SentChars < bal.SentChars && bal.SentChars < full.SentChars, $"{eco.SentChars} < {bal.SentChars} < {full.SentChars}");
        Assert.Equal(full.FullTextChars, full.SentChars);                               // full = nothing dropped
        Assert.True(bal.SentChars <= bal.FullTextChars * 0.5);
        Assert.True(eco.SentChars <= eco.FullTextChars * 0.35);
        // the UI estimate follows the same ordering
        var e = new[] { "economy", "balanced", "full" }.Select(s => SelectionService.EstimateSent(3600, s, SelectionService.DefaultCharBudget).chars).ToArray();
        Assert.True(e[0] < e[1] && e[1] < e[2]);
    }

    // ---------- telop extras ----------

    [Fact] // FR-TEL tsukkomi: code-side validation (length, caption id, invented numbers/names, insults)
    public void Tsukkomi_validation_filters_unsafe_or_ungrounded_output()
    {
        var caps = new List<Telop> { new() { Id = "t1", Text = "1万円を使い切る" }, new() { Id = "t2", Text = "100個買います" } };
        const string src = "1万円を使い切る100個買います";
        Assert.Null(TsukkomiService.Validate("いや多すぎ！", "t2", caps, src));
        Assert.Equal("length", TsukkomiService.Validate("それはさすがにおかしいでしょうよ本当に", "t1", caps, src));
        Assert.Equal("caption id", TsukkomiService.Validate("そこ？", "t9", caps, src));
        Assert.Equal("blocklist", TsukkomiService.Validate("バカなの？", "t1", caps, src));
        Assert.Equal("ungrounded", TsukkomiService.Validate("500個は無理！", "t2", caps, src));
        Assert.NotNull(TsukkomiService.Validate("ディズニーかよ！", "t2", caps, src));   // invented name (rejected by the grounding check or the katakana rule)
    }

    [Fact]
    public async Task Tsukkomi_generation_keeps_only_valid_items_and_marks_them_unapproved()
    {
        var caps = Enumerable.Range(1, 4).Select(i => new Telop { Id = $"t{i}", Text = i == 2 ? "100個買います" : "普通の字幕" + i }).ToList();
        var llm = new JsonLlm("""{"items":[{"caption_id":"t2","text":"いや多すぎ！"},{"caption_id":"t3","text":"バカなの？"},{"caption_id":"t4","text":"長すぎるツッコミですよこれは"}]}""");
        var items = await TsukkomiService.GenerateAsync(llm, caps, string.Concat(caps.Select(c => c.Text)), Ct);
        Assert.Equal([("t2", "いや多すぎ！")], items);

        var p = Fixtures.Presets["P3"];
        var assets = new List<SourceAsset> { Asset("a1", "x.mp4", 125, AssetRole.Main) };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P3"], Options = new JobOptions { Tsukkomi = true } };
        var cand = new Candidate { Id = "c1", Segments = [new Interval(8.8, 40)], HookText = "1万円で帰れない", Headline = "100円だけ" };
        var tsu = new JsonLlm("""{"items":[{"caption_id":"CAP","text":"そこ？"}]}""") { CaptionFromPrompt = true };
        var (plan, _) = await PlanBuilder.BuildAsync(new PlanRequest(cand, Tr, Fixtures.Signals(Tr), p, assets, assets[0], "p", job, new Dictionary<string, List<FaceSample>>(), null, tsu), Ct);
        var t = Assert.Single(plan.Telops, x => x.Type == TelopType.Tsukkomi);
        Assert.False(t.Approved);
        Assert.Contains(QaEngine.PlanChecks(new QaContext(plan, p, Tr, ["a1"], [])), q => q.CheckId == "H-08" && q.Status == QaStatus.Fail);   // export blocked until approved
        var approved = PlanEditor.Apply(plan, [new PlanOp("approve_all_tsukkomi")]);
        Assert.Contains(QaEngine.PlanChecks(new QaContext(approved, p, Tr, ["a1"], [])), q => q.CheckId == "H-08" && q.Status == QaStatus.Pass);
    }

    [Fact] // counters (⑥) come straight from spoken number+unit words; onomatopoeia (⑤) and SFX placement
    public void Counters_onomatopoeia_and_sfx_are_derived_from_the_timeline()
    {
        var words = new List<Word> { new("w1", 1, 1.3, "あと", 1), new("w2", 1.3, 1.8, "3000", 1), new("w3", 1.8, 2.0, "円", 1), new("w4", 2.0, 2.5, "です", 1) };
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a", SrcIn = 0, SrcOut = 10 } };
        var mapped = TelopBuilder.MapWords(clips, new Transcript { Provider = "t", Words = words });
        int n = 0;
        var counters = TelopExtras.Counters(mapped, [], 10, ref n);
        var c = Assert.Single(counters);
        Assert.Equal("3000円", c.Text); Assert.Equal(1.3, c.Start, 3);
        var onom = TelopExtras.Onomatopoeia(clips, [new Interval(5, 6)], [new Telop { Id = "e", Type = TelopType.Emphasis, Start = 8, End = 9, Text = "x" }], 10, ref n);
        Assert.Contains(onom, o => o.Text == "ワハハ" && Math.Abs(o.Start - 5) < 0.01);
        Assert.Contains(onom, o => o.Text == "ドーン");
        var telops = new List<Telop> { new() { Id = "h", Type = TelopType.Hook, Start = 0, End = 2, Text = "h" }, new() { Id = "e", Type = TelopType.Emphasis, Start = 3, End = 4, Text = "e" }, c, onom[^1] with { Start = 8 } };
        var (sfx, mutes) = SfxPlanner.Plan(clips, telops, "heavy", 10);
        Assert.Contains(sfx, s => s.Kind == "whoosh" && s.T == 0);
        Assert.Contains(sfx, s => s.Kind == "pop" && s.T == 3);
        Assert.Contains(sfx, s => s.Kind == "ding" && Math.Abs(s.T - 1.3) < 0.001);
        Assert.Contains(sfx, s => s.Kind == "boom" && s.T == 8);
        Assert.Contains(mutes, m => m.T0 < 8 && m.T1 > 8);          // music drops out just before the hit
        Assert.Empty(SfxPlanner.Plan(clips, telops, "none", 10).Sfx);
    }

    [Fact] // laughter-like bursts are loud windows outside the words
    public void Non_speech_bursts_are_found_between_words()
    {
        var loud = Enumerable.Repeat(-40.0, 200).ToList();
        for (int i = 0; i < 50; i++) loud[i] = -22;            // speech 0–5 s
        for (int i = 100; i < 112; i++) loud[i] = -20;         // 10.0–11.2 s: burst without words
        var words = new List<Word> { new("w", 0, 5, "はい", 1) };
        var res = NonSpeech.Bursts(new SignalSet { Loudness100Ms = loud, MedianLoudness = -30 }, new Transcript { Provider = "t", Words = words });
        var b = Assert.Single(res);
        Assert.InRange(b.T0, 9.9, 10.1); Assert.InRange(b.T1, 11.1, 11.3);
    }

    sealed class JsonLlm(string json) : IHighlightSelector
    {
        public bool CaptionFromPrompt { get; init; }
        public string Name => "fake";
        public string Model => "fake";
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
        {
            if (!CaptionFromPrompt) return Task.FromResult(json);
            var id = Regex.Matches(user, @"^(t\d+):", RegexOptions.Multiline).Skip(3).First().Groups[1].Value;   // a caption in the middle of the clip
            return Task.FromResult(json.Replace("CAP", id));
        }
    }
}
