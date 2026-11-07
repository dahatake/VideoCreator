// Spike 09: FR-SIG-06 / FR-RFM-02/03 — face detection (UltraFace ONNX, ONNX Runtime) + smoothed 9:16 face-follow crop via an FFmpeg time expression.
// Ground truth: a public-domain NASA portrait (spikes/faces) is moved across a 1920x1080 canvas at a known speed.
#:property PublishAot=false
#:package Microsoft.ML.OnnxRuntime@1.31.1
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var faces = Path.Combine(root, "spikes", "faces");
var outDir = Path.Combine(root, "spikes", "out", "faces"); Directory.CreateDirectory(outDir);
var IC = CultureInfo.InvariantCulture;
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }
byte[] FF(string[] a, bool capture = true)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var x in a) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!; using var ms = new MemoryStream(); var e = p.StandardError.ReadToEndAsync();
    p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception(e.Result);
    return ms.ToArray();
}

using var session = new InferenceSession(Path.Combine(root, "tools", "models", "ultraface.onnx"), new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR });
Console.WriteLine("inputs: " + string.Join(", ", session.InputMetadata.Select(k => $"{k.Key}{string.Join('x', k.Value.Dimensions)}")) + " outputs: " + string.Join(", ", session.OutputMetadata.Keys));
const int W = 320, H = 240;

List<(float x1, float y1, float x2, float y2, float s)> Detect(byte[] rgb, int offset)
{
    var t = new DenseTensor<float>(new[] { 1, 3, H, W });
    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) for (int c = 0; c < 3; c++) t[0, c, y, x] = (rgb[offset + (y * W + x) * 3 + c] - 127f) / 128f;
    using var res = session.Run([NamedOnnxValue.CreateFromTensor("input", t)]);
    var scores = res.First(r => r.Name == "scores").AsTensor<float>(); var boxes = res.First(r => r.Name == "boxes").AsTensor<float>();
    var cand = new List<(float, float, float, float, float)>();
    for (int i = 0; i < scores.Dimensions[1]; i++) if (scores[0, i, 1] > 0.7f) cand.Add((boxes[0, i, 0], boxes[0, i, 1], boxes[0, i, 2], boxes[0, i, 3], scores[0, i, 1]));
    var keep = new List<(float x1, float y1, float x2, float y2, float s)>();
    foreach (var c in cand.OrderByDescending(c => c.Item5))
    {
        bool dup = keep.Any(k => { float ix = Math.Max(0, Math.Min(k.x2, c.Item3) - Math.Max(k.x1, c.Item1)), iy = Math.Max(0, Math.Min(k.y2, c.Item4) - Math.Max(k.y1, c.Item2)); float inter = ix * iy, u = (k.x2 - k.x1) * (k.y2 - k.y1) + (c.Item3 - c.Item1) * (c.Item4 - c.Item2) - inter; return inter / u > 0.3f; });
        if (!dup) keep.Add(c);
    }
    return keep;
}

// 1) stills: counts on real photographs (1, 1, 1 and 4 people)
var expected = new[] { 1, 1, 1, 4 };
for (int i = 0; i < 4; i++)
{
    var rgb = FF(["-hide_banner", "-loglevel", "error", "-i", Path.Combine(faces, $"face{i}.jpg"), "-vf", "scale=320:240", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);
    var d = Detect(rgb, 0);
    Check(d.Count == expected[i], $"face{i}.jpg: {d.Count} face(s) detected (expected {expected[i]}) {string.Join(" ", d.Select(f => $"[{f.x1:F2},{f.y1:F2}-{f.x2:F2},{f.y2:F2} s={f.s:F2}]"))}");
}

// 2) moving face video: portrait (face0) scaled to height 800 slides from x=200 to x=1300 over 10 s on a 1920x1080 canvas
var still = FF(["-hide_banner", "-loglevel", "error", "-i", Path.Combine(faces, "face1.jpg"), "-vf", "scale=-2:800", "-f", "image2pipe", "-vcodec", "png", "-"]);
File.WriteAllBytes(Path.Combine(outDir, "portrait.png"), still);
FF(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=0x303840:s=1920x1080:r=30:d=10", "-loop", "1", "-framerate", "30", "-i", Path.Combine(outDir, "portrait.png"),
    "-filter_complex", "[0:v][1:v]overlay=x='200+110*t':y=140:shortest=1[v]", "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", Path.Combine(outDir, "moving.mp4")]);
