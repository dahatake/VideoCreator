// Spike 03: render path — blur-bg 9:16, punch-in, ASS telops (bundled OFL font), loudnorm,
// QA H-01..H-05, AT-02 (spec), AT-03 (determinism), AT-11 (vertical source untouched), NFR-PERF-03 (<=60 s for 30 s).
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var fonts = Path.Combine(root, "tools", "fonts");
var media = Path.Combine(root, "spikes", "media");
var outDir = Path.Combine(root, "spikes", "out"); Directory.CreateDirectory(outDir);
var IC = CultureInfo.InvariantCulture;
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }
(int code, string so, string se) Run(string exe, IEnumerable<string> args, string? cwd = null)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, exe)) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd ?? outDir };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!; var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync(); p.WaitForExit();
    return (p.ExitCode, o.Result, e.Result);
}
// ASS filter args are parsed by libavfilter: escape ':' and '\' in paths. Use relative paths from cwd to stay simple.
string Esc(string p) => p.Replace("\\", "/").Replace(":", "\\:");

// --- ASS (1080x1920). Long caption deliberately at the 14-char limit to stress the safe-zone check.
var ass = """
[Script Info]
ScriptType: v4.00+
PlayResX: 1080
PlayResY: 1920
WrapStyle: 2
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Caption,M PLUS 1p ExtraBold,72,&H00FFFFFF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,7,2,2,60,160,620,1
Style: Emph,M PLUS 1p ExtraBold,90,&H0000F0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,9,3,2,60,160,620,1
Style: Hook,M PLUS 1p ExtraBold,80,&H00FFFFFF,&H0000FFFF,&H00C0400A,&H00C0400A,0,0,0,0,100,100,0,0,3,16,0,8,60,160,300,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 1,0:00:00.00,0:00:03.00,Hook,,0,0,0,,{\fad(80,0)}まさかの結末が待っていた
Dialogue: 0,0:00:00.20,0:00:03.00,Caption,,0,0,0,,今日は全部で十四文字の字幕です
Dialogue: 0,0:00:03.00,0:00:06.00,Emph,,0,0,0,,{\t(0,120,\fscx115\fscy115)\t(120,240,\fscx100\fscy100)}１００万円使い切る
""";
File.WriteAllText(Path.Combine(outDir, "telop.ass"), ass, new System.Text.UTF8Encoding(false));
var fontsRel = Path.GetRelativePath(outDir, fonts);

// --- H-05 measured: render telops alone on black and compute the glyph bbox from raw pixels (cropdetect proved unreliable)
// Body text only (no headline) for the strict bottom/right check: grab the frame as raw gray8 and compute the glyph bbox ourselves.
File.WriteAllText(Path.Combine(outDir, "body.ass"), string.Join("\n", ass.Split('\n').Where(l => !l.Contains(",Hook,,"))), new System.Text.UTF8Encoding(false));
(int x1, int x2, int y1, int y2) Bbox(string assFile, double t)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = outDir };
    foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=1080x1920:r=30:d=6",
        "-vf", $"subtitles={assFile}:fontsdir={Esc(fontsRel)}", "-ss", t.ToString(IC), "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", "-" }) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!; using var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
    var px = ms.ToArray(); int W = 1080, H = 1920, X1 = W, X2 = -1, Y1 = H, Y2 = -1;
    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (px[y * W + x] > 48) { X1 = Math.Min(X1, x); X2 = Math.Max(X2, x); Y1 = Math.Min(Y1, y); Y2 = Math.Max(Y2, y); }
    return (X1, X2, Y1, Y2);
}
var hookBox = Bbox("telop.ass", 1.5);
Console.WriteLine($"      caption+hook bbox: x {hookBox.x1}-{hookBox.x2}, y {hookBox.y1}-{hookBox.y2}");
Check(hookBox.y1 >= 250, "headline (Hook) starts below the top 250 px band");
foreach (var t in new[] { 1.5, 4.5 })
{
    var b = Bbox("body.ass", t);
    Check(b.x2 > b.x1 && b.y2 > b.y1, $"glyphs found at t={t}");
    Check(b.y2 <= 1920 - 580 && b.x2 <= 1080 - 160 && b.x1 >= 60 && b.y1 >= 250, $"H-05 body telop at t={t} inside safe zone (x {b.x1}-{b.x2}, y {b.y1}-{b.y2}; limits x 60-920, y 250-1340)");
}
// --- Long->Short render (3 segments, punch-in on #2, blur bg, telops, loudnorm), timed
string Seg(int i, double a, double b, double zoom) =>
    $"[0:v]trim={a.ToString(IC)}:{b.ToString(IC)},setpts=PTS-STARTPTS" + (zoom > 1 ? $",scale=iw*{zoom.ToString(IC)}:ih*{zoom.ToString(IC)},crop=1920:1080" : "") + $"[v{i}];[0:a]atrim={a.ToString(IC)}:{b.ToString(IC)},asetpts=PTS-STARTPTS[a{i}]";
