// Spike 25: measure the on-screen style of vertical shorts — reference channels and the app's own P12 output with the same yardstick.
//  Frames at 2 fps → Windows OCR (ja) → text lines (box, glyph height, position, colour); ffmpeg scdet → cuts / ASL;
//  ebur128 → loudness; silencedetect → share of silence. Nothing here is copied from the videos: only numbers are reported.
// Usage: dotnet run spikes/25-shorts-style.cs <folder-or-mp4>... [--out report.json]
//  Files are grouped by the prefix before the first '_' (qk_xxx.mp4 → qk); a group's medians are printed after its videos.
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
string? outJson = null, linesJson = null;
int? thumbWidth = null;
var inputs = new List<string>();
for (int i = 0; i < args.Length; i++)
    if (args[i] == "--out") outJson = args[++i];
    // --lines: every OCR line per frame (t, text, box) for comparison with the intended text
    else if (args[i] == "--lines") linesJson = args[++i];
    // --thumb N: frames are first shrunk to N px wide (the Shorts feed shows ≈ 270 px), then enlarged back for OCR — a legibility test
    else if (args[i] == "--thumb") thumbWidth = int.Parse(args[++i]);
    else if (Directory.Exists(args[i])) inputs.AddRange(Directory.GetFiles(args[i], "*.mp4").Order(StringComparer.Ordinal));
    else inputs.Add(args[i]);
var ocr = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ja")) ?? throw new InvalidOperationException("Japanese OCR is not installed");
const double Fps = 2, H = 1920, W = 1080;

var results = new List<VideoStats>();
var allLines = new Dictionary<string, List<FrameText>>();
foreach (var video in inputs)
{
    var name = Path.GetFileNameWithoutExtension(video).Split('.')[0];
    var tmp = Path.Combine(Path.GetTempPath(), "vc-s25-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    try
    {
        double duration = double.Parse(Run("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", video]).Trim());
        var shrink = thumbWidth is { } tw ? $"scale={tw}:-2:flags=area," : "";
        Run("ffmpeg", ["-v", "error", "-i", video, "-vf", $"fps={Fps},{shrink}scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2", Path.Combine(tmp, "%05d.png")]);
        var frames = new List<FrameText>();
        foreach (var (f, idx) in Directory.GetFiles(tmp, "*.png").Order(StringComparer.Ordinal).Select((f, i) => (f, i)))
            frames.Add(await OcrFrame(f, idx / Fps));
        allLines[name] = frames;
        var cuts = Cuts(video);
        var (lufs, lra) = Loudness(video);
        double silence = Silence(video, duration);
        results.Add(Summarise(name, duration, frames, cuts, lufs, lra, silence));
        Console.Error.WriteLine($"done {name}");
    }
    finally { Directory.Delete(tmp, true); }
}

Console.WriteLine(VideoStats.Header);
foreach (var g in results.GroupBy(r => r.Name.Split('_')[0]))
{
    foreach (var r in g) Console.WriteLine(r.Row());
    if (g.Count() > 1) Console.WriteLine(VideoStats.Median(g.Key + " (median)", g.ToList()).Row());
}
if (outJson != null)
    File.WriteAllText(outJson, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
if (linesJson != null) File.WriteAllText(linesJson, JsonSerializer.Serialize(allLines, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

async Task<FrameText> OcrFrame(string path, double t)
{
    var file = await StorageFile.GetFileFromPathAsync(path);
    using var stream = await file.OpenAsync(FileAccessMode.Read);
    var decoder = await BitmapDecoder.CreateAsync(stream);
    using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    var px = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
    int stride = (int)decoder.PixelWidth * 4;
    var res = await ocr.RecognizeAsync(bmp);
    var lines = new List<TextLine>();
    foreach (var l in res.Lines)
    {
        var text = Regex.Replace(l.Text, @"\s+", "");
        // OCR noise on footage: single characters and tiny boxes are not telops
        if (text.Length < 2 || !text.Any(c => char.IsLetterOrDigit(c))) continue;
        var rects = l.Words.Select(w => w.BoundingRect).ToList();
        double x0 = rects.Min(r => r.X), y0 = rects.Min(r => r.Y), x1 = rects.Max(r => r.X + r.Width), y1 = rects.Max(r => r.Y + r.Height);
        double glyph = Median(rects.Select(r => (double)r.Height).ToList());
        if (glyph < 18) continue;
        lines.Add(new TextLine(text, x0, y0, x1, y1, glyph, Colourfulness(px, stride, x0, y0, x1, y1)));
    }
    return new FrameText(t, lines);
}

// share of strongly coloured, bright pixels inside the box (white/black telops ≈ 0, coloured fills ≫ 0)
static double Colourfulness(byte[] px, int stride, double x0, double y0, double x1, double y1)
{
    int n = 0, hit = 0;
    for (int y = (int)y0; y < (int)y1; y += 2)
        for (int x = (int)x0; x < (int)x1; x += 2)
        {
            int o = y * stride + x * 4;
            if (o + 2 >= px.Length) continue;
            double b = px[o] / 255.0, g = px[o + 1] / 255.0, r = px[o + 2] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            n++;
            if (max > 0.55 && (max - min) / max > 0.55) hit++;
        }
    return n == 0 ? 0 : (double)hit / n;
}

List<double> Cuts(string video)
{
    var log = Run("ffmpeg", ["-hide_banner", "-i", video, "-vf", "scale=270:480,scdet=threshold=12", "-an", "-f", "null", "-"], stderr: true);
    return Regex.Matches(log, @"lavfi\.scd\.time:\s*([\d.]+)").Select(m => double.Parse(m.Groups[1].Value)).ToList();
}

(double, double) Loudness(string video)
{
    var log = Run("ffmpeg", ["-hide_banner", "-i", video, "-vn", "-af", "ebur128", "-f", "null", "-"], stderr: true);
    var i = Regex.Matches(log, @"I:\s*(-?[\d.]+) LUFS"); var lra = Regex.Matches(log, @"LRA:\s*([\d.]+) LU");
    return (i.Count > 0 ? double.Parse(i[^1].Groups[1].Value) : double.NaN, lra.Count > 0 ? double.Parse(lra[^1].Groups[1].Value) : double.NaN);
}

double Silence(string video, double duration)
{
    var log = Run("ffmpeg", ["-hide_banner", "-i", video, "-vn", "-af", "silencedetect=noise=-40dB:d=0.25", "-f", "null", "-"], stderr: true);
    return Regex.Matches(log, @"silence_duration:\s*([\d.]+)").Sum(m => double.Parse(m.Groups[1].Value)) / duration;
}

string Run(string tool, string[] a, bool stderr = false)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, tool + ".exe")) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var x in a) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!;
    var err = p.StandardError.ReadToEndAsync(); var outp = p.StandardOutput.ReadToEndAsync();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new InvalidOperationException($"{tool} failed: {err.Result[^Math.Min(400, err.Result.Length)..]}");
    return stderr ? err.Result : outp.Result;
}

