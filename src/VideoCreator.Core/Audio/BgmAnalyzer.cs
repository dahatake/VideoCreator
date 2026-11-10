using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using VideoCreator.Media;
using VideoCreator.Models;

namespace VideoCreator.Audio;

/// <summary>
/// Measures a BGM track once (at registration): loudness per ITU-R BS.1770 / EBU R128 from FFmpeg's ebur128 meter (100 ms frames of the 400 ms
/// momentary and 3 s short-term loudness), the playable part, and the features its energy is judged from — tempo, note-attack rate (log-spectral-flux
/// onsets, Dixon 2006) and spectral centroid. Arousal is what audio features predict well; mood words are left to the user.
/// </summary>
public static partial class BgmAnalyzer
{
    public const int Version = 4;
    /// <summary>Minimum log-spectral-flux rise (above the local mean) that counts as a note attack.</summary>
    public const double OnsetFloor = 40;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    /// <summary>A part of the file quieter than this below the track's loudness is silence / fade (skipped at the start and end).</summary>
    public const double SilenceLu = 20;
    const int Fs = 16000, Hop = 160, Win = 512;

    public sealed record Frame(double T, double M, double S);

    [GeneratedRegex(@"\[(?<n>[^\]@]+(?:@(?<i>\w+))?) @ [^\]]+\]\s*t:\s*(?<t>[\d.]+)\s+TARGET:\S+\s+LUFS\s+M:\s*(?<m>-?[\d.]+|-inf|nan)\s+S:\s*(?<s>-?[\d.]+|-inf|nan)")]
    private static partial Regex FrameRe();

    /// <summary>ebur128 frame log lines (framelog=info), optionally only those of the named instance (<c>ebur128@name</c>).</summary>
    public static List<Frame> ParseFrames(string stderr, string? instance = null)
    {
        var res = new List<Frame>();
        foreach (Match m in FrameRe().Matches(stderr))
        {
            if (instance != null && m.Groups["i"].Value != instance) continue;
            res.Add(new Frame(double.Parse(m.Groups["t"].Value, IC), Num(m.Groups["m"].Value), Num(m.Groups["s"].Value)));
        }
        return res;
    }

    static double Num(string s) => double.TryParse(s, NumberStyles.Float, IC, out var v) && double.IsFinite(v) ? v : -120;

    /// <summary>
    /// BS.1770 integrated loudness of a set of 400 ms momentary blocks (ebur128 frames are exactly the standard's 75 %-overlapped blocks):
    /// absolute gate −70 LUFS, then relative gate −10 LU. NaN when nothing passes.
    /// </summary>
    public static double Integrated(IEnumerable<double> momentary)
    {
        var abs = momentary.Where(m => m > -70).ToList();
        if (abs.Count == 0) return double.NaN;
        double rel = PowerMean(abs) - 10;
        var gated = abs.Where(m => m > rel).ToList();
        return gated.Count == 0 ? double.NaN : PowerMean(gated);
    }

    public static double PowerMean(IReadOnlyCollection<double> lufs) => 10 * Math.Log10(lufs.Average(l => Math.Pow(10, l / 10)));

    /// <summary>EBU Tech 3342 loudness range from short-term values: −70 absolute and −20 LU relative gate, 10th–95th percentile.</summary>
    public static double Range(IEnumerable<double> shortTerm)
    {
        var abs = shortTerm.Where(s => s > -70).ToList();
        if (abs.Count < 2) return 0;
        double rel = PowerMean(abs) - 20;
        var g = abs.Where(s => s > rel).OrderBy(s => s).ToList();
        if (g.Count < 2) return 0;
        double P(double q) => g[(int)Math.Round(q * (g.Count - 1))];
        return P(0.95) - P(0.10);
    }

