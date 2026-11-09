using System.Text.Json;
using VideoCreator.Models;
using VideoCreator.Qa;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>自動チェック: per-item timecodes, recommended fixes and 「Copilotで自動修復」 (docs/IMPLEMENTATION.md §QA 自動修復).
/// The oracle is QaEngine.PlanChecks itself: a repair is only kept when the re-check clears its finding without adding others.</summary>
public sealed class QaRepairTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static Transcript TranscriptOf(params (string Text, int Speaker)[] utterances)
    {
        var words = utterances.Select((u, i) => new Word($"w{i}", 1 + i * 3, 3 + i * 3, u.Text, u.Speaker)).ToList();
        return new Transcript
        {
            Provider = "fixed", Duration = 30, Words = words,
            Sentences = words.Select((w, i) => new Sentence(i, w.T0, w.T1, w.Text, w.Speaker, i, i)).ToList(),
        };
    }

    static EditPlan Plan(params Telop[] telops) => new()
    {
        EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P4"].Ref,
        Timeline = [new Clip { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 30 }],
        Telops = telops.ToList(), Audio = new AudioSpec { Master = "a" },
    };

    static Telop T(string id, TelopType type, double s, double e, string text, params string[] ids) =>
        new() { Id = id, Type = type, Start = s, End = e, Text = text, SourceWordIds = ids.ToList(), Speaker = 1 };

    static QaContext Ctx(EditPlan plan, Transcript tr, IReadOnlyList<string>? mask = null) =>
        new(plan, Fixtures.Presets["P4"], tr, ["a"], [], null, mask);

    static QaResult Check(QaContext ctx, string id) => QaEngine.PlanChecks(ctx).Single(q => q.CheckId == id);

    /// <summary>Answers every requested id with a fixed text and records the prompt.</summary>
    sealed class RepairLlm(Func<string, string> answer) : IHighlightSelector
    {
        public List<string> Prompts { get; } = [];
        public string Name => "fake";
        public string Model => "fake-model";
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
        {
            Prompts.Add(user);
            var ids = user.Split('\n').Where(l => l.StartsWith("id: ")).Select(l => l[4..].Trim());
            return Task.FromResult(JsonSerializer.Serialize(new { items = ids.Select(id => new { id, text = answer(id) }) }));
        }
    }

    [Fact] // every warn/fail finding points at a target and a time in the rendered file (inserts push body content later)
    public void Findings_carry_items_on_the_final_timeline_and_advice()
    {
        var tr = TranscriptOf(("A社は100万円、B社は500万円", 1));
        var plan = Plan(T("e1", TelopType.Emphasis, 1, 3, "A社500万円", "w0")) with
        {
            Inserts = [new Insert { Id = "op", Kind = "opening", At = 0, Duration = 2, Path = "op.mp4", RightsConfirmed = true }],
        };
        var qa = QaEngine.PlanChecks(Ctx(plan, tr));
        var h11 = qa.Single(q => q.CheckId == "H-11");
        Assert.Equal(QaStatus.Fail, h11.Status);
        var item = Assert.Single(h11.Items!);
        Assert.Equal(("e1", "telop", 3.0, 5.0), (item.Target, item.Kind, item.Start!.Value, item.End!.Value));
        Assert.NotNull(h11.Advice);
        Assert.Equal("ai", h11.Advice!.Repair);
        Assert.All(qa.Where(q => q.Status is QaStatus.Warn or QaStatus.Fail), q => Assert.False(string.IsNullOrWhiteSpace(q.Advice?.Recommendation), q.CheckId));
        Assert.All(qa.Where(q => q.Status == QaStatus.Pass), q => Assert.Null(q.Advice));
        // rights and approval are people's decisions
        Assert.Equal("manual", Check(Ctx(plan, tr) with { RightsAssetIds = [] }, "H-09").Advice!.Repair);
        Assert.Equal("manual", Check(Ctx(Plan(T("k", TelopType.Tsukkomi, 1, 3, "なにそれ") with { Approved = false }), tr), "H-08").Advice!.Repair);
    }

    [Fact] // H-11: an AI rewrite that matches the utterance is kept, left unapproved, and may add itself (only) to H-08
    public async Task Llm_fix_for_grounding_is_accepted_but_needs_approval()
    {
        var tr = TranscriptOf(("A社は100万円、B社は500万円", 1));
        var ctx = Ctx(Plan(T("e1", TelopType.Emphasis, 1, 3, "A社500万円", "w0")), tr);
        var llm = new RepairLlm(_ => "A社100万円");
        var r = await QaRepair.RunAsync(ctx, null, llm, Ct);
        var c = Assert.Single(r.Changes);
        Assert.Equal(("H-11", "e1", "ai", "A社500万円", "A社100万円", true), (c.CheckId, c.Target, c.Method, c.Before, c.After, c.NeedsApproval));
        var t = r.Plan.Telops.Single();
        Assert.Equal("A社100万円", t.Text);
        Assert.False(t.Approved);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "H-11").Status);
        Assert.Equal(QaStatus.Fail, r.After.Single(q => q.CheckId == "H-08").Status);   // a person still has to approve it
        Assert.Contains(r.Plan.Provenance.Edits, e => e.Op == "qa_repair" && e.Target == "e1");
        Assert.Contains("A社は100万円、B社は500万円", Assert.Single(llm.Prompts));         // the LLM sees the utterance it must stay faithful to
        Assert.Equal("fake (fake-model)", r.Llm);

        // a wrong amount is not accepted however it is phrased
        var bad = await QaRepair.RunAsync(ctx, null, new RepairLlm(_ => "B社100万円"), Ct);
        Assert.Empty(bad.Changes);
        Assert.Equal("A社500万円", bad.Plan.Telops.Single().Text);
        Assert.Contains(bad.Skipped, s => s.CheckId == "H-11" && s.Target == "e1");
    }

    const string Long = "今日はみんなでパワーアップの方法を話します";
    static readonly string[] LongWords = ["今日は", "みんなで", "パワーアップの", "方法を", "話します"];

    /// <summary>One utterance split into recogniser words, 0.4 s each from <paramref name="t0"/>.</summary>
    static Transcript Words(double t0, params string[] words)
    {
        var ws = words.Select((x, i) => new Word($"w{i}", t0 + i * 0.4, t0 + (i + 1) * 0.4, x, 1)).ToList();
        return new Transcript { Provider = "fixed", Duration = 30, Words = ws, Sentences = [new Sentence(0, ws[0].T0, ws[^1].T1, string.Concat(words), 1, 0, ws.Count - 1)] };
    }

    static string[] Ids(int n) => Enumerable.Range(0, n).Select(i => $"w{i}").ToArray();

    [Fact] // S-01/S-02 on a caption: deletion of whole words is kept (and stays approved); paraphrase, added text or a word cut in half is rejected
    public async Task Caption_shortening_is_deletion_of_whole_words_only()
    {
        var tr = Words(1, LongWords);
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 3, Long, Ids(LongWords.Length))), tr);
        var before = QaEngine.PlanChecks(ctx);
        Assert.Contains(before, q => q.CheckId is "S-01" or "S-02" && q.Status == QaStatus.Warn);

        var llm = new RepairLlm(_ => "方法を話します");
        var ok = await QaRepair.RunAsync(ctx, null, llm, Ct);
        var c = Assert.Single(ok.Changes);
        Assert.Equal(("ai", false), (c.Method, c.NeedsApproval));
        Assert.True(ok.Plan.Telops.Single().Approved);
        Assert.All(ok.After.Where(q => q.CheckId is "S-01" or "S-02"), q => Assert.Equal(QaStatus.Pass, q.Status));
        Assert.Contains("今日は｜みんなで｜パワーアップの｜方法を｜話します", llm.Prompts.Single());   // the LLM is told where the words are

        var paraphrase = await QaRepair.RunAsync(ctx, null, new RepairLlm(_ => "強くなるコツを紹介"), Ct);
        Assert.Empty(paraphrase.Changes);
        Assert.Contains(paraphrase.Skipped, s => s.Reason.Contains("削除のみ"));
        Assert.Equal(Long, paraphrase.Plan.Telops.Single().Text);

        // a deletion, and short enough, but 「方法を」 is cut to 「方法」 (seen with a real model: 「こんにちは」→「こんにち」)
        var cut = await QaRepair.RunAsync(ctx, null, new RepairLlm(_ => "パワーアップの方法"), Ct);
        Assert.Empty(cut.Changes);
        Assert.Contains(cut.Skipped, s => s.Reason.Contains("単語の途中"));
    }

    [Fact] // S-01 is first fixed by timing: the caption stays on into the free time after it (≤0.5 s past its last word), words untouched
    public async Task Fast_caption_is_shown_longer_before_any_word_is_cut()
    {
        var tr = Words(1, "こんにちは", "それでは", "始めます");
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 1.4, "こんにちは", "w0"), T("c2", TelopType.Caption, 3, 3.8, "それでは", "w1")), tr);
        Assert.Contains("c1", Check(ctx, "S-01").Items!.Select(i => i.Target));
        var llm = new RepairLlm(_ => "こんにち");
        var r = await QaRepair.RunAsync(ctx, null, llm, Ct);
        var c = Assert.Single(r.Changes, x => x.Target == "c1");
        Assert.Equal(("S-01", "rule"), (c.CheckId, c.Method));
        var t = r.Plan.Telops.Single(x => x.Id == "c1");
        Assert.Equal("こんにちは", t.Text);
        Assert.InRange(t.End, 1.84, 1.4 + 0.5);              // 5 chars at ≤6 cps, at most 0.5 s past 「こんにちは」
        Assert.True(t.Start >= 1 - 0.25);
        Assert.DoesNotContain(r.After.Single(q => q.CheckId == "S-01").Items!, i => i.Target == "c1");
        Assert.DoesNotContain("id: c1", string.Concat(llm.Prompts));
    }

    [Fact] // a fix that clears its own finding but breaks another check is not applied (here: a faithful emphasis that is now too wide, S-02)
    public async Task Fix_that_makes_another_check_worse_is_rejected()
    {
        var tr = TranscriptOf(("A社の今年の売上は100万円でした、B社は500万円", 1));
        var ctx = Ctx(Plan(T("e1", TelopType.Emphasis, 1, 3, "A社500万円", "w0")), tr);
        Assert.Equal(QaStatus.Pass, Check(ctx, "S-02").Status);
        var r = await QaRepair.RunAsync(ctx, null, new RepairLlm(_ => "A社の今年の売上は100万円でした"), Ct);
        Assert.Empty(r.Changes);
        Assert.Contains(r.Skipped, s => s.Target == "e1" && s.Reason.Contains("S-02"));
        Assert.Equal(EditPlanFingerprint.Of(ctx.Plan), EditPlanFingerprint.Of(r.Plan));
    }

    [Theory] // ja-JP recognisers return one character per word, so cuts are checked against 文節 boundaries (real model output on the left)
    [InlineData("おはようございます", "お", false)]
    [InlineData("撮りました", "撮りま", false)]
    [InlineData("どんな時間に", "どんなに", false)]
    [InlineData("ありがとうございます", "ございます", false)]
    [InlineData("本当ですよね", "ですよね", false)]
    [InlineData("いきたいと思ってます", "いきたいと", true)]
    [InlineData("あの、今日はみんなで", "今日はみんなで", true)]
    [InlineData("今日はみんなでパワーアップの方法を話します", "方法を話します", true)]
    [InlineData("今日は「パワーアップ」の方法を話します", "今日は「", false)]
    public void Caption_cuts_fall_on_phrase_boundaries(string original, string shortened, bool ok) =>
        Assert.Equal(ok, QaRepair.CutsAtPhraseBoundaries(original, shortened));

    [Fact] // a short caption still too fast after the timing rule is left to a person, not cut
    public async Task Short_fast_caption_is_not_sent_to_the_llm()
    {
        var tr = Words(1, "撮", "り", "ま", "し", "た");
        var ctx = Ctx(Plan(T("c0", TelopType.Caption, 0.5, 0.9, "はい", "w0"), T("c1", TelopType.Caption, 1, 1.4, "撮りました", Ids(5)), T("c2", TelopType.Caption, 1.5, 3, "それでは", "w0")), tr);
        Assert.Contains("c1", Check(ctx, "S-01").Items!.Select(i => i.Target));
        var llm = new RepairLlm(_ => "撮りま");
        var r = await QaRepair.RunAsync(ctx, null, llm, Ct);
        Assert.DoesNotContain(r.Changes, c => c.Target == "c1");
        Assert.Contains(r.Skipped, s => s.Target == "c1" && s.Reason.Contains("結合"));
        Assert.DoesNotContain("id: c1", string.Concat(llm.Prompts));
    }

    [Fact] // S-08 rule: the particle at the head of a caption moves (text, words, time) to the end of the previous caption
    public async Task Kinsoku_rule_moves_leading_particle_to_previous_caption()
    {
        var tr = TranscriptOf(("今日", 1), ("は", 1), ("晴れ", 1));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 4, "今日", "w0"), T("c2", TelopType.Caption, 4, 9, "は晴れ", "w1", "w2")), tr);
        Assert.Equal(QaStatus.Warn, Check(ctx, "S-08").Status);
        Assert.Equal("rule", Check(ctx, "S-08").Advice!.Repair);
        var r = await QaRepair.RunAsync(ctx, ["S-08"], null, Ct);   // no LLM needed
        var c = Assert.Single(r.Changes);
        Assert.Equal(("S-08", "rule"), (c.CheckId, c.Method));
        var (c1, c2) = (r.Plan.Telops[0], r.Plan.Telops[1]);
        Assert.Equal(("今日は", "晴れ"), (c1.Text, c2.Text));
        Assert.Equal(["w0", "w1"], c1.SourceWordIds);
        Assert.Equal(["w2"], c2.SourceWordIds);
        Assert.True(c1.End <= c2.Start);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "S-08").Status);
    }

    [Fact] // H-11 rule: a telop whose evidence ids are missing is re-bound to the utterance it quotes, without an LLM
    public async Task Missing_evidence_is_rebound_by_rule()
    {
        var tr = TranscriptOf(("A社は100万円", 1), ("B社は500万円", 1));
        var ctx = Ctx(Plan(T("e1", TelopType.Emphasis, 4, 6, "B社は500万円")), tr);
        Assert.Equal(QaStatus.Fail, Check(ctx, "H-11").Status);
        var r = await QaRepair.RunAsync(ctx, null, null, Ct);
        var c = Assert.Single(r.Changes);
        Assert.Equal(("H-11", "rule", false), (c.CheckId, c.Method, c.NeedsApproval));
        Assert.Equal(["w1"], r.Plan.Telops.Single().SourceWordIds);
        Assert.Equal("B社は500万円", r.Plan.Telops.Single().Text);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "H-11").Status);
    }

    [Fact] // H-10 rule: a hand edit that brought a hidden word back is masked again
    public async Task Leaked_mask_word_is_masked_by_rule()
    {
        var tr = TranscriptOf(("今日は晴れ", 1));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 3, "今日は晴れ", "w0"), T("k", TelopType.Tsukkomi, 1, 3, "1万円かよ")), tr, ["1万円"]);
        var h10 = Check(ctx, "H-10");
        Assert.Equal(QaStatus.Fail, h10.Status);
        Assert.Equal("rule", h10.Advice!.Repair);
        var r = await QaRepair.RunAsync(ctx, ["H-10"], null, Ct);
        Assert.Contains(r.Changes, c => c.CheckId == "H-10" && c.Target == "k" && c.After!.Contains('●'));
        Assert.DoesNotContain("1万円", r.Plan.Telops.Single(t => t.Id == "k").Text);
    }

    [Fact] // a plan with hundreds of findings: bounded LLM work per click, H-* first, the rest reported for the next click
    public async Task Llm_work_is_batched_and_capped_per_run()
    {
        var n = QaRepair.MaxAiTargets + 5;
        var tr = TranscriptOf(Enumerable.Range(0, n).Select(_ => (Long, 1)).ToArray());
        var telops = Enumerable.Range(0, n).Select(i => T($"c{i}", TelopType.Caption, 1 + i * 3, 3 + i * 3, Long, $"w{i}")).ToArray();
        telops[^1] = telops[^1] with { Type = TelopType.Emphasis, Text = "A社500万円" };   // the last one in time, but blocking (H-11)
        var llm = new RepairLlm(id => id == telops[^1].Id ? Long[..4] : "方法を話します");
        var r = await QaRepair.RunAsync(Ctx(Plan(telops), tr), null, llm, Ct);
        Assert.Equal((QaRepair.MaxAiTargets + QaRepair.BatchSize - 1) / QaRepair.BatchSize, llm.Prompts.Count);
        Assert.All(llm.Prompts, p => Assert.True(p.Split('\n').Count(l => l.StartsWith("id: ")) <= QaRepair.BatchSize));
        Assert.Contains($"id: {telops[^1].Id}", llm.Prompts[0]);
        Assert.Equal(5, r.Skipped.Where(s => s.Reason.Contains("上限")).Select(s => s.Target).Distinct().Count());
        Assert.Equal(QaRepair.MaxAiTargets, llm.Prompts.SelectMany(p => p.Split('\n')).Count(l => l.StartsWith("id: ")));
    }

    [Fact] // no LLM configured: AI items are skipped with a reason, and the check filter limits what is touched
    public async Task Without_llm_ai_items_are_skipped_and_filter_is_honoured()
    {
        var tr = TranscriptOf((Long, 1), ("A社は100万円、B社は500万円", 1));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 3, Long, "w0"), T("e1", TelopType.Emphasis, 4, 6, "A社500万円", "w1")), tr);
        var r = await QaRepair.RunAsync(ctx, null, null, Ct);
        Assert.Empty(r.Changes);
        Assert.Null(r.Llm);
        Assert.Contains(r.Skipped, s => s.Target == "e1" && s.Reason.Contains("LLM が設定されていない"));
        Assert.Contains(r.Skipped, s => s.Target == "c1" && s.Reason.Contains("LLM が設定されていない"));

        var llm = new RepairLlm(id => id == "c1" ? "方法を話します" : "A社100万円");
        var only = await QaRepair.RunAsync(ctx, ["H-11"], llm, Ct);
        Assert.Equal(["e1"], only.Changes.Select(c => c.Target));
        Assert.DoesNotContain("id: c1", llm.Prompts.Single());
        Assert.Equal(Long, only.Plan.Telops.Single(t => t.Id == "c1").Text);
    }
}
