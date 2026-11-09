using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using VideoCreator.Models;

namespace VideoCreator.Media;

public sealed class IngestException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>FR-ING-02/03/04 (verified in spikes/01-ingest.cs).</summary>
public sealed class MediaProbe(FFmpeg ff)
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    public const double MinDuration = 10, MaxDuration = 3 * 3600;

    public async Task<MediaInfo> ProbeAsync(string file, CancellationToken ct = default)
    {
        if (!File.Exists(file)) throw new IngestException("E-ING-UNSUPPORTED", $"ファイルが見つかりません: {file}");
        var r = await ff.RunProbeAsync(["-v", "error", "-print_format", "json", "-show_format", "-show_streams", file], ct);
        if (r.ExitCode != 0) throw new IngestException("E-ING-UNSUPPORTED", "動画として読み込めない形式です（" + FFmpeg.Tail(r.StdErr, 1) + "）");
        using var doc = JsonDocument.Parse(r.StdOut);
        var streams = doc.RootElement.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
        var v = streams.FirstOrDefault(x => x.GetProperty("codec_type").GetString() == "video" && !IsAttachedPic(x));
        if (v.ValueKind == JsonValueKind.Undefined) throw new IngestException("E-ING-UNSUPPORTED", "映像トラックがありません");
        var a = streams.FirstOrDefault(x => x.GetProperty("codec_type").GetString() == "audio");
        int w = v.GetProperty("width").GetInt32(), h = v.GetProperty("height").GetInt32();
        int rot = 0;
        if (v.TryGetProperty("side_data_list", out var sdl))
            foreach (var sd in sdl.EnumerateArray())
                if (sd.TryGetProperty("rotation", out var rr)) rot = (int)Math.Round(rr.GetDouble());
        if (v.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rt) && int.TryParse(rt.GetString(), out var rti)) rot = rti;
        if (Math.Abs(rot) % 180 == 90) (w, h) = (h, w);
        double rFr = Frac(v.GetProperty("r_frame_rate").GetString()), avg = Frac(v.TryGetProperty("avg_frame_rate", out var af) ? af.GetString() : null);
        var fmt = doc.RootElement.GetProperty("format");
        double dur = fmt.TryGetProperty("duration", out var d) ? double.Parse(d.GetString()!, IC) : 0;
        bool vfr = rFr > 0 && avg > 0 && Math.Abs(rFr - avg) / rFr > 0.01;
        if (!vfr) vfr = await DetectVfrFromPacketsAsync(file, ct);
        return new MediaInfo
        {
            Duration = dur, Width = w, Height = h, Rotation = rot,
            Fps = avg > 0 ? avg : rFr, Vfr = vfr,
            AudioChannels = a.ValueKind == JsonValueKind.Undefined ? 0 : a.GetProperty("channels").GetInt32(),
            VideoCodec = v.GetProperty("codec_name").GetString() ?? "",
            Container = fmt.GetProperty("format_name").GetString() ?? "",
        };
    }

    static bool IsAttachedPic(JsonElement s) => s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;

    // Container metadata can lie (e.g. MKV): inspect real packet timestamp deltas over the first 10 s.
    async Task<bool> DetectVfrFromPacketsAsync(string file, CancellationToken ct)
    {
        var r = await ff.RunProbeAsync(["-v", "error", "-select_streams", "v:0", "-read_intervals", "%+10", "-show_entries", "packet=pts_time", "-of", "csv=p=0", file], ct);
        var ts = r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x.Trim().TrimEnd(','), NumberStyles.Float, IC, out var dd) ? dd : double.NaN)
            .Where(dd => !double.IsNaN(dd)).OrderBy(dd => dd).ToArray();
        var deltas = ts.Zip(ts.Skip(1), (p, q) => q - p).Where(dd => dd > 0).ToArray();
        if (deltas.Length <= 5) return false;
        var med = deltas.OrderBy(dd => dd).ElementAt(deltas.Length / 2);
        return deltas.Count(dd => Math.Abs(dd - med) > med * 0.5) > deltas.Length * 0.02;
    }

    static double Frac(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var p = s.Split('/');
        if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, IC, out var n) && double.TryParse(p[1], NumberStyles.Float, IC, out var dd) && dd != 0) return n / dd;
        return double.TryParse(s, NumberStyles.Float, IC, out var x) ? x : 0;
    }

    /// <summary>PRD §4.2 range checks. Throws for hard rejections, returns warnings otherwise.</summary>
    public static List<string> Validate(MediaInfo m)
    {
        var warnings = new List<string>();
        if (m.Duration < MinDuration) throw new IngestException("E-ING-UNSUPPORTED", $"動画が短すぎます（{m.Duration:F1}秒）。10秒以上の動画を選んでください。");
        if (Math.Min(m.Width, m.Height) < 360) throw new IngestException("E-ING-UNSUPPORTED", $"解像度が低すぎます（{m.Width}x{m.Height}）。");
        if (m.Duration > MaxDuration) warnings.Add("3時間を超える動画です。処理に時間がかかるため、分割をおすすめします。");
        if (m.AudioChannels == 0) warnings.Add("E-ING-NOAUDIO: 音声トラックがありません。書き起こしができないため「P9 ミニマル」をおすすめします。");
        if (Math.Min(m.Width, m.Height) < 720) warnings.Add("720p 未満の素材です。縦型にすると画質が粗くなる可能性があります。");
        if (m.Vfr) warnings.Add("可変フレームレート（VFR）の素材です。解析時に 30fps 固定に正規化します。");
        return warnings;
    }

    public static async Task<string> Sha256Async(string file, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>FR-ING-03/04: CFR 30 fps 360p analysis proxy + 16 kHz mono FLAC (for ASR/signals).</summary>
    /// <param name="refresh">Make them again even when cached (the old files are replaced only once the new ones are complete).</param>
    public async Task<(string proxy, string audio)> MakeProxyAsync(string file, MediaInfo m, string cacheDir, CancellationToken ct = default, IProgress<double>? progress = null, bool refresh = false)
    {
        Directory.CreateDirectory(cacheDir);
        var proxy = Path.Combine(cacheDir, "proxy.mp4");
        var audio = Path.Combine(cacheDir, "audio16k.flac");
        if (!refresh && File.Exists(proxy) && (m.AudioChannels == 0 || File.Exists(audio))) return (proxy, audio);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", file,
            "-map", "0:v:0", "-vf", "scale=-2:360,fps=30", "-fps_mode", "cfr", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-an", proxy + ".tmp.mp4" };
        // timestamp-true audio (a late-starting stream is padded): transcript times then equal the file times that atrim/trim cut on
        if (m.AudioChannels > 0) args.AddRange(["-map", "0:a:0", "-vn", "-af", Audio.AudioSync.TimelineFilter, "-ac", "1", "-ar", "16000", "-c:a", "flac", audio + ".tmp.flac"]);
        await ff.RunFfmpegCheckedAsync(args, ct, progress: progress, duration: m.Duration);
        File.Move(proxy + ".tmp.mp4", proxy, true);
        if (m.AudioChannels > 0) File.Move(audio + ".tmp.flac", audio, true);
        return (proxy, audio);
    }
}
