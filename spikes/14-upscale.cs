// Spike 14: which upscaler should turn a 720x1280 opening/closing into 1080x1920? Decided by measurement, not by taste.
// Protocol (full-reference SR evaluation): downscale the original to 2/3 (area), upscale it back 1.5x with each method, and compare with the
// original using FFmpeg's VMAF (NEG model: not fooled by sharpening), XPSNR and SSIM. "detail transfer" is the app's actual pipeline
// (SuperResolution.UpscaleVideoAsync). The last run times the real job (full-resolution source → 1.5x).
// Usage: dotnet run spikes/14-upscale.cs [video ...]
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using VideoCreator.Media;

var root = Directory.GetCurrentDirectory();
var ff = FFmpeg.Locate(start: root);
var files = args.Length > 0 ? args : [@"C:\Work\HubRadio\OpeningClosing\Opening-9x16.mp4", @"C:\Work\HubRadio\OpeningClosing\Closing-9x16.mp4"];
var model = Path.Combine(root, "tools", "models", SuperResolution.ModelFile);
var outDir = Path.Combine(root, "spikes", "out", "upscale"); Directory.CreateDirectory(outDir);
var IC = CultureInfo.InvariantCulture;
var ct = CancellationToken.None;
using var sr = new SuperResolution(model);
Console.WriteLine($"model on {sr.Device}");
int fails = 0;

foreach (var src in files)
{
    var m = await new MediaProbe(ff).ProbeAsync(src);
    int W = m.Width, H = m.Height, lw = (int)Math.Round(W * 2.0 / 3 / 2) * 2, lh = (int)Math.Round(H * 2.0 / 3 / 2) * 2;
    var name = Path.GetFileNameWithoutExtension(src);
    var lo = Path.Combine(outDir, $"{name}_lo.mp4");
    await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-i", src, "-an", "-vf", $"scale={lw}:{lh}:flags=area,format=yuv420p", "-c:v", "libx264", "-crf", "4", "-preset", "veryfast", lo]);

    async Task<(double vmaf, double xpsnr, double ssim)> Score(string candidate)
    {
        async Task<string> Run(string f) => (await ff.RunFfmpegAsync(["-hide_banner", "-i", candidate, "-i", src, "-filter_complex", $"[0:v]setsar=1[c];[1:v]setsar=1[r];[c][r]{f}[o]", "-map", "[o]", "-f", "null", "-"], ct)).StdErr;
        double N(string s, string p) => double.Parse(Regex.Match(s, p).Groups[1].Value, IC);
        return (N(await Run("libvmaf=n_threads=8:model=version=vmaf_v0.6.1neg"), @"VMAF score: ([\d.]+)"), N(await Run("xpsnr"), @"XPSNR\s+y:\s*([\d.]+)"), N(await Run("ssim"), @"All:([\d.]+)"));
    }

    var rows = new List<(string name, double vmaf, double xpsnr, double ssim)>();
    foreach (var (label, vf) in new[] { ("lanczos (render default)", $"scale={W}:{H}:flags=lanczos"), ("lanczos + CAS 0.4", $"scale={W}:{H}:flags=lanczos,cas=strength=0.4"), ("spline36", $"zscale=w={W}:h={H}:filter=spline36") })
    {
        var f = Path.Combine(outDir, $"{name}_{rows.Count}.mp4");
        await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-i", lo, "-vf", vf + ",format=yuv420p", "-c:v", "libx264", "-crf", "10", "-preset", "slow", f]);   // same encode as the app's cached file
        var s = await Score(f); rows.Add((label, s.vmaf, s.xpsnr, s.ssim));
    }
    var hy = Path.Combine(outDir, $"{name}_detail.mp4");
    await sr.UpscaleVideoAsync(ff, lo, lw, lh, m.Fps, W, H, hy, null, ct);
    var h = await Score(hy); rows.Add(("detail transfer (app)", h.vmaf, h.xpsnr, h.ssim));

    Console.WriteLine($"\n{name}: {W}x{H} → {lw}x{lh} → back");
    Console.WriteLine($"{"method",-28} {"VMAF-NEG",9} {"XPSNR-Y",8} {"SSIM",9}");
    foreach (var r in rows) Console.WriteLine($"{r.name,-28} {r.vmaf,9:F2} {r.xpsnr,8:F2} {r.ssim,9:F5}");
    var b = rows[0];
    bool ok = h.vmaf >= b.vmaf && h.xpsnr >= b.xpsnr - 0.05 && h.ssim >= b.ssim - 0.00005;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  detail transfer ≥ lanczos on all three (ΔVMAF {h.vmaf - b.vmaf:+0.00;-0.00}, ΔXPSNR {h.xpsnr - b.xpsnr:+0.00;-0.00} dB, ΔSSIM {h.ssim - b.ssim:+0.00000;-0.00000})");
    if (!ok) fails++;
}

var first = files[0]; var fm = await new MediaProbe(ff).ProbeAsync(first);
var full = Path.Combine(outDir, "full_1080x1920.mp4"); var sw = Stopwatch.StartNew();
await sr.UpscaleVideoAsync(ff, first, fm.Width, fm.Height, fm.Fps, (int)Math.Round(fm.Width * 1.5 / 2) * 2, (int)Math.Round(fm.Height * 1.5 / 2) * 2, full, null, ct);
Console.WriteLine($"\nreal job: {Path.GetFileName(first)} {fm.Duration:F1}s → 1.5x in {sw.Elapsed.TotalSeconds:F0}s on {sr.Device} (model {sr.ModelSeconds:F0}s, waiting for the encoder {sr.WriteSeconds:F0}s) → {full}");
return fails;
