using System.Text;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Signals;

namespace VideoCreator.Selection;

/// <summary>Code-side enforcement of PRD §20.2 limits, grounding and FR-SEL-05/06/08 for any provider.</summary>
public static class CandidateValidator
{
    public const int MaxSegments = 4, MaxHook = 14, MaxHeadline = 16, MaxEmphasis = 3, MaxReason = 120, MaxEmphasisWidth = 10;
    public const double LlmWeight = 0.6, SignalWeight = 0.4;

    public static string TextOf(Transcript tr, IEnumerable<Interval> segs) =>
        VideoLanguages.JoinWords(segs.SelectMany(s => tr.Sentences.Where(x => x.T1 > s.T0 + 0.05 && x.T0 < s.T1 - 0.05)).Distinct().Select(x => x.Text), tr.Language);

    public static Candidate Normalize(RawCandidate raw, Transcript tr, SignalSet sig, double duration, string id, string source = "llm")
    {
        var notes = new List<string>();
        // segments: clamp, drop invalid, sort, merge overlaps, cap at 4 by merging the closest gaps
        var segs = raw.Segments.Select(s => new Interval(Math.Clamp(s.T0, 0, duration), Math.Clamp(s.T1, 0, duration)))
            .Where(s => s.T1 - s.T0 >= 0.3).OrderBy(s => s.T0).ToList();
        if (segs.Count != raw.Segments.Count) notes.Add("範囲外または不正な区間を除外しました");
        segs = MergeOverlaps(segs, 0.3);
        while (segs.Count > MaxSegments)
        {
            int best = 0; double gap = double.MaxValue;
            for (int i = 0; i + 1 < segs.Count; i++) { var g = segs[i + 1].T0 - segs[i].T1; if (g < gap) { gap = g; best = i; } }
            segs[best] = new Interval(segs[best].T0, segs[best + 1].T1); segs.RemoveAt(best + 1);
            notes.Add("区間が4つを超えたため、近い区間を結合しました");
        }

        var src = TextOf(tr, segs);
        string Limit(string s, int max, string label)
        {
            s = CaptionText.Normalize(s.Trim(), tr.Language);
            if (JapaneseText.DisplayWidth(s) <= max) return s;
            var sb = new StringBuilder();
            foreach (var ch in s) { if (JapaneseText.DisplayWidth(sb.ToString() + ch) > max) break; sb.Append(ch); }
            notes.Add($"{label}が{max}字を超えたため短縮しました");
            return sb.ToString().TrimEnd('　', ' ', '、');
        }
        var hook = Limit(raw.HookText, MaxHook, "フック");
        var head = Limit(raw.Headline, MaxHeadline, "見出し");
        // FR-TEL-09 pre-check: drop claims not grounded in the clip's own transcript
        if (JapaneseText.UngroundedClaims(hook, src) is { Count: > 0 } hp) { notes.Add($"フックに書き起こしにない内容（{string.Join(",", hp)}）があるため削除しました"); hook = ""; }
        if (JapaneseText.UngroundedClaims(head, src) is { Count: > 0 } hd) { notes.Add($"見出しに書き起こしにない内容（{string.Join(",", hd)}）があるため削除しました"); head = ""; }
        var normSrc = src.Normalize(NormalizationForm.FormKC);
        var emph = raw.EmphasisWords.Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().ToList();
        var tooLong = emph.Where(w => JapaneseText.DisplayWidth(w) > MaxEmphasisWidth).ToList();
        if (tooLong.Count > 0) notes.Add($"強調語は{MaxEmphasisWidth}字以内の語句に限るため除外しました（{string.Join(",", tooLong)}）");
        emph = emph.Except(tooLong).ToList();
        var grounded = emph.Where(w => normSrc.Contains(w.Normalize(NormalizationForm.FormKC))).Take(MaxEmphasis).ToList();
        if (grounded.Count < emph.Count) notes.Add($"書き起こしに原文がない強調語を除外しました（{string.Join(",", emph.Except(grounded))}）");
        var reason = raw.Reason.Length > MaxReason ? raw.Reason[..MaxReason] : raw.Reason;

        int standalone = Math.Clamp(raw.Standalone, 0, 10);
        var first = tr.Sentences.FirstOrDefault(s => segs.Count > 0 && s.T1 > segs[0].T0 + 0.05);
        if (first != null && JapaneseText.StartsWithDemonstrative(first.Text)) { standalone = Math.Max(0, standalone - 3); notes.Add("冒頭が指示語で始まるため standalone を減点しました（FR-SEL-06）"); }

        double signal = segs.Count == 0 ? 0 : segs.Sum(s => SignalExtractor.EnergyScore(sig, s) * s.Length) / Math.Max(0.1, segs.Sum(s => s.Length));
        var scores = new Scores
        {
            Hook = Math.Clamp(raw.Hook, 0, 10), Standalone = standalone, Payoff = Math.Clamp(raw.Payoff, 0, 10), Emotion = Math.Clamp(raw.Emotion, 0, 10),
            Signal = Math.Round(signal, 1),
        };
        scores = scores with { Final = Math.Round(LlmWeight * scores.LlmAverage + SignalWeight * scores.Signal, 2) };
        return new Candidate { Id = id, Segments = segs, HookText = hook, Headline = head, EmphasisWords = grounded, Reason = reason, Scores = scores, Source = source, Notes = notes };
    }

    public static List<Interval> MergeOverlaps(List<Interval> segs, double gap)
    {
        var res = new List<Interval>();
        foreach (var s in segs.OrderBy(s => s.T0))
        {
            if (res.Count > 0 && s.T0 <= res[^1].T1 + gap) res[^1] = new Interval(res[^1].T0, Math.Max(res[^1].T1, s.T1));
            else res.Add(s);
        }
        return res;
    }

    /// <summary>Temporal IoU of two candidates' segment unions.</summary>
    public static double IoU(IReadOnlyList<Interval> a, IReadOnlyList<Interval> b)
    {
        double inter = 0;
        foreach (var x in a) foreach (var y in b) inter += Math.Max(0, Math.Min(x.T1, y.T1) - Math.Max(x.T0, y.T0));
        double union = a.Sum(x => x.Length) + b.Sum(y => y.Length) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>FR-SEL-08: drop near-duplicates (IoU &gt; 0.5), keep the higher final score; rank.</summary>
    public static List<Candidate> Dedup(IEnumerable<Candidate> cands, int take)
    {
        var kept = new List<Candidate>();
        foreach (var c in cands.Where(c => c.Segments.Count > 0).OrderByDescending(c => c.Scores.Final))
            if (kept.All(k => IoU(k.Segments, c.Segments) <= 0.5)) kept.Add(c);
        return kept.Take(take).Select((c, i) => c with { Rank = i + 1 }).ToList();
    }
}