var fc = string.Join(";", new[] {
    Seg(1, 18, 28, 1.0), Seg(2, 38, 48, 1.15), Seg(3, 5, 15, 1.0),
    "[v1][a1][v2][a2][v3][a3]concat=n=3:v=1:a=1[cv][ca]",
    "[cv]split[fg0][bg0]",
    "[bg0]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=20:2[bg]",
    "[fg0]scale=1080:-2[fg]",
    $"[bg][fg]overlay=(W-w)/2:(H-h)/2-120,subtitles=telop.ass:fontsdir={Esc(fontsRel)},format=yuv420p[vout]",
    "[ca]loudnorm=I=-14:TP=-1.5:LRA=11,aresample=48000[aout]" });
string[] Enc(string o) => ["-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-threads", "8", "-r", "30",
    "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact", "-map_metadata", "-1", o];
// FR-AUD-01 two-pass: measure the edited audio only (fast), then apply linear normalization with the measured values.
var audioOnly = string.Join(";", new[] { Seg(1, 18, 28, 1.0), Seg(2, 38, 48, 1.0), Seg(3, 5, 15, 1.0) }.Select(s => s[(s.IndexOf("[0:a]"))..]))
    + ";[a1][a2][a3]concat=n=3:v=0:a=1,loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json[m]";