// locate the face inside the static portrait (relative to the overlay) with the same detector
var pr = FF(["-hide_banner", "-loglevel", "error", "-i", Path.Combine(outDir, "portrait.png"), "-vf", "scale=320:240", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);
var pface = Detect(pr, 0).First();
var pwImg = FF(["-hide_banner", "-i", Path.Combine(outDir, "portrait.png"), "-f", "null", "-"], false); // just to ensure decodable
// portrait is 800 high; width from aspect 2400x3000 -> 640
double portraitW = 640, portraitH = 800, faceCxRel = (pface.x1 + pface.x2) / 2 * portraitW;
Console.WriteLine($"face centre within portrait: x={faceCxRel:F0}px");

var vid = FF(["-hide_banner", "-loglevel", "error", "-i", Path.Combine(outDir, "moving.mp4"), "-vf", "fps=2,scale=320:240", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);
int frames = vid.Length / (W * H * 3);
var track = new List<(double t, double cx)>();
double sumErr = 0, maxErr = 0; int n1 = 0;
for (int k = 0; k < frames; k++)
{
    var d = Detect(vid, k * W * H * 3); double t = k / 2.0;
    if (d.Count != 1) continue;
    double cx = (d[0].x1 + d[0].x2) / 2 * 1920; track.Add((t, cx));
    double truth = 200 + 110 * t + faceCxRel; double err = Math.Abs(cx - truth); sumErr += err; maxErr = Math.Max(maxErr, err); n1++;
}
Check(n1 >= frames - 2, $"single face found in {n1}/{frames} sampled frames");
Check(sumErr / n1 < 40 && maxErr < 90, $"face x error vs ground truth: mean {sumErr / n1:F1}px, max {maxErr:F1}px (of 1920)");

// 3) smoothing + simplification -> FFmpeg crop expression
const int cropW = 607, srcW = 1920, srcH = 1080;   // 9:16 window of a 1080p frame
var smooth = new List<(double t, double x)>(); double prev = track[0].cx - cropW / 2.0;
foreach (var (t, cx) in track)
{
    var targetX = Math.Clamp(cx - cropW / 2.0, 0, srcW - cropW);
    // exponential smoothing + max speed 400 px/s
    var next = prev + Math.Clamp((targetX - prev) * 0.5, -200, 200); smooth.Add((t, next)); prev = next;
}
List<(double t, double x)> Simplify(List<(double t, double x)> pts, double tol)
{
    if (pts.Count < 3) return pts;
    double Dist(int i, int a, int b) { var (ta, xa) = pts[a]; var (tb, xb) = pts[b]; var xi = xa + (xb - xa) * (pts[i].t - ta) / (tb - ta); return Math.Abs(pts[i].x - xi); }
    var keep = new SortedSet<int> { 0, pts.Count - 1 }; var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
    while (stack.Count > 0) { var (a, b) = stack.Pop(); int far = -1; double md = tol; for (int i = a + 1; i < b; i++) { var d = Dist(i, a, b); if (d > md) { md = d; far = i; } } if (far >= 0) { keep.Add(far); stack.Push((a, far)); stack.Push((far, b)); } }
    return keep.Select(i => pts[i]).ToList();
}
var keys = Simplify(smooth, 6);
Console.WriteLine($"trajectory: {track.Count} samples -> {keys.Count} keyframes");
var sb = new StringBuilder();
for (int i = 0; i + 1 < keys.Count; i++)
{
    var (t0, x0) = keys[i]; var (t1, x1) = keys[i + 1];
    sb.Append($"between(t,{t0.ToString("0.###", IC)},{t1.ToString("0.###", IC)})*({x0.ToString("0.#", IC)}+({(x1 - x0).ToString("0.#", IC)})*(t-{t0.ToString("0.###", IC)})/{(t1 - t0).ToString("0.###", IC)})");
    if (i + 2 < keys.Count) sb.Append('+');
}
var expr = sb.ToString();
Check(expr.Length < 2500, $"crop x expression length {expr.Length} chars");
FF(["-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(outDir, "moving.mp4"), "-filter_complex", $"[0:v]crop={cropW}:{srcH}:x='{expr}':y=0,scale=1080:1920,setsar=1[v]",
    "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", Path.Combine(outDir, "followed.mp4")]);
// independent check: run the detector on the OUTPUT; the face should stay near the horizontal centre
var outv = FF(["-hide_banner", "-loglevel", "error", "-i", Path.Combine(outDir, "followed.mp4"), "-vf", "fps=2,scale=320:240", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);
int of = outv.Length / (W * H * 3); var cxs = new List<double>();
for (int k = 0; k < of; k++) { var d = Detect(outv, k * W * H * 3); if (d.Count == 1) cxs.Add((d[0].x1 + d[0].x2) / 2); }
Check(cxs.Count >= of - 2, $"face still detected in {cxs.Count}/{of} output frames");
Check(cxs.All(c => Math.Abs(c - 0.5) < 0.18), $"face stays within ±18% of the output centre (min {cxs.Min():F2}, max {cxs.Max():F2})");
double maxJump = cxs.Zip(cxs.Skip(1), (a, b) => Math.Abs(b - a)).Max();
Check(maxJump < 0.12, $"no jumps between samples (max {maxJump:F3} of width)");
// fixed centre crop for comparison: face leaves the frame
FF(["-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(outDir, "moving.mp4"), "-filter_complex", $"[0:v]crop={cropW}:{srcH}:{(srcW - cropW) / 2}:0,scale=1080:1920[v]", "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", Path.Combine(outDir, "fixed.mp4")]);
var fx = FF(["-hide_banner", "-loglevel", "error", "-i", Path.Combine(outDir, "fixed.mp4"), "-vf", "fps=2,scale=320:240", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]);
int fcount = 0; for (int k = 0; k < fx.Length / (W * H * 3); k++) if (Detect(fx, k * W * H * 3).Count == 1) fcount++;
Console.WriteLine($"for comparison, a static centre crop keeps the face in only {fcount}/{fx.Length / (W * H * 3)} frames");
Check(fcount < of, "face-follow keeps the face in frame more often than a static centre crop");

// 4) benchmark: 2 fps analysis of a 60-minute video = 7200 inferences
var sw = Stopwatch.StartNew(); for (int i = 0; i < 100; i++) Detect(vid, 0); sw.Stop();
Console.WriteLine($"inference {sw.ElapsedMilliseconds / 100.0:F1} ms/frame -> 60 min @2fps ≈ {sw.ElapsedMilliseconds / 100.0 * 7200 / 1000:F0} s");
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
