using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>
/// FR-SWT-01 automatic speaker→camera assignment from per-camera microphones: a camera belongs to the speaker who is
/// relatively loudest on that camera's audio. Needs separate mics (a shared room mic gives no signal → returns null, the caller keeps manual defaults).
/// </summary>
public static class SpeakerAssign
{
    public const double MinShare = 0.6;

    /// <param name="cams">per camera: audio aligned to master time (index 0 = master t=0)</param>
    public static int[]? Assign(IReadOnlyList<float[]> cams, int rate, Transcript tr)
    {
        var speakers = tr.Sentences.Select(s => s.Speaker).Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
        if (speakers.Count < cams.Count || cams.Count < 2) return null;
        var share = new double[cams.Count, speakers.Count];
        for (int c = 0; c < cams.Count; c++)
        {
            var e = new double[speakers.Count];
            foreach (var s in tr.Sentences)
            {
                int si = speakers.IndexOf(s.Speaker); if (si < 0) continue;
                int a = Math.Max(0, (int)(s.T0 * rate)), b = Math.Min(cams[c].Length, (int)(s.T1 * rate));
                for (int i = a; i < b; i++) e[si] += (double)cams[c][i] * cams[c][i];
            }
            double tot = e.Sum(); if (tot <= 0) return null;
            for (int k = 0; k < speakers.Count; k++) share[c, k] = e[k] / tot;
        }
        // best one-to-one assignment (few cameras: brute force)
        double bestScore = -1; int[]? best = null;
        void Rec(int c, int[] cur, bool[] used, double score)
        {
            if (c == cams.Count) { if (score > bestScore) { bestScore = score; best = (int[])cur.Clone(); } return; }
            for (int k = 0; k < speakers.Count; k++) if (!used[k]) { used[k] = true; cur[c] = k; Rec(c + 1, cur, used, score + share[c, k]); used[k] = false; }
        }
        Rec(0, new int[cams.Count], new bool[speakers.Count], 0);
        if (best == null) return null;
        for (int c = 0; c < cams.Count; c++) if (share[c, best[c]] < MinShare) return null;
        return best.Select(k => speakers[k]).ToArray();
    }
}
