using System.Globalization;
using System.Numerics;
using System.Text.Json;
using VideoCreator.Media;

namespace VideoCreator.Audio;

public sealed record SyncResult(double Delay, double Confidence)
{
    public const double MinConfidence = 50;          // spike 10: matching audio ≈ 1100+, unrelated audio ≈ 5
    /// <summary>Stored as the confidence of an offset the user typed in (trusted, never re-measured).</summary>
    public const double Manual = 9999;
    public bool Reliable => Confidence >= MinConfidence;
}

/// <summary>
/// Where another recording sits on the master's timeline: cameraTime = masterTime + <see cref="Offset"/> + <see cref="DriftPpm"/>·1e-6·masterTime.
/// Drift is the two devices' clock-rate difference (consumer cameras: tens of ppm, i.e. tens of ms over an hour).
/// </summary>
public sealed record SyncFit
{
    public double Offset { get; init; }
    public double DriftPpm { get; init; }
    /// <summary>Median GCC-PHAT peak-to-mean ratio of the windows that agree (same scale as <see cref="SyncResult.Confidence"/>).</summary>
    public double Confidence { get; init; }
    /// <summary>Windows measured / windows that agree with the fit.</summary>
    public int Windows { get; init; }
    public int Agreeing { get; init; }
    /// <summary>Median |window offset − fit| in ms: the internal consistency of the result.</summary>
    public double ResidualMs { get; init; }
    /// <summary>audio | audio-coarse | metadata | none</summary>
    public string Method { get; init; } = "audio";
    /// <summary>
    /// Several independent windows on one line (random peaks of unrelated audio do not line up within milliseconds), or one very clear peak.
    /// Peak height alone is not used: clock drift smears it (measured: 200 ppm over a 30 s window → ratio 14–70) while the offset stays within 2 ms.
    /// </summary>
    public bool Reliable => Method == "audio" && Agreeing * 2 >= Windows && (Agreeing >= 3 || Confidence >= SyncResult.MinConfidence);
    public double At(double masterT) => Offset + DriftPpm * 1e-6 * masterT;
}

