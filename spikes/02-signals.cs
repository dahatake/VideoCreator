// Spike 02: FR-SIG-01/02/03 — shot boundaries (scdet), 100 ms loudness series, silence, loudness peaks.
// Ground truth in spikes/media/landscape.mp4: cuts at 10,20,30,40,50 s; silence 30–33 s; loud bursts 20–21 s and 40–41 s.
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var src = Path.Combine(root, "spikes", "media", "landscape.mp4");
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }

// --- Shot boundaries: downscale + scdet (FFmpeg-native alternative to PySceneDetect, PRD D-4)
var sw = Stopwatch.StartNew();
var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardError = true };
foreach (var a in new[] { "-hide_banner", "-nostats", "-i", src, "-an", "-vf", "scale=320:-2,scdet=threshold=10:sc_pass=0", "-f", "null", "-" }) psi.ArgumentList.Add(a);
using (var p = Process.Start(psi)!)
{
    var err = p.StandardError.ReadToEnd(); p.WaitForExit();
    var cuts = Regex.Matches(err, @"lavfi\.scd\.time:\s*([\d.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
    Console.WriteLine($"scdet {sw.ElapsedMilliseconds} ms -> cuts: {string.Join(", ", cuts.Select(c => c.ToString("F2")))}");
    var expected = new[] { 10.0, 20, 30, 40, 50 };
    Check(expected.All(e => cuts.Any(c => Math.Abs(c - e) < 0.1)) && cuts.Count <= expected.Length + 1, "shot boundaries found at 10/20/30/40/50 s (±0.1 s), no flood of false positives");
}

// --- Loudness series from raw PCM (100 ms windows)
sw.Restart();
var pcm = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-i", src, "-vn", "-ac", "1", "-ar", "16000", "-f", "s16le", "-" }) pcm.ArgumentList.Add(a);
var db = new List<double>();
using (var p = Process.Start(pcm)!)
{
    var s = p.StandardOutput.BaseStream; var buf = new byte[3200]; // 1600 samples = 100 ms
    while (true)
    {
        int n = 0; while (n < buf.Length) { int r = s.Read(buf, n, buf.Length - n); if (r == 0) break; n += r; }
        if (n == 0) break;
        double sum = 0; int cnt = n / 2;
        for (int i = 0; i < cnt; i++) { double v = BitConverter.ToInt16(buf, i * 2) / 32768.0; sum += v * v; }
        db.Add(10 * Math.Log10(sum / cnt + 1e-12));
        if (n < buf.Length) break;
    }
    p.WaitForExit();
}
Console.WriteLine($"loudness series: {db.Count} windows in {sw.ElapsedMilliseconds} ms");
Check(Math.Abs(db.Count - 600) <= 2, "600 x 100 ms windows for 60 s");

// silences: >= 0.5 s below -40 dBFS
var silences = new List<(double s, double e)>();
int? start = null;
for (int i = 0; i <= db.Count; i++)
{
    bool quiet = i < db.Count && db[i] < -40;
    if (quiet && start is null) start = i;
    if (!quiet && start is int st) { if (i - st >= 5) silences.Add((st / 10.0, i / 10.0)); start = null; }
}
Console.WriteLine("silences: " + string.Join(", ", silences.Select(x => $"{x.s:F1}-{x.e:F1}")));
Check(silences.Count == 1 && Math.Abs(silences[0].s - 30) <= 0.2 && Math.Abs(silences[0].e - 33) <= 0.2, "silence 30–33 s detected (±0.2 s)");

// peaks: local level > median + 6 dB, merged
var med = db.Where(d => d > -60).OrderBy(d => d).ElementAt(db.Count(d => d > -60) / 2);
var peaks = new List<double>();
for (int i = 0; i < db.Count; i++) if (db[i] > med + 6 && (peaks.Count == 0 || i / 10.0 - peaks[^1] > 2)) peaks.Add(i / 10.0);
Console.WriteLine($"median {med:F1} dBFS, peaks: {string.Join(", ", peaks.Select(x => x.ToString("F1")))}");
Check(peaks.Count == 2 && Math.Abs(peaks[0] - 20) <= 0.2 && Math.Abs(peaks[1] - 40) <= 0.2, "loudness peaks at 20 s and 40 s");

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
