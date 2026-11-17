using VideoCreator.Models;

namespace VideoCreator.Media;

/// <summary>Visual scene candidates, not a semantic PowerPoint classifier. Eligibility must be confirmed by the editor.</summary>
public static class ScreenScenes
{
    public static List<Interval> Detect(ScreenAnalysis analysis, double duration)
    {
        var cuts = new List<double> { 0 };
        foreach (var sample in analysis.Samples.Where(s => s.Global && s.Changed >= 0.08).OrderBy(s => s.T))
        {
            if (sample.T - cuts[^1] >= 1 && sample.T <= duration - 1) cuts.Add(sample.T);
        }
        cuts.Add(duration);
        return cuts.Zip(cuts.Skip(1), (a, b) => new Interval(a, b)).ToList();
    }

    public static bool Valid(IReadOnlyList<Interval> scenes, double duration) =>
        scenes.Count <= 500 && scenes.All(s => s != null && double.IsFinite(s.T0) && double.IsFinite(s.T1) && s.T0 >= 0 && s.T1 > s.T0 && s.T1 <= duration)
        && scenes.OrderBy(s => s.T0).Zip(scenes.OrderBy(s => s.T0).Skip(1)).All(p => p.First.T1 <= p.Second.T0);

    const int GW = 640, GH = 360;
    /// <summary>Luma at or below this is the meeting app's dark background, not content.</summary>
    public const int DarkLuma = 24;
    /// <summary>A column/row is content when this share of it is lit; small gaps (≤ <see cref="MaxGap"/> px of 640) inside the content are bridged.</summary>
    const double LitShare = 0.25;
    const int MaxGap = 6;

    /// <summary>
    /// The largest lit block of a gray <see cref="GW"/>×<see cref="GH"/> frame, as a normalised (x0, y0, x1, y1): the shared window or slide in a meeting
    /// recording, without the black margins and the participant tile beside it (separated from it by dark columns). Null when the frame has no
    /// dominant block (a dark slide, a full-screen desktop) — then the whole frame is used.
    /// </summary>
    public static (double X0, double Y0, double X1, double Y1)? ContentBox(byte[] gray)
    {
        var cols = new double[GW];
        for (int x = 0; x < GW; x++) { int n = 0; for (int y = 0; y < GH; y++) if (gray[y * GW + x] > DarkLuma) n++; cols[x] = n / (double)GH; }
        var (x0, x1) = LongestRun(cols, LitShare);
        if (x1 - x0 < GW * 0.3) return null;
        var rows = new double[GH];
        for (int y = 0; y < GH; y++) { int n = 0; for (int x = x0; x < x1; x++) if (gray[y * GW + x] > DarkLuma) n++; rows[y] = n / (double)(x1 - x0); }
        var (y0, y1) = LongestRun(rows, LitShare);
        if (y1 - y0 < GH * 0.3 || (x1 - x0) * (double)(y1 - y0) < 0.3 * GW * GH) return null;
        return ((double)x0 / GW, (double)y0 / GH, (double)x1 / GW, (double)y1 / GH);
    }

    static (int Lo, int Hi) LongestRun(double[] v, double min)
    {
        int bestLo = 0, bestHi = 0, lo = -1, last = -1;
        for (int i = 0; i <= v.Length; i++)
        {
            bool on = i < v.Length && v[i] >= min;
            if (on && lo >= 0 && i - last - 1 > MaxGap) lo = -1;
            if (!on && (i == v.Length || i - last > MaxGap) && lo >= 0) { if (last + 1 - lo > bestHi - bestLo) (bestLo, bestHi) = (lo, last + 1); lo = -1; }
            if (on) { if (lo < 0) lo = i; last = i; }
        }
        return (bestLo, bestHi);
    }

    /// <summary>Union of the content boxes at the middle of every selected scene, in source pixels; null when any scene has no clear block (stable inset across scenes).</summary>
    public static async Task<PixelRect?> ContentAsync(FFmpeg ff, SourceAsset screen, CancellationToken ct)
    {
        double x0 = 1, y0 = 1, x1 = 0, y1 = 0;
        foreach (var s in screen.PipScenes)
        {
            using var ms = new MemoryStream();
            var r = await ff.RunFfmpegAsync(["-v", "error", "-nostdin", "-ss", ((s.T0 + s.T1) / 2).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-i", screen.Path,
                "-map", "0:v:0", "-an", "-frames:v", "1", "-vf", $"scale={GW}:{GH}:flags=area,format=gray", "-f", "rawvideo", "-pix_fmt", "gray", "-"], ct, stdout: ms);
            if (r.ExitCode != 0 || ms.Length < GW * GH || ContentBox(ms.ToArray()) is not { } b) return null;
            (x0, y0, x1, y1) = (Math.Min(x0, b.X0), Math.Min(y0, b.Y0), Math.Max(x1, b.X1), Math.Max(y1, b.Y1));
        }
        if (x1 <= x0 || y1 <= y0) return null;
        int W = screen.Media.Width, H = screen.Media.Height;
        int px = (int)(x0 * W) & ~1, py = (int)(y0 * H) & ~1, pw = Math.Min(W - px, (int)Math.Ceiling((x1 - x0) * W)) & ~1, ph = Math.Min(H - py, (int)Math.Ceiling((y1 - y0) * H)) & ~1;
        return pw * ph >= 0.95 * W * H ? null : new PixelRect(px, py, pw, ph);
    }
}
