using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VideoCreator.Media;
using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>
/// Final-render super-resolution of screen-recording content that a PiP plan (P11) shows larger than it was recorded, e.g. a 640×360 demo in an
/// 860-pixel inset. Slides shown from the deck's own images are never touched; neither is content shown at or below its recorded size (the usual
/// case: a 1920×1080 desktop in a 1014-pixel inset is a downscale). For each recording/crop the method is first checked on that material
/// (<see cref="BrandKitEnhancer.SelfCheckAsync"/>: 2/3 round trip, SR vs lanczos against the original) and only adopted when it is at least as faithful;
/// then each magnified piece is cut, cropped, upscaled to its on-screen size, cached by content, and handed to the renderer in place of the recording.
/// The stored plan is never changed — the substitution exists only for the one render.
/// </summary>
public static class ScreenEnhancer
{
    /// <summary>Extra source seconds after each piece (cross-fade tails read past SrcOut).</summary>
    public const double TailSec = 1.0;

    /// <summary>One clip's magnified screen content. <see cref="Inset"/>: in a rectangle PiP (else a full-screen switch frame).</summary>
    public sealed record Piece(int Index, string AssetId, double T0, double Len, int X, int Y, int W, int H, int OutW, int OutH, double Factor, bool Inset);

    static int Even(double x) => Math.Max(2, (int)Math.Round(x / 2) * 2);

