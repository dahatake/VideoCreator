using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Selection;
using VideoCreator.Signals;

namespace VideoCreator.Planning;

/// <summary>A topic (chapter) start in the SOURCE transcript, by sentence index. SplitScore -1 = unknown (window edge of a chunked AI call).</summary>
public sealed record ChapterMark(int Start, string Title, int SplitScore, int Highlight, List<string> Emphasis);

public sealed record ChapterOutline(string SeriesTitle, List<ChapterMark> Chapters, string Source, int InputChars = 0, int OutputChars = 0);

/// <summary>One part of a series, expressed in sentence indices of the source transcript (inclusive).</summary>
public sealed record SeriesPart
{
    public int Index { get; init; }
    public int Count { get; init; }
    public required string Label { get; init; }
    public required List<string> Labels { get; init; }
    public string SeriesTitle { get; init; } = "";
    public int First { get; init; }
    public int Last { get; init; }
    /// <summary>Master-time range of the main body. Bodies of consecutive parts never overlap and together contain every word.</summary>
    public required Interval Body { get; init; }
    /// <summary>Sentence range shown first as this part's cold open (inside this part).</summary>
    public (int A, int B)? Teaser { get; init; }
    /// <summary>Sentence range from the end of the previous part (「前編のおさらい」).</summary>
    public (int A, int B)? Recap { get; init; }
    /// <summary>Sentence range of the next part's cold open (「後編につづく」).</summary>
    public (int A, int B)? Next { get; init; }
    public List<ChapterMark> Chapters { get; init; } = [];
    public List<string> Emphasis { get; init; } = [];
    public double TargetSec { get; init; }
    public int? RequestedParts { get; init; }
    public double MinSec { get; init; }
    public double MaxSec { get; init; }
    public double EstSec { get; init; }
    public string Boundary { get; init; } = "";
    public string? PrevLabel => Index > 0 ? Labels[Index - 1] : null;
    public string? NextLabel => Index + 1 < Count ? Labels[Index + 1] : null;
}

/// <summary>
/// Series mode (前編/後編): splits a long source into consecutive parts of about the requested length at topic
/// boundaries (not at clock positions), and gives every part the structure of popular multi-part videos:
/// a cold open from inside the part, a recap of the previous part's ending, chapter headlines, and a
/// 「〜につづく」 teaser of the next part.
/// </summary>
public static partial class SeriesPlanner
{
    /// <summary>Allowed deviation of a part from the balanced target, tried in order (the DP relaxes only when nothing fits).</summary>
    public static readonly double[] Tolerances = [0.25, 0.4, 10];
    /// <summary>QA range around the balanced target (a little wider than the planning tolerance: estimates vs. the real cut).</summary>
    public const double QaTolerance = 0.3;
    public const double MinChapterSec = 60, TeaserMin = 1.2, TeaserMax = 6.0, RecapMax = 8.0;
    const double DurationWeight = 40, BoundaryWeight = 2.5;

    public static List<string> Labels(int n) => n switch
    {
        2 => ["前編", "後編"],
        3 => ["前編", "中編", "後編"],
        _ => Enumerable.Range(1, n).Select(i => $"第{i}話").ToList(),
    };

    // ---------------- topic boundaries without AI: TextTiling (Hearst 1997) on content-word bigrams ----------------

