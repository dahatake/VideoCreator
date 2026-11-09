using System.Text.Json;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Qa;

public sealed record QaRepairChange(string CheckId, string Target, string Kind, string Method, string? Before, string? After, bool NeedsApproval, double? Start, double? End);
public sealed record QaRepairSkip(string CheckId, string Target, string Reason, double? Start = null);
public sealed record QaRepairResult(EditPlan Plan, List<QaRepairChange> Changes, List<QaRepairSkip> Skipped, List<QaResult> Before, List<QaResult> After, string? Llm);

/// <summary>「Copilotで自動修復」: deterministic rule fixes first, then one batched LLM request for the rest.
/// Every proposal is applied to a copy of the plan and kept only when QaEngine.PlanChecks shows its finding gone with no other check getting worse
/// (the one allowed exception: AI-written telop/card text is left unapproved, so it may add itself to H-08).</summary>
public static class QaRepair
{
    sealed record Proposal(string CheckId, QaItem Item, string Method, EditPlan Plan, string? Before, string? After, bool NeedsApproval);

    const string Schema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","text"],"properties":{"id":{"type":"string"},"text":{"type":"string"}}}}}}""";
    const string System = """
あなたはショート動画のテロップ校正者です。自動チェックで指摘されたテロップを、指摘ごとの制約を守って直します。
規則:
- 発話（文字起こし）に出てこない数字・固有名詞・事実を足さない。数字と単位と対象の組み合わせは発話どおりにする
- 発話の否定（〜ない、〜ません など）を落とさない
- 「削除のみ」と書かれた字幕は言い換えをせず、元の文字を順番どおりに残して不要な文字だけを削る。削るのは「あの」「なんか」「ちょっと」のような言い淀み・つなぎ言葉や、無くても意味が通る文節まるごと。単語や文節の途中では切らない（「こんにちは」を「こんにち」、「撮りました」を「撮りま」にしない）。意味が欠けるなら削らずに元の文字を返す
- 字数の上限を守る
- 直せない場合は元の文字をそのまま返す
- 入力の id ごとに 1 件ずつ返す
""";

