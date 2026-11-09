using System.Globalization;

namespace VideoCreator.Media;

/// <summary>
/// One step (1/<see cref="ScreenActivity.Fps"/> s) of a recording: the fraction of pixels that changed since the previous step, and where
/// (normalised box of the changed 8×8 blocks; empty when nothing changed). T is the time of the later frame (the asset's own time).
/// </summary>
public sealed record ActivitySample(double T, double Changed, double X0, double Y0, double X1, double Y1)
{
    public bool Active => Changed >= ScreenActivity.ActiveFraction;
    public double Cx => (X0 + X1) / 2;
    public double Cy => (Y0 + Y1) / 2;
    /// <summary>A change over most of the picture (slide change, window switch) says when, not where.</summary>
    public bool Global => (X1 - X0) * (Y1 - Y0) > 0.5;
}

/// <summary>How busy each cell of a <see cref="ScreenActivity.GridW"/>×<see cref="ScreenActivity.GridH"/> grid is (mean luma gradient, 0–255): text and UI are busy, empty areas are not.</summary>
public sealed record DetailSample(double T, byte[] Grid);

public sealed record ScreenAnalysis(List<ActivitySample> Samples, List<DetailSample> Details);

/// <summary>
/// Where and when a recording changes. A screen recording is mostly still (the picture changes only where the user types, clicks, scrolls or a
/// slide changes); a camera is never still. Used to tell the two apart and to decide when the screen deserves the frame and where to zoom.
/// </summary>
public static class ScreenActivity
{
    public const double Fps = 2;
    public const int W = 640, H = 360;
    /// <summary>Luma difference that counts as a change (proxy compression noise on a still screen stays below it).</summary>
    public const int PixelThreshold = 18;
    const int Block = 8, BlockMinPixels = 3;
    /// <summary>≈ 12 px of 640×360 = one typed character of a 14–34 px font (measured: 19–50 px per character on a 1080p screen); a still screen: 0–2 px.</summary>
    public const double ActiveFraction = 0.00005;

