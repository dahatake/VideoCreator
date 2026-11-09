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

    static void AssertCaptionWordsPreserved(EditPlan before, EditPlan after)
    {
        static List<Telop> Caps(EditPlan p) => p.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).OrderBy(t => t.Start).ToList();
        static string Text(List<Telop> ts) => new(string.Concat(ts.Select(t => t.Text)).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var a = Caps(before); var b = Caps(after);
        Assert.Equal(Text(a), Text(b));
        Assert.Equal(a.SelectMany(t => t.SourceWordIds), b.SelectMany(t => t.SourceWordIds));
    }

    /// <summary>Answers every requested id with fixed text and records the prompt.</summary>
    sealed class RepairLlm(Func<string, string> answer) : IHighlightSelector
    {
        public List<string> Prompts { get; } = [];
        public string Name => "fake";
        public string Model => "fake-model";
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
        {
            lock (Prompts) Prompts.Add(user);
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

    /// <summary>One utterance split into recogniser words, 0.4 s each from <paramref name="t0"/>.</summary>
    static Transcript Words(double t0, params string[] words)
    {
        var ws = words.Select((x, i) => new Word($"w{i}", t0 + i * 0.4, t0 + (i + 1) * 0.4, x, 1)).ToList();
        return new Transcript { Provider = "fixed", Duration = 30, Words = ws, Sentences = [new Sentence(0, ws[0].T0, ws[^1].T1, string.Concat(words), 1, 0, ws.Count - 1)] };
    }

    static string[] Ids(int n) => Enumerable.Range(0, n).Select(i => $"w{i}").ToArray();

    [Theory]
    [InlineData("あのちょっと今日は方法を話します", "今日は方法を話します")]
    [InlineData("ちょうどいい落としどころかなと", "ちょうどいい落としどころかな")]
    [InlineData("いいですかねこれはどうでしょうか", "ですかねこれはどうでしょうか")]
    [InlineData("今日はみんなでパワーアップの方法を話します", "方法を紹介します")]
    [InlineData("今日はみんなでパワーアップの方法を話します", "今日はみんなで方法を話")]
    public async Task Caption_words_are_never_shortened_even_when_ai_could_clear_the_warning(string text, string answer)
    {
        var tr = Words(1, text);
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 3, text, "w0")), tr);
        Assert.Equal(QaStatus.Warn, Check(ctx, "S-02").Status);
        Assert.Equal("rule", Check(ctx, "S-01").Advice!.Repair);
        Assert.Equal("rule", Check(ctx, "S-02").Advice!.Repair);
        var llm = new RepairLlm(_ => answer);
        foreach (var filter in new IReadOnlyCollection<string>?[] { ["S-01"], ["S-02"], null })
        {
            var r = await QaRepair.RunAsync(ctx, filter, llm, Ct);
            Assert.Empty(r.Changes);
            Assert.Equal(EditPlanFingerprint.Of(ctx.Plan), EditPlanFingerprint.Of(r.Plan));
            Assert.Contains(r.Skipped, s => s.Target == "c1" && s.Reason.Contains("話した言葉どおり") && s.Reason.Contains("警告を残します"));
            Assert.Equal(QaStatus.Warn, r.After.Single(q => q.CheckId == "S-02").Status);
            Assert.Null(r.Llm);
        }
        Assert.Empty(llm.Prompts);
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

    [Fact]
    public async Task Presentation_telop_can_be_shortened_by_ai_while_captions_stay_verbatim()
    {
        var tr = TranscriptOf((Condensable, 1), (Long, 1));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 3, Condensable, "w0"),
            T("e1", TelopType.Emphasis, 4, 6, Long, "w1")), tr);
        Assert.Equal("ai", Check(ctx, "S-02").Advice!.Repair);
        var llm = new RepairLlm(_ => "方法を話します");
        var r = await QaRepair.RunAsync(ctx, ["S-02"], llm, Ct);
        var c = Assert.Single(r.Changes);
        Assert.Equal(("e1", "ai", "方法を話します", true), (c.Target, c.Method, c.After, c.NeedsApproval));
        Assert.False(r.Plan.Telops.Single(t => t.Id == "e1").Approved);
        Assert.Equal(Condensable, r.Plan.Telops.Single(t => t.Id == "c1").Text);
        AssertCaptionWordsPreserved(ctx.Plan, r.Plan);
        Assert.Contains(r.After.Single(q => q.CheckId == "S-02").Items!, i => i.Target == "c1");
        Assert.DoesNotContain("id: c1", Assert.Single(llm.Prompts));
    }

    [Fact]
    public async Task Short_content_caption_is_not_sent_to_the_llm()
    {
        var tr = Words(1, "撮", "り", "ま", "し", "た");
        // neighbours by another speaker: no merge / move across a speaker change
        var ctx = Ctx(Plan(T("c0", TelopType.Caption, 0.5, 0.9, "はい", "w0") with { Speaker = 2 }, T("c1", TelopType.Caption, 1, 1.4, "撮りました", Ids(5)), T("c2", TelopType.Caption, 1.5, 3, "それでは") with { Speaker = 2 }), tr);
        Assert.Contains("c1", Check(ctx, "S-01").Items!.Select(i => i.Target));
        var llm = new RepairLlm(_ => "撮りま");
        var r = await QaRepair.RunAsync(ctx, ["S-01"], llm, Ct);
        Assert.DoesNotContain(r.Changes, c => c.Target == "c1");
        Assert.Contains(r.Skipped, s => s.Target == "c1" && s.Reason.Contains("話した言葉どおり") && s.Reason.Contains("結合"));
        Assert.DoesNotContain("id: c1", string.Concat(llm.Prompts));
    }

    [Theory]
    [InlineData("こんにちは", "こんにちは")]
    [InlineData("はい", "うん")]
    [InlineData("えっと", "あの")]
    public async Task Short_repetitions_and_fillers_are_not_deleted(string first, string second)
    {
        var tr = Words(1, first, second, "始めましょう");
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 1.1, first, "w0"),
            T("c2", TelopType.Caption, 1.1, 1.2, second, "w1") with { Speaker = 2 },
            T("c3", TelopType.Caption, 1.2, 3.2, "始めましょう", "w2")), tr);
        var llm = new RepairLlm(_ => "");
        var r = await QaRepair.RunAsync(ctx, ["S-01"], llm, Ct);
        Assert.All(r.Changes, c => Assert.Equal("rule", c.Method));
        Assert.All(r.Plan.Telops, t => Assert.True(t.Enabled));
        Assert.Equal([first, second, "始めましょう"], r.Plan.Telops.Select(t => t.Text));
        Assert.Equal(["w0", "w1", "w2"], r.Plan.Telops.SelectMany(t => t.SourceWordIds));
        Assert.Empty(llm.Prompts);
    }

    [Fact] // S-01 by regrouping: a flickering 「はい」 joins the caption after it (same speaker, no cut, short pause → a space)
    public async Task Fast_short_caption_is_merged_with_its_neighbour()
    {
        var tr = Words(1, "はいはい", "じゃあ皆さん");
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 1.3, "はいはい", "w0"), T("c2", TelopType.Caption, 1.3, 3, "じゃあ皆さん", "w1")), tr with
        {
            Words = [new Word("w0", 1, 1.3, "はいはい", 1), new Word("w1", 1.7, 2.4, "じゃあ皆さん", 1)],
        });
        Assert.Contains("c1", Check(ctx, "S-01").Items!.Select(i => i.Target));
        var r = await QaRepair.RunAsync(ctx, ["S-01"], null, Ct);
        var c = Assert.Single(r.Changes);
        Assert.Equal(("rule", "はいはい｜じゃあ皆さん", "はいはい　じゃあ皆さん"), (c.Method, c.Before, c.After));
        var t = Assert.Single(r.Plan.Telops);
        Assert.Equal((1.0, 3.0), (t.Start, t.End));
        Assert.Equal(["w0", "w1"], t.SourceWordIds);
        Assert.True(t.Approved);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "S-01").Status);
        AssertCaptionWordsPreserved(ctx.Plan, r.Plan);

        // never across a speaker change
        var other = ctx with { Plan = ctx.Plan with { Telops = [.. ctx.Plan.Telops.Select(x => x.Id == "c2" ? x with { Speaker = 2 } : x)] } };
        Assert.DoesNotContain((await QaRepair.RunAsync(other, ["S-01"], null, Ct)).Changes, x => x.Before!.Contains('｜'));
    }

    [Fact] // S-02 by regrouping: an over-long caption hands its first words to the caption before it, words and times move together
    public async Task Long_caption_hands_words_to_its_neighbour()
    {
        var tr = Words(0.5, "はい", "それでは", "みんなで", "早速始めましょう");
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 0, 1, "はい", "w0"), T("c2", TelopType.Caption, 1, 5, "それではみんなで早速始めましょう", "w1", "w2", "w3")), tr);
        Assert.Equal(QaStatus.Warn, Check(ctx, "S-02").Status);
        var r = await QaRepair.RunAsync(ctx, ["S-02"], null, Ct);
        var c = Assert.Single(r.Changes);
        Assert.Equal(("rule", "はいそれでは｜みんなで早速始めましょう"), (c.Method, c.After));
        var (a, b) = (r.Plan.Telops[0], r.Plan.Telops[1]);
        Assert.Equal(["w0", "w1"], a.SourceWordIds);
        Assert.Equal(["w2", "w3"], b.SourceWordIds);
        Assert.Equal(1.3, b.Start, 3);   // 「みんなで」 starts at 1.3 s
        Assert.True(a.End <= b.Start);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "S-02").Status);
        AssertCaptionWordsPreserved(ctx.Plan, r.Plan);
    }

    [Fact] // S-08 judges what is on screen: a head removed by filler cleaning, a conjunction after 「。」 or another speaker's caption is no left-over particle
    public void Leading_particle_check_ignores_sentence_starts_and_hidden_heads()
    {
        static Transcript Tr(params string[] w) => new()
        {
            Provider = "fixed", Duration = 30, Sentences = [],
            Words = w.Select((x, i) => new Word($"w{i}", 1 + i * 0.4, 1.4 + i * 0.4, x, 1)).ToList(),
        };
        static List<string> Flagged(Transcript tr, params Telop[] ts) => Check(Ctx(Plan(ts), tr), "S-08").Items!.Select(i => $"{i.Target} {i.Detail}").ToList();
        var tr = Tr("話ですね", "。", "で", "コメントを");
        var c1 = T("c1", TelopType.Caption, 1, 1.8, "話ですね", "w0", "w1");
        Assert.Equal([], Flagged(tr, c1, T("c2", TelopType.Caption, 1.8, 2.6, "でコメントを", "w2", "w3")));            // 「。で、」 opens a sentence
        Assert.Equal(["c2 先頭が「で」"], Flagged(Tr("話ですね", "で", "コメントを"), T("c1", TelopType.Caption, 1, 1.4, "話ですね", "w0"), T("c2", TelopType.Caption, 1.4, 2.2, "でコメントを", "w1", "w2")));
        Assert.Equal([], Flagged(Tr("来た", "でも", "Copilot", "はね"), T("c1", TelopType.Caption, 1, 1.4, "来た", "w0"), T("c2", TelopType.Caption, 1.4, 2.6, "Copilotはね", "w1", "w2", "w3")));   // 「でも」 is not shown
        Assert.Equal([], Flagged(Tr("はい", "が", "実はね"), T("c1", TelopType.Caption, 1, 1.4, "はい", "w0") with { Speaker = 2 }, T("c2", TelopType.Caption, 1.4, 2.2, "が実はね", "w1", "w2")));
        Assert.Equal(["c2 先頭が「が」"], Flagged(Tr("はい", "が", "実はね"), T("c1", TelopType.Caption, 1, 1.4, "はい", "w0"), T("c2", TelopType.Caption, 1.4, 2.2, "が実はね", "w1", "w2")));
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
        AssertCaptionWordsPreserved(ctx.Plan, r.Plan);

        var otherSpeaker = ctx with { Plan = ctx.Plan with { Telops = [ctx.Plan.Telops[0], ctx.Plan.Telops[1] with { Speaker = 2 }] } };
        Assert.Empty((await QaRepair.RunAsync(otherSpeaker, ["S-08"], null, Ct)).Changes);
        var acrossCut = ctx with { Plan = ctx.Plan with { Timeline = [
            new Clip { ClipId = "c1", AssetId = "a", SrcIn = 0, SrcOut = 4 },
            new Clip { ClipId = "c2", AssetId = "a", SrcIn = 4, SrcOut = 30 },
        ] } };
        Assert.Equal(QaStatus.Warn, Check(acrossCut, "S-08").Status);
        var blocked = await QaRepair.RunAsync(acrossCut, ["S-08"], null, Ct);
        Assert.Empty(blocked.Changes);
        AssertCaptionWordsPreserved(acrossCut.Plan, blocked.Plan);
    }

    /// <summary>Recogniser words with explicit times (start, end, text), one speaker.</summary>
    static Transcript Timed(params (double S, double E, string Text)[] ws)
    {
        var words = ws.Select((w, i) => new Word($"w{i}", w.S, w.E, w.Text, 1)).ToList();
        return new Transcript { Provider = "fixed", Duration = 30, Words = words, Sentences = [new Sentence(0, words[0].T0, words[^1].T1, string.Concat(ws.Select(w => w.Text)), 1, 0, words.Count - 1)] };
    }

    [Fact] // S-02 with no neighbour to take words: the caption is split in two at a word boundary (FR-TEL-02 分割), the 「この項目を修復」 mark agrees
    public async Task Long_caption_alone_is_split_in_two()
    {
        var tr = Timed((1, 2.2, "今日はみんなで"), (2.2, 3.4, "早速"), (3.4, 4.6, "方法を"), (4.6, 5.8, "話します"));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 5.8, "今日はみんなで早速方法を話します", Ids(4))), tr);
        var s02 = Check(ctx, "S-02");
        Assert.Equal(QaStatus.Warn, s02.Status);
        var shown = QaRepair.Assess(ctx, QaEngine.PlanChecks(ctx), llm: false).Single(q => q.CheckId == "S-02");
        Assert.Equal(("rule", 1, (string?)null), (shown.Advice!.Repair, shown.Advice.Fixable, shown.Advice.Remaining));
        Assert.Equal("rule", Assert.Single(shown.Items!).Repair);

        var r = await QaRepair.RunAsync(ctx, ["S-02"], null, Ct);
        var c = Assert.Single(r.Changes);
        Assert.Equal(("S-02", "rule", "今日はみんなで｜早速方法を話します"), (c.CheckId, c.Method, c.After));
        var caps = r.Plan.Telops.OrderBy(t => t.Start).ToList();
        Assert.Equal(["c1", "c1b"], caps.Select(t => t.Id));
        Assert.Equal(2.2, caps[1].Start, 3);              // the second half starts with 「早速」
        Assert.Equal(caps[0].End, caps[1].Start, 3);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "S-02").Status);
        Assert.Equal(QaStatus.Pass, r.After.Single(q => q.CheckId == "S-01").Status);
        AssertCaptionWordsPreserved(ctx.Plan, r.Plan);
    }

    [Fact] // a caption spoken faster than the limit, boxed in by another speaker: no rule can fix it without dropping words, so it is shown as
           // a warning that stays (no repair button), with the measured speaking rate as the reason
    public async Task Inherently_fast_caption_is_marked_unfixable_with_its_speaking_rate()
    {
        var tr = Timed((0.4, 1, "はい"), (1, 1.6, "こんにちはみなさん"), (1.6, 2.4, "どうも"));
        var ctx = Ctx(Plan(T("c0", TelopType.Caption, 0.4, 1, "はい", "w0") with { Speaker = 2 },
            T("c1", TelopType.Caption, 1, 1.6, "こんにちはみなさん", "w1"),
            T("c2", TelopType.Caption, 1.6, 2.4, "どうも", "w2") with { Speaker = 2 }), tr);
        var shown = QaRepair.Assess(ctx, QaEngine.PlanChecks(ctx), llm: true).Single(q => q.CheckId == "S-01");
        var item = Assert.Single(shown.Items!, i => i.Target == "c1");
        Assert.Null(item.Repair);
        Assert.Contains("話す速さ", item.Note);
        Assert.Contains("毎秒 15.0 字", item.Note);   // 9 chars in 0.6 s
        Assert.DoesNotContain(shown.Items!, i => i.Repair != null);
        Assert.Equal(("none", 0), (shown.Advice!.Repair, shown.Advice.Fixable));
        Assert.Contains("警告のまま残ります", shown.Advice.Remaining);

        var r = await QaRepair.RunAsync(ctx, ["S-01"], null, Ct);
        Assert.Empty(r.Changes);
        Assert.Contains(r.Skipped, s => s.Target == "c1" && s.Reason.Contains("話す速さ"));
        Assert.Equal(EditPlanFingerprint.Of(ctx.Plan), EditPlanFingerprint.Of(r.Plan));
    }

    [Fact] // the per-item mark predicts the repair: every item marked "rule" is cleared, every unmarked one stays
    public async Task Assessment_agrees_with_what_the_repair_does()
    {
        var tr = Timed((1, 1.4, "こんにちは"), (3, 3.8, "それでは"), (5, 5.6, "今日はみんなで"), (5.6, 6, "方法を話します"), (6, 6.4, "では"));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 1.4, "こんにちは", "w0"), T("c2", TelopType.Caption, 3, 3.8, "それでは", "w1"),
            T("c3", TelopType.Caption, 5, 6, "今日はみんなで方法を話します", "w2", "w3") with { Speaker = 2 }, T("c4", TelopType.Caption, 6, 6.4, "では", "w4")), tr);
        var shown = QaRepair.Assess(ctx, QaEngine.PlanChecks(ctx), llm: false);
        var r = await QaRepair.RunAsync(ctx, null, null, Ct);
        foreach (var q in shown.Where(q => q.CheckId is "S-01" or "S-02" or "S-08" && q.Items != null))
        {
            var left = r.After.Single(x => x.CheckId == q.CheckId).Items?.Select(i => i.Target).ToHashSet() ?? [];
            Assert.All(q.Items!.Where(i => i.Repair == "rule"), i => Assert.DoesNotContain(i.Target, left));
            Assert.All(q.Items!.Where(i => i.Repair == null), i => Assert.Contains(i.Target, left));
        }
        Assert.Contains(shown.SelectMany(q => q.Items ?? []), i => i.Repair == "rule");
        Assert.Contains(shown.SelectMany(q => q.Items ?? []), i => i.Repair == null);
        AssertCaptionWordsPreserved(ctx.Plan, r.Plan);
    }

    [Fact] // H-11 rule: a telop whose evidence ids are missing is re-bound to the utterance it quotes, without an LLM
    public async Task Missing_evidence_is_rebound_by_rule()
    {
        var tr = TranscriptOf(("A社は100万円", 1), ("B社は500万円", 1));
        var ctx = Ctx(Plan(T("e1", TelopType.Emphasis, 4, 6, "B社は500万円")), tr);
        Assert.Equal(QaStatus.Fail, Check(ctx, "H-11").Status);
        // with no LLM, the rule is what will fix it: the card says so instead of 「AI で修復できます」
        var shown = QaRepair.Assess(ctx, QaEngine.PlanChecks(ctx), llm: false).Single(q => q.CheckId == "H-11");
        Assert.Equal(("rule", 1), (shown.Advice!.Repair, shown.Advice.Fixable));
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

    /// <summary>A hand-edited caption with fillers that must stay verbatim.</summary>
    const string Condensable = "あの　ちょっと今日の方法を話します";

    [Fact] // a plan with hundreds of findings: bounded LLM work per click, a few requests in flight, H-* first, the rest reported for the next click
    public async Task Llm_work_is_batched_and_capped_per_run()
    {
        var n = QaRepair.MaxAiTargets + 5;
        var tr = TranscriptOf(Enumerable.Range(0, n).Select(_ => (Long, 1)).ToArray());
        var telops = Enumerable.Range(0, n).Select(i => T($"c{i}", TelopType.Emphasis, 1 + i * 3, 3 + i * 3, Long, $"w{i}")).ToArray();
        telops[^1] = telops[^1] with { Type = TelopType.Emphasis, Text = "A社500万円" };   // the last one in time, but blocking (H-11)
        var llm = new RepairLlm(_ => Long[..4]);
        var plan = Plan(telops) with { Timeline = [new Clip { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = n * 3 + 1 }] };
        var r = await QaRepair.RunAsync(Ctx(plan, tr with { Duration = plan.Duration }), null, llm, Ct);
        Assert.Equal((QaRepair.MaxAiTargets + QaRepair.BatchSize - 1) / QaRepair.BatchSize, llm.Prompts.Count);
        Assert.All(llm.Prompts, p => Assert.True(p.Split('\n').Count(l => l.StartsWith("id: ")) <= QaRepair.BatchSize));
        Assert.Contains(llm.Prompts, p => p.Contains($"id: {telops[^1].Id}\n"));
        Assert.Equal(5, r.Skipped.Where(s => s.Reason.Contains("上限")).Select(s => s.Target).Distinct().Count());
        Assert.Equal(QaRepair.MaxAiTargets, llm.Prompts.SelectMany(p => p.Split('\n')).Count(l => l.StartsWith("id: ")));
        Assert.InRange(r.Changes.Count(c => c.Method == "ai"), QaRepair.MaxAiTargets - 1, QaRepair.MaxAiTargets);
    }

    [Fact] // no LLM configured: AI items are skipped with a reason, and the check filter limits what is touched
    public async Task Without_llm_ai_items_are_skipped_and_filter_is_honoured()
    {
        var tr = TranscriptOf((Long, 1), ("A社は100万円、B社は500万円", 1));
        var ctx = Ctx(Plan(T("c1", TelopType.Caption, 1, 3, Condensable, "w0"), T("e1", TelopType.Emphasis, 4, 6, "A社500万円", "w1")), tr);
        var r = await QaRepair.RunAsync(ctx, null, null, Ct);
        Assert.DoesNotContain(r.Changes, c => c.Method == "ai");   // timing rules still apply
        Assert.Contains(r.Skipped, s => s.Target == "e1" && s.Reason.Contains("LLM が設定されていない"));
        Assert.Contains(r.Skipped, s => s.Target == "c1" && s.Reason.Contains("話した言葉どおり"));

        var llm = new RepairLlm(id => id == "c1" ? "方法を話します" : "A社100万円");
        var only = await QaRepair.RunAsync(ctx, ["H-11"], llm, Ct);
        Assert.Equal(["e1"], only.Changes.Select(c => c.Target));
        Assert.DoesNotContain("id: c1", llm.Prompts.Single());
        Assert.Equal(Condensable, only.Plan.Telops.Single(t => t.Id == "c1").Text);
    }
}
