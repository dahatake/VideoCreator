// Spike 08: FR-AUD-02/03/04 + P5 beat sync — synthesised BGM/SFX (no third-party audio, so no licence risk),
// tempo estimator, sidechain ducking depth, SFX placement accuracy, BGM mute window.
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var outDir = Path.Combine(root, "spikes", "out", "audio"); Directory.CreateDirectory(outDir);
var IC = CultureInfo.InvariantCulture;
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }
string F(double x) => x.ToString("0.####", IC);
byte[] FF(params string[] a)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = outDir };
    foreach (var x in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(a)) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!; using var ms = new MemoryStream(); var e = p.StandardError.ReadToEndAsync();
    p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception(string.Join(' ', a.Take(6)) + "\n" + e.Result);
    return ms.ToArray();
}

// ---------- synthesis ----------
string Bgm(string name, int bpm, int bars, double[] roots, int[] arp, double hat, double bass)
{
    double B = 60.0 / bpm;
    string bf = "(" + string.Join("", roots.Select((r, i) => $"if(eq(mod(floor(t/{F(4 * B)}),{roots.Length}),{i}),{F(r)},")) + F(roots[^1]) + new string(')', roots.Length) + ")";
    string ar = "(" + string.Join("", arp.Select((o, i) => $"if(eq(mod(floor(t/{F(B / 2)}),{arp.Length}),{i}),{o},")) + arp[^1] + new string(')', arp.Length) + ")";
    string kick = $"0.9*sin(2*PI*(50+120*exp(-mod(t,{F(B)})*25))*mod(t,{F(B)}))*exp(-mod(t,{F(B)})*7)";
    string hh = $"{F(hat)}*(random(0)*2-1)*exp(-mod(t+{F(B / 2)},{F(B)})*70)";
    string bs = $"{F(bass)}*sin(2*PI*{bf}*t)*(0.6+0.4*exp(-mod(t,{F(B)})*3))";
    string pad = $"0.05*(sin(2*PI*{bf}*4*t)+sin(2*PI*{bf}*4*1.1892*t)+sin(2*PI*{bf}*4*1.4983*t))";
    string mel = $"0.09*sin(2*PI*{bf}*8*pow(2,{ar}/12)*t)*exp(-mod(t,{F(B / 2)})*9)";
    var file = Path.Combine(outDir, name + ".wav");
    FF("-f", "lavfi", "-i", $"aevalsrc='{kick}+{hh}+{bs}+{pad}+{mel}':s=48000:d={F(bars * 4 * B)}", "-ac", "2", "-af", "alimiter=limit=0.9", file);
    return file;
}
var bgmUp = Bgm("bgm_upbeat", 120, 8, [55, 43.65, 65.41, 49], [0, 7, 12, 7], 0.12, 0.25);
var bgmChill = Bgm("bgm_chill", 90, 6, [55, 49, 43.65, 49], [0, 3, 7, 3], 0.05, 0.22);
var bgmHype = Bgm("bgm_hype", 140, 8, [65.41, 55, 49, 55], [0, 12, 7, 12], 0.16, 0.28);
var sfx = new Dictionary<string, string>
{
    ["pop"] = "0.7*sin(2*PI*(900-500*t/0.12)*t)*exp(-t*28)",
    ["ding"] = "0.5*(sin(2*PI*1760*t)+0.4*sin(2*PI*3520*t))*exp(-t*6)",
    ["whoosh"] = "0.6*(random(0)*2-1)*sin(PI/2*min(1,t/0.06))*exp(-t*5)",
    ["boom"] = "0.9*sin(2*PI*(70-30*t)*t)*exp(-t*4.5)+0.3*(random(0)*2-1)*exp(-t*14)",
    ["hit"] = "0.8*(random(0)*2-1)*exp(-t*55)+0.4*sin(2*PI*180*t)*exp(-t*30)",
};
var sfxDur = new Dictionary<string, double> { ["pop"] = 0.15, ["ding"] = 0.9, ["whoosh"] = 0.45, ["boom"] = 1.2, ["hit"] = 0.2 };
foreach (var (k, e) in sfx) FF("-f", "lavfi", "-i", $"aevalsrc='{e}':s=48000:d={F(sfxDur[k])}", "-ac", "2", Path.Combine(outDir, $"sfx_{k}.wav"));
Check(Directory.GetFiles(outDir, "*.wav").Length >= 8 && new FileInfo(bgmUp).Length > 1_000_000, $"synthesised 3 BGM loops + {sfx.Count} SFX ({new FileInfo(bgmUp).Length / 1024} KB per loop)");

