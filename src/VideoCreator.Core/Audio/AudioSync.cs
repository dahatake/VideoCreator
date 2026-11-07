using System.Numerics;
using VideoCreator.Media;

namespace VideoCreator.Audio;

public sealed record SyncResult(double Delay, double Confidence)
{
    public const double MinConfidence = 50;          // spike 10: matching audio ≈ 1100+, unrelated audio ≈ 5
    public bool Reliable => Confidence >= MinConfidence;
}

/// <summary>Multi-camera audio alignment with GCC-PHAT (verified in spikes/10-sync.cs: error ≤ 1 ms on known delays).</summary>
public static class AudioSync
{
    /// <summary>Delay d such that other(t) = reference(t − d). cameraTime = masterTime + d.</summary>
    public static SyncResult Gcc(float[] reference, float[] other, int rate, double maxLagSec)
    {
        int n = 1; while (n < reference.Length + other.Length) n <<= 1;
        var A = new Complex[n]; var B = new Complex[n];
        for (int i = 0; i < reference.Length; i++) A[i] = reference[i];
        for (int i = 0; i < other.Length; i++) B[i] = other[i];
        Fft(A, false); Fft(B, false);
        var R = new Complex[n];
        for (int i = 0; i < n; i++) { var x = B[i] * Complex.Conjugate(A[i]); var m = x.Magnitude; R[i] = m < 1e-12 ? Complex.Zero : x / (m + 1e-6); }
        Fft(R, true);
        int ml = Math.Min((int)(maxLagSec * rate), n / 2 - 1);
        double best = double.MinValue, sum = 0; int bi = 0, cnt = 0;
        for (int l = -ml; l <= ml; l++) { double v = R[(l + n) % n].Real; sum += Math.Abs(v); cnt++; if (v > best) { best = v; bi = l; } }
        return new SyncResult((double)bi / rate, best / Math.Max(1e-12, sum / cnt));
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
        await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", .. (start > 0 ? new[] { "-ss", start.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) } : []), "-i", file, "-vn", "-t", (length > 0 ? length : seconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-ac", "1", "-ar", rate.ToString(), "-f", "s16le", "-"], ct, stdout: ms);
        var buf = ms.GetBuffer(); var f = new float[(int)ms.Length / 2];
        for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToInt16(buf, i * 2) / 32768f;
        return f;
    }

    public static async Task<SyncResult> EstimateAsync(FFmpeg ff, string masterFile, string otherFile, CancellationToken ct, double seconds = 90, double maxLag = 20)
    {
        var a = await LoadAsync(ff, masterFile, 8000, seconds, ct);
        var b = await LoadAsync(ff, otherFile, 8000, seconds, ct);
        return Gcc(a, b, 8000, maxLag);
    }
}
