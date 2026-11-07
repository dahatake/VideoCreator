using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>A synthetic talk whose topic boundaries are known by construction (the ground truth for the split).</summary>
public static class SyntheticTalk
{
    static readonly string[][] Vocab =
    [
        ["激辛", "ラーメン", "スープ", "唐辛子", "完食", "挑戦"],
        ["節約", "財布", "千円", "スーパー", "弁当", "残金"],
        ["釣り", "海岸", "魚", "エサ", "竿", "船長"],
        ["料理", "包丁", "野菜", "フライパン", "火加減", "味見"],
        ["登山", "山頂", "地図", "リュック", "休憩", "天気"],
        ["映画", "監督", "俳優", "脚本", "映画館", "ポップコーン"],
        ["勉強", "試験", "教科書", "先生", "宿題", "合格"],
        ["旅行", "空港", "飛行機", "ホテル", "観光", "お土産"],
    ];
    public const double SentenceSec = 4.0, GapSec = 0.4;

    /// <summary>Returns the transcript and the sentence index where each topic starts.</summary>
    public static (Transcript Tr, List<int> TopicStarts) Build(int[] topicSeconds, int seed = 7)
    {
        var rnd = new Random(seed);
        var words = new List<Word>(); var sents = new List<Sentence>(); var starts = new List<int>();
        double t = 1.0;
        for (int k = 0; k < topicSeconds.Length; k++)
        {
            var v = Vocab[k % Vocab.Length];
            starts.Add(sents.Count);
            int count = Math.Max(2, (int)Math.Round(topicSeconds[k] / (SentenceSec + GapSec)));
            for (int i = 0; i < count; i++)
            {
                string a = v[rnd.Next(v.Length)], b = v[rnd.Next(v.Length)], c = v[rnd.Next(v.Length)];
                string text = i == 0 ? (k == 0 ? $"今日は{v[0]}{v[1]}の企画です。" : $"続いての企画は、{v[0]}{v[1]}です。")
                    : (i % 3) switch { 0 => $"{a}と{b}の話です。", 1 => $"{a}は{b}より{c}ですね。", _ => $"今日の{a}は{b}が大事です。" };
                int spk = i % 2 + 1, first = words.Count;
                var chunks = Enumerable.Range(0, (text.Length + 1) / 2).Select(j => text.Substring(j * 2, Math.Min(2, text.Length - j * 2))).ToList();
                double wd = SentenceSec / chunks.Count;
                for (int j = 0; j < chunks.Count; j++) words.Add(new Word($"w{words.Count}", Math.Round(t + j * wd, 3), Math.Round(t + (j + 1) * wd - 0.02, 3), chunks[j], spk));
                sents.Add(new Sentence(sents.Count, Math.Round(t, 3), Math.Round(t + SentenceSec - 0.02, 3), text, spk, first, words.Count - 1));
                t += SentenceSec + GapSec;
            }
        }
        return (new Transcript { Provider = "synthetic", Duration = Math.Round(t + 1, 3), Words = words, Sentences = sents }, starts);
    }
}

public class SeriesTests
{
    static readonly Preset P4 = Fixtures.Presets["P4"];
    static readonly int[] Topics = [100, 140, 90, 160, 120, 110, 150, 90];   // topic starts ≈ 0,100,240,330,490,610,720,870 s

    static MediaInfo Media(double d) => Fixtures.Landscape(d);

    [Fact] // the split lands on a true topic start near the requested length, not at the clock midpoint
    public void Split_cuts_at_a_topic_boundary_near_the_requested_length()
    {
        var (tr, truth) = SyntheticTalk.Build(Topics);
        var sig = Fixtures.Signals(tr);
        var notes = new List<string>();
        var parts = SeriesPlanner.Split(tr, sig, P4, tr.Duration, 8 * 60, SeriesPlanner.FallbackOutline(tr, sig), notes);
        Assert.Equal(2, parts.Count);
        Assert.Equal(["前編", "後編"], parts[0].Labels);
        Assert.Contains(parts[1].First, truth);
        Assert.Equal(truth[4], parts[1].First);                       // 490 s: the topic start closest to the balanced target
        Assert.All(parts, p => Assert.InRange(p.EstSec, p.MinSec, p.MaxSec));
        Assert.Contains(notes, n => n.Contains("2 本"));
    }