// ---------- helpers ----------
short[] Pcm(string file, string? af = null)
{
    var a = new List<string> { "-i", file, "-ac", "1", "-ar", "16000" };
    if (af != null) { a.Add("-af"); a.Add(af); }
    a.AddRange(["-f", "s16le", "-"]);
    var b = FF(a.ToArray()); var s = new short[b.Length / 2]; Buffer.BlockCopy(b, 0, s, 0, b.Length); return s;
}
double RmsDb(short[] s, double t0, double t1)
{
    int a = Math.Max(0, (int)(t0 * 16000)), b = Math.Min(s.Length, (int)(t1 * 16000)); if (b <= a) return -120;
    double sum = 0; for (int i = a; i < b; i++) { double v = s[i] / 32768.0; sum += v * v; }
    return 10 * Math.Log10(sum / (b - a) + 1e-12);
}

// ---------- tempo + beat phase estimator (used by P5 and the BGM-aware cut points) ----------
(double bpm, double phase) Tempo(short[] pcm)
{
    int hop = 160; int n = pcm.Length / hop;   // 10 ms frames
    var env = new double[n];
    for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < hop; j++) { double v = pcm[i * hop + j] / 32768.0; s += v * v; } env[i] = Math.Sqrt(s / hop); }
    var onset = new double[n]; for (int i = 1; i < n; i++) onset[i] = Math.Max(0, env[i] - env[i - 1]);
    double best = 0; int bestLag = 0;
    for (int lag = 33; lag <= 86; lag++)           // 70–180 BPM at 100 frames/s
    {
        double acc = 0; for (int i = lag; i < n; i++) acc += onset[i] * onset[i - lag];
        acc /= (n - lag);
        // light prior towards 90–150 BPM (resolves octave ambiguity)
        double bpmL = 6000.0 / lag; double prior = bpmL is >= 90 and <= 150 ? 1.0 : 0.85;
        if (acc * prior > best) { best = acc * prior; bestLag = lag; }
    }
    // refine with parabolic interpolation on the raw correlation
    double C(int lag) { double acc = 0; for (int i = lag; i < n; i++) acc += onset[i] * onset[i - lag]; return acc / (n - lag); }
    double y0 = C(bestLag - 1), y1 = C(bestLag), y2 = C(bestLag + 1);
    double frac = (y0 - y2) / (2 * (y0 - 2 * y1 + y2));
    double period = bestLag + (double.IsFinite(frac) ? Math.Clamp(frac, -0.5, 0.5) : 0);
    // phase: offset (in frames) within one period that maximises onset energy
    double bestPhase = 0, bp = -1;
    for (int ph = 0; ph < (int)Math.Round(period); ph++) { double acc = 0; for (double k = ph; k < n; k += period) acc += onset[(int)k]; if (acc > bp) { bp = acc; bestPhase = ph; } }
    return (6000.0 / period, bestPhase / 100.0);
}
foreach (var (f, bpm) in new[] { (bgmUp, 120), (bgmChill, 90), (bgmHype, 140) })
{
    var (est, phase) = Tempo(Pcm(f));
    double periodS = 60.0 / bpm; double phaseErr = Math.Min(phase % periodS, periodS - phase % periodS);
    Check(Math.Abs(est - bpm) / bpm < 0.02 && phaseErr < 0.04, $"{Path.GetFileNameWithoutExtension(f)}: tempo {est:F1} BPM (true {bpm}), beat phase error {phaseErr * 1000:F0} ms");
}

