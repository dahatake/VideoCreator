// Spike 28: per-frame telop measurement of variety videos (docs/IMPLEMENTATION.md §25) — reference videos and the app's own P3 output alike.
//  ffmpeg decodes N fps raw BGRA frames into a pipe (no images on disk) → Windows OCR (ja) → every text line: text, box, glyph height and
//  its dominant colours (quantised), plus the colour just outside the box. Only numbers and short OCR strings are written; nothing is kept from
//  the video. The tracking into telop events and all statistics are done by spikes/28-variety-telops.py.
// Usage: dotnet run spikes/28-variety-telops.cs <video.mp4> <out.jsonl> [--fps 4] [--from s] [--to s]
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
string video = args[0], outPath = args[1];
double fps = 4, from = 0, to = double.NaN;
for (int i = 2; i < args.Length; i++)
    switch (args[i])
    {
        case "--fps": fps = double.Parse(args[++i]); break;
        case "--from": from = double.Parse(args[++i]); break;
        case "--to": to = double.Parse(args[++i]); break;
    }
var ocr = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ja")) ?? throw new InvalidOperationException("Japanese OCR is not installed");

var probe = Run("ffprobe", ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", video]).Trim().Split(',');
int W = int.Parse(probe[0]), H = int.Parse(probe[1]);
var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
var ff = new List<string> { "-v", "error", "-ss", from.ToString(), "-i", video };
if (!double.IsNaN(to)) ff.AddRange(["-t", (to - from).ToString()]);
ff.AddRange(["-vf", $"fps={fps}", "-pix_fmt", "bgra", "-f", "rawvideo", "-"]);
foreach (var a in ff) psi.ArgumentList.Add(a);
using var proc = Process.Start(psi)!;
_ = proc.StandardError.ReadToEndAsync();
var stdout = proc.StandardOutput.BaseStream;
int frameBytes = W * H * 4;
var px = new byte[frameBytes];
using var outFile = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
outFile.WriteLine(JsonSerializer.Serialize(new { video = Path.GetFileName(video), width = W, height = H, fps, from }));
int idx = 0; var sw = Stopwatch.StartNew();
while (true)
{
    int read = 0;
    while (read < frameBytes) { int n = stdout.Read(px, read, frameBytes - read); if (n <= 0) break; read += n; }
    if (read < frameBytes) break;
    double t = from + idx / fps;
    using var bmp = SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8, W, H, BitmapAlphaMode.Premultiplied);
    var res = await ocr.RecognizeAsync(bmp);
    var lines = new List<object>();
    foreach (var l in res.Lines)
    {
        var text = Regex.Replace(l.Text, @"\s+", "");
        if (text.Length < 1 || !text.Any(char.IsLetterOrDigit)) continue;
        var rects = l.Words.Select(w => w.BoundingRect).ToList();
        double x0 = rects.Min(r => r.X), y0 = rects.Min(r => r.Y), x1 = rects.Max(r => r.X + r.Width), y1 = rects.Max(r => r.Y + r.Height);
        double glyph = rects.Select(r => (double)r.Height).Order().ElementAt(rects.Count / 2);
        if (glyph < 0.012 * H) continue;
        lines.Add(new { text, box = new[] { R(x0), R(y0), R(x1), R(y1) }, glyph = R(glyph), colors = TopColours(x0, y0, x1, y1), ring = Ring(x0, y0, x1, y1) });
    }
    outFile.WriteLine(JsonSerializer.Serialize(new { t = Math.Round(t, 3), lines }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    if (++idx % 400 == 0) Console.Error.WriteLine($"{Path.GetFileName(video)} t={t:F0}s  {idx / sw.Elapsed.TotalSeconds:F1} frames/s");
}
proc.WaitForExit();
Console.Error.WriteLine($"done {idx} frames → {outPath}");

static double R(double x) => Math.Round(x, 1);

// colours inside the box, quantised to 8 levels per channel; the top 4 with their share (fill, strokes and background compete here)
object[] TopColours(double x0, double y0, double x1, double y1)
{
    var hist = new Dictionary<int, (int n, long r, long g, long b)>(); int total = 0;
    for (int y = Math.Max(0, (int)y0); y < Math.Min(H, (int)y1); y += 2)
        for (int x = Math.Max(0, (int)x0); x < Math.Min(W, (int)x1); x += 2)
        {
            int o = (y * W + x) * 4; int b = px[o], g = px[o + 1], r = px[o + 2];
            int key = (r >> 5) << 6 | (g >> 5) << 3 | b >> 5;
            var h = hist.GetValueOrDefault(key); hist[key] = (h.n + 1, h.r + r, h.g + g, h.b + b); total++;
        }
    return hist.OrderByDescending(kv => kv.Value.n).Take(4)
        .Select(kv => (object)new[] { (int)(kv.Value.r / kv.Value.n), (int)(kv.Value.g / kv.Value.n), (int)(kv.Value.b / kv.Value.n), Math.Round(kv.Value.n / (double)total, 3) }).ToArray();
}

// mean colour of a 6 px ring just outside the box (the background the telop sits on, or its own box/frame)
int[] Ring(double x0, double y0, double x1, double y1)
{
    long r = 0, g = 0, b = 0; int n = 0;
    void Add(int x, int y) { if (x < 0 || y < 0 || x >= W || y >= H) return; int o = (y * W + x) * 4; b += px[o]; g += px[o + 1]; r += px[o + 2]; n++; }
    int X0 = (int)x0 - 8, X1 = (int)x1 + 8, Y0 = (int)y0 - 8, Y1 = (int)y1 + 8;
    for (int x = X0; x <= X1; x += 3) for (int d = 0; d < 6; d += 2) { Add(x, Y0 + d); Add(x, Y1 - d); }
    for (int y = Y0; y <= Y1; y += 3) for (int d = 0; d < 6; d += 2) { Add(X0 + d, y); Add(X1 - d, y); }
    return n == 0 ? [0, 0, 0] : [(int)(r / n), (int)(g / n), (int)(b / n)];
}

string Run(string tool, string[] a)
{
    var p = new ProcessStartInfo(Path.Combine(bin, tool + ".exe")) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var x in a) p.ArgumentList.Add(x);
    using var pr = Process.Start(p)!;
    var o = pr.StandardOutput.ReadToEnd(); pr.WaitForExit();
    return o;
}
