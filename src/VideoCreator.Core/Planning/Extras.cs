using System.Text.Json;
using System.Text.RegularExpressions;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Selection;

namespace VideoCreator.Planning;

public static partial class TelopExtras
{
    [GeneratedRegex(@"[0-9０-９][0-9０-９,\.]*\s*(?:万円|億円|円|万|億|％|%|人|個|回|倍|キロ|kg|分|秒|時間|日|年|歳|台|本|枚|点)")]
    private static partial Regex CounterRx();

    public static double OutputTime(IReadOnlyList<Clip> clips, double masterT)
    {
        double off = 0;
        foreach (var c in clips)
        {
            if (masterT >= c.MasterIn - 1e-6 && masterT <= c.MasterOut + 1e-6) return off + (masterT - c.MasterIn) / c.Speed;
            off += c.Length;
        }
        return -1;
    }

    /// <summary>⑥ Number/unit counters straight from the spoken words (so they are grounded by construction).</summary>
    public static List<Telop> Counters(IReadOnlyList<TelopBuilder.Mapped> mapped, IReadOnlyList<Telop> existing, double total, ref int n, int max = 3)
    {
        var res = new List<Telop>();
        var text = new System.Text.StringBuilder(); var starts = new List<int>();
        foreach (var m in mapped) { starts.Add(text.Length); text.Append(m.Word.Text); }
        foreach (Match m in CounterRx().Matches(text.ToString()))
        {
            if (res.Count >= max) break;
            int first = starts.FindLastIndex(s => s <= m.Index), last = starts.FindLastIndex(s => s < m.Index + m.Length);
            if (first < 0 || last < first) continue;
            double start = mapped[first].Start, end = Math.Min(total, Math.Max(mapped[last].End, start + 1.4));
            if (res.Any(r => r.End > start) || existing.Any(t => t.Type == TelopType.Counter && t.End > start && t.Start < end)) continue;
            res.Add(new Telop { Id = $"t{++n}", Type = TelopType.Counter, Start = Math.Round(start, 3), End = Math.Round(end, 3), Text = m.Value.Replace(" ", ""), Style = "Counter", Animation = "pop", SourceWordIds = mapped.Skip(first).Take(last - first + 1).Select(x => x.Word.Id).ToList() });
        }
        return res;
    }

    /// <summary>⑤ Onomatopoeia: a laugh burst gets 「ワハハ」, the last emphasis gets 「ドーン」 (decorative; not factual claims).</summary>
    public static List<Telop> Onomatopoeia(IReadOnlyList<Clip> clips, IReadOnlyList<Interval> bursts, IReadOnlyList<Telop> existing, double total, ref int n)
    {
        var res = new List<Telop>();
        foreach (var b in bursts.Where(b => b.Length >= 0.8).Take(2))
        {
            var t = OutputTime(clips, b.T0);
            if (t < 0 || t > total - 0.8) continue;
            res.Add(new Telop { Id = $"t{++n}", Type = TelopType.Sfx, Start = Math.Round(t, 3), End = Math.Round(Math.Min(total, t + 1.2), 3), Text = "ワハハ", Style = "Sfx", Animation = "pop" });
        }
        var lastEmph = existing.Where(t => t.Type == TelopType.Emphasis).OrderBy(t => t.Start).LastOrDefault();
        if (lastEmph != null && !res.Any(r => Math.Abs(r.Start - lastEmph.Start) < 1.2))
            res.Add(new Telop { Id = $"t{++n}", Type = TelopType.Sfx, Start = lastEmph.Start, End = Math.Round(Math.Min(total, lastEmph.Start + 1.0), 3), Text = "ドーン", Style = "Sfx", Animation = "pop" });
        return res;
    }
}

/// <summary>④ Tsukkomi telops (opt-in LLM). Output is filtered in code; everything is created unapproved (H-08) because humour is risky (RK-04).</summary>
public static class TsukkomiService
{
    public const string Schema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["caption_id","text"],"properties":{"caption_id":{"type":"string"},"text":{"type":"string"}}}}}}""";
    const string System = """
