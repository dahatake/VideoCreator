using System.Globalization;
using System.Text.RegularExpressions;
using VideoCreator.Media;
using VideoCreator.Models;

namespace VideoCreator.Signals;

/// <summary>FR-SIG-01/02/03/07 (verified in spikes/02-signals.cs).</summary>
public sealed partial class SignalExtractor(FFmpeg ff)
{
    public const double SilenceDb = -40;

    public async Task<SignalSet> ExtractAsync(string proxyVideo, string? audio, double minSilence, CancellationToken ct = default, IProgress<double>? progress = null, double duration = 0)
    {
        var shotsTask = DetectShotsAsync(proxyVideo, ct, progress, duration);
        var loud = audio != null ? await LoudnessSeriesAsync(audio, ct) : [];
        var shots = await shotsTask;
        var voiced = loud.Where(d => d > -60).OrderBy(d => d).ToList();
        var median = voiced.Count > 0 ? voiced[voiced.Count / 2] : -60;
        return new SignalSet
        {
            Shots = shots,
            Loudness100Ms = loud.Select(d => Math.Round(d, 1)).ToList(),
            Silences = FindSilences(loud, minSilence),
            Events = FindPeaks(loud, median),
            MedianLoudness = Math.Round(median, 1),
        };
    }

    public async Task<List<double>> DetectShotsAsync(string video, CancellationToken ct, IProgress<double>? progress = null, double duration = 0)
    {
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-nostats", "-i", video, "-an", "-vf", "scale=320:-2,scdet=threshold=10:sc_pass=0", "-f", "null", "-"], ct, progress: progress, duration: duration);
        return ScdetRegex().Matches(r.StdErr).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x).ToList();
    }

    public async Task<List<double>> LoudnessSeriesAsync(string audio, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-i", audio, "-vn", "-ac", "1", "-ar", "16000", "-f", "s16le", "-"], ct, stdout: ms);
        return RmsDb(ms.GetBuffer().AsSpan(0, (int)ms.Length), 1600);
    }

    public static List<double> RmsDb(ReadOnlySpan<byte> pcm16, int window)
    {
        var list = new List<double>();
        int samples = pcm16.Length / 2;
        for (int start = 0; start < samples; start += window)
        {
            int n = Math.Min(window, samples - start);
            double sum = 0;
            for (int i = 0; i < n; i++) { double v = BitConverter.ToInt16(pcm16.Slice((start + i) * 2, 2)) / 32768.0; sum += v * v; }
            list.Add(10 * Math.Log10(sum / Math.Max(1, n) + 1e-12));
        }
        return list;
    }

    public static List<Interval> FindSilences(IReadOnlyList<double> db, double minSec)
    {
        var res = new List<Interval>();
        int minWin = Math.Max(1, (int)Math.Round(minSec * 10));
        int? start = null;
        for (int i = 0; i <= db.Count; i++)
        {
            bool quiet = i < db.Count && db[i] < SilenceDb;
            if (quiet && start is null) start = i;
            if (!quiet && start is int st) { if (i - st >= minWin) res.Add(new Interval(st / 10.0, i / 10.0)); start = null; }
        }
        return res;
    }

    public static List<SignalEvent> FindPeaks(IReadOnlyList<double> db, double median)
    {
        var res = new List<SignalEvent>();
        for (int i = 0; i < db.Count; i++)
        {
            if (db[i] <= median + 6) continue;
            if (res.Count > 0 && i / 10.0 - res[^1].T <= 2) { if (db[i] - median > res[^1].Strength) res[^1] = new SignalEvent(i / 10.0, "loudness_peak", Math.Round(db[i] - median, 1)); continue; }
            res.Add(new SignalEvent(i / 10.0, "loudness_peak", Math.Round(db[i] - median, 1)));
        }
        return res;
    }

    /// <summary>Mean energy of an interval relative to the median (0..10), used for FR-SEL-05.</summary>
    public static double EnergyScore(SignalSet s, Interval iv)
    {
        int a = Math.Max(0, (int)(iv.T0 * 10)), b = Math.Min(s.Loudness100Ms.Count, (int)Math.Ceiling(iv.T1 * 10));
        if (b <= a) return 0;
        var seg = s.Loudness100Ms.Skip(a).Take(b - a).Where(d => d > SilenceDb).ToList();
        if (seg.Count == 0) return 0;
        var rel = seg.Average() - s.MedianLoudness;               // dB above median
        var peaks = s.Events.Count(e => e.T >= iv.T0 && e.T <= iv.T1);
        var voiced = (double)seg.Count / (b - a);
        return Math.Clamp(5 + rel * 0.8 + peaks * 1.5 + (voiced - 0.7) * 5, 0, 10);
    }

    [GeneratedRegex(@"lavfi\.scd\.time:\s*([\d.]+)")]
    private static partial Regex ScdetRegex();
}
