using System.Globalization;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>Variety look (P1/P3), 伏せ字 and thumbnail selection — docs/IMPLEMENTATION.md §12.</summary>
public class VarietyTests : IAsyncLifetime
{
    static readonly Transcript Tr = Fixtures.Talk;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-var-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    static Preset P3 => Fixtures.Presets["P3"];
    const int W = 1080, H = 1920;

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static EditPlan Plan(Preset p, List<Telop> telops, double dur = 10) => new()
    {
        EditplanId = "e", ProjectId = "p", Preset = p.Ref, Telops = telops, Audio = new AudioSpec { Master = "a1" },
        Timeline = [new Clip { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = dur }],
    };

    static Telop T(string id, TelopType type, double s, double e, string text, string style, string? anim = null) =>
        new() { Id = id, Type = type, Start = s, End = e, Text = text, Style = style, Animation = anim };

    [Fact]
    public void Only_entertainment_presets_use_the_variety_look()
    {
        Assert.Equal(["P1", "P3"], Fixtures.Presets.Values.Where(p => p.Telop.IsVariety).Select(p => p.Id).Order().ToList());
        foreach (var p in Fixtures.Presets.Values.Where(p => p.Telop.IsVariety))
        {
            Assert.Equal(124, p.Telop.Caption.FontPx);
            Assert.Equal(10, p.Telop.Caption.MaxCharsPerLine);   // 8 doubled the CPS warnings on the fixture; 10 keeps them at the old level
        }
        Assert.Throws<PresetValidationException>(() => PresetStore.Validate(P3 with { Telop = P3.Telop with { Look = "loud" } }));
    }

    [Fact] // geometry: every band inside the safe zone; the bands of telops that can be on screen together never overlap
    public void Variety_bands_fit_the_safe_zone_and_do_not_collide()
    {
        var (top, bottom, left, right) = QaEngine.SafeZone;
        var cap = AssWriter.Band(TelopType.Caption, P3, H)!.Value;
        var emph = AssWriter.Band(TelopType.Emphasis, P3, H)!.Value;
        var tsu = AssWriter.Band(TelopType.Tsukkomi, P3, H)!.Value;
        var counter = AssWriter.Band(TelopType.Counter, P3, H)!.Value;
        foreach (var b in new[] { cap, emph, tsu, counter }) { Assert.True(b.Top >= top, $"{b}"); Assert.True(b.Bottom <= H - bottom, $"{b}"); }
        // captions are always on screen; one punch (emphasis OR tsukkomi) at a time; the counter can coincide with a punch
        Assert.True(emph.Bottom <= cap.Top, $"emph {emph} / caption {cap}");
        Assert.True(tsu.Bottom <= cap.Top, $"tsukkomi {tsu} / caption {cap}");
        Assert.True(counter.Bottom <= Math.Min(emph.Top, tsu.Top), $"counter {counter}");
        // width: 10 caption chars and 5 emphasis chars render at full size (no shrink) within the horizontal safe zone
        var c = P3.Telop.Caption;
        var (cs, co, cx) = AssWriter.Metrics("Caption", P3);
        Assert.Equal(cs, AssWriter.FitFontSize("あいうえおかきくけこ", cs, co, W, c.MarginR, cx));
        var (es, eo, ex) = AssWriter.Metrics("Emph", P3);
        Assert.Equal(es, AssWriter.FitFontSize("あいうえお", es, eo, W, c.MarginR, ex));
        // the hierarchy the references show: punch ≈ 1.5–2× the caption, caption ≫ the standard 80 px
        Assert.InRange(es / (double)cs, 1.5, 2.0);
        Assert.True(left == AssWriter.MarginL && right == c.MarginR);
    }