あなたは日本のバラエティ番組のテロップ担当です。字幕の流れを読み、ボケや意外な発言の直後に入る「ツッコミテロップ」を最大4個作ります。
規則:
- 各ツッコミは14字以内の短い口語（例:「いや多すぎ！」「そこ？」「言い方！」）
- caption_id は与えられた字幕のIDから選び、その字幕の直後に表示される
- 字幕に出てこない数字・固有名詞・商品名・人名を新しく書かない
- 人や集団を傷つける、侮辱する、差別的な内容は書かない。ツッコミの対象は「発言の内容」や「状況」にする
- 面白くなりそうな箇所が少なければ、個数を減らしてよい（0個でもよい）
""";
    static readonly string[] Blocklist = ["死ね", "殺", "バカ", "馬鹿", "アホ", "ブス", "デブ", "キモ", "障害", "ガイジ", "クズ", "ハゲ"];

    public static async Task<List<(string CaptionId, string Text)>> GenerateAsync(IHighlightSelector llm, IReadOnlyList<Telop> captions, string sourceText, CancellationToken ct)
    {
        var res = new List<(string, string)>();
        if (captions.Count < 3) return res;
        var user = "字幕:\n" + string.Join("\n", captions.Select(c => $"{c.Id}: {c.Text}"));
        var json = await llm.CompleteJsonAsync(System, user, "tsukkomi", Schema, ct);
        using var doc = JsonDocument.Parse(json);
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = it.GetProperty("caption_id").GetString() ?? ""; var text = (it.GetProperty("text").GetString() ?? "").Trim();
            if (Validate(text, id, captions, sourceText) == null) res.Add((id, text));
            if (res.Count >= 4) break;
        }
        return res;
    }

    /// <summary>Null when acceptable, else the reason it is rejected.</summary>
    public static string? Validate(string text, string captionId, IReadOnlyList<Telop> captions, string sourceText)
    {
        if (captions.All(c => c.Id != captionId)) return "caption id";
        return ValidateText(text, sourceText);
    }

    /// <summary>Length, blocklist and grounding rules shared by tsukkomi telops and scene-change cards.</summary>
    public static string? ValidateText(string text, string sourceText)
    {
        if (text.Length == 0 || text.Length > 14) return "length";
        if (Blocklist.Any(text.Contains)) return "blocklist";
        if (JapaneseText.UngroundedClaims(text, sourceText).Count > 0) return "ungrounded";
        if (Regex.Matches(text, @"[ァ-ヴー]{4,}").Any(m => !sourceText.Contains(m.Value))) return "invented name";
        return null;
    }
}

/// <summary>① SFX placement and BGM mutes derived from the final telops/timeline (FR-AUD-05..07).</summary>
public static class SfxPlanner
{
    public static (List<SfxEvent> Sfx, List<Interval> Mutes) Plan(IReadOnlyList<Clip> clips, IReadOnlyList<Telop> telops, string level, double total)
    {
        var ev = new List<SfxEvent>(); var mutes = new List<Interval>();
        if (level == "none") return (ev, mutes);
        bool heavy = level == "heavy";
        void Add(string kind, double t, double gain = 0) { if (t >= 0 && t <= total - 0.1 && !ev.Any(e => Math.Abs(e.T - t) < 0.25)) ev.Add(new SfxEvent(kind, Math.Round(t, 3), gain)); }
        double off = 0;
        for (int i = 0; i < clips.Count; i++) { if (heavy && i == 1 && clips[0].Role == "teaser") Add("whoosh", off, -4); off += clips[i].Length; }
        foreach (var t in telops.Where(t => t.Enabled))
        {
            switch (t.Type)
            {
                case TelopType.Hook when heavy: Add("whoosh", t.Start, -4); break;
                case TelopType.Emphasis: Add("pop", t.Start, -2); break;
                case TelopType.Counter: Add("ding", t.Start, -3); break;
                case TelopType.Tsukkomi when heavy: Add("hit", t.Start, -3); break;
                case TelopType.Sfx when t.Text == "ドーン": Add("boom", t.Start, -3); mutes.Add(new Interval(Math.Max(0, t.Start - 0.4), t.Start + 0.25)); break;
            }
        }
        return (ev.OrderBy(e => e.T).ToList(), mutes);
    }
}

/// <summary>FR-TEL-02 stage 3: when splitting and holding cannot bring a caption under the CPS limit, the LLM may only DELETE characters (no paraphrase). Verified in code as a subsequence of the original.</summary>
public static class CaptionShortener
{
    const string Schema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","text"],"properties":{"id":{"type":"string"},"text":{"type":"string"}}}}}}""";
    const string System = "あなたは字幕の編集者です。各字幕を、表示時間内に読めるよう文字を削って短くします。規則: 言い換えをしない。元の文字を順番どおりに残し、新しい文字・数字・固有名詞を足さない。意味が変わる削り方をしない。助詞や「えーと」などの言いよどみ、重複を優先して削る。これ以上削れない字幕は元のまま返す。";

    public static bool IsDeletionOf(string shortened, string original)
    {
        static string Clean(string s) => new(s.Where(c => !char.IsWhiteSpace(c) && !JapaneseText.IsPunct(c.ToString())).ToArray());
        var a = Clean(shortened); var b = Clean(original);
        if (a.Length == 0 || a.Length >= b.Length) return false;
        int j = 0;
        foreach (var c in b) if (j < a.Length && a[j] == c) j++;
        return j == a.Length;
    }

    public static async Task<(List<Telop> Telops, int Changed)> ApplyAsync(Selection.IHighlightSelector llm, List<Telop> telops, double maxCps, CancellationToken ct)
    {
        var over = telops.Where(t => t.Type == TelopType.Caption && t.Enabled && JapaneseText.DisplayWidth(t.Text) / Math.Max(0.05, t.End - t.Start) > maxCps + 0.01).ToList();
        if (over.Count == 0) return (telops, 0);
        var user = string.Join("\n", over.Select(t => $"{t.Id}\t表示{t.End - t.Start:F1}秒・上限{(int)Math.Floor(maxCps * (t.End - t.Start))}文字\t{t.Text}"));
        var json = await llm.CompleteJsonAsync(System, user, "shorten", Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, string>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray()) map[it.GetProperty("id").GetString() ?? ""] = (it.GetProperty("text").GetString() ?? "").Trim();
        int changed = 0;
        var res = telops.Select(t =>
        {
            if (!map.TryGetValue(t.Id, out var s) || !over.Contains(t) || !IsDeletionOf(s, t.Text)) return t;
            changed++; return t with { Text = JapaneseText.NormalizeTelop(s) };
        }).ToList();
        return (res, changed);
    }
}