    /// <summary>Energy 0..1 on fixed scales, so a track's value does not depend on what else is registered: tempo 70→150 BPM (unknown = middle),
    /// attacks 1→7 per second, centroid 150→3000 Hz (magnitude-weighted, ≤ 8 kHz band). Weights favour tempo and attack rate, the strongest audio correlates of arousal.</summary>
    public static double Energy(double bpm, double onsetRate, double centroid)
    {
        static double C(double x) => Math.Clamp(x, 0, 1);
        double t = bpm > 0 ? C((bpm - 70) / 80) : onsetRate < 0.5 ? 0 : 0.5;
        return Math.Round(0.4 * t + 0.35 * C((onsetRate - 1) / 6) + 0.25 * C((centroid - 150) / 2850), 3);
    }

    public static async Task<BgmAnalysis> AnalyzeAsync(FFmpeg ff, string file, CancellationToken ct, double? knownBpm = null)
    {
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-nostats", "-i", file, "-vn", "-af", "ebur128=framelog=info", "-f", "null", "-"], ct);
        var frames = ParseFrames(r.StdErr);
        if (frames.Count < 10) throw new FFmpegException("BGM の音量を測れませんでした: " + FFmpeg.Tail(r.StdErr));
        double duration = frames[^1].T;
        double whole = Integrated(frames.Select(f => f.M));
        if (double.IsNaN(whole)) throw new FFmpegException("BGM がほぼ無音です");
        // playable part: from the first to the last 400 ms block within SilenceLu of the track's loudness
        int first = frames.FindIndex(f => f.M >= whole - SilenceLu), last = frames.FindLastIndex(f => f.M >= whole - SilenceLu);
        double start = Math.Max(0, frames[first].T - 0.4), end = frames[last].T;
        if (end - start < 5) { start = 0; end = duration; }
        var inside = frames.Where(f => f.T - 0.4 >= start - 1e-6 && f.T <= end + 1e-6).ToList();
        double lufs = Integrated(inside.Select(f => f.M));
        double lra = Range(inside.Where(f => f.T >= start + 3).Select(f => f.S));
        const double step = 0.5;
        var profile = new List<double>();
        for (double t = step; t <= duration + 1e-6; t += step)
        {
            var f = frames[Math.Clamp((int)Math.Round(t / 0.1) - 1, 0, frames.Count - 1)];
            profile.Add(Math.Round(f.S, 1));
        }

        double len = Math.Min(end - start, 120);
        using var ms = new MemoryStream();
        await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", F(start), "-t", F(len), "-i", file, "-vn", "-ac", "1", "-ar", Fs.ToString(IC), "-f", "s16le", "-"], ct, stdout: ms);
        var pcm = new short[(int)ms.Length / 2];
        Buffer.BlockCopy(ms.GetBuffer(), 0, pcm, 0, pcm.Length * 2);
        var (onsets, centroid) = Features(pcm);
        // no pulse (sustained pads, ambience): the tempo estimator would still return a number, so the tempo is unknown
        double bpm = onsets < 0.5 ? 0 : knownBpm ?? TempoEstimator.Estimate(pcm.Length > 90 * Fs ? pcm[..(90 * Fs)] : pcm).Bpm;
        return new BgmAnalysis
        {
            Version = Version, Duration = Math.Round(duration, 3), Lufs = Math.Round(lufs, 2), Lra = Math.Round(lra, 1),
            Start = Math.Round(start, 2), End = Math.Round(end, 2), ShortTerm = profile, ProfileStep = step,
            Bpm = Math.Round(bpm, 1), OnsetRate = Math.Round(onsets, 2), Brightness = Math.Round(centroid), Energy = Energy(bpm, onsets, centroid),
        };
    }

    static string F(double x) => x.ToString("0.###", IC);

    /// <summary>Onsets per second (half-wave-rectified log-magnitude spectral flux, peaks above a local mean + δ, ≥ 50 ms apart) and the
    /// energy-weighted mean spectral centroid, from 16 kHz mono PCM (32 ms Hann window, 10 ms hop).</summary>
    public static (double OnsetRate, double Centroid) Features(short[] pcm)
    {
        var (flux, centroid) = Spectrum(pcm);
        if (flux.Length < 50) return (0, 0);
        return (PeakRate(flux), centroid);
    }

