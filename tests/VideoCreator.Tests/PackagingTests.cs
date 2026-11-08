using System.Globalization;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>Opening / closing videos and ~1 s scene-change (tsukkomi) cards: timeline mapping, placement, QA, editing, and a real FFmpeg render.</summary>
public class PackagingTests : IAsyncLifetime
{
    static readonly Transcript Tr = Fixtures.Talk;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-pack-" + Guid.NewGuid().ToString("N")[..8]);
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static Clip C(string id, double masterIn, double len, string role = "body") => new() { ClipId = id, AssetId = "a1", SrcIn = masterIn, SrcOut = masterIn + len, Role = role };

    static List<Insert> Inserts(double total) =>
    [
        new() { Id = "opening", Kind = "opening", At = 0, Duration = 12.1 },
        new() { Id = "card1", Kind = "card", At = 10, Duration = 1.0, Text = "いや長い！" },
        new() { Id = "closing", Kind = "closing", At = total, Duration = 12.1 },
    ];

    [Fact]
    public void Body_times_map_past_inserts_and_spans_split_around_them()
    {
        var ins = Inserts(30).OrderBy(i => i.At).ToList();
        Assert.Equal(12.1, InsertMap.ToFinal(ins, 0), 3);              // content at 0 starts after the opening
        Assert.Equal(23.1, InsertMap.ToFinal(ins, 10), 3);             // a start at the card plays after it
        Assert.Equal(22.1, InsertMap.ToFinal(ins, 10, end: true), 3);  // an end at the card plays before it
        Assert.Equal(43.1, InsertMap.ToFinal(ins, 30, end: true), 3);
        Assert.Equal([(20.1, 22.1), (23.1, 25.1)], InsertMap.Span(ins, 8, 12).Select(x => (Math.Round(x.Start, 3), Math.Round(x.End, 3))).ToList());
        var spans = InsertMap.InsertSpans(ins);
        Assert.Equal([0, 22.1, 43.1], spans.Select(s => Math.Round(s.Start, 3)).ToList());
    }

    [Fact]
    public void Brand_files_are_classified_by_name_and_the_canvas_shaped_variant_is_picked()
    {
        Assert.Equal("opening", BrandKitService.Classify(@"C:\x\Opening-9x16.mp4"));
        Assert.Equal("closing", BrandKitService.Classify(@"C:\x\Closing-16x9.mp4"));
        Assert.Equal("opening", BrandKitService.Classify(@"C:\x\channel_intro.mov"));
        Assert.Equal("closing", BrandKitService.Classify(@"C:\x\エンディング.mp4"));
        Assert.Equal("card", BrandKitService.Classify(@"C:\x\stamp.png"));
        Assert.Null(BrandKitService.Classify(@"C:\x\random.mp4"));
        Assert.Null(BrandKitService.Classify(@"C:\x\notes.txt"));
        var files = new List<BrandFile>
        {
            new() { Path = "o169", Kind = "opening", Width = 1280, Height = 720, Duration = 12.1 },
            new() { Path = "o916", Kind = "opening", Width = 720, Height = 1280, Duration = 12.1 },
        };
        Assert.Equal("o916", BrandKitService.Pick(files, "opening", new Canvas())!.Path);
        Assert.Equal("o169", BrandKitService.Pick(files, "opening", new Canvas(1920, 1080))!.Path);
        Assert.Equal(1.5, BrandKitService.Upscale(720, 1280, new Canvas()), 3);
    }

