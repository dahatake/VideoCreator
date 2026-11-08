using System.Globalization;
using VideoCreator.Models;

namespace VideoCreator.Media;

/// <summary>Bounded visual sampling: prefer clear, changing frames across the source, excluding black/white and near-duplicates.</summary>
public sealed class SceneCardExtractor(FFmpeg ff)
{
    const int W = 32, H = 18, Bytes = W * H * 3;
    sealed record Frame(double T, byte[] Pixels, double Score);
    static string F(double t) => t.ToString("0.######", CultureInfo.InvariantCulture);

    public Task<List<SceneCardImage>> ExtractAsync(SourceAsset source, string outputDir, int count, CancellationToken ct)
        => ExtractAsync(source.Path, source.Id, null, outputDir, count, ct);

    /// <summary>From a registered opening / closing video; its brand-kit registration is the rights basis.</summary>
    public Task<List<SceneCardImage>> ExtractAsync(BrandFile bumper, string outputDir, int count, CancellationToken ct)
        => ExtractAsync(bumper.Path, null, bumper.Path, outputDir, count, ct);

    async Task<List<SceneCardImage>> ExtractAsync(string videoPath, string? assetId, string? brandPath, string outputDir, int count, CancellationToken ct)
    {
        if (count is < 2 or > 6) throw new ArgumentOutOfRangeException(nameof(count));
        var media = await new MediaProbe(ff).ProbeAsync(videoPath, ct);
        if (!double.IsFinite(media.Duration) || media.Duration <= 0 || media.Width <= 0 || media.Height <= 0)
            throw new IngestException("E-CARD-IMAGES", "画像を抽出できる長さ・解像度の動画ではありません");
        Directory.CreateDirectory(outputDir);
        var frames = new List<Frame>();
        byte[]? previous = null;
        int samples = count * 8;
        double lastFrame = Math.Max(0, media.Duration - (media.Fps > 0 ? 1 / media.Fps : 0.1));
        for (int i = 0; i < samples; i++)
        {
            double t = Math.Min(media.Duration * (i + 0.5) / samples, lastFrame);
            using var buffer = new MemoryStream();
            var r = await ff.RunFfmpegAsync(["-v", "error", "-nostdin", "-ss", F(t), "-i", videoPath,
                "-map", "0:v:0", "-an", "-frames:v", "1", "-vf", $"scale={W}:{H}:flags=area",
                "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], ct, stdout: buffer);
            if (r.ExitCode != 0) throw new FFmpegException("カード候補の解析に失敗しました: " + FFmpeg.Tail(r.StdErr));
            var pixels = buffer.ToArray();
            if (pixels.Length != Bytes) throw new IngestException("E-CARD-IMAGES", "動画の候補フレームを読み取れませんでした");
            var luma = Enumerable.Range(0, W * H).Select(k => 0.2126 * pixels[k * 3] + 0.7152 * pixels[k * 3 + 1] + 0.0722 * pixels[k * 3 + 2]).ToArray();
            double mean = luma.Average();
            double change = previous == null ? 0 : Difference(previous, pixels);
            previous = pixels;
            if (mean < 12 || mean > 245 || luma.Count(x => x < 16) > luma.Length * 0.9) continue;
            double contrast = Math.Sqrt(luma.Average(x => (x - mean) * (x - mean)));
            double edges = Enumerable.Range(0, luma.Length).Where(k => k % W != 0).Average(k => Math.Abs(luma[k] - luma[k - 1]));
            frames.Add(new(t, pixels, contrast + edges + change));
        }
        var selected = new List<Frame>();
        // First choose one representative per time bin, then fill any gaps from the remaining distinct candidates.
        for (int bin = 0; bin < count; bin++)
        {
            var best = frames.Where(f => (int)(f.T / media.Duration * count) == bin)
                .OrderByDescending(f => f.Score).FirstOrDefault(f => selected.All(s => Difference(s.Pixels, f.Pixels) >= 10));
            if (best != null) selected.Add(best);
        }
        foreach (var f in frames.OrderByDescending(f => f.Score))
        {
            if (selected.Count >= count) break;
            if (selected.All(s => Difference(s.Pixels, f.Pixels) >= 10)) selected.Add(f);
        }
        if (selected.Count == 0)
            throw new IngestException("E-CARD-IMAGES", "黒画面・白画面以外のカード画像が見つかりませんでした。別の動画か画像を選んでください");
        var images = new List<SceneCardImage>();
        foreach (var frame in selected.OrderBy(f => f.T))
        {
            var path = Path.Combine(outputDir, $"highlight-{images.Count + 1:00}.png");
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-nostdin", "-n", "-ss", F(frame.T), "-i", videoPath,
                "-map", "0:v:0", "-an", "-frames:v", "1", "-update", "1", path], ct);
            var image = await new MediaProbe(ff).ProbeAsync(path, ct);
            images.Add(new(path, image.Width, image.Height, assetId, frame.T, brandPath));
        }
        return images;
    }

    static double Difference(byte[] a, byte[] b) => Enumerable.Range(0, Bytes).Average(i => Math.Abs(a[i] - b[i]));
}