    /// <summary>Every non-slide screen piece of a PiP plan with the factor it is displayed at (crop → on-screen size).</summary>
    public static List<Piece> Pieces(EditPlan plan, IReadOnlyDictionary<string, SourceAsset> assets)
    {
        var result = new List<Piece>();
        if (!plan.Timeline.Any(c => c.Pip is { Shape: "rectangle" })) return result;
        for (int i = 0; i < plan.Timeline.Count; i++)
        {
            var c = plan.Timeline[i]; double len = c.SrcOut - c.SrcIn + TailSec * c.Speed;
            if (c.Pip is { Shape: "rectangle" } p && (p.Image == null || !File.Exists(p.Image)) && assets.TryGetValue(p.AssetId, out var pa))
            {
                var (x, y, w, h) = p.CropW > 0 && p.CropH > 0 ? (p.CropX, p.CropY, p.CropW & ~1, p.CropH & ~1) : (0, 0, pa.Media.Width & ~1, pa.Media.Height & ~1);
                if (w <= 0 || h <= 0 || p.Width <= 0 || p.Height <= 0) continue;
                double f = Math.Min(p.Width / (double)w, p.Height / (double)h);
                result.Add(new(i, p.AssetId, p.SrcIn, len, x, y, w, h, Even(w * f), Even(h * f), f, true));
            }
            else if (c.Pip == null && c.Reframe is { Mode: "screen", BoxH: > 0 } r && (r.Image == null || !File.Exists(r.Image)) && assets.TryGetValue(c.AssetId, out var sa)
                     && sa.Id != plan.Audio.Master)
            {
                if (r.CropW > 0)
                {
                    // the switch frame stretches a fixed crop to the band; a moving crop is left to the renderer
                    if (r.Track is not { Count: 1 } track || r.TrackY is { Count: > 1 }) continue;
                    int w = r.CropW & ~1, h = r.CropH & ~1, y = r.TrackY is { Count: 1 } ty ? (int)ty[0].X : r.CropY;
                    double f = Math.Min(plan.Canvas.Width / (double)w, r.BoxH / (double)h);
                    result.Add(new(i, c.AssetId, c.SrcIn, len, (int)track[0].X, y, w, h, plan.Canvas.Width & ~1, r.BoxH & ~1, f, false));
                }
                else
                {
                    int w = sa.Media.Width & ~1, h = sa.Media.Height & ~1;
                    if (w <= 0 || h <= 0) continue;
                    double f = Math.Min(plan.Canvas.Width / (double)w, r.BoxH / (double)h);
                    result.Add(new(i, c.AssetId, c.SrcIn, len, 0, 0, w, h, Even(w * f), Even(h * f), f, false));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// The plan to render (pieces substituted where super-resolution was adopted) and what was decided, in words for the user.
    /// <paramref name="modelPath"/> null or missing = no model: magnified pieces stay with the renderer's lanczos and a note says so.
    /// </summary>
    public static async Task<(EditPlan Plan, List<string> Notes)> ApplyAsync(FFmpeg ff, string? modelPath, EditPlan plan, IReadOnlyDictionary<string, SourceAsset> assets,
        string cacheRoot, IProgress<double>? progress, CancellationToken ct, Func<string, SuperResolution?>? load = null)
    {
        var notes = new List<string>();
        var pieces = Pieces(plan, assets);
        if (pieces.Count == 0) return (plan, notes);
        var needed = pieces.Where(p => p.Factor > BrandKitEnhancer.MinFactor).ToList();
        if (needed.Count == 0)
        {
            notes.Add($"副画面の画面録画は最大 {pieces.Max(p => p.Factor):P0} の大きさ（縮小または等倍）で表示するため、高画質化（超解像）は不要です");
            return (plan, notes);
        }
        string Seconds(IEnumerable<Piece> ps) => ps.Sum(p => p.Len - TailSec * plan.Timeline[p.Index].Speed).ToString("F1", CultureInfo.InvariantCulture);
        SuperResolution? sr = null;
        try { sr = modelPath != null && File.Exists(modelPath) ? (load ?? (m => new SuperResolution(m)))(modelPath) : null; }
        catch (Exception e) when (e is not OperationCanceledException) { notes.Add($"高画質化モデルを読み込めませんでした（{e.Message}）。拡大表示する画面録画は lanczos で拡大します"); return (plan, notes); }
        if (sr == null)
        {
            notes.Add($"副画面の画面録画 {Seconds(needed)} 秒を最大 {needed.Max(p => p.Factor):F2} 倍に拡大して表示します。高画質化モデル（tools/models/{SuperResolution.ModelFile}）がないため lanczos で拡大しました");
            return (plan, notes);
        }
        using var _ = sr;
        var timeline = plan.Timeline.ToList();
        int fps = plan.Canvas.Fps;
        double total = needed.Sum(p => p.Len), done = 0;
        foreach (var group in needed.GroupBy(p => (p.AssetId, p.X, p.Y, p.W, p.H)))
        {
            var asset = assets[group.Key.AssetId];
            var dir = Path.Combine(cacheRoot, asset.Sha256[..Math.Min(16, asset.Sha256.Length)], "sr");
            Directory.CreateDirectory(dir);
            string Key(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{asset.Sha256}|{group.Key.X},{group.Key.Y},{group.Key.W},{group.Key.H}|{fps}|{SuperResolution.Method}|{SuperResolution.DetailSigma}|{SuperResolution.DetailGain}|{s}")))[..16];
            string I(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
            async Task Cut(Piece p, string dest) => await ff.RunFfmpegCheckedAsync(["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-ss", I(p.T0), "-i", asset.Path, "-t", I(p.Len), "-an",
                "-vf", $"crop={p.W}:{p.H}:{p.X}:{p.Y},fps={fps},format=yuv420p", "-c:v", "libx264", "-crf", "4", "-preset", "veryfast", dest], ct);

            // 1) does it help on THIS recording's content?
            var record = Path.Combine(dir, $"check_{Key("check")}.json");
            QualityGain gain;
            if (File.Exists(record) && Json.Deserialize<QualityGain>(await File.ReadAllTextAsync(record, ct)) is { } cached) gain = cached;
            else
            {
                var work = Path.Combine(dir, "work_" + Key("check")); Directory.CreateDirectory(work);
                try
                {
                    var sample = group.MaxBy(p => p.Len)!;
                    var cut = Path.Combine(work, "cut.mp4");
                    await Cut(sample with { Len = Math.Min(BrandKitEnhancer.SampleSec, sample.Len) }, cut);
                    gain = await BrandKitEnhancer.SelfCheckAsync(ff, sr, cut, 0, BrandKitEnhancer.SampleSec, group.Key.W, group.Key.H, fps, work, null, ct);
                    await File.WriteAllTextAsync(record, Json.Serialize(gain), ct);
                }
                finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            string score = $"VMAF-NEG {gain.Vmaf:+0.00;-0.00} / XPSNR {gain.Xpsnr:+0.00;-0.00} dB";
            if (!BrandKitEnhancer.Adopt(gain))
            {
                string why = double.IsFinite(gain.Vmaf) && double.IsFinite(gain.Xpsnr) ? $"この素材では高画質化しても元に忠実にならなかったため（{score}、元素材での自己検証）" : "画質の改善を測定できなかったため";
                notes.Add($"副画面の画面録画（{group.Key.W}×{group.Key.H}）{Seconds(group)} 秒は拡大表示しますが、{why} lanczos で拡大しました");
                done += group.Sum(p => p.Len); progress?.Report(done / total);
                continue;
            }
            // 2) every magnified piece, cut and upscaled to its on-screen size
            foreach (var p in group)
            {
                var dest = Path.Combine(dir, $"piece_{Key($"{I(p.T0)}|{I(p.Len)}|{p.OutW}x{p.OutH}")}.mp4");
                if (!File.Exists(dest))
                {
                    var cut = dest + ".cut.mp4";
                    try
                    {
                        await Cut(p, cut);
                        double before = done;
                        await sr.UpscaleVideoAsync(ff, cut, p.W, p.H, fps, p.OutW, p.OutH, dest, new Progress<double>(v => progress?.Report((before + v * p.Len) / total)), ct);
                    }
                    finally { try { File.Delete(cut); } catch (IOException) { } }
                }
                done += p.Len; progress?.Report(done / total);
                var c = timeline[p.Index];
                timeline[p.Index] = p.Inset
                    ? c with { Pip = c.Pip! with { Video = dest, CropX = 0, CropY = 0, CropW = 0, CropH = 0 } }
                    : c with { Reframe = c.Reframe with { Video = dest, CropW = 0, CropH = 0, CropY = 0, Track = null, TrackY = null } };
            }
            notes.Add($"副画面の画面録画（{group.Key.W}×{group.Key.H}）{Seconds(group)} 秒を表示サイズ（最大 {group.Max(p => p.OutW)}×{group.Max(p => p.OutH)}）まで高画質化しました" +
                      $"（{SuperResolution.Method}、{sr.Device}。元素材での自己検証で lanczos 拡大より忠実: {score}）");
        }
        return (plan with { Timeline = timeline }, notes);
    }
}