    [Fact] // every word belongs to exactly one part body; nothing between parts is lost; bodies do not overlap
    public void Split_tiles_the_source_without_loss_or_overlap()
    {
        var (tr, _) = SyntheticTalk.Build(Topics);
        var sig = Fixtures.Signals(tr);
        var parts = SeriesPlanner.Split(tr, sig, P4, tr.Duration, 5 * 60, SeriesPlanner.FallbackOutline(tr, sig), []);
        Assert.Equal(3, parts.Count);
        Assert.Equal(["前編", "中編", "後編"], parts.Select(p => p.Label));
        Assert.Equal(0, parts[0].Body.T0);
        Assert.Equal(tr.Duration, parts[^1].Body.T1, 3);
        for (int k = 0; k + 1 < parts.Count; k++)
        {
            Assert.True(parts[k].Body.T1 <= parts[k + 1].Body.T0 + 1e-9, $"overlap between part {k} and {k + 1}");
            Assert.Equal(parts[k].Last + 1, parts[k + 1].First);
        }
        Assert.All(tr.Words, w => Assert.Equal(1, parts.Count(p => w.T0 >= p.Body.T0 - 1e-6 && w.T1 <= p.Body.T1 + 1e-6)));
    }

    [Fact] // an AI judgement that a boundary makes a good cliffhanger can move the cut away from the balanced position (within the tolerance)
    public void Ai_split_scores_can_move_the_cut_within_tolerance()
    {
        int[] topics = [100, 140, 150, 100, 120, 110, 150, 90];   // starts ≈ 0,100,240,390,490,610,720,870 s
        var (tr, truth) = SyntheticTalk.Build(topics);
        var sig = Fixtures.Signals(tr);
        var balanced = SeriesPlanner.Split(tr, sig, P4, tr.Duration, 8 * 60, SeriesPlanner.FallbackOutline(tr, sig), []);
        Assert.Equal(truth[4], balanced[1].First);
        var outline = new ChapterOutline("テスト", truth.Select((s, k) => new ChapterMark(s, $"章{k}", k == 3 ? 10 : 1, s + 1, [])).ToList(), "llm");
        var ai = SeriesPlanner.Split(tr, sig, P4, tr.Duration, 8 * 60, outline, []);
        Assert.Equal(truth[3], ai[1].First);
        Assert.Contains("分けやすさ 10/10", ai[0].Boundary);
    }

    [Fact]
    public void Too_short_for_two_parts_is_rejected_with_guidance()
    {
        var (tr, _) = SyntheticTalk.Build([100, 100]);
        var sig = Fixtures.Signals(tr);
        var ex = Assert.Throws<PlanException>(() => SeriesPlanner.Split(tr, sig, P4, tr.Duration, 10 * 60, SeriesPlanner.FallbackOutline(tr, sig), []));
        Assert.Equal("E-SERIES-SHORT", ex.Code);
        Assert.Contains("短く", ex.Message);
    }

    [Fact] // without AI, chapters come from the lexical shift + topic-change words and their titles are taken from the transcript
    public void Fallback_outline_finds_topic_starts()
    {
        var (tr, truth) = SyntheticTalk.Build(Topics);
        var o = SeriesPlanner.FallbackOutline(tr, Fixtures.Signals(tr));
        var found = o.Chapters.Select(c => c.Start).ToList();
        int hits = truth.Count(t => found.Any(f => Math.Abs(f - t) <= 1));
        Assert.True(hits >= truth.Count - 1, $"found {string.Join(",", found)} vs truth {string.Join(",", truth)}");
        Assert.Contains(o.Chapters, c => c.Title == "節約財布");
        Assert.All(o.Chapters, c => Assert.Empty(JapaneseText.UngroundedClaims(c.Title, tr.FullText)));
    }

