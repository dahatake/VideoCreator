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
- H-11 の修正では、根拠発話にある連続した語句をそのまま使い、言い換えない。根拠に結び付けられない場合は元の文字を返す
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
        // repairs never touch the timeline: word times, cut points and the caption checker are computed once
        var prep = new Prep(plan, ctx);
        var flags = Flags(plan, prep);
        var changes = new List<QaRepairChange>(); var skipped = new List<QaRepairSkip>();
        var targets = current.Where(q => q.Status is QaStatus.Warn or QaStatus.Fail && (checkIds is not { Count: > 0 } || checkIds.Contains(q.CheckId)))
            .SelectMany(q => (q.Items ?? []).Select(i => (q.CheckId, Item: i))).ToList();
        var done = new HashSet<(string, string)>();

        string? Try(Proposal pr)
        {
            if (pr.CheckId != "H-10" && !CaptionsPreserved(plan, pr.Plan))
                return "字幕の語句や発話との対応が変わるため採用しませんでした（字幕は話した言葉どおりに保持します）";
            var after = QaEngine.PlanChecks(qctx with { Plan = pr.Plan });
            var why = Regression(current, plan, after, pr.Plan, pr);
            if (why != null) return why;
            plan = pr.Plan with { Provenance = pr.Plan.Provenance with { Edits = [.. pr.Plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "qa_repair", pr.Item.Target, pr.Before, pr.After)] } };
            current = after;
            flags = Flags(plan, prep);
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
            var candidates = RuleProposals(check, plan, ctx, item, prep, flags).ToList();
            if (candidates.Count == 0)
            {
                if (check is "S-01" or "S-02" or "S-08" && item.Kind == "telop")
                {
                    var why0 = Unfixable(check, plan, ctx, prep, item.Target);
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
                foreach (var (c, i) in aiTargets) skipped.Add(new(c, i.Target, "LLM が設定されていないため自動修復できません（設定で Microsoft Foundry / GitHub Copilot を選んでください）", i.Start));
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

    /// <summary>
    /// Per finding, whether 「Copilotで自動修復」 will fix it, without running it: a rule finding counts when a candidate passes the same word-level
    /// test the repair applies (<see cref="LocalOk"/>), an AI finding when an LLM is configured. The rest get a reason, and a check with nothing
    /// fixable stops offering a repair button. Runs only where results are shown (PlanChecks stays fast for the repair loop itself).
    /// </summary>
    public static List<QaResult> Assess(QaContext ctx, List<QaResult> results, bool llm)
    {
        var plan = ctx.Plan;
        Prep? prep = null; Dictionary<string, HashSet<string>>? flags = null;
        var caption = plan.Telops.Where(t => t.Type == TelopType.Caption).Select(t => t.Id).ToHashSet();
        // H-10/H-11 have few findings: their rule is checked with the full re-check the repair applies (it may make another check worse)
        var qctx = ctx with { Approval = null };
        List<QaResult>? current = null;
        bool Accepted(Proposal? pr)
        {
            if (pr == null || pr.CheckId != "H-10" && !CaptionsPreserved(plan, pr.Plan)) return false;
            current ??= QaEngine.PlanChecks(qctx);
            return Regression(current, plan, QaEngine.PlanChecks(qctx with { Plan = pr.Plan }), pr.Plan, pr) == null;
        }
        string? Mark(string check, QaItem i)
        {
            bool text = i.Kind is "telop" or "insert" && !caption.Contains(i.Target);
            switch (check)
            {
                case "H-10": return Accepted(MaskProposal(plan, ctx, i)) ? "rule" : null;
                case "H-11": return Accepted(BindProposal(plan, ctx, i)) ? "rule" : text && llm ? "ai" : null;
                case "H-07" or "S-09": return text && llm ? "ai" : null;
                case "S-01" or "S-02" or "S-08" when caption.Contains(i.Target):
                    prep ??= new Prep(plan, ctx); flags ??= Flags(plan, prep);
                    return RuleProposals(check, plan, ctx, i, prep, flags).Any() ? "rule" : null;
                case "S-02": return text && llm ? "ai" : null;
                default: return null;
            }
        }
        var res = new List<QaResult>(results.Count);
        foreach (var q in results)
        {
            if (q.Advice is not { } a || q.Items is not { Count: > 0 } items || !QaAdvisor.IsRepairable(q.CheckId)) { res.Add(q); continue; }
            var marked = items.Select(i =>
            {
                var m = Mark(q.CheckId, i);
                string? note = null;
                if (m == null && i.Kind == "telop" && caption.Contains(i.Target) && q.CheckId is "S-01" or "S-02" or "S-08")
                {
                    prep ??= new Prep(plan, ctx);
                    note = Unfixable(q.CheckId, plan, ctx, prep, i.Target);
                }
                else if (m == null && q.CheckId is "H-07" or "H-11" or "S-02" or "S-09" && !llm && i.Kind is "telop" or "insert" && !caption.Contains(i.Target))
                    note = "LLM が設定されていないため自動では直せません";
                return i with { Repair = m, Note = note };
            }).ToList();
            int fixable = marked.Count(i => i.Repair != null);
            var reasons = marked.Where(i => i.Repair == null && i.Note != null).GroupBy(i => Category(i.Note!)).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} 件: {g.Key}").ToList();
            var remaining = fixable == marked.Count ? null
                : $"自動で直せない {marked.Count - fixable} 件は警告のまま残ります" + (reasons.Count > 0 ? $"（{string.Join(" / ", reasons)}）" : "");
            var repair = fixable == 0 ? a.Repair == "manual" ? "manual" : "none"
                : a.Repair == "ai" && !marked.Any(i => i.Repair == "ai") ? "rule" : a.Repair;
            var advice = a with { Fixable = fixable, Remaining = remaining, Repair = repair };
            res.Add(q with { Items = marked, Advice = advice });
        }
        return res;
    }

    // the reason without the per-caption numbers, to group findings
    static string Category(string note)
    {
        int k = note.IndexOf('：');
        return k > 0 ? note[..k] : note;
    }

    /// <summary>Why no rule fixes this caption finding. For S-01 the bound is computed from the words: even shown from <see cref="LeadIn"/> before
    /// its first word to <see cref="TelopBuilder.CaptionHoldMax"/> after its last (never over the neighbours' words), is it still too fast?</summary>
    static string Unfixable(string check, EditPlan plan, QaContext ctx, Prep prep, string id)
    {
        var limits = $"1 行 {prep.Checks.LineChars} 字・毎秒 {ctx.Preset.Telop.Caption.MaxCps} 字";
        if (check == "S-02") return $"話す速さか言葉の切れ目のため、区切り直すと別の字幕が基準を超えます：基準 {limits}";
        if (check == "S-08") return $"前の字幕へ移す・区切り直す・結合のどれでも、ほかの字幕が基準を超えるか話者・カットが変わります：基準 {limits}";
        var caps = Caps(plan);
        int i = caps.FindIndex(t => t.Id == id);
        if (i < 0 || prep.Span(caps[i]) is not { } s) return $"表示時間の延長や、前後の字幕への付け替え・結合では基準に収まりません：基準 {limits}";
        double w = JapaneseText.DisplayWidth(caps[i].Text), max = ctx.Preset.Telop.Caption.MaxCps;
        double lo = Math.Max(0, s.First - LeadIn), hi = Math.Min(plan.Duration, s.Last + TelopBuilder.CaptionHoldMax);
        if (i > 0 && prep.Span(caps[i - 1]) is { } a) lo = Math.Max(lo, a.Last + Gap);
        if (i + 1 < caps.Count && prep.Span(caps[i + 1]) is { } b) hi = Math.Min(hi, b.First - Gap);
        double spoken = w / Math.Max(0.05, s.Last - s.First), best = w / Math.Max(0.05, hi - lo);
        return best > max + 0.01
            ? $"話す速さが基準を超えています：発話は毎秒 {spoken:F1} 字、前後の空き時間まで表示しても毎秒 {best:F1} 字（基準 {max} 字）"
            : $"前後の字幕も速く、表示を延ばすと隣の字幕が基準を超えます：結合や付け替えでも収まりません（基準 {limits}）";
    }

    /// <summary>S-01/S-02/S-08 are judged by the spoken words they flag, not by telop ids: a split or a merge renames captions, yet only showing
    /// more words too fast / too wide / badly broken is a regression.</summary>
    static readonly string[] WordChecks = ["S-01", "S-02", "S-08"];

    static List<Telop> Caps(EditPlan p) => p.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).OrderBy(t => t.Start).ToList();
    static IEnumerable<string> Keys(Telop t) => t.SourceWordIds.Count > 0 ? t.SourceWordIds : ["#" + t.Id];
    static HashSet<string> TargetKeys(EditPlan plan, string target) =>
        plan.Telops.FirstOrDefault(t => t.Id == target) is { Type: TelopType.Caption } t ? Keys(t).ToHashSet() : ["#" + target];

    static HashSet<string> FlaggedKeys(List<QaResult> res, EditPlan plan, string check)
    {
        var set = new HashSet<string>();
        foreach (var i in res.FirstOrDefault(q => q.CheckId == check)?.Items ?? []) set.UnionWith(TargetKeys(plan, i.Target));
        return set;
    }

    /// <summary>Null when <paramref name="next"/> clears the proposal's finding and nothing else gets worse.</summary>
    static string? Regression(List<QaResult> prev, EditPlan prevPlan, List<QaResult> next, EditPlan nextPlan, Proposal pr)
    {
        bool stays = WordChecks.Contains(pr.CheckId)
            ? FlaggedKeys(next, nextPlan, pr.CheckId).Overlaps(TargetKeys(prevPlan, pr.Item.Target))
            : (next.FirstOrDefault(q => q.CheckId == pr.CheckId)?.Items ?? []).Any(i => i.Target == pr.Item.Target);
        if (stays) return "修正しても指摘が消えないため採用しませんでした";
        var worse = new List<string>();
        foreach (var n in next)
        {
            var p = prev.FirstOrDefault(q => q.CheckId == n.CheckId);
            bool added;
            if (WordChecks.Contains(n.CheckId))
            {
                var old = FlaggedKeys(prev, prevPlan, n.CheckId);
                added = FlaggedKeys(next, nextPlan, n.CheckId).Any(k => !old.Contains(k));
            }
            else
            {
                var old = (p?.Items ?? []).Select(i => i.Target).ToHashSet();
                var add = (n.Items ?? []).Where(i => !old.Contains(i.Target)).ToList();
                if (n.CheckId == "H-08" && pr.NeedsApproval && add.All(i => i.Target == pr.Item.Target)) continue;
                added = add.Count > 0;
            }
            if (Rank(n.Status) > Rank(p?.Status ?? QaStatus.Pass) || added) worse.Add(n.CheckId);
        }
        return worse.Count == 0 ? null : $"修正すると {string.Join("・", worse)} の指摘が増えるため採用しませんでした";
    }

    /// <summary>Precomputed per timeline (repairs never change it).</summary>
    sealed class Prep
    {
        public readonly List<TelopBuilder.Mapped> MappedList;
        public readonly Dictionary<string, TelopBuilder.Mapped> Mapped;
        public readonly Dictionary<string, int> WordIndex = [];
        public readonly List<double> Cuts = [];
        public readonly CaptionChecks Checks;

        public Prep(EditPlan plan, QaContext ctx)
        {
            MappedList = TelopBuilder.MapWords(plan.Timeline, ctx.Transcript);
            Mapped = MappedList.GroupBy(m => m.Word.Id).ToDictionary(g => g.Key, g => g.First());
            for (int k = 0; k < ctx.Transcript.Words.Count; k++) WordIndex.TryAdd(ctx.Transcript.Words[k].Id, k);
            double acc = 0;
            foreach (var c in plan.Timeline) { acc += c.Length; Cuts.Add(acc); }
            Checks = new CaptionChecks(ctx.Preset, plan.Canvas.Width, ctx.Transcript);
        }

        /// <summary>Output time of the caption's first and last recognised word.</summary>
        public (double First, double Last)? Span(Telop x)
        {
            double first = double.MaxValue, last = double.MinValue;
            foreach (var id in x.SourceWordIds)
                if (Mapped.TryGetValue(id, out var m)) { first = Math.Min(first, m.Start); last = Math.Max(last, m.End); }
            return first <= last ? (first, last) : null;
        }
    }

    // a caption can only start with a particle of a caption that is still on screen, a few captions back at most
    static IEnumerable<Telop> Earlier(List<Telop> caps, int i) => caps.Skip(Math.Max(0, i - 4)).Take(i - Math.Max(0, i - 4));

    /// <summary>Spoken words (or #id of a caption without words) flagged by S-01/S-02/S-08 in <paramref name="plan"/>.</summary>
    static Dictionary<string, HashSet<string>> Flags(EditPlan plan, Prep prep)
    {
        var caps = Caps(plan);
        var res = WordChecks.ToDictionary(c => c, _ => new HashSet<string>());
        for (int i = 0; i < caps.Count; i++)
            foreach (var c in WordChecks)
                if (prep.Checks.Of(c, caps[i], Earlier(caps, i)) != null) res[c].UnionWith(Keys(caps[i]));
        return res;
    }

    static bool Same(Telop a, Telop b) => a.Start == b.Start && a.End == b.End && a.Text == b.Text && a.SourceWordIds.SequenceEqual(b.SourceWordIds);

    /// <summary>The word-level regression test of <see cref="Regression"/>, run only on the captions a candidate touches (and the one after each,
    /// whose 「先頭が」 depends on it): the target's words leave <paramref name="check"/> and no caption check flags a word it did not flag before.</summary>
    static bool LocalOk(EditPlan before, EditPlan after, Dictionary<string, HashSet<string>> flags, string check, string targetId, Prep prep)
    {
        var old = new Dictionary<string, Telop>();
        foreach (var t in before.Telops) if (t.Enabled && t.Type == TelopType.Caption) old.TryAdd(t.Id, t);
        var targetKeys = TargetKeys(before, targetId);
        var caps = Caps(after);
        var window = new SortedSet<int>();
        for (int i = 0; i < caps.Count; i++)
        {
            var x = caps[i];
            if (!old.TryGetValue(x.Id, out var o) || !Same(o, x) || Keys(x).Any(targetKeys.Contains))
            {
                window.Add(i);
                if (i + 1 < caps.Count) window.Add(i + 1);
            }
        }
        foreach (var i in window)
        {
            var keys = Keys(caps[i]).ToList();
            foreach (var c in WordChecks)
            {
                if (prep.Checks.Of(c, caps[i], Earlier(caps, i)) == null) continue;
                if (c == check && keys.Any(targetKeys.Contains)) return false;
                if (!keys.All(flags[c].Contains)) return false;
            }
        }
        return true;
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
        var bound = TelopGrounding.Bind(t with { SourceWordIds = [] }, plan.Timeline, ctx.Transcript, ctx.Dictionary, plan.Series);
        if (bound.SourceWordIds.Count == 0 || bound.SourceWordIds.SequenceEqual(t.SourceWordIds)
            || TelopGrounding.Problems(bound, plan.Timeline, ctx.Transcript, ctx.Dictionary, plan.Series).Count > 0) return null;
        string Words(Telop x) => string.Concat(ctx.Transcript.Words.Where(w => x.SourceWordIds.Contains(w.Id)).Select(w => w.Text));
        return new("H-11", item, "rule", SetTelop(plan, t.Id, _ => bound), t.SourceWordIds.Count == 0 ? "（根拠なし）" : Words(t), "根拠: " + Words(bound), false);
    }

    /// <summary>S-08 「先頭が「は」」: move the caption's leading particles/auxiliaries to the end of the previous caption, with their words and time.</summary>
    static Proposal? KinsokuProposal(EditPlan plan, QaContext ctx, QaItem item, Prep prep)
    {
        if (!item.Detail.StartsWith("先頭が", StringComparison.Ordinal)) return null;
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).ToList();
        if (caps.FirstOrDefault(x => x.Id == item.Target) is not { } t) return null;
        var prev = caps.Where(o => o.End >= t.Start - 0.05 && o.Start < t.Start).OrderByDescending(o => o.Start).FirstOrDefault();
        if (prev == null || prev.Speaker != t.Speaker) return null;
        if (prep.Cuts.Any(cut => cut > prev.Start + 0.05 && cut < t.End - 0.05)) return null;
        var words = ctx.Transcript.Words;
        int n = 0; var prefix = "";
        while (n < t.SourceWordIds.Count - 1 && prep.WordIndex.TryGetValue(t.SourceWordIds[n], out var wi) && words[wi] is var w && !JapaneseText.IsPunct(w.Text) && !JapaneseText.CanStartLine(w.Text)) { prefix += w.Text; n++; }
        if (n == 0 || !t.Text.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = t.Text[prefix.Length..].TrimStart();
        var joined = prev.Text + prefix;
        if (rest.Length == 0 || joined.Length > PlanEditor.MaxTelopLength) return null;
        var lastMoved = prep.Mapped.GetValueOrDefault(t.SourceWordIds[n - 1]);
        var nextWord = prep.Mapped.GetValueOrDefault(t.SourceWordIds[n]);
        if (lastMoved == null) return null;
        double start = Math.Round(Math.Max(t.Start, nextWord?.Start ?? lastMoved.End), 3);
        if (start > t.End - 0.3) return null;
        double prevEnd = Math.Round(Math.Min(start, Math.Max(prev.End, lastMoved.End)), 3);
        var p2 = SetTelop(plan, prev.Id, x => x with { Text = joined, End = prevEnd, SourceWordIds = [.. x.SourceWordIds, .. t.SourceWordIds.Take(n)] });
        p2 = SetTelop(p2, t.Id, x => x with { Text = rest, Start = start, SourceWordIds = t.SourceWordIds.Skip(n).ToList() });
        return new("S-08", item, "rule", p2, $"{prev.Text}｜{t.Text}", $"{joined}｜{rest}", false);
    }

    static IEnumerable<Proposal> One(Proposal? p) => p == null ? [] : [p];

    /// <summary>Rule candidates for one finding, least invasive first. Captions keep the speaker's exact words: only time, grouping and word boundaries move.
    /// Caption candidates must pass <see cref="LocalOk"/> (the cheap form of the word-level re-check) before the full re-check sees them.</summary>
    static IEnumerable<Proposal> RuleProposals(string check, EditPlan plan, QaContext ctx, QaItem item, Prep prep, Dictionary<string, HashSet<string>> flags)
    {
        var all = check switch
        {
            "H-10" => One(MaskProposal(plan, ctx, item)),
            "H-11" => One(BindProposal(plan, ctx, item)),
            "S-08" => One(KinsokuProposal(plan, ctx, item, prep)).Concat(Resegment(plan, ctx, item, check, prep)),
            "S-01" => One(TimingProposal(plan, ctx, item, prep)).Concat(Resegment(plan, ctx, item, check, prep)),
            "S-02" => Resegment(plan, ctx, item, check, prep),
            _ => [],
        };
        return WordChecks.Contains(check) ? all.Where(pr => LocalOk(plan, pr.Plan, flags, check, item.Target, prep)) : all;
    }

    /// <summary>S-01 by timing, before touching words: show the caption longer, into the free time around it
    /// (≤ <see cref="TelopBuilder.CaptionHoldMax"/> after its last word, ≤ <see cref="LeadIn"/> before its first, 2-frame gap to the neighbouring captions).
    /// The previous caption gives up its hold after its last word when it can still be read in the time left.</summary>
    static Proposal? TimingProposal(EditPlan plan, QaContext ctx, QaItem item, Prep prep)
    {
        if (plan.Telops.FirstOrDefault(x => x.Id == item.Target) is not { Enabled: true, Type: TelopType.Caption } t) return null;
        if (Stretch(plan, ctx, t.Id, prep) is not { } s) return null;
        return new("S-01", item, "rule", s.Plan, $"表示 {t.End - t.Start:F2} 秒", $"表示 {s.End - s.Start:F2} 秒{s.Note}", false);
    }

    /// <summary>The caption <paramref name="id"/> shown long enough to read, within the free time around it; null when it already is or cannot be.</summary>
    static (EditPlan Plan, double Start, double End, string Note)? Stretch(EditPlan plan, QaContext ctx, string id, Prep prep)
    {
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).ToList();
        if (caps.FirstOrDefault(x => x.Id == id) is not { } t) return null;
        double cps = ctx.Preset.Telop.Caption.MaxCps;
        double need = JapaneseText.DisplayWidth(t.Text) / cps + 0.02;
        if (t.End - t.Start >= need) return null;
        (double First, double Last) Span(Telop x) => prep.Span(x) ?? (x.Start, x.End);
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
    static IEnumerable<Proposal> Resegment(EditPlan plan, QaContext ctx, QaItem item, string check, Prep prep)
    {
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).OrderBy(x => x.Start).ToList();
        int i = caps.FindIndex(x => x.Id == item.Target);
        if (i < 0) yield break;
        var t = caps[i];
        bool Joinable(Telop a, Telop b) => b.Start - a.End <= MaxJoinGap && a.Speaker == b.Speaker && !prep.Cuts.Any(c => c > a.Start + 0.05 && c < b.End - 0.05);
        var prev = i > 0 && Joinable(caps[i - 1], t) ? caps[i - 1] : null;
        var next = i + 1 < caps.Count && Joinable(t, caps[i + 1]) ? caps[i + 1] : null;
        var p = ctx.Preset.Telop.Caption;
        double maxWidth = AssWriter.CaptionLineChars(ctx.Preset, plan.Canvas.Width) * Math.Max(1, p.MaxLines);
        // cheap pre-check: every caption the candidate produces fits on screen and stays up long enough to see (speed is judged by LocalOk)
        bool Fits(params Telop[] xs) => xs.All(x => JapaneseText.DisplayWidth(x.Text) <= maxWidth && x.End - x.Start >= 0.2);
        var wordIndex = prep.WordIndex;
        var mapped = prep.Mapped;
        // the regrouped captions then take the free time around them (as the timing rule does); the pre-check runs on the result
        EditPlan? Settle(EditPlan p2, params string[] ids)
        {
            foreach (var id in ids) if (Stretch(p2, ctx, id, prep) is { } s) p2 = s.Plan;
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

        // FR-TEL-02 分割: an over-long caption becomes two at its best word boundary (a pause or a clause break first, then the more even halves);
        // the first keeps the id, the second gets a new one and starts where its first word is spoken
        IEnumerable<Proposal> Splits(Telop x)
        {
            if (Words(x) is not { Count: >= 2 } ws || x.End - x.Start < 0.4) yield break;
            var ids = plan.Telops.Select(o => o.Id).ToHashSet();
            var newId = x.Id + "b";
            for (int n = 2; ids.Contains(newId); n++) newId = $"{x.Id}b{n}";
            var options = Enumerable.Range(1, ws.Count - 1)
                .Where(k => !JapaneseText.IsPunct(ws[k].Word.Text))
                .Select(k => (k, cost: JapaneseText.TokenBreakCost(ws[k - 1].Word.Text, ws[k].Word.Text)))
                .Where(o => o.cost < JapaneseText.Detached)
                .Select(o =>
                {
                    var ta = JapaneseText.NormalizeTelop(string.Concat(ws.Take(o.k).Select(w => w.Word.Text)));
                    var tb = JapaneseText.NormalizeTelop(string.Concat(ws.Skip(o.k).Select(w => w.Word.Text)));
                    bool pause = ws[o.k].Start - ws[o.k - 1].End >= 0.25;
                    double score = (pause ? 0 : o.cost) + Math.Abs(JapaneseText.DisplayWidth(ta) - JapaneseText.DisplayWidth(tb)) * 0.5;
                    return (o.k, ta, tb, score);
                })
                .Where(o => o.ta.Length > 0 && o.tb.Length > 0 && JapaneseText.DisplayWidth(o.ta) <= maxWidth && JapaneseText.DisplayWidth(o.tb) <= maxWidth)
                .OrderBy(o => o.score).Take(4);
            foreach (var (k, ta, tb, _) in options)
            {
                double boundary = Math.Round(Math.Clamp(ws[k].Start, x.Start + 0.2, x.End - 0.2), 3);
                var a = x with { Text = ta, End = boundary, SourceWordIds = ws.Take(k).Select(w => w.Word.Id).ToList() };
                var b = x with { Id = newId, Text = tb, Start = boundary, SourceWordIds = ws.Skip(k).Select(w => w.Word.Id).ToList() };
                var telops = plan.Telops.SelectMany(o => o.Id == x.Id ? new[] { a, b } : [o]).ToList();
                if (Settle(plan with { Telops = telops }, a.Id, b.Id) is not { } p2) continue;
                yield return new(check, item, "rule", p2, x.Text, $"{ta}｜{tb}", false);
            }
        }

        var merges = new[] { prev != null ? Merge(prev, t) : null, next != null ? Merge(t, next) : null };
        var moves = (prev != null ? Moves(prev, t) : []).Concat(next != null ? Moves(t, next) : []);
        var last = prev != null && next != null ? Merge(prev, t, next) : null;
        // a flickering caption is best joined; an over-long one is best re-split; a badly-started one takes its head from / gives it to a neighbour
        IEnumerable<Proposal?> ordered = check switch
        {
            "S-01" => merges.Concat(moves).Append(last),
            "S-02" => moves.Concat(Splits(t)).Concat(merges).Append(last),
            _ => moves.Concat(merges).Append(last).Concat(Splits(t)),
        };
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
                        ? kept : TelopGrounding.Bind(kept with { SourceWordIds = [] }, plan.Timeline, ctx.Transcript, ctx.Dictionary, plan.Series);
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
