using VideoCreator.Models;
using VideoCreator.Planning;

namespace VideoCreator.Rendering;

/// <summary>
/// Thumbnail candidates (docs/IMPLEMENTATION.md §12): the measured references take their thumbnail line from the moment the audience
/// reaction starts and show the biggest, most expressive face. Here: frames while a punch telop (emphasis / tsukkomi / onomatopoeia / counter)
/// is fully on screen are candidates, ranked by the largest detected face; the opening frame is kept as a third, different option.
/// </summary>
public static class ThumbnailPicker
{
    /// <summary>Seconds after a punch telop starts before it is fully popped in (the pop animation ends at 0.22 s).</summary>
    public const double Settle = 0.45;
    public const int MaxCandidates = 12;
    public const double MinGapSec = 2.0;

    /// <summary>Candidate times on the BODY timeline, in plan order (earlier punch first); evenly spaced fallbacks when there is no punch telop.</summary>
    public static List<double> Candidates(EditPlan plan)
    {
        var d = plan.Duration;
        var punch = plan.Telops.Where(t => t.Enabled && t.Approved && t.Type is TelopType.Emphasis or TelopType.Tsukkomi or TelopType.Sfx or TelopType.Counter)
            .OrderBy(t => t.Start).Select(t => Math.Round(t.Start + Math.Min(Settle, (t.End - t.Start) / 2), 3)).Where(t => t < d - 0.1).ToList();
        var res = new List<double>();
        foreach (var t in punch.Concat([d * 0.25, d * 0.5, d * 0.75]))
            if (res.All(x => Math.Abs(x - t) >= MinGapSec)) res.Add(t);
        return res.Take(MaxCandidates).ToList();
    }

    /// <summary>
    /// Final-timeline times of the 3 thumbnails: the 2 best-scoring candidates (≥ <see cref="MinGapSec"/> apart; ties keep plan order) and the opening frame.
    /// <paramref name="score"/> gets a final-timeline time (e.g. the largest face area there); null = no scorer (plan order).
    /// </summary>
    public static async Task<List<double>> PickAsync(EditPlan plan, Func<double, Task<double>>? score)
    {
        var ins = InsertMap.Active(plan);
        double Final(double t) => InsertMap.ToFinal(ins, t);
        var cands = Candidates(plan).Select(Final).ToList();
        var scored = new List<(double T, double S, int I)>();
        for (int i = 0; i < cands.Count; i++) scored.Add((cands[i], score == null ? 0 : await score(cands[i]), i));
        var best = new List<double>();
        foreach (var c in scored.OrderByDescending(x => x.S).ThenBy(x => x.I))
            if (best.Count < 2 && best.All(b => Math.Abs(b - c.T) >= MinGapSec)) best.Add(c.T);
        var opening = Final(Math.Min(1.0, plan.Duration * 0.1));
        if (best.All(b => Math.Abs(b - opening) >= 0.5)) best.Add(opening);
        return best;
    }
}
