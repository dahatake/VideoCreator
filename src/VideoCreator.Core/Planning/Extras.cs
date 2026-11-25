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

    /// <summary>
    /// Variety look: big punch telops (emphasis, tsukkomi, onomatopoeia) are shown one at a time, as in TV variety editing (measured: one big telop
    /// per ~1 s onset, never stacked). A later punch cuts the earlier one short if that one has been readable for ≥ 0.8 s; otherwise the later one is dropped.
    /// Emphasis wins ties (it is the speaker's own words), then tsukkomi, then onomatopoeia.
    /// </summary>
    public static List<Telop> OnePunchAtATime(IReadOnlyList<Telop> telops, List<string> notes)
    {
        static int Rank(TelopType t) => t switch { TelopType.Emphasis => 0, TelopType.Tsukkomi => 1, _ => 2 };
        static bool IsPunch(Telop t) => t.Enabled && t.Type is TelopType.Emphasis or TelopType.Tsukkomi or TelopType.Sfx;
        var res = telops.Where(t => !IsPunch(t)).ToList();
        Telop? last = null;
        foreach (var t in telops.Where(IsPunch).OrderBy(t => t.Start).ThenBy(t => Rank(t.Type)))
        {
            if (last != null && last.End > t.Start + 0.01)
            {
                if (t.Start - last.Start >= 0.8) { res[res.IndexOf(last)] = last = last with { End = t.Start }; }
                else
                {
                    if (t.Type == TelopType.Tsukkomi) notes.Add($"ツッコミ「{t.Text}」は直前の大きなテロップと重なるため表示しません");
                    continue;
                }
            }
            res.Add(t); last = t;
        }
        return res.OrderBy(t => t.Start).ThenBy(t => t.Type).ToList();
    }

    /// <summary>⑤ Onomatopoeia: a laugh burst gets 「ワハハ」, the last emphasis gets 「ドーン」 (decorative; not factual claims).</summary>
    public static List<Telop> Onomatopoeia(IReadOnlyList<Clip> clips, IReadOnlyList<Interval> bursts, IReadOnlyList<Telop> existing, double total, ref int n, string language = "ja-JP")
    {
        var res = new List<Telop>();
        foreach (var b in bursts.Where(b => b.Length >= 0.8).Take(2))
        {
            var t = OutputTime(clips, b.T0);
            if (t < 0 || t > total - 0.8) continue;
            res.Add(new Telop { Id = $"t{++n}", Type = TelopType.Sfx, Start = Math.Round(t, 3), End = Math.Round(Math.Min(total, t + 1.2), 3), Text = VideoLanguages.Text(language, "ワハハ", "Ha ha!", "哈哈", "하하"), Style = "Sfx", Animation = "pop" });
        }
        var lastEmph = existing.Where(t => t.Type == TelopType.Emphasis).OrderBy(t => t.Start).LastOrDefault();
        if (lastEmph != null && !res.Any(r => Math.Abs(r.Start - lastEmph.Start) < 1.2))
            res.Add(new Telop { Id = $"t{++n}", Type = TelopType.Sfx, Start = lastEmph.Start, End = Math.Round(Math.Min(total, lastEmph.Start + 1.0), 3), Text = VideoLanguages.Text(language, "ドーン", "Boom!", "咚", "쾅"), Style = "Sfx", Animation = "pop" });
        return res;
    }
}

