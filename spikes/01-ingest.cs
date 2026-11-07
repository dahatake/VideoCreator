// Spike 01: FR-ING-02/03/04 — probe metadata, detect VFR/rotation, reject invalid input, build CFR proxy + 16k mono WAV.
// Run: dotnet run spikes/01-ingest.cs
#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")); // not reliable for file-based apps
root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var media = Path.Combine(root, "spikes", "media");
var outDir = Path.Combine(root, "spikes", "out"); Directory.CreateDirectory(outDir);
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }

(int code, string stdout, string stderr) Run(string exe, params string[] args)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, exe)) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
    p.WaitForExit(); return (p.ExitCode, o.Result, e.Result);
}

double Frac(string s) { var p = s.Split('/'); return p.Length == 2 && double.Parse(p[1]) != 0 ? double.Parse(p[0]) / double.Parse(p[1]) : 0; }

object? Probe(string file)
{
    var (code, json, err) = Run("ffprobe.exe", "-v", "error", "-print_format", "json", "-show_format", "-show_streams", file);
    if (code != 0) return $"E-ING-UNSUPPORTED: {err.Trim().Split('\n')[0]}";
    using var doc = JsonDocument.Parse(json);
    var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
    var v = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "video");
    if (v.ValueKind == JsonValueKind.Undefined) return "E-ING-UNSUPPORTED: no video stream";
    var a = streams.FirstOrDefault(s => s.GetProperty("codec_type").GetString() == "audio");
    int w = v.GetProperty("width").GetInt32(), h = v.GetProperty("height").GetInt32();
    int rot = 0;
    if (v.TryGetProperty("side_data_list", out var sdl))
        foreach (var sd in sdl.EnumerateArray())
            if (sd.TryGetProperty("rotation", out var r)) rot = (int)r.GetDouble();
    if (Math.Abs(rot) % 180 == 90) (w, h) = (h, w);
    double rFr = Frac(v.GetProperty("r_frame_rate").GetString()!), avg = Frac(v.GetProperty("avg_frame_rate").GetString()!);
    bool vfr = rFr > 0 && avg > 0 && Math.Abs(rFr - avg) / rFr > 0.01;
    if (!vfr)
    {
        // Container metadata can lie (MKV); inspect real packet timestamp deltas over the first 10 s.
        var (_, pts, _) = Run("ffprobe.exe", "-v", "error", "-select_streams", "v:0", "-read_intervals", "%+10", "-show_entries", "packet=pts_time", "-of", "csv=p=0", file);
        var ts = pts.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => double.TryParse(x.Trim().TrimEnd(','), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN).Where(d => !double.IsNaN(d)).OrderBy(d => d).ToArray();
        var deltas = ts.Zip(ts.Skip(1), (p, q) => q - p).Where(d => d > 0).ToArray();
        if (deltas.Length > 5) { var med = deltas.OrderBy(d => d).ElementAt(deltas.Length / 2); vfr = deltas.Count(d => Math.Abs(d - med) > med * 0.5) > deltas.Length * 0.02; }
    }
    double dur = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    return new { w, h, rot, rFr, avg, vfr, dur, audioCh = a.ValueKind == JsonValueKind.Undefined ? 0 : a.GetProperty("channels").GetInt32(), codec = v.GetProperty("codec_name").GetString() };
}

foreach (var f in Directory.GetFiles(media).OrderBy(x => x))
    Console.WriteLine($"{Path.GetFileName(f),-14} {JsonSerializer.Serialize(Probe(f))}");

dynamic L = Probe(Path.Combine(media, "landscape.mp4"))!;
Check(L.w == 1920 && L.h == 1080 && Math.Abs(L.dur - 60) < 0.2 && L.audioCh >= 1 && !L.vfr, "landscape: 1920x1080, 60s, audio, CFR");
dynamic R = Probe(Path.Combine(media, "rotated.mp4"))!;
Check(R.w == 1080 && R.h == 1920 && Math.Abs(R.rot) == 90, "rotated: display dims swapped to 1080x1920 (rotation metadata honoured)");
dynamic V = Probe(Path.Combine(media, "vfr.mkv"))!;
Check(V.vfr, $"vfr.mkv detected as VFR (r={V.rFr:F2}, avg={V.avg:F2})");
dynamic N = Probe(Path.Combine(media, "noaudio.mp4"))!;
Check(N.audioCh == 0, "noaudio: zero audio channels -> E-ING-NOAUDIO warning");
var bad = Probe(Path.Combine(media, "notvideo.mp4"));
Check(bad is string s && s.StartsWith("E-ING-UNSUPPORTED"), $"notvideo rejected: {bad}");

// FR-ING-03/04: CFR proxy (360p for analysis speed) + 16 kHz mono WAV
var sw = Stopwatch.StartNew();
var proxy = Path.Combine(outDir, "vfr_proxy.mp4"); var wav = Path.Combine(outDir, "vfr_16k.wav");
var r1 = Run("ffmpeg.exe", "-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(media, "vfr.mkv"),
    "-map", "0:v:0", "-vf", "scale=-2:360,fps=30", "-fps_mode", "cfr", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-an", proxy,
    "-map", "0:a:0", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav);
Console.WriteLine($"proxy+wav in {sw.ElapsedMilliseconds} ms {r1.stderr}");
dynamic P = Probe(proxy)!;
Check(!P.vfr && Math.Abs(P.avg - 30) < 0.01 && P.h == 360, $"proxy is CFR 30fps 360p (avg={P.avg:F3})");
var (c2, wj, _) = Run("ffprobe.exe", "-v", "error", "-print_format", "json", "-show_streams", wav);
using (var wd = JsonDocument.Parse(wj)) { var st = wd.RootElement.GetProperty("streams")[0]; Check(st.GetProperty("sample_rate").GetString() == "16000" && st.GetProperty("channels").GetInt32() == 1, "wav is 16 kHz mono"); }

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