    public static async Task<ScreenAnalysis> AnalyzeAsync(FFmpeg ff, string video, string cacheFile, CancellationToken ct, bool refresh = false)
    {
        if (!refresh && File.Exists(cacheFile)) return Json.Deserialize<ScreenAnalysis>(await File.ReadAllTextAsync(cacheFile, ct));
        var sink = new FrameSink();
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-i", video, "-an",
            "-vf", $"fps={Fps.ToString(CultureInfo.InvariantCulture)},scale={W}:{H}:flags=area,format=gray", "-f", "rawvideo", "-pix_fmt", "gray", "-"], ct, stdout: sink);
        if (r.ExitCode != 0) throw new FFmpegException("画面の動きの解析に失敗しました: " + FFmpeg.Tail(r.StdErr));
        var res = new ScreenAnalysis(sink.Samples, sink.Details);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        await File.WriteAllTextAsync(cacheFile, Json.Serialize(res), ct);
        return res;
    }

    public const int GridW = 16, GridH = 9;
    /// <summary>A detail map every this many steps (2 s).</summary>
    public const int DetailEvery = 4;

    /// <summary>Mean detail (0–1) of the normalised source rectangle in the detail map nearest to <paramref name="t"/>; 0 when there is none.</summary>
    public static double DetailIn(IReadOnlyList<DetailSample> d, double t, double x0, double y0, double x1, double y1)
    {
        if (d.Count == 0) return 0;
        int lo = 0, hi = d.Count - 1;
        while (lo < hi) { int m = (lo + hi) / 2; if (d[m].T < t) lo = m + 1; else hi = m; }
        if (lo > 0 && Math.Abs(d[lo - 1].T - t) < Math.Abs(d[lo].T - t)) lo--;
        var g = d[lo].Grid; double sum = 0, area = 0;
        for (int gy = 0; gy < GridH; gy++)
            for (int gx = 0; gx < GridW; gx++)
            {
                double ix = Math.Max(0, Math.Min(x1, (gx + 1.0) / GridW) - Math.Max(x0, (double)gx / GridW));
                double iy = Math.Max(0, Math.Min(y1, (gy + 1.0) / GridH) - Math.Max(y0, (double)gy / GridH));
                sum += ix * iy * g[gy * GridW + gx]; area += ix * iy;
            }
        return area > 0 ? sum / area / 255 : 0;
    }

    /// <summary>Share of steps in which (almost) nothing changed. Screen recordings: typically ≥ 0.5; cameras: ≈ 0.</summary>
    public static double StaticRatio(IReadOnlyList<ActivitySample> s) => s.Count == 0 ? 0 : s.Count(x => !x.Active) / (double)s.Count;

    public static IEnumerable<ActivitySample> Between(IReadOnlyList<ActivitySample> s, double t0, double t1)
    {
        int i = LowerBound(s, t0);
        for (; i < s.Count && s[i].T <= t1; i++) yield return s[i];
    }

    public static bool AnyActive(IReadOnlyList<ActivitySample> s, double t0, double t1)
    {
        for (int i = LowerBound(s, t0); i < s.Count && s[i].T <= t1; i++) if (s[i].Active) return true;
        return false;
    }

    /// <summary>The last active sample at or before <paramref name="t"/> within <paramref name="lookback"/> seconds.</summary>
    public static ActivitySample? LastActive(IReadOnlyList<ActivitySample> s, double t, double lookback)
    {
        int i = LowerBound(s, t + 1e-9) - 1;
        for (; i >= 0 && s[i].T >= t - lookback; i--) if (s[i].Active) return s[i];
        return null;
    }

    static int LowerBound(IReadOnlyList<ActivitySample> s, double t)
    {
        int lo = 0, hi = s.Count;
        while (lo < hi) { int m = (lo + hi) / 2; if (s[m].T < t) lo = m + 1; else hi = m; }
        return lo;
    }

    /// <summary>
    /// Compares each gray frame with the previous one as the bytes arrive (no frame list in memory). A pixel counts only when it also differs from the
    /// frame two steps back: a blinking text caret or a hover highlight that toggles back is not activity; typing, pointer movement and scrolling are.
    /// </summary>
    public sealed class FrameSink : Stream
    {
        public readonly List<ActivitySample> Samples = [];
        public readonly List<DetailSample> Details = [];
        readonly byte[] _cur = new byte[W * H], _prev = new byte[W * H], _prev2 = new byte[W * H];
        readonly int[] _blocks = new int[(W / Block) * (H / Block)];
        int _fill, _frames;

        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int n = Math.Min(count, _cur.Length - _fill);
                Buffer.BlockCopy(buffer, offset, _cur, _fill, n);
                _fill += n; offset += n; count -= n;
                if (_fill == _cur.Length) { Frame(); _fill = 0; }
            }
        }

        void Frame()
        {
            if (_frames % DetailEvery == 0) Details.Add(new DetailSample(Math.Round(_frames / Fps, 3), DetailGrid(_cur)));
            if (_frames++ > 0)
            {
                Array.Clear(_blocks);
                int changed = 0, bw = W / Block;
                bool two = _frames > 2;
                for (int y = 0; y < H; y++)
                {
                    int row = y * W, brow = (y / Block) * bw;
                    for (int x = 0; x < W; x++)
                    {
                        int i = row + x;
                        if (Math.Abs(_cur[i] - _prev[i]) > PixelThreshold && (!two || Math.Abs(_cur[i] - _prev2[i]) > PixelThreshold)) { changed++; _blocks[brow + x / Block]++; }
                    }
                }
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                for (int b = 0; b < _blocks.Length; b++)
                {
                    if (_blocks[b] < BlockMinPixels) continue;
                    int bx = b % bw, by = b / bw;
                    x0 = Math.Min(x0, bx); y0 = Math.Min(y0, by); x1 = Math.Max(x1, bx + 1); y1 = Math.Max(y1, by + 1);
                }
                double t = Math.Round((_frames - 1) / Fps, 3), frac = Math.Round(changed / (double)(W * H), 6);
                Samples.Add(x1 < 0 ? new ActivitySample(t, frac, 0, 0, 0, 0)
                    : new ActivitySample(t, frac, Math.Round(x0 * Block / (double)W, 4), Math.Round(y0 * Block / (double)H, 4), Math.Round(x1 * Block / (double)W, 4), Math.Round(y1 * Block / (double)H, 4)));
            }
            Buffer.BlockCopy(_prev, 0, _prev2, 0, _cur.Length);
            Buffer.BlockCopy(_cur, 0, _prev, 0, _cur.Length);
        }

        /// <summary>Mean |horizontal| + |vertical| luma gradient per grid cell, scaled ×4 and clamped to a byte.</summary>
        static byte[] DetailGrid(byte[] f)
        {
            var sum = new double[GridW * GridH]; var n = new int[GridW * GridH];
            for (int y = 0; y < H - 1; y += 2)
                for (int x = 0; x < W - 1; x += 2)
                {
                    int i = y * W + x, c = (y * GridH / H) * GridW + x * GridW / W;
                    sum[c] += Math.Abs(f[i] - f[i + 1]) + Math.Abs(f[i] - f[i + W]); n[c]++;
                }
            return sum.Select((s, k) => (byte)Math.Clamp(s / Math.Max(1, n[k]) * 4, 0, 255)).ToArray();
        }

        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}
