using System.Text.RegularExpressions;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>End-to-end render + QA with real FFmpeg on synthetic media (AT-02, AT-03, AT-11, AT-12/H-05).</summary>
public class RenderingTests : IAsyncLifetime
{
    static readonly Transcript Tr = Fixtures.Talk;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-tests-" + Guid.NewGuid().ToString("N")[..8]);
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    string _landscape = "", _vertical = "";

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _landscape = Path.Combine(_dir, "land.mp4"); _vertical = Path.Combine(_dir, "vert.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=130", "-f", "lavfi", "-i", "sine=f=330:d=130,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", _landscape], TestContext.Current.CancellationToken);
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=720x1280:r=30:d=130", "-f", "lavfi", "-i", "sine=f=330:d=130,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", _vertical], TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    (EditPlan plan, Dictionary<string, SourceAsset> assets, QaContext ctx) Build(string file, int w, int h, Preset? preset = null)
    {
        var p = preset ?? Fixtures.Presets["P4"];
        var media = new MediaInfo { Duration = 130, Width = w, Height = h, Fps = 30, AudioChannels = 1 };
        var res = Solver.Plan([new Interval(74, 103)], Tr, Fixtures.Signals(Tr), p, media, "a1", ["300円の商品"]);
        var plan = new EditPlan
        {
            EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips,
            Telops = TelopBuilder.Build(res.Clips, Tr, p, "あと2個", "300円の商品", ["300円の商品"], []),
            Audio = new AudioSpec { Master = "a1" },
        };
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = new() { Id = "a1", ProjectId = "p", Path = file, Sha256 = "x", Media = media } };
        return (plan, assets, new QaContext(plan, p, Tr, ["a1"], []));
    }

    [Fact] // landscape in → 1920×1080 out with the 16:9 opening: hard QA (H-02 size, H-05 title-safe) passes and the frame edge shows the picture, not a blurred band
    public async Task Landscape_source_renders_landscape_with_the_landscape_opening()
    {
        var ct = TestContext.Current.CancellationToken;
        var op169 = Path.Combine(_dir, "op169.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=30:d=1.5", "-f", "lavfi", "-i", "sine=f=550:d=1.5,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", op169], ct);
        var (plan, assets, ctx) = Build(_landscape, 1280, 720);
        var canvas = Canvas.For(1280, 720);
        var kit = new BrandKit
        {
            RightsConfirmed = true,
            Files = [new() { Path = "op916.mp4", Kind = "opening", Width = 1080, Height = 1920, Duration = 1.5, HasAudio = true, Lufs = -14 },
                     new() { Path = op169, Kind = "opening", Width = 1920, Height = 1080, Duration = 1.5, HasAudio = true, Lufs = -14 }],
        };
        var (packed, inserts) = InsertPlanner.Plan(PlanBuilder.Orient(plan.Timeline.ToList(), [.. assets.Values], canvas), new JobOptions { Opening = true }, kit, canvas, -14, null, []);
        plan = plan with { Canvas = canvas, Timeline = packed, Inserts = inserts };
        Assert.Equal(op169, plan.Inserts.Single().Path);

        var outp = await new Renderer(Ff).RenderAsync(plan, ctx.Preset, assets, Path.Combine(_dir, "land_final.mp4"), preview: false, ct);
        var qa = await new QaEngine(Ff).RunAsync(ctx with { Plan = plan }, outp.Path, preview: false, Path.Combine(_dir, "qa_land"), ct);
        Assert.All(qa.Where(q => q.Hard), q => Assert.True(q.Status == QaStatus.Pass, $"{q.CheckId} {q.Measured} vs {q.Threshold}"));
        Assert.Equal("1920,1080", (await Ff.RunProbeAsync(["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", outp.Path], ct)).StdOut.Trim());

        // edge energy of the left 64 px (outside the 96 px title-safe margin, so no telop) in the body: the sharp test pattern, not boxblur(20:2)
        async Task<double> Edges(string extra) => double.Parse(Regex.Match((await Ff.RunFfmpegAsync(["-hide_banner", "-ss", "4", "-i", outp.Path, "-frames:v", "1",
            "-vf", $"{extra}crop=64:1080:0:0,edgedetect,signalstats,metadata=print:key=lavfi.signalstats.YAVG", "-f", "null", "-"], ct)).StdErr, @"YAVG=([\d.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        double sharp = await Edges(""), blurred = await Edges("boxblur=20:2,");
        Assert.True(sharp > 0.5 && sharp > 3 * blurred, $"left edge looks blurred: {sharp:F2} vs {blurred:F2}");
    }

    [Fact] // §25: the P3 pause card is an off-white, silent 2.5 s pause with dark static text (the references: white card, −30 dB, no music, no hit)
    public async Task Pause_card_renders_off_white_silent_and_with_dark_text()
    {
        var ct = TestContext.Current.CancellationToken;
        var p3 = Fixtures.Presets["P3"];
        var (plan, assets, ctx) = Build(_vertical, 720, 1280, p3);
        var clips = plan.Timeline.ToList();
        InsertPlanner.SplitAt(clips, 4.0);
        var card = new Insert { Id = "card1", Kind = "card", At = 4.0, Duration = 2.5, Text = "そりゃそう。", TextSource = "fixed", Look = "pause" };
        plan = plan with { Timeline = clips, Inserts = [card], Telops = [], Audio = plan.Audio with { SfxEnabled = true } };
        Assert.DoesNotContain(Renderer.FinalAudio(plan).Sfx, s => Math.Abs(s.T - 4.0) < 0.01);                 // no hit on the card
        Assert.True(BgmPlanner.Mutes(card));                                                                     // the body's music stops under it
        var ass = AssWriter.Write(plan, p3);
        Assert.Contains($"Style: CardPause,{AssWriter.FontName},{p3.Telop.Caption.FontPx},{AssWriter.PauseInk},", ass);
        Assert.DoesNotContain(@"\frz", ass.Split('\n').Single(l => l.Contains(",CardPause,,")));                // static: no tilt, no pop

        var outp = await new Renderer(Ff).RenderAsync(plan, p3, assets, Path.Combine(_dir, "pause.mp4"), preview: false, ct);
        async Task<byte[]> Rgb(double t, string crop)
        {
            using var ms = new MemoryStream();
            await Ff.RunFfmpegAsync(["-v", "error", "-ss", t.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", outp.Path, "-frames:v", "1", "-vf", crop, "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], ct, stdout: ms);
            return ms.ToArray();
        }
        var corner = await Rgb(5.25, "crop=64:64:40:40");
        double Avg(byte[] px, int ch) => Enumerable.Range(0, px.Length / 3).Average(i => px[i * 3 + ch]);
        Assert.True(Math.Abs(Avg(corner, 0) - 0xF2) < 10 && Math.Abs(Avg(corner, 1) - 0xEF) < 10 && Math.Abs(Avg(corner, 2) - 0xE8) < 10,
            $"paper {Avg(corner, 0):F0},{Avg(corner, 1):F0},{Avg(corner, 2):F0}");
        var middle = await Rgb(5.25, "crop=760:240:160:840");
        Assert.True(Enumerable.Range(0, middle.Length / 3).Count(i => middle[i * 3] < 80 && middle[i * 3 + 1] < 80 && middle[i * 3 + 2] < 80) > 2000, "no dark text in the middle");
        async Task<double> Rms(double t0, double t1) => double.Parse(Regex.Matches((await Ff.RunFfmpegAsync(["-hide_banner", "-ss", t0.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-t", (t1 - t0).ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", outp.Path, "-vn", "-af", "astats=measure_overall=RMS_level:measure_perchannel=none", "-f", "null", "-"], ct)).StdErr,
            @"RMS level dB: (-?[\d.]+|-inf)").Last().Groups[1].Value.Replace("-inf", "-200"), System.Globalization.CultureInfo.InvariantCulture);
        double body = await Rms(1, 3.5), pause = await Rms(4.3, 6.2);
        Assert.True(body > -40 && pause < body - 30, $"body {body:F1} dB, pause {pause:F1} dB");
    }

    [Fact] // AT-02 + AT-03 + AT-12: platform spec and all hard QA on a real final render, and a second render of the same plan decodes to identical frames
    public async Task Final_render_meets_platform_spec_passes_hard_qa_and_is_deterministic()
    {
        var (plan, assets, ctx) = Build(_landscape, 1280, 720);
        var r = new Renderer(Ff);
        var a = await r.RenderAsync(plan, ctx.Preset, assets, Path.Combine(_dir, "det_a.mp4"), false, TestContext.Current.CancellationToken);
        var qa = await new QaEngine(Ff).RunAsync(ctx, a.Path, preview: false, Path.Combine(_dir, "qa"), TestContext.Current.CancellationToken);
        Assert.All(qa.Where(q => q.Hard), q => Assert.True(q.Status == QaStatus.Pass, $"{q.CheckId} {q.Measured} vs {q.Threshold}"));
        Assert.InRange(a.LoudnessI, -15, -13);
        Assert.True(a.TruePeak <= -1.0);
        var probe = await Ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=codec_name,sample_rate,channels", "-of", "csv=p=0", a.Path], TestContext.Current.CancellationToken);
        Assert.Contains("h264", probe.StdOut);
        Assert.Contains("aac,48000,2", probe.StdOut);

        var b = await r.RenderAsync(plan, ctx.Preset, assets, Path.Combine(_dir, "det_b.mp4"), false, TestContext.Current.CancellationToken);
        async Task<string> Hash(string f) => (await Ff.RunFfmpegCheckedAsync(["-v", "error", "-i", f, "-map", "0:v", "-f", "framemd5", "-"], TestContext.Current.CancellationToken)).StdOut.Trim();
        var ha = await Hash(a.Path);
        Assert.Contains("#hash: MD5", ha);
        Assert.Contains("\n0,", ha);
        Assert.Equal(ha, await Hash(b.Path));
        var pa = await RenderProvenance.CaptureAsync(Ff, plan, ctx.Preset, [_landscape], Path.ChangeExtension(a.Path, ".ass"), TestContext.Current.CancellationToken);
        var pb = await RenderProvenance.CaptureAsync(Ff, plan, ctx.Preset, [_landscape], Path.ChangeExtension(b.Path, ".ass"), TestContext.Current.CancellationToken);
        Assert.Equal(VideoCreator.Json.Serialize(pa), VideoCreator.Json.Serialize(pb));
        Assert.Equal(EditPlanFingerprint.Of(plan), pa.PlanHash);
        Assert.Equal(await RenderProvenance.HashFileAsync(_landscape, TestContext.Current.CancellationToken), Assert.Single(pa.SourceSha256, f => f.Path == _landscape).Sha256);
        Assert.Equal(64, pa.FfmpegSha256.Length);
        Assert.Equal(64, pa.BuildconfSha256.Length);
        Assert.Equal(64, pa.PresetSha256.Length);
        Assert.NotEmpty(pa.FontSha256);
        Assert.Contains("libass API version:", pa.TextLibraryInfo);
        Assert.Contains("HarfBuzz", pa.TextLibraryInfo);
        Assert.Equal(Ff.Threads, pa.EncoderThreads);
    }

    [Fact]
    public async Task Preview_marks_final_only_checks_as_not_run_and_binds_plan_checks_to_the_plan()
    {
        var (plan, assets, ctx) = Build(_landscape, 1280, 720);
        var output = await new Renderer(Ff).RenderAsync(plan, ctx.Preset, assets, Path.Combine(_dir, "preview.mp4"), true, TestContext.Current.CancellationToken);
        var qa = await new QaEngine(Ff).RunAsync(ctx, output.Path, true, Path.Combine(_dir, "qa_preview"), TestContext.Current.CancellationToken);
        Assert.All(qa, q => Assert.Equal(EditPlanFingerprint.Of(plan), q.PlanHash));
        Assert.All(qa.Where(q => q.Hard && q.Stage == "plan"), q => Assert.Equal(QaStatus.Pass, q.Status));
        foreach (var id in new[] { "H-02", "H-03", "H-04" })
        {
            var check = Assert.Single(qa, q => q.CheckId == id);
            Assert.Equal(QaStatus.NotRun, check.Status);
            Assert.Equal("final", check.Stage);
        }
        Assert.Contains("\"not_run\"", VideoCreator.Json.Serialize(qa));
    }

    [Fact] // AT-11: vertical source is not blurred/letterboxed
    public async Task Vertical_source_passes_through()
    {
        var p = Fixtures.Presets["P4"] with { Telop = Fixtures.Presets["P4"].Telop with { Types = [] } };
        var (plan, assets, ctx) = Build(_vertical, 720, 1280, p);
        Assert.All(plan.Timeline, c => Assert.Equal("fit", c.Reframe.Mode));
        var plain = plan with { Timeline = plan.Timeline.Select(c => c with { Zoom = 1.0 }).ToList() };
        var outp = await new Renderer(Ff).RenderAsync(plain, p, assets, Path.Combine(_dir, "vert_out.mp4"), true, TestContext.Current.CancellationToken);
        var c0 = plain.Timeline[0];
        var ps = await Ff.RunFfmpegAsync(["-hide_banner", "-i", outp.Path, "-ss", c0.SrcIn.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", _vertical,
            "-filter_complex", "[0:v]trim=0:1,setpts=PTS-STARTPTS[a];[1:v]trim=0:1,setpts=PTS-STARTPTS,scale=540:960[b];[a][b]psnr", "-f", "null", "-"], TestContext.Current.CancellationToken);
        var psnr = Regex.Match(ps.StdErr, @"average:([\d.]+|inf)").Groups[1].Value;
        Assert.True(psnr == "inf" || double.Parse(psnr, System.Globalization.CultureInfo.InvariantCulture) > 30, $"PSNR {psnr}");
    }

    [Fact] // regression from E2E: a 13-char emphasis clause overflowed x 8–969; auto-fit must keep every telop inside (measured)
    public async Task Long_telops_are_shrunk_to_fit_the_safe_zone()
    {
        var (plan, _, ctx) = Build(_landscape, 1280, 720);
        var t0 = plan.Telops.First(t => t.Type == TelopType.Caption);
        var stressed = plan with
        {
            Telops = [.. plan.Telops.Where(t => t.Type != TelopType.Emphasis).Select(t => t.Id == t0.Id ? t with { Text = "あいうえおかきくけこさしすせそたちつてとなに" } : t),
                new Telop { Id = "e1", Type = TelopType.Emphasis, Start = 1, End = 3, Text = "それは100円じゃなくて300円", Style = "Emph", Animation = "pop" }],
        };
        var res = await new QaEngine(Ff).SafeZoneAsync(ctx with { Plan = stressed }, Path.Combine(_dir, "qa_fit"), TestContext.Current.CancellationToken);
        Assert.True(res.Status == QaStatus.Pass, res.Measured);
        Assert.Contains(@"\fs", AssWriter.Write(stressed, ctx.Preset));
    }

    [Fact] // 字幕 in every caption preset: a line of the automatically chosen maximum length, and a two-line caption, fit the safe zone as libass draws them — without shrinking
    public async Task Caption_lengths_fit_the_safe_zone_as_rendered()
    {
        var (plan, _, _) = Build(_landscape, 1280, 720);
        Assert.Contains(Fixtures.Presets.Values, p => p.Id == "P13" && p.HasTelop("caption") && p.Telop.Caption.MaxLines == 2);   // a preset whose default caption already wraps is covered as shipped
        foreach (var p in Fixtures.Presets.Values.Where(p => p.HasTelop("caption")))
        {
            int n = AssWriter.CaptionLineChars(p);
            var two = p with { Telop = p.Telop with { Caption = p.Telop.Caption with { MaxLines = 2 } } };
            var twoText = n >= 14 ? "今日はみんなでパワーアップの方法を話します" : "今日はみんなでパワーの話をします";
            foreach (var (pp, text) in new[] { (p, new string('漢', n)), (two, twoText) })
            {
                var tp = plan with { Preset = pp.Ref, Telops = [new Telop { Id = "c", Type = TelopType.Caption, Start = 1, End = 3, Text = text, Style = "Caption" }] };
                var ass = AssWriter.Write(tp, pp);
                Assert.DoesNotContain(@"\fs", ass);
                int perLine = AssWriter.CaptionLineChars(pp, tp.Canvas.Width);
                if (pp.Telop.Caption.MaxLines > 1) Assert.True(twoText.Length > perLine, $"{pp.Id}: two-line sample must exceed one line ({perLine} chars)");
                int expectedLines = Math.Min(pp.Telop.Caption.MaxLines, (text.Length + perLine - 1) / perLine);
                var dialogues = ass.Split('\n').Where(l => l.StartsWith("Dialogue:") && l.Contains(text[..2])).ToList();
                Assert.NotEmpty(dialogues);
                Assert.All(dialogues, d => Assert.Equal(expectedLines, d.Split(@"\N").Length));   // wraps exactly when the text is longer than one rendered line and the preset allows more lines
                var res = await new QaEngine(Ff).SafeZoneAsync(new QaContext(tp, pp, Tr, ["a1"], []), Path.Combine(_dir, $"qa_cap_{pp.Id}_{pp.Telop.Caption.MaxLines}"), TestContext.Current.CancellationToken);
                Assert.True(res.Status == QaStatus.Pass, $"{pp.Id} lines={pp.Telop.Caption.MaxLines}: {res.Measured}");
            }
        }
    }

    [Fact] // H-05 negative test: telops pushed into the bottom UI band must be detected by measurement
    public async Task SafeZone_check_detects_violations()
    {
        var p = Fixtures.Presets["P4"];
        var unsafeP = p with { Telop = p.Telop with { Caption = p.Telop.Caption with { MarginV = 120 } } };   // bypasses PresetStore.Validate on purpose
        var (plan, _, _) = Build(_landscape, 1280, 720, unsafeP);
        var bad = await new QaEngine(Ff).SafeZoneAsync(new QaContext(plan, unsafeP, Tr, ["a1"], []), Path.Combine(_dir, "qa_bad"), TestContext.Current.CancellationToken);
        Assert.Equal(QaStatus.Fail, bad.Status);
        var (okPlan, _, ctx) = Build(_landscape, 1280, 720);
        var ok = await new QaEngine(Ff).SafeZoneAsync(ctx, Path.Combine(_dir, "qa_ok"), TestContext.Current.CancellationToken);
        Assert.Equal(QaStatus.Pass, ok.Status);
    }

    [Fact] // FR-ING-02/04 via MediaProbe
    public async Task Probe_rejects_non_video_and_reports_metadata()
    {
        var bogus = Path.Combine(_dir, "bogus.mp4"); await File.WriteAllTextAsync(bogus, "not a video", TestContext.Current.CancellationToken);
        var probe = new MediaProbe(Ff);
        var ex = await Assert.ThrowsAsync<IngestException>(() => probe.ProbeAsync(bogus, TestContext.Current.CancellationToken));
        Assert.Equal("E-ING-UNSUPPORTED", ex.Code);
        var m = await probe.ProbeAsync(_landscape, TestContext.Current.CancellationToken);
        Assert.Equal((1280, 720, 1), (m.Width, m.Height, m.AudioChannels));
        Assert.False(m.Vfr);
        Assert.DoesNotContain(MediaProbe.Validate(m), w => w.StartsWith("E-"));
    }
}