    [Fact] // scene changes = source jumps between body clips; cards replace the cross-fade there; the opening goes after the cold open
    public void Planner_places_opening_after_teaser_cards_at_jumps_and_closing_at_end()
    {
        var clips = new List<Clip> { C("t", 60, 2, "teaser"), C("c1", 10, 6), C("c2", 16.3, 6), C("c3", 50, 6) with { TransitionSec = 0.3 }, C("c4", 56.2, 6) };
        var kit = new BrandKit
        {
            RightsConfirmed = true, CardSec = 1.0,
            Files = [new() { Path = "op.mp4", Kind = "opening", Width = 720, Height = 1280, Duration = 12.1, HasAudio = true, Lufs = -15.4 },
                     new() { Path = "ed.mp4", Kind = "closing", Width = 720, Height = 1280, Duration = 12.1, HasAudio = true, Lufs = -15.0 }],
        };
        var notes = new List<string>();
        var (packed, ins) = InsertPlanner.Plan(clips, new JobOptions { Opening = true, Closing = true, SceneCards = true }, kit, new Canvas(), -14, null, notes);
        Assert.Equal(["opening", "card1", "closing"], ins.Select(i => i.Id).ToList());
        Assert.Equal(2, ins[0].At, 3);                     // after the 2 s teaser
        Assert.Equal(1.4, ins[0].GainDb, 2);               // -15.4 LUFS → -14 LUFS
        Assert.Equal(14, ins[1].At, 3);                    // the 16.3 → 50 jump (the 16 → 16.3 jump cut is not a scene change)
        Assert.Equal(22.3, ins[1].SourceT!.Value, 3);
        Assert.Equal(0, packed[3].TransitionSec);          // the card replaces the cross-fade
        Assert.Equal(26, ins[2].At, 3);
        Assert.False(string.IsNullOrEmpty(ins[1].Text));   // no AI: a neutral fixed phrase
        Assert.Contains(notes, n => n.Contains("場面転換カード"));

        var none = InsertPlanner.Plan(clips.ToList(), new JobOptions { Opening = true }, null, new Canvas(), -14, null, notes);
        Assert.Empty(none.Inserts);                        // nothing registered → nothing inserted (and a note says so)
        Assert.Contains(notes, n => n.Contains("オープニング動画が登録されていない"));
    }

    [Fact]
    public void Series_cards_go_to_chapter_starts_snapped_to_a_cut()
    {
        var clips = new List<Clip> { C("t", 300, 3, "teaser") };
        for (int i = 0; i < 20; i++) clips.Add(C($"b{i}", 10 + i * 6.5, 6));
        var se = new SeriesInfo { Label = "前編", Body = new Interval(10, 140), Chapters = [new(0, "見どころ"), new(3, "第1章"), new(40.5, "第2章"), new(90, "第3章")] };
        var b = InsertPlanner.SceneChanges(clips, se);
        Assert.Equal([39, 87], b.Select(x => x.At).ToList());   // nearest cuts to 40.5 and 90; the first chapter (body start) gets none
    }

    [Fact] // real-data case: continuous talk, the nearest cut is 16 s from the chapter start → the clip is cut at the chapter instead
    public void Series_chapter_far_from_any_cut_splits_the_clip()
    {
        var clips = new List<Clip> { C("t", 140, 2.8, "teaser"), C("r", 78, 8, "recap"), C("b1", 101.68, 10.23), C("b2", 112.41, 35.82), C("b3", 148.65, 28.14), C("b4", 177.29, 35.76) };
        var se = new SeriesInfo { Label = "後編", Body = new Interval(101, 213), Chapters = [new(0, "見どころ"), new(2.8, "前編のおさらい"), new(10.8, "第1章"), new(101.3, "第2章")] };
        var (packed, ins) = InsertPlanner.Plan(clips, new JobOptions { SceneCards = true }, null, new Canvas(), -14, se, []);
        Assert.Equal(clips.Count + 1, packed.Count);
        Assert.Equal(clips.Sum(c => c.Length), packed.Sum(c => c.Length), 6);           // nothing moves
        var card = Assert.Single(ins);
        Assert.Equal(101.3, card.At, 3);
        var k = packed.FindIndex(c => c.ClipId == "b4s");
        Assert.Equal(packed[k - 1].SrcOut, packed[k].SrcIn);                             // contiguous halves
        Assert.Equal(177.29 + (101.3 - 84.99), packed[k].SrcIn, 2);
    }

