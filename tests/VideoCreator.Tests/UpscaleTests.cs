using System.Globalization;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>Registration-time upscaling of small opening / closing videos (Real-ESRGAN detail transfer, measured per file).</summary>
public class UpscaleTests : IAsyncLifetime
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-up-" + Guid.NewGuid().ToString("N")[..8]);
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    static string Model => Path.Combine(Fixtures.RepoRoot, "tools", "models", SuperResolution.ModelFile);

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static BrandKit Kit(BrandEnhancement? e, string path) => new()
    {
        RightsConfirmed = true,
        Files = [new() { Path = path, Kind = "opening", Width = 720, Height = 1280, Duration = 3, HasAudio = true, Lufs = -15, Enhancement = e }],
    };

    [Theory]
    [InlineData(0.45, 0.44, true)]                  // measured improvement
    [InlineData(0.30, -0.10, true)]                 // XPSNR within its 0.1 dB tolerance
    [InlineData(0.0, 0.0, false)]                   // no improvement → lanczos
    [InlineData(0.0, 0.5, false)]                   // VMAF-NEG must improve, not just XPSNR
    [InlineData(-0.2, 0.3, false)]
    [InlineData(0.5, -0.11, false)]
    [InlineData(double.NaN, 0.4, false)]            // a metric that could not be measured is no evidence
    [InlineData(0.5, double.NaN, false)]
    [InlineData(double.PositiveInfinity, 0.4, false)]
    public void Upscale_is_adopted_only_on_a_measured_improvement(double vmaf, double xpsnr, bool adopt) =>
        Assert.Equal(adopt, BrandKitEnhancer.Adopt(new QualityGain(vmaf, xpsnr, 0)));

    [Fact]
    public void Planner_uses_the_upscaled_copy_and_qa_reports_it()
    {
        var orig = Path.Combine(_dir, "Opening.mp4"); File.WriteAllText(orig, "x");
        var up = Path.Combine(_dir, "Opening_up.mp4"); File.WriteAllText(up, "x");
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 30 } };
        var e = new BrandEnhancement { Path = up, Width = 1080, Height = 1920, Method = SuperResolution.Method, Gain = new QualityGain(1.1, 0.4, 0.00002) };

        var (_, ins) = InsertPlanner.Plan(clips.ToList(), new JobOptions { Opening = true }, Kit(e, orig), new Canvas(), -14, null, []);
        var op = Assert.Single(ins);
        Assert.Equal((up, 1080, 1920, orig, 720, 1280), (op.Path, op.Width, op.Height, op.OriginalPath, op.OriginalWidth, op.OriginalHeight));
        Assert.NotNull(op.Enhanced);

        var p = Fixtures.Presets["P4"];
        var plan = new EditPlan { EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Timeline = clips, Audio = new AudioSpec { Master = "a1" }, Inserts = ins };
        var s12 = QaEngine.PlanChecks(new QaContext(plan, p, Fixtures.Talk, ["a1"], [])).Single(q => q.CheckId == "S-12");
        Assert.Equal(QaStatus.Pass, s12.Status);
        Assert.Contains("高画質化済み", s12.Measured);

        // not adopted (the check found it less faithful) or the cached copy is gone → the registered file, and S-12 still warns
        var notAdopted = InsertPlanner.Plan(clips.ToList(), new JobOptions { Opening = true }, Kit(e with { Path = null }, orig), new Canvas(), -14, null, []).Inserts.Single();
        Assert.Equal((orig, 720, (string?)null), (notAdopted.Path, notAdopted.Width, notAdopted.Enhanced));
        File.Delete(up);
        Assert.Equal(orig, InsertPlanner.Plan(clips.ToList(), new JobOptions { Opening = true }, Kit(e, orig), new Canvas(), -14, null, []).Inserts.Single().Path);
        Assert.Equal(QaStatus.Warn, QaEngine.PlanChecks(new QaContext(plan with { Inserts = [notAdopted] }, p, Fixtures.Talk, ["a1"], [])).Single(q => q.CheckId == "S-12").Status);
    }

    [Fact]
    public void Renderer_falls_back_to_the_registered_file_when_the_cache_was_deleted()
    {
        var orig = Path.Combine(_dir, "Opening.mp4"); File.WriteAllText(orig, "x");
        var src = Path.Combine(_dir, "src.mp4"); File.WriteAllText(src, "x");
        var plan = new EditPlan
        {
            EditplanId = "ep", ProjectId = "p", Preset = new PresetRef("P4", "1"), Audio = new AudioSpec { Master = "a1" },
            Timeline = [new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 10 }],
            Inserts = [new() { Id = "opening", Kind = "opening", At = 0, Duration = 3, Path = Path.Combine(_dir, "gone.mp4"), OriginalPath = orig, Width = 1080, Height = 1920 }],
        };
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = new() { Id = "a1", ProjectId = "p", Path = src, Sha256 = "x", Media = Fixtures.Landscape() } };
        var inp = new Renderer(Ff).ResolveInputs(plan, assets);
        Assert.Contains(orig, inp.Flat);
    }

    async Task<string> MakeAsync(string name, int w, int h, double dur)
    {
        var f = Path.Combine(_dir, name);
        // graphics-like content: flat colours, sharp text and a moving pattern, with audio at 96 kHz like the real bumpers
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=s={w}x{h}:r=30:d={dur.ToString(IC)}", "-f", "lavfi", "-i", $"sine=f=440:d={dur.ToString(IC)}",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "12", "-pix_fmt", "yuv420p", "-c:a", "aac", "-ar", "96000", "-shortest", f], Ct);
        return f;
    }

    [Fact] // real model + FFmpeg: exact size, every frame kept, audio copied untouched, no colour/tone shift against lanczos, and the per-file check is cached
    public async Task Upscale_keeps_frames_audio_and_tone_and_is_cached()
    {
        Assert.True(File.Exists(Model), "run scripts/get-tools.ps1 (realesr-animevideov3.onnx)");
        var src = await MakeAsync("Opening-small.mp4", 240, 426, 1.0);
        using var sr = new SuperResolution(Model);
        var dst = Path.Combine(_dir, "up.mp4");
        await sr.UpscaleVideoAsync(Ff, src, 240, 426, 30, 360, 640, dst, null, Ct);

        async Task<string> Probe(string f, string sel, string entries) => (await Ff.RunProbeAsync(["-v", "error", "-select_streams", sel, "-count_packets", "-show_entries", entries, "-of", "csv=p=0", f], Ct)).StdOut.Trim();
        Assert.Equal("360,640,30", await Probe(dst, "v:0", "stream=width,height,nb_read_packets"));
        Assert.Equal(await Probe(src, "a:0", "stream=codec_name,sample_rate,nb_read_packets"), await Probe(dst, "a:0", "stream=codec_name,sample_rate,nb_read_packets"));

        // detail transfer only adds fine luma detail: against a plain lanczos upscale the picture is otherwise the same (a tone/colour shift would drop this far)
        var lz = Path.Combine(_dir, "lz.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-i", src, "-vf", "scale=360:640:flags=lanczos,format=yuv420p", "-c:v", "libx264", "-crf", "10", lz], Ct);
        var psnr = System.Text.RegularExpressions.Regex.Match((await Ff.RunFfmpegAsync(["-hide_banner", "-i", dst, "-i", lz, "-lavfi", "[0:v][1:v]psnr", "-f", "null", "-"], Ct)).StdErr, @"PSNR y:([\d.]+) u:([\d.]+) v:([\d.]+)");
        Assert.True(double.Parse(psnr.Groups[1].Value, IC) > 30, $"luma vs lanczos {psnr.Value}");
        Assert.True(double.Parse(psnr.Groups[2].Value, IC) > 45 && double.Parse(psnr.Groups[3].Value, IC) > 45, $"chroma comes from lanczos {psnr.Value}");

        // the per-file check + full job, then the cached record
        var f = new BrandFile { Path = src, Kind = "opening", Width = 240, Height = 426, Duration = 1.0, HasAudio = true };
        var canvas = new Canvas(360, 640);
        var cache = Path.Combine(_dir, "cache");
        var e1 = (await BrandKitEnhancer.EnhanceAsync(Ff, sr, f, canvas, cache, null, Ct)).Enhancement!;
        Assert.NotNull(e1.Gain);
        Assert.False(double.IsNaN(e1.Gain!.Xpsnr));
        Assert.Equal(BrandKitEnhancer.Adopt(e1.Gain), e1.Adopted);
        if (e1.Adopted) { Assert.True(File.Exists(e1.Path)); Assert.Equal((360, 640), (e1.Width, e1.Height)); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var e2 = (await BrandKitEnhancer.EnhanceAsync(Ff, sr, f, canvas, cache, null, Ct)).Enhancement!;
        Assert.True(sw.Elapsed.TotalSeconds < 2, "cached");
        Assert.Equal(e1.Gain, e2.Gain);
        Assert.Null((await BrandKitEnhancer.EnhanceAsync(Ff, sr, f with { Width = 360, Height = 640 }, canvas, cache, null, Ct)).Enhancement);   // already canvas-sized: untouched
    }

    static SourceAsset ScreenAsset(string id, string path, int w, int h) => new() { Id = id, ProjectId = "p", Path = path, Sha256 = id + "0123456789abcdef", Media = Fixtures.Landscape(6) with { Width = w, Height = h } };

    static EditPlan PipPlan(PipSpec pip) => new()
    {
        EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P11"].Ref, Audio = new AudioSpec { Master = "m", SfxEnabled = false },
        Timeline = [new() { ClipId = "c1", AssetId = "m", SrcIn = 0, SrcOut = 3, Reframe = new ReframeSpec { Mode = "blur_bg" }, Pip = pip }],
    };

    [Fact] // no FFmpeg: what is (not) magnified, and the fallbacks say what happened without touching the plan
    public async Task Screen_enhancer_only_targets_magnified_non_slide_content()
    {
        var slide = Path.Combine(_dir, "slide.png"); File.WriteAllText(slide, "x");
        var small = new Dictionary<string, SourceAsset> { ["s"] = ScreenAsset("s", "small.mp4", 640, 360) };
        var big = new Dictionary<string, SourceAsset> { ["s"] = ScreenAsset("s", "big.mp4", 1920, 1080) };
        var inset = new PipSpec { AssetId = "s", Shape = "rectangle", X = 24, Y = 1200, Width = 1032, Height = 580, Border = 0, SrcIn = 1 };

        var piece = Assert.Single(ScreenEnhancer.Pieces(PipPlan(inset), small));
        Assert.Equal((640, 360, 1032, 580), (piece.W, piece.H, piece.OutW, piece.OutH));
        Assert.Equal(580.0 / 360, piece.Factor, 4);
        Assert.Empty(ScreenEnhancer.Pieces(PipPlan(inset with { Image = slide }), small));   // the deck's own image is already sharp
        // a content crop is what gets magnified, not the whole frame
        Assert.Equal(1032.0 / 400, Assert.Single(ScreenEnhancer.Pieces(PipPlan(inset with { CropX = 100, CropY = 50, CropW = 400, CropH = 225 }), big)).Factor, 4);

        var plan = PipPlan(inset);
        var (same, notes) = await ScreenEnhancer.ApplyAsync(Ff, Model, plan, big, _dir, null, Ct);
        Assert.Same(plan, same);
        Assert.Contains("不要", Assert.Single(notes));
        (same, notes) = await ScreenEnhancer.ApplyAsync(Ff, null, plan, small, _dir, null, Ct);
        Assert.Same(plan, same);
        Assert.Contains("lanczos", Assert.Single(notes));
    }

    [Fact] // real model + FFmpeg: self-checked, cut to the inset's size, frame-aligned with the recording, cached
    public async Task Magnified_screen_piece_is_super_resolved_in_place_and_frame_aligned()
    {
        Assert.True(File.Exists(Model), "run scripts/get-tools.ps1 (realesr-animevideov3.onnx)");
        var screen = await MakeAsync("screen.mp4", 640, 360, 6);
        var mainFile = Path.Combine(_dir, "main.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=640x360:r=30:d=6", "-f", "lavfi", "-i", "sine=f=440:d=6",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", mainFile], Ct);
        var assets = new Dictionary<string, SourceAsset>
        {
            ["m"] = new() { Id = "m", ProjectId = "p", Path = mainFile, Sha256 = "m0123456789abcdef", Media = await new MediaProbe(Ff).ProbeAsync(mainFile, Ct) },
            ["s"] = ScreenAsset("s", screen, 640, 360),
        };
        var plan = PipPlan(new PipSpec { AssetId = "s", Shape = "rectangle", X = 24, Y = 1200, Width = 1032, Height = 580, Border = 0, SrcIn = 1 });
        var cache = Path.Combine(_dir, "cache");
        var (enhanced, notes) = await ScreenEnhancer.ApplyAsync(Ff, Model, plan, assets, cache, null, Ct);
        var note = Assert.Single(notes);
        var pip = enhanced.Timeline[0].Pip!;
        if (pip.Video == null) { Assert.Contains("lanczos", note); return; }   // the self-check is the judge; testsrc2 is normally improved
        Assert.Contains("高画質化しました", note);
        Assert.Null(plan.Timeline[0].Pip!.Video);   // the stored plan is untouched
        var size = (await Ff.RunProbeAsync(["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", pip.Video], Ct)).StdOut.Trim();
        Assert.Equal("1032,580", size);

        // final renders (previews are half size); the substituted inset shows the same picture at the same time as the lanczos render: aligned beats one frame early or late
        var p = Fixtures.Presets["P11"];
        var a = (await new Renderer(Ff).RenderAsync(enhanced, p, assets, Path.Combine(_dir, "sr.mp4"), preview: false, Ct)).Path;
        var b = (await new Renderer(Ff).RenderAsync(plan, p, assets, Path.Combine(_dir, "lz.mp4"), preview: false, Ct)).Path;
        async Task<double> Psnr(int shift)
        {
            string inset = "crop=1032:580:24:1200,trim=start_frame=5:end_frame=80,setpts=PTS-STARTPTS";
            string other = $"crop=1032:580:24:1200,trim=start_frame={5 + shift}:end_frame={80 + shift},setpts=PTS-STARTPTS";
            var log = (await Ff.RunFfmpegAsync(["-hide_banner", "-i", a, "-i", b, "-lavfi", $"[0:v]{inset}[x];[1:v]{other}[y];[x][y]psnr", "-f", "null", "-"], Ct)).StdErr;
            var m = System.Text.RegularExpressions.Regex.Match(log, @"PSNR y:([\d.]+)");
            Assert.True(m.Success, FFmpeg.Tail(log));
            return double.Parse(m.Groups[1].Value, IC);
        }
        double aligned = await Psnr(0), early = await Psnr(-1), late = await Psnr(1);
        Assert.True(aligned > 28 && aligned > early + 1 && aligned > late + 1, $"inset PSNR aligned {aligned:F2} / -1 frame {early:F2} / +1 frame {late:F2}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var again = await ScreenEnhancer.ApplyAsync(Ff, Model, plan, assets, cache, null, Ct);
        Assert.True(sw.Elapsed.TotalSeconds < 3, "cached");
        Assert.Equal(pip.Video, again.Plan.Timeline[0].Pip!.Video);
    }
}
