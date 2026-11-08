using System.Security.Cryptography;
using System.Text;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

public static class RenderProvenance
{
    public static async Task<string> HashFileAsync(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public static async Task<RenderReproducibility> CaptureAsync(FFmpeg ff, EditPlan plan, Preset preset,
        IEnumerable<string> sources, string ass, CancellationToken ct)
    {
        var build = await ff.RunFfmpegCheckedAsync(["-hide_banner", "-buildconf"], ct);
        var text = await ff.RunFfmpegCheckedAsync(["-hide_banner", "-nostdin", "-f", "lavfi", "-i", "color=s=32x32:d=0.1",
            "-vf", $"subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(ff.FontsDir)}", "-frames:v", "1", "-f", "null", "-"], ct);
        var libraries = string.Join('\n', text.StdErr.Split('\n')
            .Where(l => l.Contains("libass ", StringComparison.Ordinal) || l.Contains("Shaper:", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf(']') + 1)..].Trim()));
        if (!libraries.Contains("libass API version:", StringComparison.Ordinal) || !libraries.Contains("HarfBuzz", StringComparison.Ordinal))
            throw new FFmpegException("字幕ライブラリの識別情報を取得できません");
        async Task<List<HashedFile>> Hashes(IEnumerable<string> files)
        {
            var result = new List<HashedFile>();
            foreach (var file in files.Distinct().Order(StringComparer.Ordinal))
                result.Add(new HashedFile(file, await HashFileAsync(file, ct)));
            return result;
        }
        var fonts = Directory.EnumerateFiles(ff.FontsDir, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc").ToList();
        if (fonts.Count == 0) throw new FFmpegException("同梱フォントのハッシュを取得できません");
        return new RenderReproducibility
        {
            PlanHash = EditPlanFingerprint.Of(plan),
            FfmpegSha256 = await HashFileAsync(ff.FfmpegPath, ct),
            BuildconfSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(build.StdOut + build.StdErr))),
            TextLibraryInfo = libraries,
            FontSha256 = await Hashes(fonts),
            Preset = preset.Ref,
            PresetSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Serialize(preset)))),
            SourceSha256 = await Hashes(sources),
            EncoderThreads = ff.Threads,
        };
    }
}
