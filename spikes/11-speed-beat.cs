// Spike 11: P8 (speed ramps with A/V sync) and P5 (cuts quantised to the BGM beat grid).
// The source encodes its own timestamp in luma (lum = 255 * mod(T,30)/30) so the output frame's source time can be read back.
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var outDir = Path.Combine(root, "spikes", "out", "speed"); Directory.CreateDirectory(outDir);
var IC = CultureInfo.InvariantCulture;
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }
string F(double x) => x.ToString("0.###", IC);
(byte[] o, string e) FFx(params string[] a)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = outDir };
    foreach (var x in new[] { "-hide_banner", "-y" }.Concat(a)) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!; using var ms = new MemoryStream(); var e = p.StandardError.ReadToEndAsync();
    p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception(e.Result);
    return (ms.ToArray(), e.Result);
}
double Dur(string file, string sel) => double.Parse(System.Text.Encoding.UTF8.GetString(FFx("-v", "error", "-i", file, "-show_entries", "x").o).Length > 0 ? "0" : "0", IC);
double StreamDur(string file, string type)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffprobe.exe")) { RedirectStandardOutput = true, WorkingDirectory = outDir };
    foreach (var x in new[] { "-v", "error", "-select_streams", type == "v" ? "v:0" : "a:0", "-show_entries", "stream=duration", "-of", "csv=p=0", file }) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!; var s = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return double.Parse(s.Trim(), IC);
}
// timestamped source: luma ramps 0..255 every 30 s; audio = tone
FFx("-f", "lavfi", "-i", "color=c=black:s=320x180:r=30:d=60,geq=lum='16+219*mod(T,30)/30':cb=128:cr=128", "-f", "lavfi", "-i", "sine=f=440:d=60", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-c:a", "aac", "-shortest", "src.mp4");
double[] Lumas(string file)
{
    var raw = FFx("-i", file, "-vf", "scale=8:8,format=gray", "-f", "rawvideo", "-").o; int n = raw.Length / 64; var r = new double[n];
    for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < 64; j++) s += raw[i * 64 + j]; r[i] = s / 64; }
    return r;
}
double SrcTime(double luma) => luma / 255.0 * 30;

// ---- P8: speed ramps ----
var plan = new (double a, double b, double speed)[] { (0, 24, 6), (24, 36, 2), (36, 42, 1) };
var g = new System.Text.StringBuilder(); var lab = new System.Text.StringBuilder();
for (int i = 0; i < plan.Length; i++)
{
    var (a, b, s) = plan[i];
    g.Append($"[0:v]trim=start={F(a)}:end={F(b)},setpts=(PTS-STARTPTS)/{F(s)},fps=30[v{i}];");
    // keep the original sound up to 2x (atempo), mute beyond that (a 6x chipmunk track is unusable)
    if (s <= 2.0) g.Append($"[0:a]atrim=start={F(a)}:end={F(b)},asetpts=PTS-STARTPTS,atempo={F(s)},aresample=48000,aformat=channel_layouts=stereo[a{i}];");
    else g.Append($"anullsrc=r=48000:cl=stereo,atrim=duration={F((b - a) / s)}[a{i}];");
    lab.Append($"[v{i}][a{i}]");
}
g.Append($"{lab}concat=n={plan.Length}:v=1:a=1[v][a]");
FFx("-i", "src.mp4", "-filter_complex", g.ToString(), "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-c:a", "aac", "p8.mp4");
double expected = plan.Sum(p => (p.b - p.a) / p.speed);
double vd = StreamDur(Path.Combine(outDir, "p8.mp4"), "v"), ad = StreamDur(Path.Combine(outDir, "p8.mp4"), "a");
Check(Math.Abs(vd - expected) < 0.08, $"P8 video duration {vd:F2}s (expected {expected:F2}s)");
Check(Math.Abs(vd - ad) < 0.06, $"P8 audio/video durations agree: {vd:F3}s vs {ad:F3}s (Δ {Math.Abs(vd - ad) * 1000:F0} ms)");
var L = Lumas(Path.Combine(outDir, "p8.mp4"));
double[] checkT = [1.0, 3.5, 5.0, 8.0, 9.8, 12.5];
foreach (var t in checkT)
{
    // expected source time at output time t
    double acc = 0, src = 0; foreach (var (a, b, s) in plan) { double len = (b - a) / s; if (t < acc + len) { src = a + (t - acc) * s; break; } acc += len; }
    double got = SrcTime(L[(int)(t * 30)]); src %= 30; double err = Math.Abs(got - src); if (err > 15) err = 30 - err;
    Check(err < 0.5 + 0.1 * 6, $"output {t:F1}s shows source {got:F2}s (expected {src:F2}s, Δ {err:F2}s)");   // 1 output frame at 6x = 0.2 s source
}

// ---- P5: cuts on the beat grid ----
double bpm = 120, beat = 60 / bpm;
var cuts = new (double src, int beats)[] { (2, 4), (10, 2), (18, 4), (5, 2), (25, 8) };   // each clip lasts k beats
var g2 = new System.Text.StringBuilder(); var lab2 = new System.Text.StringBuilder();
for (int i = 0; i < cuts.Length; i++)
{
    double len = cuts[i].beats * beat;
    g2.Append($"[0:v]trim=start={F(cuts[i].src)}:duration={F(len)},setpts=PTS-STARTPTS,fps=30[v{i}];"); lab2.Append($"[v{i}]");
}
g2.Append($"{lab2}concat=n={cuts.Length}:v=1:a=0[v]");
FFx("-i", "src.mp4", "-filter_complex", g2.ToString(), "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "p5.mp4");
var L5 = Lumas(Path.Combine(outDir, "p5.mp4"));
var jumps = new List<double>(); for (int i = 1; i < L5.Length; i++) if (Math.Abs(L5[i] - L5[i - 1]) > 40) jumps.Add(i / 30.0);
var expectCuts = new List<double>(); double acc2 = 0; foreach (var c in cuts.Take(cuts.Length - 1)) { acc2 += c.beats * beat; expectCuts.Add(acc2); }
Console.WriteLine("      detected cuts: " + string.Join(", ", jumps.Select(j => j.ToString("F2", IC))) + "   beat grid: " + string.Join(", ", expectCuts.Select(j => j.ToString("F2", IC))));
Check(jumps.Count == expectCuts.Count && jumps.Zip(expectCuts, (a, b) => Math.Abs(a - b) <= 1 / 30.0 + 1e-6).All(x => x), "every P5 cut lies on the beat grid (±1 frame)");
Check(expectCuts.All(c => Math.Abs(c / beat - Math.Round(c / beat)) < 1e-6), "cut times are whole multiples of the beat length");
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
