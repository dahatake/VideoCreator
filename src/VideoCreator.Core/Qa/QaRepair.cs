using System.Text.Json;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Qa;

public sealed record QaRepairChange(string CheckId, string Target, string Kind, string Method, string? Before, string? After, bool NeedsApproval, double? Start, double? End);
public sealed record QaRepairSkip(string CheckId, string Target, string Reason, double? Start = null);
public sealed record QaRepairResult(EditPlan Plan, List<QaRepairChange> Changes, List<QaRepairSkip> Skipped, List<QaResult> Before, List<QaResult> After, string? Llm);

/// <summary>「Copilotで自動修復」: deterministic rule fixes first, then batched LLM requests for non-caption text.
/// Every proposal is applied to a copy of the plan and kept only when QaEngine.PlanChecks shows its finding gone with no other check getting worse
/// (the one allowed exception: AI-written telop/card text is left unapproved, so it may add itself to H-08).</summary>
public static class QaRepair
{
    sealed record Proposal(string CheckId, QaItem Item, string Method, EditPlan Plan, string? Before, string? After, bool NeedsApproval);
    sealed record Answer(string Text);

    const string Schema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","text"],"properties":{"id":{"type":"string"},"text":{"type":"string"}}}}}}""";
    const string System = """
あなたはショート動画のテロップ校正者です。自動チェックで指摘されたテロップを、指摘ごとの制約を守って直します。
規則:
- 発話（文字起こし）に出てこない数字・固有名詞・事実を足さない。数字と単位と対象の組み合わせは発話どおりにする
- 発話の否定（〜ない、〜ません など）を落とさない
- 字幕は修正対象外。演出テロップ・カードだけを、読みやすさと見た目を優先して修正する
- 字数の上限を守る
- 直せない場合は元の文字をそのまま返す
- 入力の id ごとに 1 件ずつ返す
""";

    static readonly string[] AiChecks = ["H-07", "H-11", "S-09", "S-02"];
    /// <summary>Telops/cards sent to the LLM per run, per request, and requests in flight at once.</summary>
    public const int MaxAiTargets = 200, BatchSize = 25, MaxParallel = 4;

    public static async Task<QaRepairResult> RunAsync(QaContext ctx, IReadOnlyCollection<string>? checkIds, IHighlightSelector? llm, CancellationToken ct)
    {
        // a stale candidate approval is not something to repair; keep it out of the comparison
        var qctx = ctx with { Approval = null };
        var before = QaEngine.PlanChecks(ctx);
        var current = QaEngine.PlanChecks(qctx);
        var plan = ctx.Plan;
        var changes = new List<QaRepairChange>(); var skipped = new List<QaRepairSkip>();
        var targets = current.Where(q => q.Status is QaStatus.Warn or QaStatus.Fail && (checkIds is not { Count: > 0 } || checkIds.Contains(q.CheckId)))
            .SelectMany(q => (q.Items ?? []).Select(i => (q.CheckId, Item: i))).ToList();
        var done = new HashSet<(string, string)>();

        string? Try(Proposal pr)
        {
            if (pr.CheckId != "H-10" && !CaptionsPreserved(plan, pr.Plan))
                return "字幕の語句や発話との対応が変わるため採用しませんでした（字幕は話した言葉どおりに保持します）";
            var after = QaEngine.PlanChecks(qctx with { Plan = pr.Plan });
            var why = Regression(current, after, pr);
            if (why != null) return why;
            plan = pr.Plan with { Provenance = pr.Plan.Provenance with { Edits = [.. pr.Plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "qa_repair", pr.Item.Target, pr.Before, pr.After)] } };
            current = after;
            changes.Add(new(pr.CheckId, pr.Item.Target, pr.Item.Kind, pr.Method, pr.Before, pr.After, pr.NeedsApproval, pr.Item.Start, pr.Item.End));
            // one edit can clear other findings too (H-07 and H-11 on one telop; a merge clears both captions it joins)
            foreach (var (c, i) in targets) if (!(current.FirstOrDefault(q => q.CheckId == c)?.Items ?? []).Any(x => x.Target == i.Target)) done.Add((c, i.Target));
            return null;
        }

