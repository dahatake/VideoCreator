using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Signals;

namespace VideoCreator.Planning;

public sealed record PlanResult(List<Clip> Clips, List<string> Notes, bool Feasible)
{
    public double Duration => Clips.Sum(c => c.Length);
}

/// <summary>Deterministic constraint solver (FR-SOL-01..07). Same inputs -> same clips.</summary>
public static class Solver
{
    public const double SnapWindow = 1.5, EdgePad = 0.12, ShotSnap = 0.3, JumpPad = 0.15;

    public static PlanResult Plan(IReadOnlyList<Interval> segments, Transcript tr, SignalSet sig, Preset p, MediaInfo media, string assetId, IReadOnlyList<string>? emphasisWords = null)
    {
        var notes = new List<string>();
        double dur = media.Duration;
        var reframe = new ReframeSpec { Mode = media.Aspect < 0.9 ? "fit" : "blur_bg" };   // vertical sources pass through (AT-11)
        List<Interval> ranges;
        bool feasible = true;
        double teaserLen = 0; Interval? teaser = null;

        if (tr.Sentences.Count > 0)
        {
            // FR-SOL-01: map to sentence ranges (sentences overlapping the segment by > 50 % of their length)
            var sr = new List<(int a, int b)>();
            foreach (var s in segments)
            {
                var idx = tr.Sentences.Where(x => Overlap(x.T0, x.T1, s.T0 - 0.01, s.T1 + 0.01) > 0.5 * Math.Max(0.05, x.T1 - x.T0)).Select(x => x.Index).ToList();
                if (idx.Count == 0)
                {
                    var near = tr.Sentences.OrderBy(x => Math.Abs(x.T0 - s.T0)).First();
                    if (Math.Abs(near.T0 - s.T0) <= SnapWindow + s.Length) idx.Add(near.Index);
                }
                if (idx.Count > 0) sr.Add((idx.Min(), idx.Max()));
            }
            sr = MergeRanges(sr);
            if (sr.Count == 0) { notes.Add("E-SOL-INFEASIBLE: 区間に発話がありません"); return new PlanResult([], notes, false); }

            // cold open teaser (P4 etc.): the most energetic short sentence that is not already at the start
            if (p.Cut.ColdOpen == "teaser")
            {
                var pool = sr.SelectMany(r => Enumerable.Range(r.a, r.b - r.a + 1)).Skip(1).Select(i => tr.Sentences[i])
                    .Where(s => s.T1 - s.T0 is >= 1.0 and <= 3.5 && !JapaneseText.StartsWithDemonstrative(s.Text)).ToList();
                var best = pool.OrderByDescending(s => SignalExtractor.EnergyScore(sig, new Interval(s.T0, s.T1)) + ((emphasisWords ?? []).Any(w => s.Text.Contains(w)) ? 3 : 0)).FirstOrDefault();
                if (best != null && SignalExtractor.EnergyScore(sig, new Interval(best.T0, best.T1)) >= 4 || best != null && (emphasisWords ?? []).Any(w => best.Text.Contains(w)))
                {
                    teaser = Edges(tr, best.Index, best.Index, sig);
                    teaserLen = teaser.Length;
                    notes.Add($"冒頭に「{Trunc(best.Text, 20)}」を先出し（コールドオープン）");
                }
            }

            // FR-SOL-03: fit duration by whole sentences (estimate includes jump-cut savings)
            double Est() => sr.Sum(r => EstimatedLength(tr, r.a, r.b, p)) + teaserLen;
            int guard = 0;
            while (Est() > p.Target.Max && guard++ < 500)
            {
                int k = sr.Select((r, i) => (r, i)).Where(x => x.r.b > x.r.a || sr.Count > 1).OrderByDescending(x => EstimatedLength(tr, x.r.a, x.r.b, p)).Select(x => x.i).DefaultIfEmpty(-1).First();
                if (k < 0) break;
                var r = sr[k];
                if (r.b > r.a) sr[k] = (r.a, r.b - 1);                 // drop the last sentence of the longest range
                else if (sr.Count > 1 && k != 0) sr.RemoveAt(k);        // keep the hook range
                else break;
            }
            guard = 0;
            while (Est() < p.Target.Min && guard++ < 500)
            {
                var last = sr[^1]; var firstR = sr[0];
                bool canFwd = last.b + 1 < tr.Sentences.Count && EstimatedLength(tr, last.a, last.b + 1, p) + Est() - EstimatedLength(tr, last.a, last.b, p) <= p.Target.Max;
                bool canBack = firstR.a > 0 && (sr.Count == 1 || firstR.a - 1 > -1) && EstimatedLength(tr, firstR.a - 1, firstR.b, p) + Est() - EstimatedLength(tr, firstR.a, firstR.b, p) <= p.Target.Max;
                // keep a demonstrative from becoming the opening line
                if (canBack && JapaneseText.StartsWithDemonstrative(tr.Sentences[firstR.a - 1].Text)) canBack = false;
                if (canFwd) sr[^1] = (last.a, last.b + 1);              // extend toward the payoff first
                else if (canBack) sr[0] = (firstR.a - 1, firstR.b);
                else break;
                sr = MergeRanges(sr);
            }
            if (Est() < p.Target.Min) { feasible = false; notes.Add($"E-SOL-INFEASIBLE: 尺が {Est():F1} 秒で下限 {p.Target.Min} 秒に届きません"); }
            if (Est() > p.Target.Max) { feasible = false; notes.Add($"E-SOL-INFEASIBLE: 尺が {Est():F1} 秒で上限 {p.Target.Max} 秒を超えます"); }
            ranges = sr.Select(r => Edges(tr, r.a, r.b, sig)).ToList();
        }
        else
        {
            // no speech (P9 / no audio): snap to shot boundaries, trim/extend by time
            ranges = segments.Select(s => new Interval(SnapTo(sig.Shots, s.T0, SnapWindow), SnapTo(sig.Shots, s.T1, SnapWindow))).Where(s => s.Length > 0.5).ToList();
            double total = ranges.Sum(r => r.Length);
            if (total > p.Target.Max && ranges.Count > 0) { var l = ranges[^1]; ranges[^1] = new Interval(l.T0, l.T1 - (total - p.Target.Max)); }
            if (total < p.Target.Min && ranges.Count > 0) { var l = ranges[^1]; ranges[^1] = new Interval(l.T0, Math.Min(dur, l.T1 + (p.Target.Min - total))); }
            if (ranges.Sum(r => r.Length) < p.Target.Min - 0.01) { feasible = false; notes.Add("E-SOL-INFEASIBLE: 素材が短すぎます"); }
        }

        // FR-SOL-05: jump cuts inside ranges (word gaps / silences above the preset threshold); then min shot
        var clips = new List<Clip>();
        int ci = 0;
        if (teaser != null) clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = R(teaser.T0), SrcOut = R(teaser.T1), Reframe = reframe, Zoom = p.Cut.PunchInScale, Role = "teaser" });
        foreach (var range in ranges)
        {
            var pieces = JumpCut(range, tr, sig, p);
            for (int i = 0; i < pieces.Count; i++)
                clips.Add(new Clip { ClipId = $"c{++ci}", AssetId = assetId, SrcIn = R(pieces[i].T0), SrcOut = R(pieces[i].T1), Reframe = reframe, Zoom = i % 2 == 1 ? p.Cut.PunchInScale : 1.0 });
        }
        var cut = ranges.Sum(r => r.Length) - clips.Where(c => c.Role == "body").Sum(c => c.Length);
        if (cut > 0.05) notes.Add($"無音・間を {cut:F1} 秒カット（ジャンプカット）");
        return new PlanResult(clips, notes, feasible);
    }

    static double R(double x) => Math.Round(x, 3);
    static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;
    static double Overlap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    static List<(int a, int b)> MergeRanges(List<(int a, int b)> r)
    {
        var res = new List<(int a, int b)>();
        foreach (var x in r.OrderBy(x => x.a))
            if (res.Count > 0 && x.a <= res[^1].b + 1) res[^1] = (res[^1].a, Math.Max(res[^1].b, x.b)); else res.Add(x);
        return res;
    }

    static double SnapTo(IReadOnlyList<double> points, double t, double window)
    {
        double best = t, bd = window;
        foreach (var x in points) { var d = Math.Abs(x - t); if (d < bd) { bd = d; best = x; } }
        return best;
    }

    /// <summary>Edges for sentences a..b: start in the gap before the first word, end in the gap after the last (H-06),
    /// aligned to a nearby shot boundary when that does not cut a word.</summary>
    public static Interval Edges(Transcript tr, int a, int b, SignalSet sig)
    {
        var s0 = tr.Sentences[a]; var s1 = tr.Sentences[b];
        double first = tr.Words.Count > s0.FirstWord ? tr.Words[s0.FirstWord].T0 : s0.T0;
        double last = tr.Words.Count > s1.LastWord ? tr.Words[s1.LastWord].T1 : s1.T1;
        double prevEnd = s0.FirstWord > 0 && tr.Words.Count > 0 ? tr.Words[s0.FirstWord - 1].T1 : 0;
        double nextStart = s1.LastWord + 1 < tr.Words.Count ? tr.Words[s1.LastWord + 1].T0 : Math.Max(last + 1, tr.Duration);
        double start = Math.Min(first, Math.Max(prevEnd, first - EdgePad));
        double end = Math.Max(last, Math.Min(nextStart, last + EdgePad + 0.08));
        // shot alignment within ±0.3 s, only inside the inter-word gap
        foreach (var sh in sig.Shots)
        {
            if (Math.Abs(sh - start) <= ShotSnap && sh >= prevEnd && sh <= first) start = sh;
            if (Math.Abs(sh - end) <= ShotSnap && sh >= last && sh <= nextStart) end = sh;
        }
        return new Interval(Math.Max(0, start), Math.Max(start + 0.1, end));
    }

    static double EstimatedLength(Transcript tr, int a, int b, Preset p)
    {
        var e = new Interval(tr.Sentences[a].T0, tr.Sentences[b].T1);
        double removable = 0;
        if (p.Cut.RemoveSilenceOverSec is double th)
            foreach (var g in WordGaps(tr, e)) if (g.Length > th) removable += g.Length - 2 * JumpPad;
        return e.Length + 2 * EdgePad - removable;
    }

    static IEnumerable<Interval> WordGaps(Transcript tr, Interval range)
    {
        var ws = tr.Words.Where(w => w.T0 >= range.T0 - 0.01 && w.T1 <= range.T1 + 0.01 && w.T1 > w.T0).OrderBy(w => w.T0).ToList();
        for (int i = 0; i + 1 < ws.Count; i++)
            if (ws[i + 1].T0 > ws[i].T1) yield return new Interval(ws[i].T1, ws[i + 1].T0);
    }

    /// <summary>Remove pauses longer than the preset threshold, keeping a pad on both sides; merge pieces shorter than min shot.</summary>
    public static List<Interval> JumpCut(Interval range, Transcript tr, SignalSet sig, Preset p)
    {
        if (p.Cut.RemoveSilenceOverSec is not double th) return [range];
        var gaps = WordGaps(tr, range).Where(g => g.Length > th).ToList();
        // signal silences inside the range that do not overlap any word
        foreach (var s in sig.Silences.Where(s => s.T0 > range.T0 && s.T1 < range.T1 && s.Length > th))
            if (!tr.Words.Any(w => w.T1 > s.T0 && w.T0 < s.T1) && !gaps.Any(g => Overlap(g.T0, g.T1, s.T0, s.T1) > 0)) gaps.Add(s);
        var pieces = new List<Interval>(); double cur = range.T0;
        foreach (var g in gaps.OrderBy(g => g.T0))
        {
            double cutStart = g.T0 + JumpPad, cutEnd = g.T1 - JumpPad;
            if (cutEnd - cutStart < 0.1) continue;
            pieces.Add(new Interval(cur, cutStart)); cur = cutEnd;
        }
        pieces.Add(new Interval(cur, range.T1));
        // min shot: merge short pieces back into a neighbour (re-include the removed gap)
        bool changed = true;
        while (changed && pieces.Count > 1)
        {
            changed = false;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Length >= p.Cut.MinShotSec) continue;
                int j = i == 0 ? 1 : (i == pieces.Count - 1 ? i - 1 : (pieces[i - 1].Length <= pieces[i + 1].Length ? i - 1 : i + 1));
                int lo = Math.Min(i, j), hi = Math.Max(i, j);
                pieces[lo] = new Interval(pieces[lo].T0, pieces[hi].T1); pieces.RemoveAt(hi);
                changed = true; break;
            }
        }
        return pieces;
    }
}