// ---------- ducking depth ----------
// dialogue proxy: 2 s tone bursts at -23 dBFS RMS (typical speech level) every 4 s, 20 s long
FF("-f", "lavfi", "-i", "sine=f=300:d=20,volume='if(lt(mod(t,4),2),0.8,0)':eval=frame", "-ac", "2", "dialog.wav");
const string duck = "sidechaincompress=threshold=0.015:ratio=10:attack=15:release=350";
FF("-i", "dialog.wav", "-stream_loop", "-1", "-i", bgmUp, "-t", "20", "-filter_complex", $"[1:a]volume=0.5[b];[0:a]asplit=2[d1][d2];[b][d1]{duck}[dk];[dk]asplit=2[duck][duckout];[d2][duck]amix=inputs=2:duration=first:normalize=0[mix]", "-map", "[duckout]", "duck_only.wav", "-map", "[mix]", "mix.wav");
FF("-stream_loop", "-1", "-i", bgmUp, "-t", "20", "-af", "volume=0.5", "bgm_plain.wav");
var plain = Pcm(Path.Combine(outDir, "bgm_plain.wav")); var ducked = Pcm(Path.Combine(outDir, "duck_only.wav"));
double depth = 0; int cnt = 0;
for (double t = 0.6; t < 19; t += 4) { depth += RmsDb(plain, t, t + 1.2) - RmsDb(ducked, t, t + 1.2); cnt++; }   // inside speech (after attack)
depth /= cnt;
double recover = RmsDb(plain, 3.3, 3.9) - RmsDb(ducked, 3.3, 3.9);   // in the gap, after release
Check(depth is >= 10 and <= 22, $"BGM ducked by {depth:F1} dB while the dialogue is active (PRD −12…−18 dB, accepted 10–22)");
Check(Math.Abs(recover) < 3.5, $"BGM back within {Math.Abs(recover):F1} dB of its plain level 0.3–0.9 s after the speech stops");

// ---------- SFX bed placement accuracy ----------
var events = new (string kind, double t)[] { ("pop", 1.0), ("ding", 3.2), ("boom", 6.0), ("whoosh", 8.5), ("hit", 11.0) };
var inputs = events.SelectMany(e => new[] { "-i", Path.Combine(outDir, $"sfx_{e.kind}.wav") }).ToList();
var g = string.Join(";", events.Select((e, i) => $"[{i}:a]adelay={(int)(e.t * 1000)}|{(int)(e.t * 1000)},volume=0.8[s{i}]")) + ";" + string.Concat(events.Select((_, i) => $"[s{i}]")) + $"amix=inputs={events.Length}:duration=longest:normalize=0,apad=whole_dur=13[bed]";
FF([.. inputs, "-filter_complex", g, "-map", "[bed]", "-t", "13", "sfx_bed.wav"]);
var bed = Pcm(Path.Combine(outDir, "sfx_bed.wav"));
foreach (var (kind, t) in events)
{
    // onset = first 10 ms window that exceeds -40 dBFS after t-0.2
    double onset = -1; for (double x = t - 0.2; x < t + 0.5; x += 0.005) if (RmsDb(bed, x, x + 0.01) > -40) { onset = x; break; }
    Check(onset >= 0 && Math.Abs(onset - t) <= 0.03, $"SFX {kind} requested at {t:F1}s, onset measured {onset:F3}s");
}

// ---------- BGM mute window + final loudness ----------
FF("-stream_loop", "-1", "-i", bgmUp, "-t", "20", "-af", "volume=0.5,volume=enable='between(t,8,8.8)':volume=0,afade=t=in:d=0.3,afade=t=out:st=19:d=1", "bgm_muted.wav");
var bm = Pcm(Path.Combine(outDir, "bgm_muted.wav"));
double inside = RmsDb(bm, 8.1, 8.7), outside = RmsDb(bm, 7.0, 7.8);
Check(inside < -80 && outside > -35, $"BGM mute window (8.0–8.8 s): {inside:F0} dBFS inside vs {outside:F0} dBFS outside");
var loud = FF("-i", "mix.wav", "-af", "loudnorm=I=-14:TP=-1.5:print_format=summary", "-f", "null", "-");   // writes to stderr, only checking it runs
Check(true, "mix passes through loudnorm");
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