/// <summary>
/// Multi-camera alignment. Single-window GCC-PHAT was verified in spikes/10-sync.cs (error ≤ 1 ms on known delays); <see cref="AlignAsync"/> adds what
/// real recordings need: a full-length coarse search (cameras started minutes apart), PCM on the file's TIMESTAMP timeline (audio streams that start
/// after the video, gaps), several fine windows with sub-sample peaks, and a robust drift fit.
/// </summary>
public static class AudioSync
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static string F(double x) => x.ToString("0.###", IC);

    /// <summary>
    /// Decoded samples are re-timed to the stream's timestamps, starting at file time 0: a late-starting audio stream is padded and gaps are filled,
    /// so sample i is file time i/rate — the same timeline that trim/atrim cut on. Without this, a stream starting 0.28 s after its video
    /// (measured on an AAC/MP4 file) shifts the estimate by 0.28 s while still looking "reliable".
    /// </summary>
    public const string TimelineFilter = "aresample=async=1:first_pts=0";

    /// <summary>Delay d such that other(t) = reference(t − d). cameraTime = masterTime + d. The peak is refined to sub-sample precision.</summary>
    public static SyncResult Gcc(float[] reference, float[] other, int rate, double maxLagSec)
    {
        var (lag, peak, meanAbs, _) = GccCore(reference, other, rate, maxLagSec);
        return new SyncResult(lag / rate, peak / Math.Max(1e-12, meanAbs));
    }

    /// <returns>lag in samples (parabolic sub-sample peak), peak value, mean |R| and std of R over the searched lags.</returns>
    static (double Lag, double Peak, double MeanAbs, double Std) GccCore(float[] reference, float[] other, int rate, double maxLagSec)
    {
        int n = 1; while (n < reference.Length + other.Length) n <<= 1;
        var A = new Complex[n]; var B = new Complex[n];
        for (int i = 0; i < reference.Length; i++) A[i] = reference[i];
        for (int i = 0; i < other.Length; i++) B[i] = other[i];
        Fft(A, false); Fft(B, false);
        var R = A;   // reuse
        for (int i = 0; i < n; i++) { var x = B[i] * Complex.Conjugate(A[i]); var m = x.Magnitude; R[i] = m < 1e-12 ? Complex.Zero : x / (m + 1e-6); }
        Fft(R, true);
        // lags in [−(|ref|−1), |other|−1] are unambiguous because n ≥ |ref| + |other| (a short clip that starts late in a long master needs the full range)
        int lneg = Math.Min((int)(maxLagSec * rate), reference.Length - 1), lpos = Math.Min((int)(maxLagSec * rate), other.Length - 1);
        double best = double.MinValue, sumAbs = 0, sum = 0, sum2 = 0; int bi = 0, cnt = 0;
        for (int l = -lneg; l <= lpos; l++) { double v = R[(l + n) % n].Real; sumAbs += Math.Abs(v); sum += v; sum2 += v * v; cnt++; if (v > best) { best = v; bi = l; } }
        double y0 = R[(bi - 1 + n) % n].Real, y2 = R[(bi + 1 + n) % n].Real, den = y0 - 2 * best + y2;
        double frac = bi > -lneg && bi < lpos && den < 0 ? Math.Clamp(0.5 * (y0 - y2) / den, -0.5, 0.5) : 0;
        double mean = sum / cnt;
        return (bi + frac, best, sumAbs / cnt, Math.Sqrt(Math.Max(0, sum2 / cnt - mean * mean)));
    }

    static void Fft(Complex[] a, bool inverse)
    {
        int n = a.Length;
        for (int i = 1, j = 0; i < n; i++) { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) (a[i], a[j]) = (a[j], a[i]); }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inverse ? -1 : 1); var wl = new Complex(Math.Cos(ang), -Math.Sin(ang));
            for (int i = 0; i < n; i += len)
            {
                Complex w = 1;
                for (int j = 0; j < len / 2; j++) { var u = a[i + j]; var v = a[i + j + len / 2] * w; a[i + j] = u + v; a[i + j + len / 2] = u - v; w *= wl; }
            }
        }
        if (inverse) for (int i = 0; i < n; i++) a[i] /= n;
    }

    public static async Task<float[]> LoadAsync(FFmpeg ff, string file, int rate, double seconds, CancellationToken ct, double start = 0, double length = 0)
    {
        using var ms = new MemoryStream();
        double len = length > 0 ? length : seconds;
        await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-nostdin", .. (start > 0 ? new[] { "-ss", F(start) } : []), "-i", file, "-vn", "-af", TimelineFilter,
            .. (len > 0 ? new[] { "-t", F(len) } : []), "-ac", "1", "-ar", rate.ToString(IC), "-f", "s16le", "-"], ct, stdout: ms);
        var buf = ms.GetBuffer(); var f = new float[(int)ms.Length / 2];
        for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToInt16(buf, i * 2) / 32768f;
        return f;
    }

    /// <summary>Single-window estimate on the first <paramref name="seconds"/> (kept for small inputs and tests; the pipeline uses <see cref="AlignAsync"/>).</summary>
    public static async Task<SyncResult> EstimateAsync(FFmpeg ff, string masterFile, string otherFile, CancellationToken ct, double seconds = 90, double maxLag = 20)
    {
        var a = await LoadAsync(ff, masterFile, 8000, seconds, ct);
        var b = await LoadAsync(ff, otherFile, 8000, seconds, ct);
        return Gcc(a, b, 8000, maxLag);
    }

    // ---- full alignment ----

    public const int EnvelopeRate = 50;           // 20 ms frames: a 3-hour file is 540 k values
    const int EnvelopeDecodeRate = 4000;
    public const double CoarseMinZ = 8;           // GCC peak in standard deviations; unrelated audio peaks at ≈ 5 over ~10^6 lags
    /// <summary>10 s: short enough that 200 ppm of drift moves the delay only 2 ms inside a window (measured: peak ratio 23–100 vs 14–70 at 30 s).</summary>
    public const double WindowSec = 10;
    public const int MaxWindows = 24;
    /// <summary>Per-window gate before the agreement test (unrelated audio: ≈ 5).</summary>
    public const double WindowMinConfidence = 10;
    public const int FineRate = 8000;
    /// <summary>Drift below this (over the measured span) is noise, not a clock difference.</summary>
    public const double MinDriftEffectSec = 0.002;
    public const double MaxPlausibleDriftPpm = 1000;

    /// <summary>Log-energy envelope at <see cref="EnvelopeRate"/> Hz with the slow level removed (onset-like), from the whole file, streamed (no full PCM in memory).</summary>
    public static async Task<float[]> EnvelopeAsync(FFmpeg ff, string file, CancellationToken ct)
    {
        var sink = new EnvelopeSink(EnvelopeDecodeRate / EnvelopeRate);
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-i", file, "-vn", "-af", TimelineFilter, "-ac", "1", "-ar", EnvelopeDecodeRate.ToString(IC), "-f", "s16le", "-"], ct, stdout: sink);
        if (r.ExitCode != 0) throw new FFmpegException("同期用の音声を読み込めませんでした: " + FFmpeg.Tail(r.StdErr));
        var e = sink.Values;
        int half = EnvelopeRate / 2;
        var res = new float[e.Count];
        double acc = 0; int lo = 0, hi = -1;
        for (int i = 0; i < e.Count; i++)
        {
            while (hi < Math.Min(e.Count - 1, i + half)) acc += e[++hi];
            while (lo < i - half) acc -= e[lo++];
            res[i] = e[i] < -9 ? 0 : (float)(e[i] - acc / (hi - lo + 1));   // digital silence carries no timing information
        }
        return res;
    }

    sealed class EnvelopeSink(int hop) : Stream
    {
        public readonly List<float> Values = [];
        double _acc; int _n; int _odd = -1;
        public override void Write(byte[] buffer, int offset, int count)
        {
            int i = offset, end = offset + count;
            if (_odd >= 0 && i < end) { Add((short)(_odd | (buffer[i++] << 8))); _odd = -1; }
            for (; i + 1 < end; i += 2) Add((short)(buffer[i] | (buffer[i + 1] << 8)));
            if (i < end) _odd = buffer[i];
        }
        void Add(short s)
        {
            double v = s / 32768.0; _acc += v * v;
            if (++_n == hop) { Values.Add((float)Math.Log10(_acc / hop + 1e-10)); _acc = 0; _n = 0; }
        }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }

    /// <summary>Coarse offset from whole-file envelopes: any start difference that leaves ≥ 5 s of overlap. Returns (offset, z-score of the peak).</summary>
    public static (double Offset, double Z) Coarse(float[] master, float[] other)
    {
        var (lag, peak, _, std) = GccCore(master, other, EnvelopeRate, (master.Length + other.Length) / (double)EnvelopeRate);
        return (lag / EnvelopeRate, std > 0 ? peak / std : 0);
    }

    /// <summary>
    /// Aligns <paramref name="otherFile"/> to <paramref name="masterFile"/>: coarse envelope search over the full length, then GCC-PHAT on up to 12 windows of
    /// <see cref="WindowSec"/> spread over the overlap (8 kHz, sub-sample peak), then a Theil–Sen line through the window offsets (robust to windows
    /// without shared sound). The fit's residual is reported so the result can be judged, not just trusted.
    /// </summary>
    public static async Task<SyncFit> AlignAsync(FFmpeg ff, string masterFile, double masterDur, string otherFile, double otherDur, CancellationToken ct)
    {
        var em = await EnvelopeAsync(ff, masterFile, ct);
        var eo = await EnvelopeAsync(ff, otherFile, ct);
        var (d0, z) = Coarse(em, eo);
        double lo = Math.Max(0, -d0), hi = Math.Min(masterDur, otherDur - d0);
        if (hi - lo < 5) return new SyncFit { Offset = d0, Confidence = 0, Method = "none" };

        double w = Math.Min(WindowSec, hi - lo), span = hi - lo;
        int k = span < 2 * w ? 1 : Math.Clamp((int)(span / 30) + 2, 3, MaxWindows);
        double margin = 0.3 + 1e-4 * span;   // coarse step (20 ms) + up to 100 ppm of drift across the overlap
        var centres = Enumerable.Range(0, k).Select(i => k == 1 ? (lo + hi) / 2 : lo + w / 2 + (span - w) * i / (k - 1)).ToList();
        using var gate = new SemaphoreSlim(4);
        var measured = await Task.WhenAll(centres.Select(async c =>
        {
            await gate.WaitAsync(ct);
            try
            {
                double a = Math.Max(0, c - w / 2), b = Math.Max(0, a + d0 - margin);
                var ref_ = await LoadAsync(ff, masterFile, FineRate, 0, ct, a, w);
                var oth = await LoadAsync(ff, otherFile, FineRate, 0, ct, b, w + 2 * margin);
                if (ref_.Length < FineRate || oth.Length < FineRate) return ((double T, double D, double Conf)?)null;
                var g = Gcc(ref_, oth, FineRate, 2 * margin + 0.05);
                return (a + w / 2, b - a + g.Delay, g.Confidence);
            }
            finally { gate.Release(); }
        }));
        var pts = measured.Where(p => p != null).Select(p => p!.Value).ToList();
        var good = pts.Where(p => p.Conf >= WindowMinConfidence).ToList();
        if (good.Count == 0)
            return new SyncFit { Offset = d0, Confidence = pts.Count > 0 ? pts.Max(p => p.Conf) : 0, Windows = pts.Count, Method = z >= CoarseMinZ ? "audio-coarse" : "none" };

        var (off, ppm) = Fit(good);
        // windows off the line (repeated sounds, a wrong peak, unrelated audio) do not count; refit on the agreeing ones
        bool Agrees((double T, double D, double Conf) p, double o, double r) => Math.Abs(p.D - (o + r * 1e-6 * p.T)) <= AgreeSec;
        var keep = good.Where(p => Agrees(p, off, ppm)).ToList();
        if (keep.Count >= 1 && keep.Count < good.Count) { (off, ppm) = Fit(keep); keep = good.Where(p => Agrees(p, off, ppm)).ToList(); }
        if (keep.Count == 0) keep = [good.MaxBy(p => p.Conf)];
        var resid = keep.Select(p => Math.Abs(p.D - (off + ppm * 1e-6 * p.T))).ToList();
        return new SyncFit
        {
            Offset = Math.Round(off, 4), DriftPpm = Math.Round(ppm, 1), Confidence = Math.Round(Median(keep.Select(p => p.Conf).ToList()), 1),
            Windows = good.Count, Agreeing = keep.Count, ResidualMs = Math.Round(Median(resid) * 1000, 2), Method = "audio",
        };
    }

    /// <summary>A window agrees with the fit within a quarter frame at 30 fps.</summary>
    public const double AgreeSec = 0.008;

    /// <summary>Theil–Sen line d(t) = offset + ppm·1e-6·t; a drift too small to matter over the measured span (or implausibly large) is dropped.</summary>
    public static (double Offset, double Ppm) Fit(IReadOnlyList<(double T, double D, double Conf)> pts)
    {
        double span = pts.Count == 0 ? 0 : pts.Max(p => p.T) - pts.Min(p => p.T);
        if (pts.Count < 3 || span < 60) return (Median(pts.Select(p => p.D).ToList()), 0);
        var slopes = new List<double>();
        for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++) if (pts[j].T - pts[i].T > 1) slopes.Add((pts[j].D - pts[i].D) / (pts[j].T - pts[i].T));
        double s = Median(slopes);
        if (Math.Abs(s) * span < MinDriftEffectSec || Math.Abs(s) * 1e6 > MaxPlausibleDriftPpm) return (Median(pts.Select(p => p.D).ToList()), 0);
        return (Median(pts.Select(p => p.D - s * p.T).ToList()), s * 1e6);
    }

    static double Median(List<double> v)
    {
        if (v.Count == 0) return 0;
        var o = v.OrderBy(x => x).ToList(); int m = o.Count / 2;
        return o.Count % 2 == 1 ? o[m] : (o[m - 1] + o[m]) / 2;
    }

    /// <summary>Recording start (UTC) from container metadata, when present. Second precision on most devices — a last resort for recordings without audio.</summary>
    public static async Task<DateTimeOffset?> CreationTimeAsync(FFmpeg ff, string file, CancellationToken ct)
    {
        var r = await ff.RunProbeAsync(["-v", "error", "-show_entries", "format_tags=creation_time", "-of", "json", file], ct);
        if (r.ExitCode != 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            if (doc.RootElement.TryGetProperty("format", out var f) && f.TryGetProperty("tags", out var t) && t.TryGetProperty("creation_time", out var c)
                && DateTimeOffset.TryParse(c.GetString(), IC, DateTimeStyles.AssumeUniversal, out var dt) && dt.Year > 2000) return dt;
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Offset from both files' creation times (cameraTime = masterTime + (masterStart − otherStart)); null when either is missing or they do not overlap.</summary>
    public static async Task<SyncFit?> FromMetadataAsync(FFmpeg ff, string masterFile, double masterDur, string otherFile, double otherDur, CancellationToken ct)
    {
        if (await CreationTimeAsync(ff, masterFile, ct) is not { } m || await CreationTimeAsync(ff, otherFile, ct) is not { } o) return null;
        double d = (m - o).TotalSeconds;
        if (Math.Min(masterDur, otherDur - d) - Math.Max(0, -d) < 5) return null;
        return new SyncFit { Offset = Math.Round(d, 3), Confidence = 0, Method = "metadata" };
    }
}