/// <summary>
/// ④ Tsukkomi telops (opt-in LLM). Output is filtered in code; everything is created unapproved (H-08) because humour is risky (RK-04).
/// The register, timing and density follow the editor's narration telops measured in two variety videos (docs/IMPLEMENTATION.md §25):
/// a deadpan one-liner of the staff (no 「！」, 78 % end in 「。」), median 6–7 characters, 0.6 s after the line ends, 2.25–2.5 s on screen.
/// </summary>
public static class TsukkomiService
{
    public const string Schema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["caption_id","text"],"properties":{"caption_id":{"type":"string"},"text":{"type":"string"}}}}}}""";

    /// <summary>Onset after the end of the line it reacts to (measured median 0.59–0.66 s).</summary>
    public const double OnsetLagSec = 0.6;
    /// <summary>A cut this soon after the line is where the narration starts (36–52 % start within 0.3 s of a cut: on the reaction shot).</summary>
    public const double CutSnapMinSec = 0.2, CutSnapMaxSec = 1.5;
    /// <summary>Time on screen (measured median 2.25–2.5 s).</summary>
    public const double DisplaySec = 2.4;
    /// <summary>Latest onset after the line when the comment waits for the speaker's emphasis (measured 90th percentile 2.65–2.84 s).</summary>
    public const double MaxDelaySec = 2.8;
    /// <summary>Minimum spacing between two narrations (measured 10th percentile 5.5 s) and the densest rate (4 per 30 s).</summary>
    public const double MinGapSec = 5.0, SecondsPerItem = 7.5;
    public const int MaxItemsCap = 6;

    /// <summary>How many tsukkomi a body of this length may carry: the densest measured stretches (4 per 30 s), at least 1, at most <see cref="MaxItemsCap"/>.</summary>
    public static int MaxItems(double totalSec) => Math.Clamp((int)Math.Floor(totalSec / SecondsPerItem), 1, MaxItemsCap);

    /// <summary>
    /// Output-time onset of a narration reacting to a line that ends at <paramref name="lineEnd"/>: on the first cut 0.2–1.5 s later
    /// (the reaction shot), else <see cref="OnsetLagSec"/> later; never so late that it cannot be shown for a second before the end.
    /// </summary>
    public static double Onset(double lineEnd, IReadOnlyList<double> cuts, double total)
    {
        var cut = cuts.Where(c => c >= lineEnd + CutSnapMinSec - 1e-6 && c <= lineEnd + CutSnapMaxSec + 1e-6).Order().Cast<double?>().FirstOrDefault();
        double at = cut ?? lineEnd + OnsetLagSec;
        return Math.Round(Math.Max(0, at <= total - 1.0 ? at : Math.Min(lineEnd, total - 1.0)), 3);
    }

    const string System = """
