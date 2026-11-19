// Spike 27: telop looks per template (docs/IMPLEMENTATION.md §23).
//  ass <outDir>               Writes one ASS per preset (P*.json) from the same fixed plan — compare two checkouts byte by byte.
//  render <outDir> <bg.png>   Also renders each preset's telops over a background still (1080×1920) at two moments, for viewing and OCR.
//  ocr <dir>...               Windows OCR (ja) of every P*_a/b.png scaled to 270 px wide (a Shorts grid tile): char-bigram recall of the telop texts.
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Diagnostics;
using System.Globalization;
using System.Text;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Rendering;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

var mode = args.ElementAtOrDefault(0) ?? "ass";
if (mode == "ocr") { await Ocr(args.Skip(1).ToArray()); return; }
var outDir = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "spikes/out/telop-looks");
Directory.CreateDirectory(outDir);
var root = AppContext.GetData("EntryPointFileDirectoryPath") is string d ? Path.GetFullPath(Path.Combine(d, "..")) : Directory.GetCurrentDirectory();
var presets = PresetStore.LoadDirectory(Path.Combine(root, "presets"));

static Telop T(string id, TelopType type, double s, double e, string text, string style, string? anim = null, int? speaker = null) =>
    new() { Id = id, Type = type, Start = s, End = e, Text = text, Style = style, Animation = anim, Speaker = speaker };

// moment A (1.0 s): caption + emphasis + headline + counter; moment B (3.0 s): speaker-2 caption + tsukkomi + sfx + headline
List<Telop> telops =
[
    T("h", TelopType.Headline, 0, 4, "1時間で1万円使い切れるか", "Headline"),
    T("c1", TelopType.Caption, 0, 2, "ネタを書いてる芸人です", "Caption", speaker: 1),
    T("e", TelopType.Emphasis, 0.5, 2, "1万円", "Emph", "pop"),
    T("n", TelopType.Counter, 0.5, 2, "残り30分", "Counter", "pop"),
    T("c2", TelopType.Caption, 2, 4, "それは1万円じゃないです", "Caption", speaker: 2),
    T("k", TelopType.Tsukkomi, 2.5, 4, "いや多すぎ！", "Tsukkomi", "pop"),
    T("s", TelopType.Sfx, 2.5, 4, "ドーン", "Sfx", "pop"),
];
foreach (var p in presets)
{
    var plan = new EditPlan
    {
        EditplanId = "e", ProjectId = "p", Preset = p.Ref, Audio = new AudioSpec { Master = "a1" },
        Timeline = [new Clip { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 4 }],
        Telops = telops.Where(t => t.Type == TelopType.Caption || p.HasTelop(t.Type switch
        {
            TelopType.Emphasis => "emphasis", TelopType.Tsukkomi => "tsukkomi", TelopType.Headline => "headline", TelopType.Sfx => "sfx", TelopType.Counter => "counter", _ => "caption",
        })).ToList(),
    };
    var ass = Path.Combine(outDir, p.Id + ".ass");
    File.WriteAllText(ass, AssWriter.Write(plan, p), new UTF8Encoding(false));
    if (mode != "render") continue;
    var bg = Path.GetFullPath(args[2]);
    var ff = Path.Combine(root, "tools", "ffmpeg", "bin", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    foreach (var (t, tag) in new[] { (1.0, "a"), (3.0, "b") })
    {
        string Esc(string s) => s.Replace("\\", "/").Replace(":", "\\:");
        var psi = new ProcessStartInfo(ff) { RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-loop", "1", "-i", bg, "-vf",
            $"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,subtitles='{Esc(ass)}':fontsdir='{Esc(Path.Combine(root, "tools", "fonts"))}'",
            "-ss", t.ToString(CultureInfo.InvariantCulture), "-frames:v", "1", Path.Combine(outDir, $"{p.Id}_{tag}.png") }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!; var err = proc.StandardError.ReadToEnd(); proc.WaitForExit();
        if (proc.ExitCode != 0) throw new Exception(err);
    }
}
Console.WriteLine($"{presets.Count} presets -> {outDir}");

// Expected texts per moment (only those the preset shows are scored).
static async Task Ocr(string[] dirs)
{
    var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ja")) ?? throw new InvalidOperationException("Japanese OCR is not installed");
    var expected = new Dictionary<string, string[]>
    {
        ["a"] = ["1時間で1万円使い切れるか", "ネタを書いてる芸人です", "1万円", "残り30分"],
        ["b"] = ["1時間で1万円使い切れるか", "それは1万円じゃないです", "いや多すぎ！", "ドーン"],
    };
    static string Norm(string s) => new(s.Normalize(NormalizationForm.FormKC).Where(ch => !char.IsWhiteSpace(ch) && ch is not ('!' or '！')).ToArray());
    static HashSet<string> Bigrams(string s) => Enumerable.Range(0, Math.Max(0, s.Length - 1)).Select(i => s.Substring(i, 2)).ToHashSet();
    foreach (var dir in dirs)
    {
        Console.WriteLine($"== {dir}");
        // expected.txt (optional): "<file.png>\t<text>" lines replace the fixed P*_a/b texts
        var custom = File.Exists(Path.Combine(dir, "expected.txt"))
            ? File.ReadAllLines(Path.Combine(dir, "expected.txt")).Select(l => l.Split('\t')).ToDictionary(x => x[0], x => x[1..]) : null;
        foreach (var png in (custom?.Keys.Select(k => Path.Combine(dir, k)) ?? Directory.GetFiles(dir, "P*_?.png")).OrderBy(f => f, StringComparer.Ordinal))
        {
            var small = Path.Combine(Path.GetTempPath(), $"vc27_{Guid.NewGuid():N}.png");
            var ff = Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "tools", "ffmpeg", "bin", "ffmpeg.exe");
            using (var p = Process.Start(new ProcessStartInfo(ff, ["-hide_banner", "-loglevel", "error", "-y", "-i", png, "-vf", "scale=270:-2:flags=area", small]))!)
            {
                p.WaitForExit();
                if (p.ExitCode != 0) throw new Exception($"ffmpeg failed for {png}");
            }
            var file = await StorageFile.GetFileFromPathAsync(small);
            using var stream = await file.OpenReadAsync();
            var bmp = await (await BitmapDecoder.CreateAsync(stream)).GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var text = Norm(string.Concat((await engine.RecognizeAsync(bmp)).Lines.Select(l => l.Text)));
            var tag = Path.GetFileNameWithoutExtension(png)[^1..];
            var scores = (custom?[Path.GetFileName(png)] ?? expected[tag]).Select(e => { var b = Bigrams(Norm(e)); return (e, r: b.Count(text.Contains) / (double)b.Count); }).ToList();
            Console.WriteLine($"{Path.GetFileName(png),-10} " + string.Join("  ", scores.Select(s => $"{s.e}={s.r:F2}")) + $"   | {text}");
        }
    }
}