    static readonly string[] AiChecks = ["H-07", "H-11", "S-09", "S-01", "S-02"];
    /// <summary>Telops/cards sent to the LLM per run, and per request.</summary>
    public const int MaxAiTargets = 60, BatchSize = 20;

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
            var after = QaEngine.PlanChecks(qctx with { Plan = pr.Plan });
            var why = Regression(current, after, pr);
            if (why != null) return why;
            plan = pr.Plan with { Provenance = pr.Plan.Provenance with { Edits = [.. pr.Plan.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "qa_repair", pr.Item.Target, pr.Before, pr.After)] } };
            current = after;
            changes.Add(new(pr.CheckId, pr.Item.Target, pr.Item.Kind, pr.Method, pr.Before, pr.After, pr.NeedsApproval, pr.Item.Start, pr.Item.End));
            // one edit can clear the same target in several checks (e.g. H-07 and H-11)
            foreach (var (c, i) in targets) if (i.Target == pr.Item.Target && !(current.FirstOrDefault(q => q.CheckId == c)?.Items ?? []).Any(x => x.Target == i.Target)) done.Add((c, i.Target));
            return null;
        }

        // 1) rules
        foreach (var (check, item) in targets)
        {
            if (done.Contains((check, item.Target))) continue;
            var pr = check switch
            {
                "H-10" => MaskProposal(plan, ctx, item),
                "H-11" => BindProposal(plan, ctx, item),
                "S-08" => KinsokuProposal(plan, ctx, item),
                "S-01" => TimingProposal(plan, ctx, item),
                _ => null,
            };
            if (pr == null) continue;
            // a rule that does not work out leaves AI-capable findings to the LLM step below
            if (Try(pr) is { } why) { if (!AiChecks.Contains(check)) skipped.Add(new(check, item.Target, why, item.Start)); }
            else done.Add((check, item.Target));
        }

        // 2) LLM, one request for every remaining text finding
        var aiTargets = targets.Where(x => !done.Contains((x.CheckId, x.Item.Target)) && AiChecks.Contains(x.CheckId) && x.Item.Kind is "telop" or "insert").ToList();
        // a short caption that is still too fast after the timing rule has no word to spare: cutting it loses the meaning (「おはようございます」→「お」)
        foreach (var x in aiTargets.Where(x => x.CheckId == "S-01" && TooShortToCut(plan, x.Item.Target)).ToList())
        {
            aiTargets.Remove(x);
            skipped.Add(new(x.CheckId, x.Item.Target, $"{MinCutChars} 文字未満の字幕は削ると意味が欠けるため自動では直しません。前後の字幕と結合するか、表示時間を調整してください", x.Item.Start));
        }
        string? llmName = null;
        if (aiTargets.Count > 0)
        {
            if (llm == null)
                foreach (var (c, i) in aiTargets) skipped.Add(new(c, i.Target, "LLM が設定されていないため自動修復できません（設定で Azure OpenAI / GitHub Copilot を選んでください）", i.Start));
            else
            {
                llmName = $"{llm.Name} ({llm.Model})";
                // H-* (blocking) first, then by time; bounded per run so one click stays a few LLM calls (press again for the rest)
                var all = aiTargets.GroupBy(x => x.Item.Target)
                    .OrderBy(g => g.Any(x => x.CheckId.StartsWith("H-", StringComparison.Ordinal)) ? 0 : 1).ThenBy(g => g.First().Item.Start ?? double.MaxValue).ToList();
                var groups = all.Take(MaxAiTargets).ToList();
                foreach (var g in all.Skip(MaxAiTargets)) foreach (var x in g) skipped.Add(new(x.CheckId, g.Key, $"一度に AI で直す上限（{MaxAiTargets} 件）を超えました。もう一度「Copilotで自動修復」を押すと続きを直します", x.Item.Start));
                Dictionary<string, string> answers;
                try
                {
                    var batches = await Task.WhenAll(groups.Chunk(BatchSize).Select(b =>
                        AskAsync(llm, plan, ctx, b.Select(g => (g.Key, g.Select(x => x.CheckId).ToList(), g.Select(x => x.Item.Detail).ToList())).ToList(), ct)));
                    answers = batches.SelectMany(d => d).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    answers = [];
                    foreach (var (c, i) in aiTargets) skipped.Add(new(c, i.Target, "LLM の呼び出しに失敗しました: " + ex.Message, i.Start));
                    groups = [];
                }
                foreach (var g in groups)
                {
                    var first = g.First();
                    if (g.All(x => done.Contains((x.CheckId, x.Item.Target)))) continue;
                    if (!answers.TryGetValue(g.Key, out var text)) { foreach (var x in g) skipped.Add(new(x.CheckId, g.Key, "LLM が修正案を返しませんでした", x.Item.Start)); continue; }
                    var (pr, why) = TextProposal(plan, ctx, first.CheckId, first.Item, g.Select(x => x.CheckId).ToList(), text);
                    why ??= pr == null ? "修正案を作れませんでした" : Try(pr);
                    if (why != null) foreach (var x in g.Where(x => !done.Contains((x.CheckId, x.Item.Target)))) skipped.Add(new(x.CheckId, g.Key, why, x.Item.Start));
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
        if (prev == null) return null;
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

    /// <summary>S-01 by timing, before touching words: show the caption longer, into the free time around it
    /// (≤ <see cref="TelopBuilder.CaptionHoldMax"/> after its last word, ≤ <see cref="LeadIn"/> before its first, 2-frame gap to the neighbouring captions).</summary>
    static Proposal? TimingProposal(EditPlan plan, QaContext ctx, QaItem item)
    {
        var caps = plan.Telops.Where(x => x.Enabled && x.Type == TelopType.Caption).ToList();
        if (caps.FirstOrDefault(x => x.Id == item.Target) is not { } t) return null;
        double need = JapaneseText.DisplayWidth(t.Text) / ctx.Preset.Telop.Caption.MaxCps + 0.02;
        if (t.End - t.Start >= need) return null;
        var ids = t.SourceWordIds.ToHashSet();
        var own = TelopBuilder.MapWords(plan.Timeline, ctx.Transcript).Where(m => ids.Contains(m.Word.Id)).ToList();
        double first = own.Count > 0 ? own.Min(m => m.Start) : t.Start, last = own.Count > 0 ? own.Max(m => m.End) : t.End;
        double nextStart = caps.Where(o => o.Id != t.Id && o.Start >= t.End - 1e-3).Select(o => o.Start).DefaultIfEmpty(double.MaxValue).Min();
        double prevEnd = caps.Where(o => o.Id != t.Id && o.End <= t.Start + 1e-3).Select(o => o.End).DefaultIfEmpty(0).Max();
        double maxEnd = Math.Min(Math.Min(nextStart - Gap, last + TelopBuilder.CaptionHoldMax), plan.Duration);
        double minStart = Math.Max(Math.Max(prevEnd + Gap, first - LeadIn), 0);
        double end = Math.Max(t.End, Math.Min(maxEnd, t.Start + need));
        double start = Math.Min(t.Start, Math.Max(minStart, end - need));
        end = Math.Floor(end * 100) / 100; start = Math.Ceiling(start * 100) / 100;
        if (end - start < need - 0.02 - 1e-6 || (end <= t.End && start >= t.Start)) return null;
        return new("S-01", item, "rule", SetTelop(plan, t.Id, x => x with { Start = start, End = end }), $"表示 {t.End - t.Start:F2} 秒", $"表示 {end - start:F2} 秒", false);
    }

    const double Gap = 2 / 30.0, LeadIn = 0.25;

    /// <summary>A caption's words as the recogniser split them, when the caption is still exactly those words (null after a hand edit).</summary>
    static List<string>? CaptionWords(EditPlan plan, QaContext ctx, Telop t)
    {
        var byId = ctx.Transcript.Words.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
        var words = t.SourceWordIds.Select(id => byId.GetValueOrDefault(id)?.Text).Where(x => x != null).Select(x => x!).ToList();
        if (words.Count == 0 || words.Count != t.SourceWordIds.Count) return null;
        return Clean(string.Concat(words)) == Clean(t.Text) ? words.Where(w => Clean(w).Length > 0).ToList() : null;
    }

    static string Clean(string s) => new(JapaneseText.NormalizeTelop(s).Where(c => !char.IsWhiteSpace(c) && !JapaneseText.IsPunct(c.ToString())).ToArray());

    /// <summary>The shortened caption keeps whole recognised words only (no 「こんにちは」→「こんにち」).</summary>
    static bool KeepsWholeWords(string shortened, List<string> words)
    {
        var a = Clean(shortened);
        var reach = new HashSet<int> { 0 };
        foreach (var w in words.Select(Clean))
            foreach (var p in reach.ToList())
                if (p + w.Length <= a.Length && string.CompareOrdinal(a, p, w, 0, w.Length) == 0) reach.Add(p + w.Length);
        return reach.Contains(a.Length);
    }

    /// <summary>Captions shorter than this are not shortened for reading speed (S-01).</summary>
    public const int MinCutChars = 8;
    /// <summary>Highest <see cref="JapaneseText.BreakPenalty"/> allowed where a cut starts or ends: space/punctuation (0), before 「 (1), hiragana → kanji/katakana (2).</summary>
    const double MaxCutPenalty = 2;

    static bool TooShortToCut(EditPlan plan, string target) =>
        plan.Telops.FirstOrDefault(t => t.Id == target) is { Type: TelopType.Caption } t && Clean(t.Text).Length < MinCutChars;

    /// <summary>
    /// The shortened caption drops whole phrases only: some way of deleting characters from <paramref name="original"/> gives
    /// <paramref name="shortened"/> with every cut on an approximate 文節 boundary. Recognisers that return one character per
    /// word (Azure ja-JP) make <see cref="KeepsWholeWords"/> vacuous, so this is the check that stops 「撮りました」→「撮りま」.
    /// </summary>
    public static bool CutsAtPhraseBoundaries(string original, string shortened)
    {
        var o = JapaneseText.NormalizeTelop(original);
        var s = Clean(shortened);
        // reach[j, k]: s[..j] matched so far, last counted character kept (k = 1) / deleted (k = 0); before any character both are open
        var reach = new bool[s.Length + 1, 2];
        reach[0, 0] = reach[0, 1] = true;
        char prev = '\0';
        foreach (var c in o)
        {
            if (char.IsWhiteSpace(c) || JapaneseText.IsPunct(c.ToString())) { prev = c; continue; }   // free to keep or drop, and a clean place to cut
            bool cutOk = prev == '\0' || JapaneseText.BreakPenalty(prev, c) <= MaxCutPenalty;
            var next = new bool[s.Length + 1, 2];
            for (int j = 0; j <= s.Length; j++)
            {
                if (reach[j, 0] || reach[j, 1] && cutOk) next[j, 0] = true;                                         // delete c
                if (j < s.Length && s[j] == c && (reach[j, 1] || reach[j, 0] && cutOk)) next[j + 1, 1] = true;   // keep c
            }
            reach = next;
            prev = c;
        }
        return reach[s.Length, 0] || reach[s.Length, 1];
    }

    static string Spoken(EditPlan plan, QaContext ctx, double from, double to)
    {
        var mapped = TelopBuilder.MapWords(plan.Timeline, ctx.Transcript);
        return string.Concat(mapped.Where(m => m.End > from && m.Start < to).Select(m => m.Word.Text));
    }

    static (string Kind, string Text, string Constraint, string Context)? Describe(EditPlan plan, QaContext ctx, string target, List<string> checks)
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
                    int limit = PlanEditor.MaxTelopLength;
                    if (checks.Contains("S-01")) limit = Math.Min(limit, (int)Math.Floor(p.Telop.Caption.MaxCps * Math.Max(0.05, t.End - t.Start)));
                    if (checks.Contains("S-02")) limit = Math.Min(limit, AssWriter.CaptionLineChars(p, plan.Canvas.Width) * Math.Max(1, p.Telop.Caption.MaxLines));
                    var cw = CaptionWords(plan, ctx, t);
                    return ("caption", t.Text, $"字幕。削除のみ（言い換え禁止）。{limit} 文字以内" + (cw != null ? $"。単語の途中で切らず、単語単位で削る。単語: {string.Join("｜", cw)}" : ""), context);
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

    static async Task<Dictionary<string, string>> AskAsync(IHighlightSelector llm, EditPlan plan, QaContext ctx, List<(string Target, List<string> Checks, List<string> Details)> items, CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var (target, checks, details) in items)
            if (Describe(plan, ctx, target, checks) is { } d)
                lines.Add($"id: {target}\n種類: {d.Kind}\n指摘: {string.Join(" / ", checks.Zip(details, (c, x) => $"{c} {x}"))}\n制約: {d.Constraint}\n現在: {d.Text}\n{d.Context}");
        if (lines.Count == 0) return [];
        var json = await llm.CompleteJsonAsync(System, string.Join("\n\n", lines), "qa_repair", Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var res = new Dictionary<string, string>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
            res[it.GetProperty("id").GetString() ?? ""] = (it.GetProperty("text").GetString() ?? "").Replace("\r", "").Replace("\n", " ").Trim();
        return res;
    }

    /// <summary>Type-specific validation of an LLM answer before the QA re-check.</summary>
    static (Proposal? Proposal, string? Why) TextProposal(EditPlan plan, QaContext ctx, string check, QaItem item, List<string> checks, string text)
    {
        if (plan.Telops.FirstOrDefault(t => t.Id == item.Target) is { } t)
        {
            var s = JapaneseText.NormalizeTelop(text);
            if (s.Length == 0 || s == t.Text) return (null, "LLM が直せないと判断しました");
            if (s.Length > PlanEditor.MaxTelopLength) return (null, $"修正案が {PlanEditor.MaxTelopLength} 文字を超えました");
            switch (t.Type)
            {
                case TelopType.Caption:
                    // captions are the speaker's words: deletion only, so they stay as approved as they were
                    if (!CaptionShortener.IsDeletionOf(s, t.Text)) return (null, "言い換えや文字の追加が含まれていたため採用しませんでした（字幕は削除のみ）");
                    if (CaptionWords(plan, ctx, t) is { } cw && !KeepsWholeWords(s, cw) || !CutsAtPhraseBoundaries(t.Text, s))
                        return (null, "単語の途中で切れていたため採用しませんでした（字幕は文節単位で削ります）");
                    return (new(check, item, "ai", SetTelop(plan, t.Id, x => x with { Text = s }), t.Text, s, false), null);
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