        // 1) rules: candidates from the least to the most invasive, the first one the re-check accepts is kept
        var ruleWhy = new Dictionary<(string, string), string>();
        foreach (var (check, item) in targets)
        {
            if (done.Contains((check, item.Target))) continue;
            var candidates = RuleProposals(check, plan, ctx, item).ToList();
            if (candidates.Count == 0)
            {
                if (check is "S-01" or "S-02" or "S-08" && item.Kind == "telop")
                {
                    var limits = $"1 行 {AssWriter.CaptionLineChars(ctx.Preset, plan.Canvas.Width)} 字・毎秒 {ctx.Preset.Telop.Caption.MaxCps} 字";
                    var why0 = check == "S-08"
                        ? $"前の字幕へ移したり結合したりすると基準（{limits}）に収まらないか、話者・カットが変わります"
                        : $"表示時間の延長や、前後の字幕への付け替え・結合では基準（{limits}）に収まりません";
                    ruleWhy[(check, item.Target)] = why0;
                    if (!AiChecks.Contains(check)) skipped.Add(new(check, item.Target, why0, item.Start));
                }
                continue;
            }
            string? why = null;
            foreach (var pr in candidates)
            {
                var w = Try(pr);
                if (w == null) { why = null; break; }
                why ??= w;
            }
            if (why == null) { done.Add((check, item.Target)); continue; }
            ruleWhy[(check, item.Target)] = why;
            // a rule that does not work out leaves AI-capable findings to the LLM step below
            if (!AiChecks.Contains(check)) skipped.Add(new(check, item.Target, why, item.Start));
        }

        // 2) LLM text repair is for presentation telops/cards, never spoken captions.
        var remaining = targets.Where(x => !done.Contains((x.CheckId, x.Item.Target))).ToList();
        foreach (var (check, item) in remaining.Where(x => x.CheckId is "S-01" or "S-02" or "S-08"
            && plan.Telops.Any(t => t.Id == x.Item.Target && t.Type == TelopType.Caption)))
        {
            skipped.RemoveAll(s => s.CheckId == check && s.Target == item.Target);
            var rule = ruleWhy.TryGetValue((check, item.Target), out var rw) ? $"。{rw}" : "";
            skipped.Add(new(check, item.Target, $"字幕は話した言葉どおりに保持するため、言い換え・省略・削除はしません。表示時間・区切り・結合の調整では解消できないため、警告を残します{rule}", item.Start));
        }
        var aiTargets = remaining.Where(x => AiChecks.Contains(x.CheckId) && x.Item.Kind is "telop" or "insert"
            && !plan.Telops.Any(t => t.Id == x.Item.Target && t.Type == TelopType.Caption)).ToList();
        string? llmName = null;
        if (aiTargets.Count > 0)
        {
            if (llm == null)
                foreach (var (c, i) in aiTargets) skipped.Add(new(c, i.Target, "LLM が設定されていないため自動修復できません（設定で Azure OpenAI / GitHub Copilot を選んでください）", i.Start));
            else
            {
                llmName = $"{llm.Name} ({llm.Model})";
                // H-* (blocking) first, then by time; bounded per run (a few parallel LLM calls), the rest reported for the next click
                var all = aiTargets.GroupBy(x => x.Item.Target)
                    .OrderBy(g => g.Any(x => x.CheckId.StartsWith("H-", StringComparison.Ordinal)) ? 0 : 1).ThenBy(g => g.First().Item.Start ?? double.MaxValue).ToList();
                var groups = all.Take(MaxAiTargets).ToList();
                foreach (var g in all.Skip(MaxAiTargets)) foreach (var x in g) skipped.Add(new(x.CheckId, g.Key, $"一度に AI で直す上限（{MaxAiTargets} 件）を超えました。もう一度「Copilotで自動修復」を押すと続きを直します", x.Item.Start));
                var answers = new Dictionary<string, Answer>();
                var failed = new Dictionary<string, string>();
                using var gate = new SemaphoreSlim(MaxParallel);
                var batches = await Task.WhenAll(groups.Chunk(BatchSize).Select(async b =>
                {
                    await gate.WaitAsync(ct);
                    try { return (b, Answers: await AskAsync(llm, plan, ctx, b.Select(g => (g.Key, g.Select(x => x.CheckId).ToList(), g.Select(x => x.Item.Detail).ToList())).ToList(), ct), Error: (string?)null); }
                    // one failed request does not lose the others
                    catch (Exception ex) when (ex is not OperationCanceledException) { return (b, Answers: new Dictionary<string, Answer>(), Error: ex.Message); }
                    finally { gate.Release(); }
                }));
                foreach (var (b, ans, err) in batches)
                {
                    foreach (var kv in ans) answers.TryAdd(kv.Key, kv.Value);
                    if (err != null) foreach (var g in b) failed[g.Key] = err;
                }
                foreach (var g in groups)
                {
                    var first = g.First();
                    if (g.All(x => done.Contains((x.CheckId, x.Item.Target)))) continue;
                    if (failed.TryGetValue(g.Key, out var err)) { foreach (var x in g) skipped.Add(new(x.CheckId, g.Key, "LLM の呼び出しに失敗しました: " + err, x.Item.Start)); continue; }
                    if (!answers.TryGetValue(g.Key, out var answer)) { foreach (var x in g) skipped.Add(new(x.CheckId, g.Key, "LLM が修正案を返しませんでした", x.Item.Start)); continue; }
                    var (pr, why) = TextProposal(plan, ctx, first.CheckId, first.Item, answer);
                    why ??= pr == null ? "修正案を作れませんでした" : Try(pr);
                    string Why(string c) => why + (ruleWhy.TryGetValue((c, g.Key), out var rw) ? $"。ルールでも直せませんでした（{rw}）" : "");
                    if (why != null) foreach (var x in g.Where(x => !done.Contains((x.CheckId, x.Item.Target)))) skipped.Add(new(x.CheckId, g.Key, Why(x.CheckId), x.Item.Start));
                    else foreach (var x in g.Where(x => !done.Contains((x.CheckId, x.Item.Target)))) skipped.Add(new(x.CheckId, g.Key, "修正後も指摘が残っています", x.Item.Start));
                }
            }
        }