    [Fact] // AI output is validated in code: ranges, order, highlight inside its chapter, grounded titles, verbatim emphasis
    public void ParseOutline_rejects_ungrounded_and_out_of_range_items()
    {
        var (tr, truth) = SyntheticTalk.Build(Topics);
        var json = $$"""
        {"series_title":"激辛と100万円","chapters":[
          {"start":{{truth[1]}},"title":"節約の極意","split_score":12,"highlight":{{truth[1] + 2}},"emphasis_words":["財布","存在しない語","とても長すぎる強調語句です"]},
          {"start":{{truth[2]}},"title":"500万円の魚","split_score":7,"highlight":0,"emphasis_words":[]},
          {"start":99999,"title":"範囲外","split_score":5,"highlight":1,"emphasis_words":[]}]}
        """;
        var o = SeriesPlanner.ParseOutline(json, tr, 0, tr.Sentences.Count - 1);
        Assert.Equal(0, o.Chapters[0].Start);                          // inserted: the first chapter starts at the window start
        var c1 = o.Chapters.Single(c => c.Start == truth[1]);
        Assert.Equal(10, c1.SplitScore);                                // clamped
        Assert.Equal(["財布"], c1.Emphasis);
        Assert.Equal(truth[1] + 2, c1.Highlight);
        var c2 = o.Chapters.Single(c => c.Start == truth[2]);
        Assert.Equal(-1, c2.Highlight);                                 // outside its chapter
        Assert.Empty(JapaneseText.UngroundedClaims(c2.Title, tr.FullText));   // "500万円" replaced by a transcript title
        Assert.DoesNotContain(o.Chapters, c => c.Title == "範囲外");
    }

    [Fact] // YouTube Help "Video chapters": first 00:00, ≥ 3 chapters, each ≥ 10 s
    public void YouTube_chapters_follow_the_published_rules()
    {
        var lines = SeriesPlanner.YouTubeChapters([new(0, "見どころ"), new(5, "前編のおさらい"), new(12, "釣り"), new(300, "料理"), new(595, "後編の予告")], 600);
        Assert.Equal(["00:00 前編のおさらい", "00:12 釣り", "05:00 料理"], lines);   // a chapter closer than 10 s replaces the one before it
        Assert.Null(SeriesPlanner.YouTubeChapters([new(0, "A"), new(30, "B")], 100));
        Assert.Equal("1:02:03", SeriesPlanner.Stamp(3723, 4000));
    }

