// Spike 10: FR-ING-05 (multi-camera audio sync, GCC-PHAT) and FR-SIG-04 (non-speech bursts: laughter / cheers).
// Ground truth: known delays applied to the real TTS conversation audio; synthetic laughter inserted at known times.
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var media = Path.Combine(root, "spikes", "media");
var outDir = Path.Combine(root, "spikes", "out", "sync"); Directory.CreateDirectory(outDir);
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }
byte[] FF(params string[] a)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = outDir };
    foreach (var x in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(a)) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!; using var ms = new MemoryStream(); var e = p.StandardError.ReadToEndAsync();
    p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception(e.Result);
    return ms.ToArray();
}
float[] Pcm(string file, int rate, string? af = null)
{
    var a = new List<string> { "-i", file, "-ac", "1", "-ar", rate.ToString() }; if (af != null) { a.Add("-af"); a.Add(af); }
    a.AddRange(["-f", "s16le", "-"]);
    var b = FF(a.ToArray()); var f = new float[b.Length / 2]; for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToInt16(b, i * 2) / 32768f; return f;
}

static void Fft(Complex[] a, bool inverse)
{
    int n = a.Length;
    for (int i = 1, j = 0; i < n; i++) { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) (a[i], a[j]) = (a[j], a[i]); }
    for (int len = 2; len <= n; len <<= 1)
    {
        double ang = 2 * Math.PI / len * (inverse ? -1 : 1); var wl = new Complex(Math.Cos(ang), -Math.Sin(ang));
        for (int i = 0; i < n; i += len) { Complex w = 1; for (int j = 0; j < len / 2; j++) { var u = a[i + j]; var v = a[i + j + len / 2] * w; a[i + j] = u + v; a[i + j + len / 2] = u - v; w *= wl; } }
    }
    if (inverse) for (int i = 0; i < n; i++) a[i] /= n;
}
/// <summary>Delay (seconds) of <paramref name="other"/> relative to <paramref name="reference"/>: other(t) = reference(t - delay). GCC-PHAT.</summary>
(double delay, double confidence) Gcc(float[] reference, float[] other, int rate, double maxLag)
{
    int n = 1; while (n < reference.Length + other.Length) n <<= 1;
    var A = new Complex[n]; var B = new Complex[n];
    for (int i = 0; i < reference.Length; i++) A[i] = reference[i];
    for (int i = 0; i < other.Length; i++) B[i] = other[i];
    Fft(A, false); Fft(B, false);
    var R = new Complex[n];
    for (int i = 0; i < n; i++) { var x = B[i] * Complex.Conjugate(A[i]); var m = x.Magnitude; R[i] = m < 1e-12 ? 0 : x / (m + 1e-9 * 0 + 1e-6); }   // PHAT weighting
    Fft(R, true);
    int ml = (int)(maxLag * rate); double best = double.MinValue; int bi = 0; double sum = 0; int cnt = 0;
    for (int l = -ml; l <= ml; l++) { double v = R[(l + n) % n].Real; sum += Math.Abs(v); cnt++; if (v > best) { best = v; bi = l; } }
    return ((double)bi / rate, best / (sum / cnt));   // peak-to-mean ratio as confidence
}