        // 3) whatever is left needs a person
        foreach (var (c, i) in targets)
            if (!done.Contains((c, i.Target)) && !skipped.Any(s => s.CheckId == c && s.Target == i.Target))
                skipped.Add(new(c, i.Target, QaAdvisor.IsRepairable(c) ? "自動では直せない指摘です" : "人の判断が必要な項目です（推奨する修正方法を参照）", i.Start));

        var final = QaEngine.PlanChecks(ctx with { Plan = plan });
        return new(plan, changes, skipped, before, final, llmName);
    }

    static int Rank(QaStatus s) => s switch { QaStatus.Fail => 2, QaStatus.Warn => 1, _ => 0 };

    /// <summary>Null when <paramref name="next"/> clears the proposal's finding and nothing else gets worse.</summary>
    static string? Regression(List<QaResult> prev, List<QaResult> next, Proposal pr)
    {
        if ((next.FirstOrDefault(q => q.CheckId == pr.CheckId)?.Items ?? []).Any(i => i.Target == pr.Item.Target))
            return "修正しても指摘が消えないため採用しませんでした";
        var worse = new List<string>();
        foreach (var n in next)
        {
            var p = prev.FirstOrDefault(q => q.CheckId == n.CheckId);
            var old = (p?.Items ?? []).Select(i => i.Target).ToHashSet();
            var added = (n.Items ?? []).Where(i => !old.Contains(i.Target)).ToList();
            bool exempt = n.CheckId == "H-08" && pr.NeedsApproval && added.All(i => i.Target == pr.Item.Target);
            if (exempt) continue;
            if (Rank(n.Status) > Rank(p?.Status ?? QaStatus.Pass) || added.Count > 0) worse.Add(n.CheckId);
        }
        return worse.Count == 0 ? null : $"修正すると {string.Join("・", worse)} の指摘が増えるため採用しませんでした";
    }

    static EditPlan SetTelop(EditPlan plan, string id, Func<Telop, Telop> f) => plan with { Telops = plan.Telops.Select(t => t.Id == id ? f(t) : t).ToList() };
    static EditPlan SetInsert(EditPlan plan, string id, Func<Insert, Insert> f) => plan with { Inserts = plan.Inserts.Select(i => i.Id == id ? f(i) : i).ToList() };

    static bool CaptionsPreserved(EditPlan before, EditPlan after)
    {
        static List<Telop> Caps(EditPlan p) => p.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).OrderBy(t => t.Start).ToList();
        var a = Caps(before); var b = Caps(after);
        return Clean(string.Concat(a.Select(t => t.Text))) == Clean(string.Concat(b.Select(t => t.Text)))
            && a.SelectMany(t => t.SourceWordIds).SequenceEqual(b.SelectMany(t => t.SourceWordIds));
    }

    static Proposal? MaskProposal(EditPlan plan, QaContext ctx, QaItem item)
    {
        var mask = Masking.Clean(ctx.MaskWords);
        if (item.Kind == "telop" && plan.Telops.FirstOrDefault(t => t.Id == item.Target) is { } t)
        {
            var s = Masking.MaskText(t.Text, mask);
            return s == t.Text ? null : new("H-10", item, "rule", SetTelop(plan, t.Id, x => x with { Text = s }), t.Text, s, false);
        }
        if (item.Kind == "insert" && plan.Inserts.FirstOrDefault(i => i.Id == item.Target) is { } ins)
        {
            var s = Masking.MaskText(ins.Text, mask);
            return s == ins.Text ? null : new("H-10", item, "rule", SetInsert(plan, ins.Id, x => x with { Text = s }), ins.Text, s, false);
        }
        return null;
    }

    static Proposal? BindProposal(EditPlan plan, QaContext ctx, QaItem item)
    {
        if (plan.Telops.FirstOrDefault(t => t.Id == item.Target) is not { } t || !TelopGrounding.Applies(t)) return null;
        var bound = TelopGrounding.Bind(t with { SourceWordIds = [] }, plan.Timeline, ctx.Transcript);
        if (bound.SourceWordIds.Count == 0 || bound.SourceWordIds.SequenceEqual(t.SourceWordIds)
            || TelopGrounding.Problems(bound, plan.Timeline, ctx.Transcript, ctx.Dictionary, plan.Series).Count > 0) return null;
        string Words(Telop x) => string.Concat(ctx.Transcript.Words.Where(w => x.SourceWordIds.Contains(w.Id)).Select(w => w.Text));
        return new("H-11", item, "rule", SetTelop(plan, t.Id, _ => bound), t.SourceWordIds.Count == 0 ? "（根拠なし）" : Words(t), "根拠: " + Words(bound), false);
    }

    /// <summary>S-08 「先頭が「は」」: move the caption's leading particles/auxiliaries to the end of the previous caption, with their words and time.</summary>
    static Proposal? KinsokuProposal(EditPlan plan, QaContext ctx, QaItem item)
    {
        if (!item.Detail.StartsWith("先頭が", StringComparison.Ordinal)) return null;
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).ToList();
        if (caps.FirstOrDefault(x => x.Id == item.Target) is not { } t) return null;
        var prev = caps.Where(o => o.End >= t.Start - 0.05 && o.Start < t.Start).OrderByDescending(o => o.Start).FirstOrDefault();
        if (prev == null || prev.Speaker != t.Speaker) return null;
        double cut = 0;
        foreach (var clip in plan.Timeline)
        {
            cut += clip.Length;
            if (cut > prev.Start + 0.05 && cut < t.End - 0.05) return null;
        }
        var words = ctx.Transcript.Words.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
        int n = 0; var prefix = "";
        while (n < t.SourceWordIds.Count - 1 && words.TryGetValue(t.SourceWordIds[n], out var w) && !JapaneseText.IsPunct(w.Text) && !JapaneseText.CanStartLine(w.Text)) { prefix += w.Text; n++; }
        if (n == 0 || !t.Text.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = t.Text[prefix.Length..].TrimStart();
        var joined = prev.Text + prefix;
        if (rest.Length == 0 || joined.Length > PlanEditor.MaxTelopLength) return null;
        var mapped = TelopBuilder.MapWords(plan.Timeline, ctx.Transcript);
        var lastMoved = mapped.FirstOrDefault(m => m.Word.Id == t.SourceWordIds[n - 1]);
        var nextWord = mapped.FirstOrDefault(m => m.Word.Id == t.SourceWordIds[n]);
        if (lastMoved == null) return null;
        double start = Math.Round(Math.Max(t.Start, nextWord?.Start ?? lastMoved.End), 3);
        if (start > t.End - 0.3) return null;
        double prevEnd = Math.Round(Math.Min(start, Math.Max(prev.End, lastMoved.End)), 3);
        var p2 = SetTelop(plan, prev.Id, x => x with { Text = joined, End = prevEnd, SourceWordIds = [.. x.SourceWordIds, .. t.SourceWordIds.Take(n)] });
        p2 = SetTelop(p2, t.Id, x => x with { Text = rest, Start = start, SourceWordIds = t.SourceWordIds.Skip(n).ToList() });
        return new("S-08", item, "rule", p2, $"{prev.Text}｜{t.Text}", $"{joined}｜{rest}", false);
    }

    static IEnumerable<Proposal> One(Proposal? p) => p == null ? [] : [p];

    /// <summary>Rule candidates for one finding, least invasive first. Captions keep the speaker's exact words: only time, grouping and word boundaries move.</summary>
    static IEnumerable<Proposal> RuleProposals(string check, EditPlan plan, QaContext ctx, QaItem item) => check switch
    {
        "H-10" => One(MaskProposal(plan, ctx, item)),
        "H-11" => One(BindProposal(plan, ctx, item)),
        "S-08" => One(KinsokuProposal(plan, ctx, item)).Concat(Resegment(plan, ctx, item, check)),
        "S-01" => One(TimingProposal(plan, ctx, item)).Concat(Resegment(plan, ctx, item, check)),
        "S-02" => Resegment(plan, ctx, item, check),
        _ => [],
    };

    /// <summary>S-01 by timing, before touching words: show the caption longer, into the free time around it
    /// (≤ <see cref="TelopBuilder.CaptionHoldMax"/> after its last word, ≤ <see cref="LeadIn"/> before its first, 2-frame gap to the neighbouring captions).
    /// The previous caption gives up its hold after its last word when it can still be read in the time left.</summary>
    static Proposal? TimingProposal(EditPlan plan, QaContext ctx, QaItem item)
    {
        if (plan.Telops.FirstOrDefault(x => x.Id == item.Target) is not { Enabled: true, Type: TelopType.Caption } t) return null;
        if (Stretch(plan, ctx, t.Id, TelopBuilder.MapWords(plan.Timeline, ctx.Transcript)) is not { } s) return null;
        return new("S-01", item, "rule", s.Plan, $"表示 {t.End - t.Start:F2} 秒", $"表示 {s.End - s.Start:F2} 秒{s.Note}", false);
    }

    /// <summary>The caption <paramref name="id"/> shown long enough to read, within the free time around it; null when it already is or cannot be.</summary>
    static (EditPlan Plan, double Start, double End, string Note)? Stretch(EditPlan plan, QaContext ctx, string id, List<TelopBuilder.Mapped> mapped)
    {
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).ToList();
        if (caps.FirstOrDefault(x => x.Id == id) is not { } t) return null;
        double cps = ctx.Preset.Telop.Caption.MaxCps;
        double need = JapaneseText.DisplayWidth(t.Text) / cps + 0.02;
        if (t.End - t.Start >= need) return null;
        (double First, double Last) Span(Telop x)
        {
            var ids = x.SourceWordIds.ToHashSet();
            var own = mapped.Where(m => ids.Contains(m.Word.Id)).ToList();
            return own.Count > 0 ? (own.Min(m => m.Start), own.Max(m => m.End)) : (x.Start, x.End);
        }
        var (first, last) = Span(t);
        double nextStart = caps.Where(o => o.Id != t.Id && o.Start >= t.End - 1e-3).Select(o => o.Start).DefaultIfEmpty(double.MaxValue).Min();
        var prev = caps.Where(o => o.Id != t.Id && o.End <= t.Start + 1e-3).OrderByDescending(o => o.End).FirstOrDefault();
        double prevFloor = 0;
        if (prev != null)
        {
            double prevNeed = JapaneseText.DisplayWidth(prev.Text) / cps + 0.02;
            prevFloor = Math.Min(prev.End, Math.Max(Span(prev).Last, prev.Start + prevNeed));
        }
        double maxEnd = Math.Min(Math.Min(nextStart - Gap, last + TelopBuilder.CaptionHoldMax), plan.Duration);
        double minStart = Math.Max(Math.Max(prevFloor + Gap, first - LeadIn), 0);
        double end = Math.Max(t.End, Math.Min(maxEnd, t.Start + need));
        double start = Math.Min(t.Start, Math.Max(minStart, end - need));
        end = Math.Floor(end * 100) / 100; start = Math.Ceiling(start * 100) / 100;
        if (end - start < need - 0.02 - 1e-6 || (end <= t.End && start >= t.Start)) return null;
        var p2 = SetTelop(plan, t.Id, x => x with { Start = start, End = end });
        var note = "";
        if (prev != null && start < prev.End + Gap - 1e-6)
        {
            double prevEnd = Math.Floor((start - Gap) * 100) / 100;
            p2 = SetTelop(p2, prev.Id, x => x with { End = prevEnd });
            note = $"（前の字幕の余韻を {prev.End - prevEnd:F2} 秒ゆずる）";
        }
        return (p2, start, end, note);
    }

    const double Gap = 2 / 30.0, LeadIn = 0.25, MaxJoinGap = 0.6;

    /// <summary>
    /// Captions regrouped with a neighbour, words untouched: join it with the caption before/after (as subtitle editors merge a
    /// flickering 「はい」), or move words across the boundary (「…ですね｜で」, an over-long line handing its tail to the next caption).
    /// Never across a speaker change, a cut, or a pause longer than <see cref="MaxJoinGap"/>; the re-check decides what is kept.
    /// </summary>
    static IEnumerable<Proposal> Resegment(EditPlan plan, QaContext ctx, QaItem item, string check)
    {
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).OrderBy(x => x.Start).ToList();
        int i = caps.FindIndex(x => x.Id == item.Target);
        if (i < 0) yield break;
        var t = caps[i];
        var cuts = new List<double>(); double acc = 0;
        foreach (var c in plan.Timeline) { acc += c.Length; cuts.Add(acc); }
        bool Joinable(Telop a, Telop b) => b.Start - a.End <= MaxJoinGap && a.Speaker == b.Speaker && !cuts.Any(c => c > a.Start + 0.05 && c < b.End - 0.05);
        var prev = i > 0 && Joinable(caps[i - 1], t) ? caps[i - 1] : null;
        var next = i + 1 < caps.Count && Joinable(t, caps[i + 1]) ? caps[i + 1] : null;
        var p = ctx.Preset.Telop.Caption;
        double maxWidth = AssWriter.CaptionLineChars(ctx.Preset, plan.Canvas.Width) * Math.Max(1, p.MaxLines);
        // cheap pre-check before the full re-check: every caption the candidate produces is readable and fits
        bool Fits(params Telop[] xs) => xs.All(x => JapaneseText.DisplayWidth(x.Text) <= maxWidth && x.End - x.Start >= 0.2
            && JapaneseText.DisplayWidth(x.Text) / (x.End - x.Start) <= p.MaxCps + 0.01);
        var wordIndex = new Dictionary<string, int>();
        for (int k = 0; k < ctx.Transcript.Words.Count; k++) wordIndex.TryAdd(ctx.Transcript.Words[k].Id, k);
        var mappedList = TelopBuilder.MapWords(plan.Timeline, ctx.Transcript);
        var mapped = mappedList.GroupBy(m => m.Word.Id).ToDictionary(g => g.Key, g => g.First());
        // the regrouped captions then take the free time around them (as the timing rule does); the pre-check runs on the result
        EditPlan? Settle(EditPlan p2, params string[] ids)
        {
            foreach (var id in ids) if (Stretch(p2, ctx, id, mappedList) is { } s) p2 = s.Plan;
            return Fits([.. p2.Telops.Where(x => ids.Contains(x.Id))]) ? p2 : null;
        }

        Proposal? Merge(params Telop[] xs)
        {
            var text = xs[0].Text;
            for (int k = 1; k < xs.Length; k++)
            {
                var (a, b) = (xs[k - 1], xs[k]);
                // a sentence/clause break or a breath pause between them stays visible as a space
                bool punct = b.SourceWordIds.Count > 0 && wordIndex.TryGetValue(b.SourceWordIds[0], out var wi) && wi > 0 && JapaneseText.IsPunct(ctx.Transcript.Words[wi - 1].Text);
                bool pause = a.SourceWordIds.Count > 0 && b.SourceWordIds.Count > 0 && mapped.TryGetValue(a.SourceWordIds[^1], out var la) && mapped.TryGetValue(b.SourceWordIds[0], out var fb) && fb.Start - la.End >= 0.25;
                bool latin = text.Length > 0 && b.Text.Length > 0 && char.IsAsciiLetterOrDigit(text[^1]) && char.IsAsciiLetterOrDigit(b.Text[0]);
                text += (punct || pause || latin ? "　" : "") + b.Text;
            }
            var merged = xs[0] with { End = xs[^1].End, Text = text, SourceWordIds = xs.SelectMany(x => x.SourceWordIds).ToList(), Approved = xs.All(x => x.Approved) };
            if (JapaneseText.DisplayWidth(merged.Text) > maxWidth) return null;
            var drop = xs.Skip(1).Select(x => x.Id).ToHashSet();
            if (Settle(plan with { Telops = plan.Telops.Where(x => !drop.Contains(x.Id)).Select(x => x.Id == merged.Id ? merged : x).ToList() }, merged.Id) is not { } p2) return null;
            return new(check, item, "rule", p2, string.Join("｜", xs.Select(x => x.Text)), merged.Text, false);
        }

        // words of a caption that still shows exactly its recognised words, with their output times
        List<TelopBuilder.Mapped>? Words(Telop x)
        {
            var ws = x.SourceWordIds.Select(id => mapped.GetValueOrDefault(id)).ToList();
            if (ws.Count == 0 || ws.Any(w => w == null) || Clean(string.Concat(ws.Select(w => w!.Word.Text))) != Clean(x.Text)) return null;
            return ws!;
        }
        IEnumerable<Proposal> Moves(Telop a, Telop b)
        {
            if (Words(a) is not { } wa || Words(b) is not { } wb) yield break;
            var all = wa.Concat(wb).ToList();
            foreach (var k in Enumerable.Range(1, all.Count - 1).Where(k => k != wa.Count && Math.Abs(k - wa.Count) <= 3).OrderBy(k => Math.Abs(k - wa.Count)))
            {
                if (JapaneseText.IsPunct(all[k].Word.Text) || JapaneseText.TokenBreakCost(all[k - 1].Word.Text, all[k].Word.Text) >= JapaneseText.Detached) continue;
                var (A, B) = (all.Take(k).ToList(), all.Skip(k).ToList());
                var ta = JapaneseText.NormalizeTelop(string.Concat(A.Select(w => w.Word.Text)));
                var tb = JapaneseText.NormalizeTelop(string.Concat(B.Select(w => w.Word.Text)));
                if (ta.Length == 0 || tb.Length == 0) continue;
                double boundary = Math.Round(Math.Clamp(B[0].Start, a.Start + 0.2, b.End - 0.2), 3);
                var na = a with { Text = ta, End = boundary, SourceWordIds = A.Select(w => w.Word.Id).ToList() };
                var nb = b with { Text = tb, Start = boundary, SourceWordIds = B.Select(w => w.Word.Id).ToList() };
                if (JapaneseText.DisplayWidth(ta) > maxWidth || JapaneseText.DisplayWidth(tb) > maxWidth) continue;
                if (Settle(SetTelop(SetTelop(plan, a.Id, _ => na), b.Id, _ => nb), a.Id, b.Id) is not { } p2) continue;
                yield return new(check, item, "rule", p2, $"{a.Text}｜{b.Text}", $"{ta}｜{tb}", false);
            }
        }

        var merges = new[] { prev != null ? Merge(prev, t) : null, next != null ? Merge(t, next) : null };
        var moves = (prev != null ? Moves(prev, t) : []).Concat(next != null ? Moves(t, next) : []);
        var last = prev != null && next != null ? Merge(prev, t, next) : null;
        // a flickering caption is best joined; an over-long or badly-started one is best re-split
        var ordered = check == "S-01" ? merges.Concat(moves).Append(last) : moves.Concat(merges).Append(last);
        foreach (var pr in ordered) if (pr != null) yield return pr;
    }

    static string Clean(string s) => new(JapaneseText.NormalizeTelop(s).Where(c => !char.IsWhiteSpace(c) && !JapaneseText.IsPunct(c.ToString())).ToArray());

    static string Spoken(EditPlan plan, QaContext ctx, double from, double to)
    {
        var mapped = TelopBuilder.MapWords(plan.Timeline, ctx.Transcript);
        return string.Concat(mapped.Where(m => m.End > from && m.Start < to).Select(m => m.Word.Text));
    }

    static (string Kind, string Text, string Constraint, string Context)? Describe(EditPlan plan, QaContext ctx, string target)
    {
        var p = ctx.Preset;
        if (plan.Telops.FirstOrDefault(t => t.Id == target) is { } t)
        {
            var near = Spoken(plan, ctx, t.Start - 1.5, t.End + 1.5);
            var evidence = string.Concat(TelopGrounding.Evidence(t, plan.Timeline, ctx.Transcript).Select(w => w.Text));
            var context = evidence.Length > 0 ? $"根拠発話: {evidence}／前後の発話: {near}" : $"前後の発話: {near}";
            switch (t.Type)
            {
                case TelopType.Caption:
                    return null;
                case TelopType.Tsukkomi:
                    return ("tsukkomi", t.Text, "視聴者のツッコミ。14 文字以内。発話をそのまま引用しない。発話にない数字・名前を使わない", context);
                case TelopType.Emphasis or TelopType.Hook or TelopType.Headline or TelopType.Counter:
                    int max = t.Type == TelopType.Emphasis ? Math.Min(PlanEditor.MaxTelopLength, p.Telop.Caption.MaxCharsPerLine) : PlanEditor.MaxTelopLength;
                    return (t.Type.ToString().ToLowerInvariant(), t.Text, $"{TypeName(t.Type)}テロップ。根拠発話の一節をそのまま使う（数字・単位・固有名詞・否定は発話どおり）。{max} 文字以内", context);
            }
            return null;
        }
        if (plan.Inserts.FirstOrDefault(i => i.Id == target) is { Kind: "card" } card)
            return ("card", card.Text, "場面転換カード。14 文字以内の短い一言。発話をそのまま引用しない。発話にない数字・名前を使わない", "前後の発話: " + Spoken(plan, ctx, card.At - 6, card.At + 6));
        return null;
    }

    static string TypeName(TelopType t) => t switch { TelopType.Emphasis => "強調", TelopType.Hook => "冒頭フック", TelopType.Headline => "見出し", TelopType.Counter => "カウンター", _ => "" };

    static async Task<Dictionary<string, Answer>> AskAsync(IHighlightSelector llm, EditPlan plan, QaContext ctx, List<(string Target, List<string> Checks, List<string> Details)> items, CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var (target, checks, details) in items)
            if (Describe(plan, ctx, target) is { } d)
                lines.Add($"id: {target}\n種類: {d.Kind}\n指摘: {string.Join(" / ", checks.Zip(details, (c, x) => $"{c} {x}"))}\n制約: {d.Constraint}\n現在: {d.Text}\n{d.Context}");
        if (lines.Count == 0) return [];
        var json = await llm.CompleteJsonAsync(System, string.Join("\n\n", lines), "qa_repair", Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var res = new Dictionary<string, Answer>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            res[it.GetProperty("id").GetString() ?? ""] = new((it.GetProperty("text").GetString() ?? "").Replace("\r", "").Replace("\n", " ").Trim());
        }
        return res;
    }

    /// <summary>Type-specific validation of an LLM answer before the QA re-check.</summary>
    static (Proposal? Proposal, string? Why) TextProposal(EditPlan plan, QaContext ctx, string check, QaItem item, Answer answer)
    {
        var text = answer.Text;
        if (plan.Telops.FirstOrDefault(t => t.Id == item.Target) is { } t)
        {
            var s = JapaneseText.NormalizeTelop(text);
            if (t.Type == TelopType.Caption) return (null, "字幕は話した言葉どおりに保持するため、AI で文言を変更しません");
            if (s.Length == 0 || s == t.Text) return (null, "LLM が直せないと判断しました");
            if (s.Length > PlanEditor.MaxTelopLength) return (null, $"修正案が {PlanEditor.MaxTelopLength} 文字を超えました");
            switch (t.Type)
            {
                case TelopType.Tsukkomi:
                    if (TsukkomiService.ValidateText(s, Spoken(plan, ctx, t.Start - 1.5, t.End + 1.5)) is { } bad) return (null, $"ツッコミの基準を満たしません（{bad}）");
                    return (new(check, item, "ai", SetTelop(plan, t.Id, x => x with { Text = s, Approved = false }), t.Text, s, true), null);
                default:
                    var kept = t with { Text = s, Approved = false };
                    var cand = TelopGrounding.Problems(kept, plan.Timeline, ctx.Transcript, ctx.Dictionary, plan.Series).Count == 0 && !TelopGrounding.NegationDropped(kept, plan.Timeline, ctx.Transcript)
                        ? kept : TelopGrounding.Bind(kept with { SourceWordIds = [] }, plan.Timeline, ctx.Transcript);
                    if (TelopGrounding.Problems(cand, plan.Timeline, ctx.Transcript, ctx.Dictionary, plan.Series).Count > 0) return (null, "修正案を根拠発話で確認できませんでした");
                    if (TelopGrounding.NegationDropped(cand, plan.Timeline, ctx.Transcript)) return (null, "修正案で否定が抜けています");
                    return (new(check, item, "ai", SetTelop(plan, t.Id, _ => cand), t.Text, s, true), null);
            }
        }
        if (plan.Inserts.FirstOrDefault(i => i.Id == item.Target) is { Kind: "card" } card)
        {
            var s = text.Trim();
            if (s.Length == 0 || s == card.Text) return (null, "LLM が直せないと判断しました");
            if (TsukkomiService.ValidateText(s, Spoken(plan, ctx, card.At - 6, card.At + 6)) is { } bad) return (null, $"カードの基準を満たしません（{bad}）");
            return (new(check, item, "ai", SetInsert(plan, card.Id, x => x with { Text = s, TextSource = "ai", Approved = false }), card.Text, s, true), null);
        }
        return (null, "対象が見つかりません");
    }
}