    [Fact]
    public void Qa_counts_cards_for_approval_rights_grounding_and_warns_about_a_leading_opening()
    {
        var p = Fixtures.Presets["P4"];
        var res = Solver.Plan([new Interval(74, 103)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        var plan = new EditPlan { EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips, Audio = new AudioSpec { Master = "a1" } };
        var at = res.Clips[0].Length;
        var ai = plan with { Inserts = [new() { Id = "card1", Kind = "card", At = at, Duration = 1, Text = "そこ？", TextSource = "ai", Approved = false }] };
        QaStatus Check(EditPlan x, string id) => QaEngine.PlanChecks(new QaContext(x, p, Tr, ["a1"], [])).Single(q => q.CheckId == id).Status;
        Assert.Equal(QaStatus.Fail, Check(ai, "H-08"));
        Assert.Equal(QaStatus.Pass, Check(PlanEditor.Apply(ai, [new PlanOp("approve_all_cards")]), "H-08"));
        Assert.Equal(QaStatus.Pass, Check(PlanEditor.Apply(ai, [new PlanOp("set_insert_enabled", "card1", Enabled: false)]), "H-08"));
        var invented = PlanEditor.Apply(ai, [new PlanOp("replace_card_text", "card1", "500万円！")]);
        Assert.Equal(QaStatus.Fail, Check(invented, "H-07"));     // a number never said in the clip
        var noRights = plan with { Inserts = [new() { Id = "opening", Kind = "opening", At = 0, Duration = 3, Path = @"C:\x\op.mp4", Width = 720, Height = 1280 }] };
        Assert.Equal(QaStatus.Fail, Check(noRights, "H-09"));
        var ok = noRights with { Inserts = [noRights.Inserts[0] with { RightsConfirmed = true }] };
        Assert.Equal(QaStatus.Pass, Check(ok, "H-09"));
        Assert.Equal(QaStatus.Warn, Check(ok, "S-03"));            // the hook is pushed behind the opening
        Assert.Equal(QaStatus.Warn, Check(ok, "S-12"));            // 720x1280 → 1.5x upscale
        // the preset range is for the body; the whole file must stay under the Shorts cap
        Assert.Equal(QaStatus.Pass, Check(ok, "H-01"));
        var tooLong = ok with { Inserts = [ok.Inserts[0] with { Duration = 160 }] };
        Assert.Equal(QaStatus.Fail, Check(tooLong, "H-01"));
    }

    [Fact]
    public void Card_edits_survive_a_rebuild_and_ass_srt_use_the_final_timeline()
    {
        var p = Fixtures.Presets["P4"];
        var res = Solver.Plan([new Interval(74, 103)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        var telops = TelopBuilder.Build(res.Clips, Tr, p, "あと2個で失敗", "100円足りない", [], []);
        double at = res.Clips[0].Length;
        var plan = new EditPlan
        {
            EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips, Telops = telops, Audio = new AudioSpec { Master = "a1" },
            Inserts = [new() { Id = "opening", Kind = "opening", At = 0, Duration = 3 }, new() { Id = "card1", Kind = "card", At = at, Duration = 1, Text = "さて、次！", TextSource = "fixed", SourceT = 80 }],
        };
        var edited = PlanEditor.Apply(plan, [new PlanOp("replace_card_text", "card1", "そこ？"), new PlanOp("set_insert_enabled", "opening", Enabled: false)]);
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, [new PlanOp("replace_card_text", "card1", new string('あ', 25))]));
        var rebuilt = plan with { Inserts = [plan.Inserts[0], plan.Inserts[1] with { SourceT = 80.2 }] };
        var re = PlanEditor.Reapply(rebuilt, edited);
        Assert.Equal("そこ？", re.Inserts.Single(i => i.Id == "card1").Text);
        Assert.False(re.Inserts.Single(i => i.Id == "opening").Enabled);

        var ass = AssWriter.Write(plan, p);
        Assert.Contains("Style: Card,", ass);
        Assert.Contains(",Card,,0,0,0,,", ass);
        var hook = telops.First(t => t.Type == TelopType.Hook);
        Assert.Contains($"{AssWriter.Time(hook.Start + 3)},", ass);                  // shifted past the 3 s opening
        Assert.DoesNotContain($"Dialogue: 2,{AssWriter.Time(hook.Start)},", ass);
        var srt = AssWriter.Srt(plan);
        Assert.DoesNotContain("00:00:00,", srt.Split('\n').First(l => l.Contains("-->")));
        // nothing is drawn over the card: no event spans the card's final time range
        double cs = InsertMap.ToFinal(plan, at, end: true), ce = cs + 1;
        foreach (var line in ass.Split('\n').Where(l => l.StartsWith("Dialogue:") && !l.Contains(",Card,")))
        {
            var f = line.Split(',');
            double s = TimeSpan.Parse("0" + f[1], IC).TotalSeconds, e = TimeSpan.Parse("0" + f[2], IC).TotalSeconds;
            Assert.False(s < ce - 0.02 && e > cs + 0.02, line);
        }
    }

    sealed class CardLlm(string json) : IHighlightSelector
    {
        public string Name => "fake";
        public string Model => "fake";
        public string? LastUser;
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct) { LastUser = user; return Task.FromResult(json); }
    }

    [Fact]
    public async Task Ai_card_text_is_validated_and_unapproved()
    {
        var caps = new List<Telop> { new() { Id = "t1", Start = 1, End = 2, Text = "100個買います" }, new() { Id = "t2", Start = 3, End = 4, Text = "次の話" } };
        var ins = new List<Insert> { new() { Id = "card1", Kind = "card", At = 2.5, Duration = 1, Text = "さて、次！", TextSource = "fixed" }, new() { Id = "card2", Kind = "card", At = 2.6, Duration = 1, Text = "ところで…", TextSource = "fixed" } };
        var llm = new CardLlm("""{"items":[{"card_id":"card1","text":"いや多すぎ！"},{"card_id":"card2","text":"バカなの？"}]}""");
        var n = await InsertPlanner.AiTextAsync(llm, ins, caps, "100個買います次の話", Ct);
        Assert.Equal(1, n);
        Assert.Equal(("いや多すぎ！", "ai", false), (ins[0].Text, ins[0].TextSource, ins[0].Approved));
        Assert.Equal(("ところで…", "fixed", true), (ins[1].Text, ins[1].TextSource, ins[1].Approved));   // the insult was rejected; the safe phrase stays
        Assert.Contains("100個買います", llm.LastUser);
    }

    async Task<string> MakeAsync(string name, string video, double tone, double gain, double dur, string vf = "null")
    {
        var f = Path.Combine(_dir, name);
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"{video}:d={dur.ToString(IC)}", "-f", "lavfi", "-i", $"sine=f={tone.ToString(IC)}:d={dur.ToString(IC)}",
            "-vf", vf, "-af", $"volume={gain.ToString(IC)}", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-ar", "96000", "-shortest", f], Ct);
        return f;
    }

    async Task<(int R, int G, int B)> PixelAsync(string file, double t, string crop)
    {
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", t.ToString("0.###", IC), "-i", file, "-frames:v", "1", "-vf", $"crop={crop},scale=1:1:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], Ct, stdout: ms);
        var b = ms.ToArray(); return (b[0], b[1], b[2]);
    }

    async Task<double> YellowShareAsync(string file, double t, string crop)
    {
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", t.ToString("0.###", IC), "-i", file, "-frames:v", "1", "-vf", $"crop={crop}", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], Ct, stdout: ms);
        var b = ms.ToArray(); int n = 0;
        for (int i = 0; i + 2 < b.Length; i += 3) if (b[i] > 200 && b[i + 1] > 170 && b[i + 2] < 90) n++;
        return n / (b.Length / 3.0);
    }

    async Task<double> LufsAsync(string file, double t0, double t1)
    {
        var r = await Ff.RunFfmpegAsync(["-hide_banner", "-ss", t0.ToString("0.###", IC), "-t", (t1 - t0).ToString("0.###", IC), "-i", file, "-vn", "-af", "ebur128", "-f", "null", "-"], Ct);
        var m = System.Text.RegularExpressions.Regex.Matches(r.StdErr, @"I:\s+(-?[\d.]+) LUFS");
        return double.Parse(m[^1].Groups[1].Value, IC);
    }

    [Fact] // real FFmpeg: register a kit like C:\Work\HubRadio\OpeningClosing (720p, 96 kHz, both shapes), render opening + card + closing, verify by measurement
    public async Task Final_render_with_opening_card_and_closing_is_measured_correct()
    {
        var kitDir = Path.Combine(_dir, "kit"); Directory.CreateDirectory(kitDir);
        // loud green 9:16 opening, a 16:9 decoy, a blue 16:9-only closing (exercises the blurred-background fit), an unknown file
        File.Move(await MakeAsync("o1.mp4", "color=c=0x00C000:s=720x1280:r=30", 660, 0.9, 3), Path.Combine(kitDir, "Opening-9x16.mp4"));
        File.Move(await MakeAsync("o2.mp4", "color=c=0xC00000:s=1280x720:r=30", 660, 0.9, 3), Path.Combine(kitDir, "Opening-16x9.mp4"));
        File.Move(await MakeAsync("c1.mp4", "color=c=0x0000C0:s=1280x720:r=30", 440, 0.05, 2), Path.Combine(kitDir, "Closing-16x9.mp4"));
        File.Move(await MakeAsync("x.mp4", "color=c=gray:s=320x240:r=30", 440, 0.5, 2), Path.Combine(kitDir, "random.mp4"));
        var (kit, problems) = await BrandKitService.ScanAsync(Ff, [kitDir], true, 1.0, Ct);
        Assert.Contains(problems, x => x.Contains("random.mp4"));
        Assert.Equal(3, kit.Files.Count);
        Assert.All(kit.Files, f => Assert.InRange(f.Lufs, -60, 0));
        Assert.True(kit.Files.Single(f => f.Path.EndsWith("Opening-9x16.mp4")).Lufs > kit.Files.Single(f => f.Kind == "closing").Lufs + 20);

        var src = await MakeAsync("src.mp4", "testsrc2=s=1280x720:r=30", 330, 0.3, 130, "hue=s=0");   // grey, so only the card text is yellow
        var p = Fixtures.Presets["P4"];
        var media = new MediaInfo { Duration = 130, Width = 1280, Height = 720, Fps = 30, AudioChannels = 1 };
        var res = Solver.Plan([new Interval(74, 103)], Tr, Fixtures.Signals(Tr), p, media, "a1", ["300円の商品"]);
        var clips = res.Clips;
        var (packed, ins) = InsertPlanner.Plan(clips.ToList(), new JobOptions { Opening = true, Closing = true }, kit, new Canvas(), -14, null, []);
        double cardAt = clips.Take(2).Sum(c => c.Length);
        ins.Insert(1, new Insert { Id = "card1", Kind = "card", At = Math.Round(cardAt, 3), Duration = 1, Text = "いや長い！", TextSource = "user", Sfx = InsertPlanner.CardSfx, SourceT = clips[1].MasterOut });
        var plan = new EditPlan
        {
            EditplanId = "ep", ProjectId = "p", Preset = p.Ref, Timeline = packed, Inserts = ins,
            Telops = TelopBuilder.Build(packed, Tr, p, "あと2個で失敗", "100円足りない", ["300円の商品"], []),
            Audio = new AudioSpec { Master = "a1" },
        };
        Assert.Equal(3.0, plan.Inserts.Single(i => i.Id == "opening").Duration, 2);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = new() { Id = "a1", ProjectId = "p", Path = src, Sha256 = "x", Media = media } };
        var ctx = new QaContext(plan, p, Tr, ["a1"], []);
        var outp = await new Renderer(Ff).RenderAsync(plan, p, assets, Path.Combine(_dir, "final.mp4"), preview: false, Ct);
        var qa = await new QaEngine(Ff).RunAsync(ctx, outp.Path, preview: false, Path.Combine(_dir, "qa"), Ct);
        Assert.All(qa.Where(q => q.Hard), q => Assert.True(q.Status == QaStatus.Pass, $"{q.CheckId} {q.Measured} vs {q.Threshold}"));
        Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "S-06").Status);   // the frozen card is not reported as a freeze

        var probe = await Ff.RunProbeAsync(["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=duration", "-of", "csv=p=0", outp.Path], Ct);
        // frame-exact video: no drift from rounding each clip (the audio stream only adds AAC padding)
        Assert.InRange(double.Parse(probe.StdOut.Trim(), IC) - plan.TotalDuration, -0.034, 0.034);

        var spans = InsertMap.InsertSpans(InsertMap.Active(plan)).ToDictionary(x => x.Insert.Id);
        var (_, os, oe) = spans["opening"];
        Assert.Equal(packed.TakeWhile(c => c.Role == "teaser").Sum(c => c.Length), os, 3);   // P4 has a cold open: teaser first, then the title
        var op = await PixelAsync(outp.Path, (os + oe) / 2, "1080:1920:0:0");
        Assert.True(op.G > 150 && op.R < 60 && op.B < 60, $"opening should be the green 9:16 variant: {op}");
        var capBand = await PixelAsync(outp.Path, (os + oe) / 2, "1080:200:0:1150");
        Assert.True(capBand.G > 150 && capBand.R < 60, $"no caption over the opening: {capBand}");
        var (_, cs, ce) = spans["card1"];
        var yellow = await YellowShareAsync(outp.Path, (cs + ce) / 2, "560:160:210:726");
        Assert.True(yellow > 0.08, $"yellow card text expected, share {yellow:P1}");
        Assert.True(await YellowShareAsync(outp.Path, cs - 0.3, "560:160:210:726") < 0.01, "no card text before the card");
        var (_, ls, le) = spans["closing"];
        var mid = await PixelAsync(outp.Path, (ls + le) / 2, "200:200:440:860");
        Assert.True(mid.B > 150 && mid.R < 60, $"closing (fitted 16:9) in the middle: {mid}");

        // the picture cuts exactly where the plan says (telops, SFX and card text are timed from the plan)
        var cuts = System.Text.RegularExpressions.Regex.Matches((await Ff.RunFfmpegAsync(["-hide_banner", "-i", outp.Path, "-an", "-vf", "scale=135:-2,scdet=threshold=25", "-f", "null", "-"], Ct)).StdErr, @"lavfi\.scd\.time: ([\d.]+)")
            .Select(m => double.Parse(m.Groups[1].Value, IC)).ToList();
        foreach (var t in new[] { os, oe, ls })
            Assert.Contains(cuts, c => Math.Abs(c - t) <= 0.05);

        // the bumpers' own music sits at the same loudness as the body (normalised before mixing), not 15+ LU apart
        double lOpen = await LufsAsync(outp.Path, os + 0.2, oe - 0.2), lBody = await LufsAsync(outp.Path, oe + 0.5, cs - 0.2);
        Assert.True(Math.Abs(lOpen - lBody) < 4, $"opening {lOpen:F1} LUFS vs body {lBody:F1} LUFS");
    }
}