// ---- sync: the real conversation audio, delayed / degraded like a second camera's microphone ----
var master = Path.Combine(media, "talk.mp4");
var refPcm = Pcm(master, 8000, "atrim=0:90");
foreach (var (delay, label) in new[] { (1.3, "camera started 1.30 s later"), (-0.75, "camera started 0.75 s earlier"), (0.0, "already aligned"), (4.217, "camera started 4.217 s later") })
{
    // other = master delayed by `delay` (negative = advanced), then degraded: band-limited, noisy, attenuated
    string af = delay >= 0 ? $"adelay={(int)(delay * 1000)}|{(int)(delay * 1000)}" : $"atrim=start={-delay},asetpts=PTS-STARTPTS";
    FF("-i", master, "-af", $"{af},lowpass=f=3500,highpass=f=200,volume=0.35,aeval=val(0)+0.01*(random(0)*2-1)", "-ac", "1", "-t", "125", "cam.wav");
    var oth = Pcm(Path.Combine(outDir, "cam.wav"), 8000, "atrim=0:90");
    var (d, conf) = Gcc(refPcm, oth, 8000, 10);
    Check(Math.Abs(d - delay) <= 0.01 && conf > 8, $"{label}: estimated {d:F3} s (true {delay:F3}), confidence {conf:F1}");
}
// unrelated audio -> low confidence so the app can warn instead of silently mis-syncing
FF("-f", "lavfi", "-i", "anoisesrc=d=95:c=pink:r=8000", "-ac", "1", "noise.wav");
var (dn, cn) = Gcc(refPcm, Pcm(Path.Combine(outDir, "noise.wav"), 8000, "atrim=0:90"), 8000, 10);
Check(cn < 8, $"unrelated audio gives low confidence ({cn:F1}) -> sync is rejected");
var sw = Stopwatch.StartNew(); Gcc(refPcm, refPcm, 8000, 10); Console.WriteLine($"      GCC-PHAT on 90 s @8 kHz: {sw.ElapsedMilliseconds} ms");

// ---- non-speech burst detector ----
// speech proxy: tone "words" at known times; laughter proxy: amplitude-modulated noise (5 Hz) at known times
var words = new[] { (0.5, 2.0), (2.3, 4.0), (9.0, 10.5), (10.8, 12.0), (18.0, 19.5) };
var laughs = new[] { (4.6, 6.4), (13.0, 15.0) };
var expr = "0.12*sin(2*PI*220*t*(1+0.02*sin(2*PI*5*t)))*(" + string.Join("+", words.Select(w => $"between(t,{w.Item1.ToString(CultureInfo.InvariantCulture)},{w.Item2.ToString(CultureInfo.InvariantCulture)})")) + ")"
         + "+0.18*(random(0)*2-1)*(0.5+0.5*sin(2*PI*5*t))*(" + string.Join("+", laughs.Select(w => $"between(t,{w.Item1.ToString(CultureInfo.InvariantCulture)},{w.Item2.ToString(CultureInfo.InvariantCulture)})")) + ")";
FF("-f", "lavfi", "-i", $"aevalsrc='{expr}':s=16000:d=22", "laugh.wav");
var pcm = Pcm(Path.Combine(outDir, "laugh.wav"), 16000);
var db = new List<double>();
for (int i = 0; i + 1600 <= pcm.Length; i += 1600) { double s = 0; for (int j = 0; j < 1600; j++) s += pcm[i + j] * pcm[i + j]; db.Add(10 * Math.Log10(s / 1600 + 1e-12)); }
bool InWord(double t) => words.Any(w => t >= w.Item1 - 0.15 && t <= w.Item2 + 0.15);
var voiced = db.Where(d => d > -60).OrderBy(d => d).ToList(); double med = voiced[voiced.Count / 2];
var loudIv = new List<(double a, double b)>();
for (int i = 0; i < db.Count; i++) if (db[i] > Math.Max(-45, med - 8) && !InWord(i / 10.0 + 0.05)) loudIv.Add((i / 10.0, i / 10.0 + 0.1));
// laughter fluctuates: close gaps up to 0.3 s, then keep runs of at least 0.5 s
var bursts = new List<(double a, double b)>();
foreach (var iv in loudIv) { if (bursts.Count > 0 && iv.a - bursts[^1].b <= 0.3) bursts[^1] = (bursts[^1].a, iv.b); else bursts.Add(iv); }
bursts = bursts.Where(b => b.b - b.a >= 0.5).ToList();
Console.WriteLine("      bursts: " + string.Join(", ", bursts.Select(b => $"{b.a:F1}-{b.b:F1}")));
Check(bursts.Count == laughs.Length && bursts.Zip(laughs, (b, l) => Math.Abs(b.a - l.Item1) < 0.4 && Math.Abs(b.b - l.Item2) < 0.4).All(x => x), "laughter bursts found at the inserted times (±0.4 s), none inside speech");
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