static double Median(List<double> xs) => Pct(xs, 0.5);
static double Pct(List<double> xs, double q)
{
    if (xs.Count == 0) return double.NaN;
    var s = xs.Order().ToList();
    double pos = q * (s.Count - 1); int lo = (int)pos;
    return lo + 1 < s.Count ? s[lo] + (s[lo + 1] - s[lo]) * (pos - lo) : s[lo];
}

VideoStats Summarise(string name, double duration, List<FrameText> frames, List<double> cuts, double lufs, double lra, double silence)
{
    // the app keeps its title bar below the 250 px top safe edge, so "top" reaches 25 % of the height (480 px)
    const double TopBand = 0.25 * H;
    bool IsTop(TextLine l) => (l.Y0 + l.Y1) / 2 < TopBand;
    var withText = frames.Where(f => f.Lines.Count > 0).ToList();
    var body = frames.SelectMany(f => f.Lines.Where(l => !IsTop(l))).ToList();
    var all = frames.SelectMany(f => f.Lines).ToList();
    // telop births: a body line not seen (similar text) in the previous frame
    int births = 0;
    for (int i = 0; i < frames.Count; i++)
        foreach (var l in frames[i].Lines.Where(l => !IsTop(l)))
            if (i == 0 || !frames[i - 1].Lines.Any(p => Similar(p.Text, l.Text))) births++;
    var first2 = frames.Where(f => f.T <= 2.0).ToList();
    // the biggest glyph per frame vs. that frame's smallest body glyph: the size hierarchy on screen
    var perFrameMax = withText.Select(f => f.Lines.Max(l => l.Glyph)).ToList();
    return new VideoStats
    {
        Name = name, Duration = duration,
        Asl = duration / (cuts.Count + 1), CutsPerMin = cuts.Count / duration * 60,
        TextFrames = (double)withText.Count / frames.Count,
        TopTitleFrames = (double)frames.Count(f => f.Lines.Any(IsTop)) / frames.Count,
        TextAtStart = first2.Count(f => f.Lines.Count > 0) / (double)Math.Max(1, first2.Count),
        StartMaxGlyph = first2.SelectMany(f => f.Lines).Select(l => l.Glyph).DefaultIfEmpty(0).Max(),
        BodyGlyphP50 = Median(body.Select(l => l.Glyph).ToList()),
        BodyGlyphP90 = Pct(body.Select(l => l.Glyph).ToList(), 0.9),
        TopGlyphP50 = Median(all.Where(IsTop).Select(l => l.Glyph).ToList()),
        FrameMaxGlyphP50 = Median(perFrameMax),
        LinesPerTextFrame = withText.Count == 0 ? 0 : withText.Average(f => f.Lines.Count),
        BodyYP25 = Pct(body.Select(l => (l.Y0 + l.Y1) / 2 / H).ToList(), 0.25),
        BodyYP50 = Median(body.Select(l => (l.Y0 + l.Y1) / 2 / H).ToList()),
        BodyYP75 = Pct(body.Select(l => (l.Y0 + l.Y1) / 2 / H).ToList(), 0.75),
        BodyWidthP50 = Median(body.Select(l => (l.X1 - l.X0) / W).ToList()),
        ColouredShare = body.Count == 0 ? 0 : (double)body.Count(l => l.Colour > 0.12) / body.Count,
        TelopsPerMin = births / duration * 60,
        Lufs = lufs, Lra = lra, Silence = silence,
    };
}