    public static double[] Flux(short[] pcm) => Spectrum(pcm).Flux;

    /// <summary>Per 10 ms frame: half-wave-rectified log-magnitude spectral flux; and the mean spectral centroid.</summary>
    public static (double[] Flux, double Centroid) Spectrum(short[] pcm)
    {
        int n = Math.Max(0, (pcm.Length - Win) / Hop);
        if (n < 50) return (new double[n], 0);
        // level-normalised (−20 dBFS RMS) so the onset floor means the same for a quiet and a loud master
        double rms = Math.Sqrt(pcm.Average(x => (double)x * x)) / 32768.0, norm = rms > 1e-6 ? 0.1 / rms : 1;
        var hann = new double[Win];
        for (int i = 0; i < Win; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Win - 1));
        int bins = Win / 2;
        var prev = new double[bins]; var flux = new double[n];
        double cNum = 0, cDen = 0;
        var buf = new Complex[Win];
        for (int f = 0; f < n; f++)
        {
            for (int i = 0; i < Win; i++) buf[i] = new Complex(pcm[f * Hop + i] / 32768.0 * norm * hann[i], 0);
            Fft(buf);
            double fl = 0, e = 0, c = 0, m = 0;
            for (int k = 1; k < bins; k++)
            {
                double mag = buf[k].Magnitude, lm = Math.Log(1 + 1000 * mag);
                if (f > 0) fl += Math.Max(0, lm - prev[k]);
                prev[k] = lm;
                double p = mag * mag; e += p; c += mag * k * Fs / (double)Win; m += mag;
            }
            flux[f] = fl;
            // magnitude-weighted centroid per frame (the usual definition; power weighting is dominated by the bass), averaged over frames by energy
            if (m > 0) { cNum += e * (c / m); cDen += e; }
        }
        return (flux, cDen > 0 ? cNum / cDen : 0);
    }

    static double PeakRate(double[] flux)
    {
        int n = flux.Length;
        // Dixon (2006) peak picking: local maximum within ±3 frames, above the mean of the surrounding window plus δ (relative to the flux spread,
        // with an absolute floor: a sustained pad's flux stays under ~25 while note attacks reach 100–1000 — spikes/21-onset-calibration.cs)
        double mean = flux.Average(), sd = Math.Sqrt(flux.Average(x => (x - mean) * (x - mean)));
        double delta = Math.Max(0.5 * sd, OnsetFloor);
        int count = 0, lastPeak = -100;
        for (int i = 3; i < n - 3; i++)
        {
            bool max = true;
            for (int j = i - 3; j <= i + 3; j++) if (flux[j] > flux[i]) { max = false; break; }
            if (!max) continue;
            int a = Math.Max(0, i - 9), b = Math.Min(n - 1, i + 3);
            double local = 0; for (int j = a; j <= b; j++) local += flux[j]; local /= b - a + 1;
            if (flux[i] < local + delta || i - lastPeak < 5) continue;
            count++; lastPeak = i;
        }
        return count / (n * Hop / (double)Fs);
    }

    static void Fft(Complex[] a)
    {
        int n = a.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            var wl = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    var u = a[i + j]; var v = a[i + j + len / 2] * w;
                    a[i + j] = u + v; a[i + j + len / 2] = u - v;
                    w *= wl;
                }
            }
        }
    }

    /// <summary>The track's short-term loudness (LUFS) around file position <paramref name="pos"/> (the 3 s window centred there).</summary>
    public static double ShortTermAt(BgmAnalysis a, double pos)
    {
        if (a.ShortTerm.Count == 0) return a.Lufs;
        int i = Math.Clamp((int)Math.Round((pos + 1.5) / a.ProfileStep) - 1, 0, a.ShortTerm.Count - 1);
        return a.ShortTerm[i];
    }
}