/// <summary>FR-SOL-06: for loop presets, pick the end position whose last sentence leads best back into the first sentence (LLM-judged).</summary>
public static class LoopEnding
{
    const string Schema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["index","score"],"properties":{"index":{"type":"integer"},"score":{"type":"integer"}}}}}}""";
    const string System = "あなたはショート動画のループ編集者です。動画は最後まで再生されると冒頭に戻って繰り返されます。冒頭の文と、各案の「最後の文」を続けて読んだとき、自然につながって「もう一度見たくなる」かを0〜10の整数で採点してください。問いに答えが出て終わる・オチで終わる案を高く、文の途中や話の続きが気になるだけの案を低くします。";

    public static async Task<(List<Interval> Segments, string Note)?> ChooseAsync(Selection.IHighlightSelector llm, Transcript tr, IReadOnlyList<Interval> segs, SignalSet sig, Preset p, MediaInfo media, string assetId, CancellationToken ct)
    {
        var variants = new List<(int delta, List<Interval> segs, string last)>();
        foreach (var d in new[] { 0, -1, 1, -2, 2 })
        {
            var v = PlanEditor.AdjustSegments(segs, tr, "end", d);
            if (v.Count == 0) continue;
            if (!Solver.Plan(v, tr, sig, p, media, assetId).Feasible) continue;
            var lastSent = tr.Sentences.LastOrDefault(s => s.T0 < v[^1].T1 - 0.05);
            if (lastSent == null || variants.Any(x => x.last == lastSent.Text)) continue;
            variants.Add((d, v, lastSent.Text));
        }
        var first = tr.Sentences.FirstOrDefault(s => s.T1 > segs[0].T0 + 0.05);
        if (variants.Count < 2 || first == null) return null;
        var user = $"冒頭の文: {first.Text}\n" + string.Join("\n", variants.Select((v, i) => $"案{i}の最後の文: {v.last}"));
        var json = await llm.CompleteJsonAsync(System, user, "loop_end", Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var scores = new Dictionary<int, int>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray()) scores[it.GetProperty("index").GetInt32()] = Math.Clamp(it.GetProperty("score").GetInt32(), 0, 10);
        int best = Enumerable.Range(0, variants.Count).OrderByDescending(i => scores.GetValueOrDefault(i, -1)).ThenBy(i => i).First();
        return (variants[best].segs, $"ループ: 終わりを「{variants[best].last}」に決定（つながり {scores.GetValueOrDefault(best, 0)}/10、{variants.Count} 案から選択）");
    }
}