static bool Similar(string a, string b)
{
    if (a == b) return true;
    int common = a.Intersect(b).Count();
    return common >= 0.6 * Math.Min(a.Distinct().Count(), b.Distinct().Count());
}

record TextLine(string Text, double X0, double Y0, double X1, double Y1, double Glyph, double Colour);
record FrameText(double T, List<TextLine> Lines);

record VideoStats
{
    public string Name { get; init; } = "";
    public double Duration { get; init; }
    public double Asl { get; init; }
    public double CutsPerMin { get; init; }
    public double TextFrames { get; init; }
    public double TopTitleFrames { get; init; }
    public double TextAtStart { get; init; }
    public double StartMaxGlyph { get; init; }
    public double BodyGlyphP50 { get; init; }
    public double BodyGlyphP90 { get; init; }
    public double TopGlyphP50 { get; init; }
    public double FrameMaxGlyphP50 { get; init; }
    public double LinesPerTextFrame { get; init; }
    public double BodyYP25 { get; init; }
    public double BodyYP50 { get; init; }
    public double BodyYP75 { get; init; }
    public double BodyWidthP50 { get; init; }
    public double ColouredShare { get; init; }
    public double TelopsPerMin { get; init; }
    public double Lufs { get; init; }
    public double Lra { get; init; }
    public double Silence { get; init; }

    public const string Header = "name\tdur\tASL\tcuts/min\ttext%\ttop%\tstart%\tstartMaxPx\tbodyPx50\tbodyPx90\ttopPx\tframeMaxPx\tlines\tY25\tY50\tY75\twidth\tcolour%\ttelops/min\tLUFS\tLRA\tsilence%";
    public string Row() => string.Join('\t', Name, F(Duration, 1), F(Asl, 2), F(CutsPerMin, 1), P(TextFrames), P(TopTitleFrames), P(TextAtStart), F(StartMaxGlyph, 0),
        F(BodyGlyphP50, 0), F(BodyGlyphP90, 0), F(TopGlyphP50, 0), F(FrameMaxGlyphP50, 0), F(LinesPerTextFrame, 2), F(BodyYP25, 2), F(BodyYP50, 2), F(BodyYP75, 2),
        F(BodyWidthP50, 2), P(ColouredShare), F(TelopsPerMin, 1), F(Lufs, 1), F(Lra, 1), P(Silence));
    static string F(double x, int d) => double.IsNaN(x) ? "-" : x.ToString("F" + d, CultureInfo.InvariantCulture);
    static string P(double x) => double.IsNaN(x) ? "-" : (x * 100).ToString("F0", CultureInfo.InvariantCulture);

    public static VideoStats Median(string name, List<VideoStats> xs)
    {
        double M(Func<VideoStats, double> f) => Pct(xs.Select(f).Where(v => !double.IsNaN(v)).ToList(), 0.5);
        static double Pct(List<double> v, double q)
        {
            if (v.Count == 0) return double.NaN;
            var s = v.Order().ToList(); double pos = q * (s.Count - 1); int lo = (int)pos;
            return lo + 1 < s.Count ? s[lo] + (s[lo + 1] - s[lo]) * (pos - lo) : s[lo];
        }
        return new VideoStats
        {
            Name = name, Duration = M(x => x.Duration), Asl = M(x => x.Asl), CutsPerMin = M(x => x.CutsPerMin), TextFrames = M(x => x.TextFrames),
            TopTitleFrames = M(x => x.TopTitleFrames), TextAtStart = M(x => x.TextAtStart), StartMaxGlyph = M(x => x.StartMaxGlyph),
            BodyGlyphP50 = M(x => x.BodyGlyphP50), BodyGlyphP90 = M(x => x.BodyGlyphP90), TopGlyphP50 = M(x => x.TopGlyphP50), FrameMaxGlyphP50 = M(x => x.FrameMaxGlyphP50),
            LinesPerTextFrame = M(x => x.LinesPerTextFrame), BodyYP25 = M(x => x.BodyYP25), BodyYP50 = M(x => x.BodyYP50), BodyYP75 = M(x => x.BodyYP75),
            BodyWidthP50 = M(x => x.BodyWidthP50), ColouredShare = M(x => x.ColouredShare), TelopsPerMin = M(x => x.TelopsPerMin),
            Lufs = M(x => x.Lufs), Lra = M(x => x.Lra), Silence = M(x => x.Silence),
        };
    }
}