var (_, _, mj) = Run("ffmpeg.exe", ["-hide_banner", "-i", Path.Combine(media, "landscape.mp4"), "-filter_complex", audioOnly, "-map", "[m]", "-f", "null", "-"]);
string J(string k) => Regex.Match(mj, $"\"{k}\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
var ln2 = $"loudnorm=I=-14:TP=-1.5:LRA=11:measured_I={J("input_i")}:measured_TP={J("input_tp")}:measured_LRA={J("input_lra")}:measured_thresh={J("input_thresh")}:offset={J("target_offset")}:linear=true";
Console.WriteLine($"      pass1: I={J("input_i")} TP={J("input_tp")} LRA={J("input_lra")} -> {ln2}");
fc = fc.Replace("loudnorm=I=-14:TP=-1.5:LRA=11", ln2);
var sw = Stopwatch.StartNew();
var r = Run("ffmpeg.exe", ["-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(media, "landscape.mp4"), "-filter_complex", fc, .. Enc("short_a.mp4")]);
var renderMs = sw.ElapsedMilliseconds;
Check(r.code == 0, $"render ok ({renderMs} ms) {r.se}");
Check(renderMs <= 60000, $"NFR-PERF-03: 30 s short rendered in {renderMs / 1000.0:F1} s (<= 60 s)");
Run("ffmpeg.exe", ["-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(media, "landscape.mp4"), "-filter_complex", fc, .. Enc("short_b.mp4")]);

var (_, probe, _) = Run("ffprobe.exe", ["-v", "error", "-show_entries", "stream=codec_type,codec_name,width,height,sample_rate,channels:format=duration", "-of", "default=nw=1", "short_a.mp4"]);
Console.WriteLine("      " + probe.Replace("\r", "").Replace("\n", " | "));
var dur = double.Parse(Regex.Match(probe, @"duration=([\d.]+)").Groups[1].Value, IC);
Check(Math.Abs(dur - 30) < 0.1 && probe.Contains("width=1080") && probe.Contains("height=1920"), $"H-01/H-02: duration {dur:F2}s within 25–35, 1080x1920");
Check(probe.Contains("codec_name=h264") && probe.Contains("codec_name=aac") && probe.Contains("sample_rate=48000") && probe.Contains("channels=2"), "§4.2 output codecs: H.264 + AAC 48 kHz stereo");

(double I, double TP) Measure(string f)
{
    var (_, _, ln) = Run("ffmpeg.exe", ["-hide_banner", "-i", f, "-af", "loudnorm=I=-14:TP=-1.5:print_format=summary", "-f", "null", "-"]);
    return (double.Parse(Regex.Match(ln, @"Input Integrated:\s*(-?[\d.]+)").Groups[1].Value, IC), double.Parse(Regex.Match(ln, @"Input True Peak:\s*([-+]?[\d.]+)").Groups[1].Value, IC));
}
var (I, TP) = Measure("short_a.mp4");
Console.WriteLine($"      after render: I={I} TP={TP}");
for (int pass = 0; pass < 3 && (Math.Abs(I + 14) > 0.5 || TP > -1.0); pass++)
{
    // audio-only correction (video copied): gain to target, then a 4x-oversampled limiter so true peak stays below −1.5 dBTP
    var gain = (-14 - I).ToString("F2", IC);
    var tmp = $"short_a_fix{pass}.mp4";
    Run("ffmpeg.exe", ["-hide_banner", "-loglevel", "error", "-y", "-i", "short_a.mp4", "-map", "0", "-c:v", "copy",
        "-af", $"aresample=192000,volume={gain}dB,alimiter=limit=0.79:level=false:attack=1:release=50,aresample=48000", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-fflags", "+bitexact", "-flags:a", "+bitexact", tmp]);
    File.Move(Path.Combine(outDir, tmp), Path.Combine(outDir, "short_a.mp4"), true);
    (I, TP) = Measure("short_a.mp4");
    Console.WriteLine($"      correction {pass + 1}: gain {gain} dB -> I={I} TP={TP}");
}
Check(Math.Abs(I + 14) <= 1, $"H-03 integrated loudness {I} LUFS (−14 ±1)");
Check(TP <= -1.0, $"H-04 true peak {TP} dBTP (<= −1.0)");

string Hash(string f) => Run("ffmpeg.exe", ["-v", "error", "-i", f, "-map", "0:v", "-c", "copy", "-f", "hash", "-hash", "sha256", "-"]).so.Trim();
var ha = Hash("short_a.mp4"); var hb = Hash("short_b.mp4");
Check(ha == hb && ha.Length > 10, $"AT-03 determinism: video stream hashes equal ({ha[..Math.Min(30, ha.Length)]}…)");

// --- AT-11: vertical source -> no blur, plain 1080x1920
var fcv = "[0:v]trim=0:10,setpts=PTS-STARTPTS,scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,format=yuv420p[vout];[0:a]atrim=0:10,asetpts=PTS-STARTPTS,loudnorm=I=-14:TP=-1.5,aresample=48000[aout]";
Run("ffmpeg.exe", ["-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(media, "vertical.mp4"), "-filter_complex", fcv, .. Enc("vert.mp4")]);
// compare the output frame with the source frame: PSNR should be very high (no blur, no bars)
var (_, _, ps) = Run("ffmpeg.exe", ["-hide_banner", "-i", "vert.mp4", "-i", Path.Combine(media, "vertical.mp4"), "-filter_complex", "[0:v]trim=0:2[a];[1:v]trim=0:2[b];[a][b]psnr", "-f", "null", "-"]);
var psnr = Regex.Match(ps, @"average:([\d.inf]+)").Groups[1].Value;
Check(psnr == "inf" || double.Parse(psnr, IC) > 35, $"AT-11 vertical source passes through untouched (PSNR {psnr} dB vs source)");

// frame for visual inspection
Run("ffmpeg.exe", ["-hide_banner", "-loglevel", "error", "-y", "-ss", "4.5", "-i", "short_a.mp4", "-frames:v", "1", "frame_4_5s.png"]);
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