    /// <summary>Glyph bounding box of an ASS file rendered by libass at one time (full canvas, luminance &gt; 48 on black).</summary>
    async Task<(int x1, int x2, int y1, int y2)> MeasureAsync(EditPlan plan, Preset p, double t, string name)
    {
        var ass = Path.Combine(_dir, name + ".ass");
        await File.WriteAllTextAsync(ass, AssWriter.Write(plan, p), new System.Text.UTF8Encoding(false), Ct);
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=black:s={W}x{H}:r=10:d={(plan.Duration + 1).ToString(IC)}",
            "-vf", $"subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(Ff.FontsDir)},select='eq(n,{(int)Math.Round(t * 10)})'", "-fps_mode", "passthrough", "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", "-"], Ct, stdout: ms);
        var buf = ms.ToArray(); Assert.Equal(W * H, buf.Length);
        int x1 = W, x2 = -1, y1 = H, y2 = -1;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (buf[y * W + x] > 48) { x1 = Math.Min(x1, x); x2 = Math.Max(x2, x); y1 = Math.Min(y1, y); y2 = Math.Max(y2, y); }
        return (x1, x2, y1, y2);
    }

    [Fact] // the band table must describe what libass actually draws (independent measurement)
    public async Task Rendered_variety_telops_stay_inside_their_bands()
    {
        var cases = new (TelopType Type, string Style, string Text)[]
        {
            (TelopType.Caption, "Caption", "芝が言ってたんです"), (TelopType.Emphasis, "Emph", "アウトです"), (TelopType.Tsukkomi, "Tsukkomi", "いや多すぎ！"),
        };
        foreach (var (type, style, text) in cases)
        {
            var plan = Plan(P3, [T("x", type, 0, 5, text, style)]);
            var (x1, x2, y1, y2) = await MeasureAsync(plan, P3, 1.0, "band_" + style);
            var band = AssWriter.Band(type, P3, H)!.Value;
            Assert.True(x2 > 0, $"{style}: nothing drawn");
            Assert.True(y1 >= band.Top && y2 <= band.Bottom, $"{style}: drawn y{y1}-{y2}, band {band.Top:F0}-{band.Bottom:F0}");
            Assert.True(x1 >= QaEngine.SafeZone.Left && x2 <= W - QaEngine.SafeZone.Right, $"{style}: drawn x{x1}-{x2}");
            if (type == TelopType.Caption) Assert.True(y2 - y1 >= 0.6 * P3.Telop.Caption.FontPx, $"caption glyphs only {y2 - y1}px tall");   // fill height ≈ 0.65 × Fontsize (calibrated)
        }
        // H-05 (the production check) agrees, also for a long caption that has to shrink
        var all = Plan(P3, [T("c", TelopType.Caption, 0, 2, "あいうえおかきくけこさしすせそ", "Caption"), T("e", TelopType.Emphasis, 2, 4, "それは100円じゃなくて300円", "Emph", "pop"),
            T("k", TelopType.Tsukkomi, 4, 6, "長すぎるツッコミですよこれは", "Tsukkomi", "pop"), T("h", TelopType.Headline, 0, 6, "1時間で1万円使い切れるか", "Headline")], 7);
        var qa = await new QaEngine(Ff).SafeZoneAsync(new QaContext(all, P3, Tr, ["a1"], []), Path.Combine(_dir, "qa"), Ct);
        Assert.True(qa.Status == QaStatus.Pass, qa.Measured);
    }

    [Fact]
    public void Variety_ass_has_halo_condensed_captions_red_keywords_and_escapes_text()
    {
        var plan = Plan(P3, [T("c", TelopType.Caption, 0, 2, "芝が言って{た}", "Caption"), T("e", TelopType.Emphasis, 0.5, 2, "芝が", "Emph", "pop"),
            T("k", TelopType.Tsukkomi, 3, 4, "そこ？", "Tsukkomi", "pop")]);
        var ass = AssWriter.Write(plan, P3);
        Assert.Contains($"Style: Caption,{AssWriter.FontName},124,", ass);
        Assert.Contains(",85,100,0,0,1,10,", ass);                                                      // ScaleX 85 (長体), outline 10
        Assert.Contains($@"Dialogue: 0,0:00:00.00,0:00:02.00,Halo,,0,0,0,,{{\blur10}}芝が言って｛た｝", ass);
        Assert.Contains($@"Dialogue: 1,0:00:00.00,0:00:02.00,Caption,,0,0,0,,{{\c{AssWriter.Variety.KeywordRed}}}芝が{{\c&HFFFFFF&}}言って｛た｝", ass);
        Assert.Contains($"Style: Tsukkomi,{AssWriter.FontName},180,&H00FFFFFF,&H0000FFFF,{AssWriter.Variety.BoxRed}", ass);   // red zabuton
        Assert.Contains("Dialogue: 2,0:00:03.00,", ass);
        // standard presets are not affected: no halo, no colouring
        var std = AssWriter.Write(Plan(Fixtures.Presets["P4"], plan.Telops), Fixtures.Presets["P4"]);
        Assert.DoesNotContain("Halo", std); Assert.DoesNotContain(AssWriter.Variety.KeywordRed, std);
    }

    [Fact]
    public void Colorize_marks_longest_matches_and_never_unescapes()
    {
        Assert.Equal(@"{\c&H2A2AE8&}1万円{\c&HFFFFFF&}で｛x｝", AssWriter.Colorize("1万円で{x}", ["万円", "1万円"]));
        Assert.Equal("ふつう", AssWriter.Colorize("ふつう", ["x"]));
        Assert.Equal("aあ", AssWriter.Colorize("aあ", ["あ"]));                           // single characters are not coloured
    }

    [Fact] // one punch telop at a time
    public void Punch_telops_never_overlap_in_the_variety_look()
    {
        var notes = new List<string>();
        var res = TelopExtras.OnePunchAtATime([
            T("c", TelopType.Caption, 0, 9, "字幕", "Caption"),
            T("e1", TelopType.Emphasis, 1, 2.6, "強調", "Emph"),
            T("d", TelopType.Sfx, 1, 2, "ドーン", "Sfx"),                 // same start: emphasis wins
            T("k", TelopType.Tsukkomi, 2.0, 3.4, "そこ？", "Tsukkomi"),   // ≥ 0.8 s after e1: e1 is cut at 2.0
            T("k2", TelopType.Tsukkomi, 2.3, 3.0, "早い", "Tsukkomi"),    // 0.3 s after k: dropped with a note
        ], notes);
        Assert.Equal(["c", "e1", "k"], res.Select(t => t.Id).ToList());
        Assert.Equal(2.0, res.Single(t => t.Id == "e1").End);
        var punch = res.Where(t => t.Type != TelopType.Caption).OrderBy(t => t.Start).ToList();
        for (int i = 1; i < punch.Count; i++) Assert.True(punch[i].Start >= punch[i - 1].End - 1e-9);
        Assert.Single(notes);
    }

    // ---------- 伏せ字 ----------

    [Fact]
    public void Mask_text_is_width_and_case_insensitive_and_crosses_word_boundaries()
    {
        Assert.Equal("●●●で帰れない", Masking.MaskText("1万円で帰れない", ["１万円"]));
        Assert.Equal("Hi ●●● san", Masking.MaskText("Hi ABC san", ["abc"]));
        var tr = new Transcript { Provider = "t", Words = [new("w1", 0, 0.4, "あの", 1), new("w2", 0.4, 0.7, "名", 1), new("w3", 0.7, 1.0, "倉さん", 1), new("w4", 1.5, 2, "です", 1)],
            Sentences = [new Sentence(0, 0, 2, "あの名倉さんです", 1, 0, 3)] };
        var (m, spans) = Masking.Apply(tr, ["名倉"]);
        Assert.Equal(["あの", "●", "●さん", "です"], m.Words.Select(w => w.Text).ToList());
        Assert.Equal("あの●●さんです", m.Sentences[0].Text);
        Assert.Equal([new Interval(0.4, 1.0)], spans);                                                  // whole words, merged
        var clips = new List<Clip> { new() { ClipId = "a", AssetId = "x", SrcIn = 0.5, SrcOut = 0.8 }, new() { ClipId = "b", AssetId = "x", SrcIn = 3, SrcOut = 4 } };
        Assert.Equal([new Interval(0, 0.3)], Masking.ToOutput(clips, spans));
        Assert.Equal(["a", "b"], Masking.Clean([" a ", "", "A", "ａ", "b"]));
    }

    [Fact] // the hidden word is gone from every telop / SRT (also the LLM hook), the dialogue is bleeped, and QA H-10 recomputes both independently
    public async Task Masked_words_are_hidden_everywhere_and_checked_by_qa()
    {
        var assets = new List<SourceAsset> { new() { Id = "a1", ProjectId = "p", Path = "x.mp4", Sha256 = "x", Role = AssetRole.Main, Media = Fixtures.Landscape() } };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P3"], Options = new JobOptions { MaskWords = ["１万円"] } };
        var cand = new Candidate { Id = "c1", Segments = [new Interval(8.8, 40)], HookText = "1万円で帰れない", Headline = "100円だけで1万円", EmphasisWords = ["1万円"] };
        var (plan, notes) = await PlanBuilder.BuildAsync(new PlanRequest(cand, Tr, Fixtures.Signals(Tr), P3, assets, assets[0], "p", job, new Dictionary<string, List<FaceSample>>(), null, null), Ct);
        var all = string.Concat(plan.Telops.Select(t => t.Text)) + AssWriter.Srt(plan) + AssWriter.Write(plan, P3);
        Assert.DoesNotContain("1万円", all);
        Assert.Contains("●●●", all);
        Assert.Contains(plan.Telops, t => t.Type == TelopType.Hook && t.Text.StartsWith("●●●"));
        Assert.NotEmpty(plan.Audio.Bleeps);
        Assert.Contains(notes, n => n.StartsWith("伏せ字"));
        var ctx = new QaContext(plan, P3, Tr, ["a1"], [], null, job.Options.MaskWords);
        Assert.Contains(QaEngine.PlanChecks(ctx), q => q.CheckId == "H-10" && q.Status == QaStatus.Pass);
        // a hand edit that brings the word back, or a lost bleep, blocks the export
        var leaked = plan with { Telops = plan.Telops.Select((t, i) => i == 0 ? t with { Text = "1万円です" } : t).ToList() };
        Assert.Contains(QaEngine.PlanChecks(ctx with { Plan = leaked }), q => q.CheckId == "H-10" && q.Status == QaStatus.Fail);
        var silent = plan with { Audio = plan.Audio with { Bleeps = [] } };
        Assert.Contains(QaEngine.PlanChecks(ctx with { Plan = silent }), q => q.CheckId == "H-10" && q.Status == QaStatus.Fail);
        // no mask words → no H-10 row
        Assert.DoesNotContain(QaEngine.PlanChecks(ctx with { MaskWords = null }), q => q.CheckId == "H-10");
    }

    static double Tone(short[] pcm, double t0, double t1, double hz)
    {
        // Goertzel power at hz, in dB relative to full scale
        int a = (int)(t0 * 48000), b = (int)(t1 * 48000); double w = 2 * Math.PI * hz / 48000, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = a; i < b; i++) { double s0 = pcm[i] / 32768.0 + c * s1 - s2; s2 = s1; s1 = s0; }
        double p = s1 * s1 + s2 * s2 - c * s1 * s2;
        return 10 * Math.Log10(p / Math.Pow((b - a) / 2.0, 2) + 1e-12);
    }

    [Fact] // the dialogue under a masked word is replaced by the 1 kHz tone, exactly inside the span (measured on the decoded PCM)
    public async Task Bleep_replaces_the_dialogue_inside_the_span_only()
    {
        var src = Path.Combine(_dir, "dlg.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=30:d=6", "-f", "lavfi", "-i", "sine=f=440:d=6,volume=0.5",
            "-shortest", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", src], Ct);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = new() { Id = "a1", ProjectId = "p", Path = src, Sha256 = "x", Role = AssetRole.Main, Media = new MediaInfo { Duration = 6, Width = 320, Height = 180, Fps = 30, AudioChannels = 1 } } };
        var plan = Plan(P3, [], 6) with { Audio = new AudioSpec { Master = "a1", SfxEnabled = false, Bleeps = [new Interval(2.0, 3.0)] } };
        var lib = new AudioLibrary(Ff, Path.Combine(_dir, "audio")); await lib.EnsureAsync(Ct);
        var r = new Renderer(Ff) { BgmResolver = id => lib.Find(id)?.Path, SfxResolver = lib.SfxPath };
        var inp = r.ResolveInputs(plan, assets);
        var wav = Path.Combine(_dir, "bleep.wav");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", .. inp.Flat, "-filter_complex", Renderer.BuildAudioOnlyGraph(plan, inp, "aresample=48000,aformat=channel_layouts=mono"), "-map", "[m]", "-ar", "48000", "-c:a", "pcm_s16le", wav], Ct);
        var bytes = await File.ReadAllBytesAsync(wav, Ct); var pcm = new short[(bytes.Length - 44) / 2]; Buffer.BlockCopy(bytes, 44, pcm, 0, pcm.Length * 2);
        double dlgIn = Tone(pcm, 2.05, 2.95, 440), dlgOut = Tone(pcm, 0.5, 1.9, 440), bpIn = Tone(pcm, 2.05, 2.95, 1000), bpOut = Tone(pcm, 3.1, 5.5, 1000);
        Assert.True(dlgOut - dlgIn >= 40, $"dialogue {dlgOut:F1} dB outside vs {dlgIn:F1} dB inside");
        Assert.True(bpIn - bpOut >= 40, $"tone {bpIn:F1} dB inside vs {bpOut:F1} dB outside");
        Assert.InRange(bpIn, -20, -6);
    }

    // ---------- thumbnails ----------

    [Fact]
    public async Task Thumbnails_prefer_punch_moments_with_the_biggest_face_and_keep_the_opening()
    {
        var plan = Plan(P3, [
            T("c", TelopType.Caption, 0, 20, "字幕", "Caption"),
            T("e", TelopType.Emphasis, 3, 4.6, "強調", "Emph", "pop"),
            T("k", TelopType.Tsukkomi, 8, 9.4, "そこ？", "Tsukkomi", "pop") with { Approved = false },      // unapproved: not a candidate
            T("s", TelopType.Sfx, 12, 13.2, "ワハハ", "Sfx", "pop"),
        ], 20);
        Assert.Equal([3.45, 12.45, 10, 15], ThumbnailPicker.Candidates(plan));   // the 5 s fallback is < 2 s from 3.45
        var picked = await ThumbnailPicker.PickAsync(plan, t => Task.FromResult(t switch { 12.45 => 0.3, 15 => 0.2, _ => 0.05 }));
        Assert.Equal([12.45, 15, 1.0], picked);
        Assert.Equal([3.45, 12.45, 1.0], await ThumbnailPicker.PickAsync(plan, null));                   // no face model: plan order
    }
}