    static async Task<(EditPlan Plan, List<string> Notes, Transcript Tr, SignalSet Sig, List<SeriesPart> Parts)> PlanPart(int index, int[] topics, double partMinutes)
    {
        var (tr, _) = SyntheticTalk.Build(topics);
        var sig = Fixtures.Signals(tr);
        var parts = SeriesPlanner.Split(tr, sig, P4, tr.Duration, partMinutes * 60, SeriesPlanner.FallbackOutline(tr, sig), []);
        var part = parts[index];
        var media = Media(tr.Duration);
        var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = "x.mp4", Sha256 = "x", Media = media };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P4"], Options = new JobOptions { PartMinutes = partMinutes } };
        var cand = new Candidate { Id = $"part{index + 1}", Segments = [part.Body] };
        var (plan, notes) = await PlanBuilder.BuildAsync(new PlanRequest(cand, tr, sig, P4, [asset], asset, "p", job, new Dictionary<string, List<FaceSample>>(), null, null, Part: part), TestContext.Current.CancellationToken);
        return (plan, notes, tr, sig, parts);
    }

    [Fact] // popular multi-part structure: cold open → recap → body (jump cuts, chapter headlines) → 「〜につづく」 + next teaser
    public async Task Part_plans_have_the_series_structure_and_pass_plan_qa()
    {
        var (first, _, tr, _, parts) = await PlanPart(0, Topics, 8);
        var (last, notes, _, _, _) = await PlanPart(1, Topics, 8);

        Assert.Equal(["teaser"], first.Timeline.TakeWhile(c => c.Role != "body").Select(c => c.Role));
        Assert.Equal("next", first.Timeline[^1].Role);
        Assert.Equal(["teaser", "recap"], last.Timeline.TakeWhile(c => c.Role != "body").Select(c => c.Role));
        Assert.Equal("body", last.Timeline[^1].Role);
        // the recap is the previous part's ending; the next teaser is the next part's cold open
        Assert.Equal(parts[0].Last, tr.Sentences.Last(s => s.T0 < last.Timeline.First(c => c.Role == "recap").MasterOut - 0.2).Index);
        Assert.Equal(last.Timeline[0].SrcIn, first.Timeline[^1].SrcIn, 3);

        Assert.Contains(first.Telops, t => t.Type == TelopType.Hook && t.Text.StartsWith("【前編】"));
        Assert.Contains(first.Telops, t => t.Type == TelopType.Headline && t.Text == "後編につづく");
        Assert.Contains(last.Telops, t => t.Type == TelopType.Headline && t.Text == "前編のおさらい");
        Assert.True(first.Telops.Count(t => t.Type == TelopType.Headline) >= 3, "chapter headlines");
        Assert.Contains(first.Telops, t => t.Type == TelopType.Caption);
        Assert.Equal(2, last.Series!.Part);
        Assert.Contains(notes, n => n.Contains("おさらい"));
        var yt = SeriesPlanner.YouTubeChapters(first.Series!.Chapters, first.Duration)!;
        Assert.StartsWith("00:00 ", yt[0]);
        Assert.True(yt.Count >= 3);

        foreach (var plan in new[] { first, last })
        {
            var qa = QaEngine.PlanChecks(new QaContext(plan, P4, tr, ["a1"], []));
            Assert.All(qa.Where(q => q.Hard), q => Assert.True(q.Status == QaStatus.Pass, $"{plan.Series!.Label} {q.CheckId} {q.Measured} vs {q.Threshold}"));
            Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "S-10").Status);
            Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "S-11").Status);
            Assert.True(plan.Duration > 180, "a series part is not limited to the 180 s short-video cap");
        }
    }

    [Fact] // labels like 第2話 are structure, not facts: H-07 must not flag them
    public async Task Numbered_labels_pass_the_grounding_check()
    {
        var (plan, _, tr, _, parts) = await PlanPart(1, [100, 100, 100, 100, 100, 100, 100, 100], 2);
        Assert.True(parts.Count >= 4);
        Assert.StartsWith("第", plan.Series!.Label);
        var qa = QaEngine.PlanChecks(new QaContext(plan, P4, tr, ["a1"], []));
        Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "H-07").Status);
    }

    [Fact] // out-of-order clips get their own accurately-seeked input; trims are relative to the seek point
    public async Task Series_inputs_seek_each_forward_block()
    {
        var (plan, _, _, _, _) = await PlanPart(1, Topics, 8);
        var assets = new Dictionary<string, SourceAsset> { ["a1"] = new() { Id = "a1", ProjectId = "p", Path = "src.mp4", Sha256 = "x", Media = Media(1000) } };
        var inp = new Renderer(Fixtures.Ff ?? FFmpeg.Locate(start: AppContext.BaseDirectory)).ResolveInputs(plan, assets);
        Assert.Equal(2, inp.Args.Count);                     // teaser; recap (a backwards jump) and the body continuing forward from it
        Assert.All(inp.Args, a => Assert.Equal("-ss", a[0]));
        var teaser = plan.Timeline[0];
        Assert.Equal(teaser.SrcIn - Renderer.SeekMargin, inp.Video[0].Offset, 3);
        Assert.Equal(inp.Video[1].Index, inp.Video[^1].Index);  // recap and body share one forward-running input
        var graph = Renderer.BuildGraph(plan, inp, null, "fonts", preview: true, null);
        Assert.Contains($"[0:v]trim=start={Renderer.SeekMargin}:", graph);              // the cold open is the only clip of its input
        Assert.StartsWith("[1:a]asplit=", graph);                                       // one decoder output per input, split inside the graph
        Assert.DoesNotContain("[1:v]trim", graph);
        Assert.Contains("scale=540:960:force_original_aspect_ratio=increase", graph);   // previews are composed at half size
        Assert.DoesNotContain("scale=540:960,format", graph);
    }
}