あなたは日本のバラエティ動画の編集者です。字幕の流れを読み、見どころの直後に画面の右下に出す「編集者のひとことテロップ」を作ります（最大{MAX}個）。
これは出演者のセリフではなく、編集スタッフが冷静に入れる一言です。出演者のように叫ばず、少し引いた目線で状況や人物を言い当てます。
人気の動画で実際に使われている型と割合（目安）:
- 約6割: 状況・行動への短い感想やツッコミ。文末は「。」（例の型:「〜すぎる。」「〜てる。」「〜やん。」「〜な。」）
- 約3割: 人物や状況に名前を付ける体言止め（例の型:「〜する〇〇。」「全員〇〇。」「〜問題。」）
- 約1割: とぼけた疑問（「〜？」）
規則:
- 1個は5〜10字が目安（14字以内）。「！」は使わない。文末は「。」、疑問のときだけ「？」
- caption_id は与えられた字幕のIDから選ぶ。その字幕の発言が終わった直後に表示される。笑いや驚きが起きた発言、言い間違い、意外な行動、話のズレの直後を選ぶ
- 字幕の言葉をそのまま繰り返さない（発言への反応を書く）。同じ文を2回使わない。近い字幕に続けて付けない（5秒以上あける）
- 字幕に出てこない数字・固有名詞・商品名・人名を新しく書かない（人物を指すときは字幕に出てくる名前か「全員」「本人」などを使う）
- 人や集団を傷つける、侮辱する、差別的な内容は書かない。ツッコミの対象は「発言の内容」や「状況」にする
- 面白くなりそうな箇所が少なければ、個数を減らしてよい（0個でもよい）
""";
    static readonly string[] Blocklist = ["死ね", "殺", "バカ", "馬鹿", "アホ", "ブス", "デブ", "キモ", "障害", "ガイジ", "クズ", "ハゲ"];

    public static async Task<List<(string CaptionId, string Text)>> GenerateAsync(IHighlightSelector llm, IReadOnlyList<Telop> captions, string sourceText, CancellationToken ct, string language = "ja-JP", int maxItems = 4)
    {
        var res = new List<(string, string)>();
        if (captions.Count < 3) return res;
        var user = "字幕:\n" + string.Join("\n", captions.Select(c => $"{c.Id}: {c.Text}"));
        var json = await llm.CompleteJsonAsync(System.Replace("{MAX}", maxItems.ToString(global::System.Globalization.CultureInfo.InvariantCulture)) + VideoLanguages.Prompt(language), user, "tsukkomi", Schema, ct);
        using var doc = JsonDocument.Parse(json);
        var seen = new HashSet<string>();
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = it.GetProperty("caption_id").GetString() ?? ""; var text = Deadpan((it.GetProperty("text").GetString() ?? "").Trim(), language);
            if (Validate(text, id, captions, sourceText) == null && seen.Add(Bare(text))) res.Add((id, text));
            if (res.Count >= maxItems) break;
        }
        return res;
    }

    /// <summary>The editor's narration never shouts (0 of 92 measured end in 「！」): a Japanese line ending in 「！」 ends in 「。」 instead (「!?」 stays a question).</summary>
    public static string Deadpan(string text, string language = "ja-JP")
    {
        if (language != "ja-JP") return text;
        var m = Regex.Match(text, @"[!！]+$");
        if (!m.Success) return text;
        var head = text[..m.Index].TrimEnd();
        if (head.EndsWith('?') || head.EndsWith('？')) return head;
        return head.Length == 0 ? text : head.TrimEnd('。') + "。";
    }

    /// <summary>Null when acceptable, else the reason it is rejected.</summary>
    public static string? Validate(string text, string captionId, IReadOnlyList<Telop> captions, string sourceText)
    {
        if (captions.All(c => c.Id != captionId)) return "caption id";
        return ValidateText(text, sourceText);
    }

    /// <summary>Length, blocklist, echo and grounding rules shared by tsukkomi telops and scene-change cards.</summary>
    public static string? ValidateText(string text, string sourceText)
    {
        if (text.Length == 0 || text.Length > 14) return "length";
        if (Blocklist.Any(text.Contains)) return "blocklist";
        // the line is the viewer's reaction, not a quote: a copy of what was said only duplicates the caption
        var bare = Bare(text);
        if (bare.Length >= 4 && Bare(sourceText).Contains(bare, StringComparison.Ordinal)) return "echo";
        if (JapaneseText.UngroundedClaims(text, sourceText, ReactionWords).Count > 0) return "ungrounded";
        if (Regex.Matches(text, @"[ァ-ヴー]{4,}").Any(m => !sourceText.Contains(m.Value) && !ReactionWords.Contains(m.Value))) return "invented name";
        return null;
    }

    /// <summary>Colloquial katakana in viewer reactions (「イケてる！」「マジで!?」) — not names, so they need no grounding in the transcript.</summary>
    public static readonly string[] ReactionWords =
        ["イケ", "マジ", "ヤバ", "ウケ", "スゴ", "エモ", "ガチ", "ナイス", "ドンマイ", "ワクワク", "ドキドキ", "ハラハラ", "ヒヤヒヤ",
         "Wow", "Really", "Nice", "Exactly", "Wait", "Seriously", "Amazing", "Incredible", "Relatable", "Well", "done"];

    /// <summary>Letters and digits only (NFKC), so 「わかる〜！」 and 「わかる」 compare equal.</summary>
    public static string Bare(string s) => new(s.Normalize(global::System.Text.NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray());
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