    static readonly string[] Markers = ["続いて", "続きまして", "次は", "次に", "次の", "さて", "ところで", "最後に", "それでは", "では、", "まず", "改めて", "話は変わ", "本日は", "今日は"];
    public static bool IsTopicMarker(string sentence) { var t = sentence.TrimStart('　', ' '); return Markers.Any(m => t.StartsWith(m, StringComparison.Ordinal)); }

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}々]+|[ァ-ヴー]{2,}|[A-Za-z][A-Za-z0-9]+")]
    private static partial Regex ContentRun();

    static Dictionary<string, int> Terms(string text)
    {
        var d = new Dictionary<string, int>();
        void Add(string t) => d[t] = d.GetValueOrDefault(t) + 1;
        foreach (Match m in ContentRun().Matches(text.Normalize(NormalizationForm.FormKC)))
        {
            var v = m.Value.ToLowerInvariant();
            if (v[0] is >= 'ァ' and <= 'ヴ' or 'ー' || char.IsAsciiLetter(v[0])) { Add(v); continue; }
            if (v.Length == 1) Add(v);
            else for (int i = 0; i + 1 < v.Length; i++) Add(v.Substring(i, 2));
        }
        return d;
    }

    static double Cosine(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        double dot = 0;
        foreach (var (k, v) in a) if (b.TryGetValue(k, out var w)) dot += v * w;
        double na = Math.Sqrt(a.Values.Sum(v => (double)v * v)), nb = Math.Sqrt(b.Values.Sum(v => (double)v * v));
        return dot / (na * nb);
    }

    /// <summary>Normalised (0..1) TextTiling depth score for the boundary before each sentence (index 0 and n are 0).</summary>
    public static double[] Depth(Transcript tr, double windowSec = 45)
    {
        var s = tr.Sentences; int n = s.Count;
        var depth = new double[n + 1];
        if (n < 3) return depth;
        var terms = s.Select(x => Terms(x.Text)).ToList();
        var sim = new double[n + 1];
        for (int i = 1; i < n; i++)
        {
            var left = new Dictionary<string, int>(); var right = new Dictionary<string, int>();
            for (int j = i - 1; j >= 0 && (j == i - 1 || s[i].T0 - s[j].T0 <= windowSec); j--) foreach (var (k, v) in terms[j]) left[k] = left.GetValueOrDefault(k) + v;
            for (int j = i; j < n && (j == i || s[j].T1 - s[i].T0 <= windowSec); j++) foreach (var (k, v) in terms[j]) right[k] = right.GetValueOrDefault(k) + v;
            sim[i] = Cosine(left, right);
        }
        for (int i = 1; i < n; i++)
        {
            double lp = sim[i], rp = sim[i];
            for (int j = i - 1; j >= 1 && sim[j] >= lp; j--) lp = sim[j];
            for (int j = i + 1; j < n && sim[j] >= rp; j++) rp = sim[j];
            depth[i] = lp - sim[i] + rp - sim[i];
        }
        double max = depth.Max();
        if (max > 0) for (int i = 0; i <= n; i++) depth[i] /= max;
        return depth;
    }

    /// <summary>How good a split before sentence i is: lexical shift, pause, topic-change words, speaker change, and the AI's chapter judgement.</summary>
    public static double[] BoundaryScores(Transcript tr, double[] depth, ChapterOutline outline)
    {
        var s = tr.Sentences; int n = s.Count;
        var score = new double[n + 1];
        var marks = outline.Chapters.ToDictionary(c => c.Start);
        for (int i = 1; i < n; i++)
        {
            double gap = Math.Max(0, s[i].T0 - s[i - 1].T1);
            score[i] = depth[i] + 0.5 * Math.Min(1, gap / 1.5) + (IsTopicMarker(s[i].Text) ? 0.6 : 0) + (s[i].Speaker != s[i - 1].Speaker ? 0.1 : 0);
            if (marks.TryGetValue(i, out var m)) score[i] += m.SplitScore < 0 ? 0.2 : 0.4 + 0.12 * m.SplitScore;
        }
        return score;
    }

    /// <summary>Chapters without AI: the strongest TextTiling/marker boundaries at least <see cref="MinChapterSec"/> apart. Titles are taken from the transcript (grounded by construction).</summary>
    public static ChapterOutline FallbackOutline(Transcript tr, SignalSet sig)
    {
        var s = tr.Sentences; int n = s.Count;
        if (n == 0) return new ChapterOutline("", [], "text");
        var depth = Depth(tr);
        var cand = Enumerable.Range(1, Math.Max(0, n - 1)).Select(i => (i, v: depth[i] + (IsTopicMarker(s[i].Text) ? 0.6 : 0))).Where(x => x.v >= 0.45).OrderByDescending(x => x.v).ToList();
        var starts = new List<(int i, double v)> { (0, 1) };
        foreach (var c in cand)
            if (starts.All(x => Math.Abs(s[x.i].T0 - s[c.i].T0) >= MinChapterSec) && s[c.i].T0 >= MinChapterSec && tr.Duration - s[c.i].T0 >= MinChapterSec / 2) starts.Add(c);
        starts = starts.OrderBy(x => x.i).ToList();
        var chapters = new List<ChapterMark>();
        for (int k = 0; k < starts.Count; k++)
        {
            int a = starts[k].i, b = k + 1 < starts.Count ? starts[k + 1].i - 1 : n - 1;
            chapters.Add(new ChapterMark(a, TitleFromSentence(s[a].Text), (int)Math.Round(10 * Math.Min(1, starts[k].v)), BestSentence(tr, sig, a, b, []) ?? a, []));
        }
        return new ChapterOutline("", chapters, "text");
    }

    /// <summary>A grounded fallback title: the chapter's first sentence without a leading topic-change phrase (「続いての企画は、…」).</summary>
    public static string TitleFromSentence(string text)
    {
        var t = text.Trim();
        int comma = t.IndexOf('、');
        if (IsTopicMarker(t) && comma > 0 && t.Length - comma > 3) t = t[(comma + 1)..];
        t = JapaneseText.NormalizeTelop(t.TrimEnd('。', '！', '？', '!', '?'));
        if (t.EndsWith("です", StringComparison.Ordinal) && t.Length >= 4) t = t[..^2];
        return Fit(t, CandidateValidator.MaxHeadline);
    }

    static string Fit(string s, double maxWidth)
    {
        if (JapaneseText.DisplayWidth(s) <= maxWidth) return s;
        var sb = new StringBuilder();
        foreach (var ch in s) { if (JapaneseText.DisplayWidth(sb.ToString() + ch) > maxWidth) break; sb.Append(ch); }
        return sb.ToString().TrimEnd('　', ' ');
    }

    /// <summary>The most watchable single sentence in a..b for a cold open / teaser (1.2–6 s, self-contained, energetic).</summary>
    static int? BestSentence(Transcript tr, SignalSet sig, int a, int b, IReadOnlyCollection<int> preferred, IReadOnlyList<string>? emphasis = null)
    {
        int? best = null; double bv = double.MinValue;
        for (int i = a; i <= b && i < tr.Sentences.Count; i++)
        {
            var x = tr.Sentences[i];
            if (x.T1 - x.T0 is < TeaserMin or > TeaserMax || JapaneseText.StartsWithDemonstrative(x.Text) || x.LastWord - x.FirstWord < 1) continue;
            double v = SignalExtractor.EnergyScore(sig, new Interval(x.T0, x.T1)) + (preferred.Contains(i) ? 3 : 0) + ((emphasis ?? []).Any(w => x.Text.Contains(w, StringComparison.Ordinal)) ? 1 : 0);
            if (v > bv) { bv = v; best = i; }
        }
        return best;
    }

    // ---------------- topic boundaries with AI (Structured Outputs) ----------------

    public const string OutlineSchema = """
{"type":"object","additionalProperties":false,"required":["series_title","chapters"],"properties":{
 "series_title":{"type":"string","description":"title of the whole video, max 20 Japanese characters, only facts from the transcript"},
 "chapters":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["start","title","split_score","highlight","emphasis_words"],"properties":{
  "start":{"type":"integer","description":"line number (#) of the chapter's first line"},
  "title":{"type":"string","description":"chapter headline, max 16 Japanese characters"},
  "split_score":{"type":"integer","description":"0-10"},
  "highlight":{"type":"integer","description":"line number of the chapter's most watchable line"},
  "emphasis_words":{"type":"array","items":{"type":"string"},"description":"max 3 phrases copied verbatim"}}}}}}
""";

    public static string OutlineSystem(int first) => $"""
あなたは YouTube / TikTok の長尺動画の構成作家です。行番号付きの書き起こしを、話題のまとまり（チャプター）に分けてください。
結果は、動画を「前編・後編」のような連続したパートに分ける位置の判断と、章の見出しテロップに使います。
規則:
- chapters は時系列順。start はそのチャプターの最初の行の番号（#）。最初のチャプターの start は {first}
- 1つのチャプターはおおむね {MinChapterSec:0} 秒以上。話題・企画・場面が変わる行で区切る
- title は16字以内の見出し。書き起こしの内容だけに基づき、書き起こしにない数字・固有名詞を書かない
- split_score（0〜10）: その行の直前で動画を別のパートに分けたとき、前のパートが自然に区切れ、かつ視聴者が次のパートを見たくなるか。話題が変わり、次への期待が高まる位置は高く、話やオチの途中で切れてしまう位置は低く
- highlight: そのチャプターで最も見どころになる1行の番号（冒頭の先出しや次回予告に使う。単体で意味が通る短い発言）
- emphasis_words: 強調テロップにする語句を最大3つ、書き起こしから原文どおりに抜き出す
- series_title: 動画全体のタイトル（20字以内、書き起こしにある内容のみ）
""";

    static string Line(Sentence s, int i)
    {
        var text = s.Text.Length > 80 ? s.Text[..80] + "…" : s.Text;
        return $"#{i} [{(int)(s.T0 / 60)}:{(int)(s.T0 % 60):D2}] S{s.Speaker}: {text}";
    }

    /// <summary>Chapters by the LLM. Long transcripts are sent in windows (in parallel); returns null when any window fails (the caller falls back to <see cref="FallbackOutline"/>).</summary>
    public static async Task<ChapterOutline?> OutlineAsync(IHighlightSelector llm, Transcript tr, Action<string>? log, CancellationToken ct, int windowChars = 24_000)
    {
        var s = tr.Sentences; if (s.Count == 0) return null;
        var windows = new List<(int a, int b)>(); int start = 0, chars = 0;
        for (int i = 0; i < s.Count; i++)
        {
            int len = Line(s[i], i).Length + 1;
            if (chars + len > windowChars && i > start) { windows.Add((start, i - 1)); start = i; chars = 0; }
            chars += len;
        }
        windows.Add((start, s.Count - 1));
        int inChars = 0, outChars = 0;
        var results = await Task.WhenAll(windows.Select(async w =>
        {
            var user = string.Join("\n", Enumerable.Range(w.a, w.b - w.a + 1).Select(i => Line(s[i], i)));
            Interlocked.Add(ref inChars, user.Length + OutlineSystem(w.a).Length);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var json = await llm.CompleteJsonAsync(OutlineSystem(w.a), user, "series_outline", OutlineSchema, ct);
                    Interlocked.Add(ref outChars, json.Length);
                    return ParseOutline(json, tr, w.a, w.b);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { log?.Invoke($"series outline attempt {attempt + 1} failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            return null;
        }));
        if (results.Any(r => r == null)) return null;
        var chapters = results.SelectMany(r => r!.Chapters).OrderBy(c => c.Start).ToList();
        var title = results[0]!.SeriesTitle;
        if (JapaneseText.UngroundedClaims(title, tr.FullText) is { Count: > 0 }) title = "";
        return new ChapterOutline(title, chapters, "llm", inChars, outChars);
    }

    /// <summary>Validates the AI outline in code: indices in range and ordered, first chapter at the window start, highlights inside their chapter, titles/emphasis grounded in the chapter's own transcript.</summary>
    public static ChapterOutline ParseOutline(string json, Transcript tr, int first, int last)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var s = tr.Sentences;
        var raw = new List<(int start, string title, int score, int hl, List<string> emph)>();
        foreach (var c in root.GetProperty("chapters").EnumerateArray())
        {
            int st = c.GetProperty("start").GetInt32();
            if (st < first || st > last) continue;
            raw.Add((st, c.GetProperty("title").GetString() ?? "", Math.Clamp(c.GetProperty("split_score").GetInt32(), 0, 10), c.GetProperty("highlight").GetInt32(),
                c.GetProperty("emphasis_words").EnumerateArray().Select(e => e.GetString() ?? "").ToList()));
        }
        raw = raw.GroupBy(r => r.start).Select(g => g.First()).OrderBy(r => r.start).ToList();
        if (raw.Count == 0 || raw[0].start != first) raw.Insert(0, (first, "", 0, -1, []));
        var res = new List<ChapterMark>();
        for (int k = 0; k < raw.Count; k++)
        {
            var (a, title, score, hl, emph) = raw[k];
            int b = k + 1 < raw.Count ? raw[k + 1].start - 1 : last;
            var text = string.Concat(Enumerable.Range(a, b - a + 1).Select(i => s[i].Text));
            title = Fit(JapaneseText.NormalizeTelop(title.Trim()), CandidateValidator.MaxHeadline);
            if (title.Length == 0 || JapaneseText.UngroundedClaims(title, text).Count > 0) title = TitleFromSentence(s[a].Text);
            var norm = text.Normalize(NormalizationForm.FormKC);
            var words = emph.Select(w => w.Trim()).Where(w => w.Length > 0 && JapaneseText.DisplayWidth(w) <= CandidateValidator.MaxEmphasisWidth && norm.Contains(w.Normalize(NormalizationForm.FormKC), StringComparison.Ordinal))
                .Distinct().Take(CandidateValidator.MaxEmphasis).ToList();
            // the first chapter of a later window starts at the window edge, not necessarily at a topic change
            int sc = k == 0 && first > 0 ? -1 : score;
            res.Add(new ChapterMark(a, title, sc, hl >= a && hl <= b ? hl : -1, words));
        }
        var seriesTitle = Fit(JapaneseText.NormalizeTelop((root.GetProperty("series_title").GetString() ?? "").Trim()), 20);
        return new ChapterOutline(seriesTitle, res, "llm");
    }

    // ---------------- the split: dynamic programming over sentence boundaries ----------------

    sealed class LengthModel
    {
        readonly Transcript _tr; readonly double[] _gapT0, _gapT1, _prefix; readonly double _duration;
        public LengthModel(Transcript tr, Preset p, double duration)
        {
            _tr = tr; _duration = duration;
            var gaps = new List<Interval>();
            if (p.Cut.RemoveSilenceOverSec is double th)
                for (int i = 0; i + 1 < tr.Words.Count; i++)
                {
                    var g = new Interval(tr.Words[i].T1, tr.Words[i + 1].T0);
                    if (g.Length > th) gaps.Add(g);
                }
            _gapT0 = gaps.Select(g => g.T0).ToArray(); _gapT1 = gaps.Select(g => g.T1).ToArray();
            _prefix = new double[gaps.Count + 1];
            for (int i = 0; i < gaps.Count; i++) _prefix[i + 1] = _prefix[i] + gaps[i].Length - 2 * Solver.JumpPad;
        }

        /// <summary>Edited length of sentences a..b (pauses above the preset threshold are removed by jump cuts); the first/last part also keeps the non-speech before/after.</summary>
        public double Of(int a, int b)
        {
            var s = _tr.Sentences;
            double t0 = a == 0 ? 0 : s[a].T0, t1 = b == s.Count - 1 ? Math.Max(s[b].T1, _duration) : s[b].T1;
            int lo = LowerBound(_gapT0, t0 - 0.01), hi = LowerBound(_gapT1, t1 + 0.01 + 1e-9);
            double removable = hi > lo ? _prefix[hi] - _prefix[lo] : 0;
            return t1 - t0 + 2 * Solver.EdgePad - removable;
        }

        static int LowerBound(double[] a, double v) { int lo = 0, hi = a.Length; while (lo < hi) { int m = (lo + hi) / 2; if (a[m] < v) lo = m + 1; else hi = m; } return lo; }
    }

    /// <summary>Edited length of the whole source for a preset (pauses above its threshold removed) — the basis of the part count / length preview.</summary>
    public static double EditedLength(Transcript tr, Preset p, double duration) =>
        tr.Sentences.Count == 0 ? duration : new LengthModel(tr, p, duration).Of(0, tr.Sentences.Count - 1);

    public const double MinPartSec = 60;
    public const int MaxParts = 20;

    /// <summary>The largest part count the material allows (each part ≥ <see cref="MinPartSec"/> of edited video and at least two sentences).</summary>
    public static int MaxPartsFor(double editedSec, int sentences) => Math.Max(0, Math.Min(MaxParts, Math.Min((int)Math.Floor(editedSec / MinPartSec), sentences / 2)));

    public static List<SeriesPart> Split(Transcript tr, SignalSet sig, Preset p, double duration, double targetSec, ChapterOutline outline, List<string> notes) =>
        Split(tr, sig, p, duration, targetSec, null, outline, notes);

    /// <summary>
    /// Chooses the part count — exactly <paramref name="requestedParts"/> when the user picked one, otherwise round(edited length / target) —
    /// and the split positions that minimise Σ duration deviation from the balanced length + Σ (−boundary quality). Splits are only ever placed between sentences.
    /// </summary>
    public static List<SeriesPart> Split(Transcript tr, SignalSet sig, Preset p, double duration, double? targetSec, int? requestedParts, ChapterOutline outline, List<string> notes)
    {
        var s = tr.Sentences; int n = s.Count;
        if (n < 4) throw new PlanException("E-SERIES-SHORT", "発話が少なすぎるため、前編・後編に分けられません");
        var len = new LengthModel(tr, p, duration);
        double total = len.Of(0, n - 1);
        int parts;
        if (requestedParts is int want)
        {
            int max = MaxPartsFor(total, n);
            if (want < 2) throw new PlanException("E-SERIES-PARTS", "本数は2本以上で指定してください");
            if (want > max) throw new PlanException("E-SERIES-PARTS", max < 2
                ? $"編集後の長さ（約{total / 60:F1}分）が短いため、分けられません（1本あたり{MinPartSec / 60:0}分以上が必要です）"
                : $"{want}本に分けると1本あたり約{total / want / 60:F1}分になり短すぎます。この動画（編集後 約{total / 60:F1}分）は最大 {max} 本まで分けられます");
            parts = want;
        }
        else
        {
            double t = targetSec ?? throw new ArgumentException("targetSec or requestedParts is required");
            parts = (int)Math.Round(total / t);
            if (parts < 2) throw new PlanException("E-SERIES-SHORT", $"編集後の長さ（約{total / 60:F1}分）が、指定した1本あたりの長さ（{t / 60:F1}分）の2本分に足りません。1本あたりの長さを短くしてください");
            parts = Math.Min(parts, n / 2);
        }
        // cold open (~4 s) in every part, recap (~5 s) in all but the first, next teaser (~4 s) in all but the last; exact after planning
        double Extras(int k) => 4 + (k > 0 ? 5 : 0) + (k < parts - 1 ? 4 : 0);
        double target = (total + Enumerable.Range(0, parts).Sum(Extras)) / parts;
        var labels = Labels(parts);
        var depth = Depth(tr);
        var score = BoundaryScores(tr, depth, outline);

        List<int>? cuts = null; double usedTol = 0;
        foreach (var tol in Tolerances)
        {
            cuts = Dp(n, parts, (a, b, k) => len.Of(a, b) + Extras(k), score, target, tol);
            if (cuts != null) { usedTol = tol; break; }
        }
        if (cuts == null) throw new PlanException("E-SERIES-SHORT", "指定に近い長さのパートに分けられませんでした。本数か1本あたりの長さを変えてください");
        if (usedTol > Tolerances[0]) notes.Add($"話題の切れ目を優先したため、各パートの長さの差が目安（±{Tolerances[0]:P0}）を超えています");

        var bounds = new List<int> { 0 }; bounds.AddRange(cuts); bounds.Add(n);
        var marks = outline.Chapters.OrderBy(c => c.Start).ToList();
        var specs = new List<SeriesPart>();
        for (int k = 0; k < parts; k++)
        {
            int a = bounds[k], b = bounds[k + 1] - 1;
            var inPart = marks.Where(m => m.Start > a && m.Start <= b).ToList();
            var opening = marks.LastOrDefault(m => m.Start <= a);
            var chapters = new List<ChapterMark>();
            if (opening != null) chapters.Add(opening with { Start = a });
            chapters.AddRange(inPart);
            var highlights = chapters.Select(c => c.Highlight).Where(h => h >= a && h <= b).ToHashSet();
            var emphasis = chapters.SelectMany(c => c.Emphasis).Distinct().ToList();
            int? teaser = BestSentence(tr, sig, Math.Min(b, a + 2), b, highlights, emphasis) ?? BestSentence(tr, sig, a, b, highlights, emphasis);
            specs.Add(new SeriesPart
            {
                Index = k, Count = parts, Label = labels[k], Labels = labels, SeriesTitle = outline.SeriesTitle,
                First = a, Last = b, Body = new Interval(0, 0), Teaser = teaser is int t ? (t, t) : null,
                Chapters = chapters, Emphasis = emphasis, TargetSec = Math.Round(targetSec ?? target, 1), RequestedParts = requestedParts,
                MinSec = Math.Round(target * (1 - QaTolerance), 1), MaxSec = Math.Round(target * (1 + QaTolerance), 1), EstSec = Math.Round(len.Of(a, b) + Extras(k), 1),
            });
        }
        // bodies: cut inside the pause between the last word of one part and the first word of the next, so every word is in exactly one body
        for (int k = 0; k < parts; k++)
        {
            var sp = specs[k];
            double t0 = k == 0 ? 0 : specs[k - 1].Body.T1;
            double t1 = k == parts - 1 ? duration : Solver.Edges(tr, sp.First, sp.Last, sig).T1;
            if (k > 0) t0 = Math.Max(t0, Solver.Edges(tr, sp.First, sp.Last, sig).T0);
            specs[k] = sp with { Body = new Interval(Math.Round(t0, 3), Math.Round(Math.Max(t0 + 0.1, t1), 3)) };
        }
        for (int k = 0; k < parts; k++)
        {
            var sp = specs[k];
            (int, int)? recap = k > 0 ? RecapRange(tr, specs[k - 1].First, specs[k - 1].Last) : null;
            var next = k + 1 < parts ? specs[k + 1].Teaser : null;
            string boundary = k + 1 < parts ? Describe(tr, depth, outline, bounds[k + 1], labels[k + 1]) : "最後のパート";
            specs[k] = sp with { Recap = recap, Next = next, Boundary = boundary };
        }
        var how = requestedParts != null ? $"指定の {parts} 本（{string.Join("・", labels)}、1本あたり約{total / parts / 60:F1}分）" : $"指定の {targetSec!.Value / 60:0.#}分に近い {parts} 本（{string.Join("・", labels)}）";
        notes.Add($"編集後の合計 約{total / 60:F1}分を、{how}に分けました。分け目は話題の切れ目から選んでいます（{(outline.Source == "llm" ? "AI の章分け" : "書き起こしの語彙の変化")}＋間＋話題転換語）");
        // a split that is not at a topic change (not a chapter start, no topic-change words) means the requested count outran the material's topics
        var starts = outline.Chapters.Where(c => c.SplitScore >= 0).Select(c => c.Start).ToHashSet();
        var mid = cuts.Select((c, k) => (c, k)).Where(x => !starts.Contains(x.c) && !IsTopicMarker(s[x.c].Text)).ToList();
        if (mid.Count > 0)
            notes.Add($"話題の切れ目が足りないため、{string.Join("、", mid.Select(x => $"{labels[x.k]}と{labels[x.k + 1]}の間（{(int)(s[x.c].T0 / 60)}:{(int)(s[x.c].T0 % 60):D2}）"))}は話題の途中で分けています（文の切れ目では分けています）。本数を減らすと話題の切れ目で分けやすくなります");
        return specs;
    }

    static List<int>? Dp(int n, int parts, Func<int, int, int, double> length, double[] score, double target, double tol)
    {
        double lo = target * (1 - tol), hi = target * (1 + tol);
        var dp = new double[parts + 1, n + 1]; var from = new int[parts + 1, n + 1];
        for (int k = 0; k <= parts; k++) for (int i = 0; i <= n; i++) dp[k, i] = double.PositiveInfinity;
        dp[0, 0] = 0;
        for (int k = 1; k <= parts; k++)
            for (int i = 1; i <= n; i++)
            {
                if (k == parts && i != n) continue;
                for (int j = i - 1; j >= k - 1; j--)
                {
                    if (double.IsInfinity(dp[k - 1, j])) continue;
                    double l = length(j, i - 1, k - 1);
                    if (l > hi) break;          // longer as j decreases
                    if (l < lo) continue;
                    double d = (l - target) / target;
                    double c = dp[k - 1, j] + DurationWeight * d * d - (i < n ? BoundaryWeight * score[i] : 0);
                    if (c < dp[k, i]) { dp[k, i] = c; from[k, i] = j; }
                }
            }
        if (double.IsInfinity(dp[parts, n])) return null;
        var cuts = new List<int>();
        for (int k = parts, i = n; k > 1; k--) { i = from[k, i]; cuts.Insert(0, i); }
        return cuts;
    }

    /// <summary>The previous part's ending (1–3 sentences, 2–8 s) — 「前編のおさらい」.</summary>
    static (int, int)? RecapRange(Transcript tr, int first, int last)
    {
        var s = tr.Sentences;
        for (int b = last; b >= Math.Max(first, last - 3); b--)
        {
            int a = b;
            while (a > first && s[b].T1 - s[a].T0 < 2.0 && b - a < 2) a--;
            double d = s[b].T1 - s[a].T0;
            if (d is < 1.0 or > RecapMax) continue;
            if (a == first || !JapaneseText.StartsWithDemonstrative(s[a].Text)) return (a, b);
        }
        return null;
    }

    static string Describe(Transcript tr, double[] depth, ChapterOutline outline, int i, string nextLabel)
    {
        var s = tr.Sentences;
        var why = new List<string>();
        var mark = outline.Chapters.FirstOrDefault(c => c.Start == i);
        if (mark != null) why.Add(mark.SplitScore >= 0 ? $"AI の章の切れ目「{mark.Title}」（分けやすさ {mark.SplitScore}/10）" : $"章「{mark.Title}」の始まり");
        if (IsTopicMarker(s[i].Text)) why.Add("話題転換の言葉");
        double gap = Math.Max(0, s[i].T0 - s[i - 1].T1);
        if (gap >= 0.5) why.Add($"間 {gap:F1}秒");
        if (depth[i] >= 0.5) why.Add("語彙が大きく変わる位置");
        var head = s[i].Text.Length > 24 ? s[i].Text[..24] + "…" : s[i].Text;
        return $"{(int)(s[i].T0 / 60)}:{(int)(s[i].T0 % 60):D2}「{head}」から{nextLabel}" + (why.Count > 0 ? $"（{string.Join("・", why)}）" : "");
    }

    // ---------------- part → clips and telops ----------------

    /// <summary>[cold open] [recap] [body with jump cuts] [next teaser]. Body pieces alternate punch-ins like the short patterns.</summary>
    public static List<Clip> Clips(SeriesPart part, Transcript tr, SignalSet sig, Preset p, MediaInfo media, string assetId, List<string> notes)
    {
        var reframe = new ReframeSpec { Mode = media.Aspect < 0.9 ? "fit" : "blur_bg" };
        var clips = new List<Clip>(); int ci = 0;
        void Add(Interval iv, string role, double zoom) => clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = R(iv.T0), SrcOut = R(iv.T1), Reframe = reframe, Zoom = zoom, Role = role });
        if (part.Teaser is { } te) { Add(Solver.Edges(tr, te.A, te.B, sig), "teaser", p.Cut.PunchInScale); notes.Add($"冒頭に「{Trunc(tr.Sentences[te.A].Text, 20)}」を先出し（このパートの見どころ）"); }
        if (part.Recap is { } rc) { Add(Solver.Edges(tr, rc.A, rc.B, sig), "recap", 1.0); notes.Add($"{part.PrevLabel}のおさらいに「{Trunc(tr.Sentences[rc.A].Text, 20)}」"); }
        var pieces = Solver.JumpCut(part.Body, tr, sig, p);
        for (int i = 0; i < pieces.Count; i++) Add(pieces[i], "body", i % 2 == 1 ? p.Cut.PunchInScale : 1.0);
        double cut = part.Body.Length - pieces.Sum(x => x.Length);
        if (cut > 0.05) notes.Add($"無音・間を {cut:F1} 秒カット（ジャンプカット {pieces.Count - 1} か所）");
        if (part.Next is { } nx) { Add(Solver.Edges(tr, nx.A, nx.B, sig), "next", p.Cut.PunchInScale); notes.Add($"最後に{part.NextLabel}の予告「{Trunc(tr.Sentences[nx.A].Text, 20)}」"); }
        return clips;
    }

    /// <summary>Output time of a master time inside the BODY clips (teasers repeat body moments, so they are skipped).</summary>
    public static double BodyOutputTime(IReadOnlyList<Clip> clips, double t)
    {
        double off = 0;
        foreach (var c in clips)
        {
            if (c.Role is "body" or "reaction" or "wide" && t < c.MasterOut - 1e-6) return off + Math.Max(0, t - c.MasterIn) / c.Speed;
            off += c.Length;
        }
        return off;
    }

    public static string HookText(SeriesPart part)
    {
        var label = $"【{part.Label}】";
        var rest = Fit(part.SeriesTitle, CandidateValidator.MaxHook - JapaneseText.DisplayWidth(label));
        return label + rest;
    }

    /// <summary>Structural telops (hook with the part label, recap / next labels, per-chapter headlines) and the part's chapters on the output timeline.</summary>
    public static (List<Telop> Telops, List<SeriesChapter> Chapters) Telops(SeriesPart part, IReadOnlyList<Clip> clips, Transcript tr, Preset p)
    {
        var telops = new List<Telop>(); var chapters = new List<SeriesChapter>(); int n = 0;
        double total = clips.Sum(c => c.Length);
        (double start, double end)? Span(string role)
        {
            double off = 0; double? a = null, b = null;
            foreach (var c in clips) { if (c.Role == role) { a ??= off; b = off + c.Length; } off += c.Length; }
            return a is double x && b is double y ? (x, y) : null;
        }
        void Add(TelopType type, double a, double b, string text, string style, string? anim = null)
        {
            if (b - a < 0.3 || text.Length == 0) return;
            telops.Add(new Telop { Id = $"s{++n}", Type = type, Start = R(a), End = R(b), Text = text, Style = style, Animation = anim });
        }
        var teaser = Span("teaser"); var recap = Span("recap"); var next = Span("next");
        double bodyStart = clips.TakeWhile(c => c.Role is "teaser" or "recap").Sum(c => c.Length);
        double bodyEnd = next?.start ?? total;
        // hook: the part label over the cold open (or over the first seconds when there is none)
        var (h0, h1) = teaser ?? (0, Math.Min(TelopBuilder.HookSeconds, bodyEnd));
        Add(TelopType.Hook, h0, h1, HookText(part), "Hook", "pop");
        if (teaser != null) chapters.Add(new SeriesChapter(0, "見どころ"));
        if (recap is { } rs && part.PrevLabel != null) { Add(TelopType.Headline, rs.start, rs.end, $"{part.PrevLabel}のおさらい", "Headline"); chapters.Add(new SeriesChapter(R(rs.start), $"{part.PrevLabel}のおさらい")); }
        var marks = part.Chapters.OrderBy(c => c.Start).ToList();
        for (int k = 0; k < marks.Count; k++)
        {
            double a = k == 0 ? bodyStart : BodyOutputTime(clips, tr.Sentences[marks[k].Start].T0 - Solver.EdgePad);
            double b = k + 1 < marks.Count ? BodyOutputTime(clips, tr.Sentences[marks[k + 1].Start].T0 - Solver.EdgePad) : bodyEnd;
            if (k == 0 && teaser == null && recap == null) a = Math.Max(a, h1);   // do not stack on the hook
            if (p.HasTelop("headline")) Add(TelopType.Headline, a, b, marks[k].Title, "Headline");
            chapters.Add(new SeriesChapter(R(k == 0 ? bodyStart : a), marks[k].Title));
        }
        if (next is { } ns && part.NextLabel != null) { Add(TelopType.Headline, ns.start, ns.end, $"{part.NextLabel}につづく", "Headline"); chapters.Add(new SeriesChapter(R(ns.start), $"{part.NextLabel}の予告")); }
        return (telops, chapters);
    }

    /// <summary>
    /// YouTube description chapters (YouTube Help "Video chapters": first timestamp 00:00, at least three in ascending order, each at least 10 s).
    /// Chapters that would be shorter than 10 s are merged into the previous one. Returns null when fewer than three remain.
    /// </summary>
    public static List<string>? YouTubeChapters(IReadOnlyList<SeriesChapter> chapters, double duration)
    {
        var list = chapters.Where(c => c.Start < duration - 10).OrderBy(c => c.Start).ToList();
        if (list.Count == 0) return null;
        list[0] = list[0] with { Start = 0 };
        var kept = new List<SeriesChapter>();
        foreach (var c in list)
        {
            // too close to the previous chapter: keep the earlier start but the later (content) title
            if (kept.Count > 0 && c.Start - kept[^1].Start < 10) { kept[^1] = kept[^1] with { Title = c.Title }; continue; }
            kept.Add(c);
        }
        if (kept.Count > 0 && duration - kept[^1].Start < 10) kept.RemoveAt(kept.Count - 1);
        if (kept.Count < 3) return null;
        return kept.Select(c => $"{Stamp(c.Start, duration)} {c.Title}").ToList();
    }

    public static string Stamp(double t, double duration)
    {
        var ts = TimeSpan.FromSeconds(Math.Floor(t));
        return duration >= 3600 ? ts.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : ts.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    static double R(double x) => Math.Round(x, 3);
    static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}