/// <summary>Real FFmpeg render of a series part with out-of-order clips (cold open from later, recap from earlier) and its QA.</summary>
public class SeriesRenderTests : IAsyncLifetime
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-series-" + Guid.NewGuid().ToString("N")[..8]);
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    string _src = "";

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _src = Path.Combine(_dir, "src.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=30:d=200", "-f", "lavfi", "-i", "sine=f=330:d=200,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-g", "60", "-c:a", "aac", "-shortest", _src], TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    [Fact]
    public async Task Second_part_renders_with_correct_content_order_and_passes_hard_qa()
    {
        var p = Fixtures.Presets["P4"];
        var (tr, _) = SyntheticTalk.Build([40, 50, 45, 40]);
        var sig = Fixtures.Signals(tr);
        var parts = SeriesPlanner.Split(tr, sig, p, tr.Duration, 1.5 * 60, SeriesPlanner.FallbackOutline(tr, sig), []);
        Assert.Equal(2, parts.Count);
        var media = new MediaInfo { Duration = 200, Width = 640, Height = 360, Fps = 30, AudioChannels = 1 };
        var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = _src, Sha256 = "x", Media = media };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P4"], Options = new JobOptions { PartMinutes = 1.5 } };
        var (plan, _) = await PlanBuilder.BuildAsync(new PlanRequest(new Candidate { Id = "part2", Segments = [parts[1].Body] }, tr, sig, p, [asset], asset, "p", job,
            new Dictionary<string, List<FaceSample>>(), null, null, Part: parts[1]), TestContext.Current.CancellationToken);
        Assert.Equal("teaser", plan.Timeline[0].Role);
        Assert.Equal("recap", plan.Timeline[1].Role);
        Assert.True(plan.Timeline[1].SrcIn < plan.Timeline[0].SrcIn, "recap comes from earlier in the source than the cold open");

        var assets = new Dictionary<string, SourceAsset> { ["a1"] = asset };
        var ctx = new QaContext(plan, p, tr, ["a1"], []);
        var outp = await new Renderer(Ff).RenderAsync(plan, p, assets, Path.Combine(_dir, "part2.mp4"), preview: false, TestContext.Current.CancellationToken);
        var qa = await new QaEngine(Ff).RunAsync(ctx, outp.Path, preview: false, Path.Combine(_dir, "qa"), TestContext.Current.CancellationToken);
        Assert.All(qa.Where(q => q.Hard), q => Assert.True(q.Status == QaStatus.Pass, $"{q.CheckId} {q.Measured} vs {q.Threshold}"));

        // independent check of the seeking: the same plan through the original single-input path must give a bit-identical video stream
        var reference = await new Renderer(Ff).RenderAsync(plan with { Series = null }, p, assets, Path.Combine(_dir, "part2_ref.mp4"), preview: false, TestContext.Current.CancellationToken);
        async Task<string> Hash(string f) => (await Ff.RunFfmpegAsync(["-v", "error", "-i", f, "-map", "0:v", "-c", "copy", "-f", "hash", "-hash", "sha256", "-"], TestContext.Current.CancellationToken)).StdOut.Trim();
        var ha = await Hash(outp.Path);
        Assert.StartsWith("SHA256=", ha);
        Assert.Equal(await Hash(reference.Path), ha);

        var preview = await new Renderer(Ff).RenderAsync(plan, p, assets, Path.Combine(_dir, "part2_preview.mp4"), preview: true, TestContext.Current.CancellationToken);
        var probe = await Ff.RunProbeAsync(["-v", "error", "-select_streams", "v", "-show_entries", "stream=width,height", "-of", "csv=p=0", preview.Path], TestContext.Current.CancellationToken);
        Assert.Equal("540,960", probe.StdOut.Trim());
    }
}
