using System.Diagnostics;
using System.Globalization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoCreator.Media;

/// <summary>
/// Upscaling for low-resolution brand videos (openings / closings): lanczos for colour, tone and gradients, plus the fine luma detail of a learned
/// 4x model — Real-ESRGAN "realesr-animevideov3" (SRVGGNetCompact, anime / graphics video, BSD-3-Clause, Xintao Wang) as ONNX.
/// Decided by measurement (spikes/14-upscale.cs: downscale the original to 2/3, upscale back, compare with the original):
/// the model alone looks crisp but re-tones the smooth pastel gradients (XPSNR 44.6 → 24.5 dB), and lanczos+CAS sharpening trades
/// XPSNR (−4.7 dB) for VMAF. Adding only the high-frequency part of (model − lanczos) to lanczos is the one variant that is at least
/// as good as lanczos on VMAF-NEG, XPSNR and SSIM at once. Frames are run in overlapping tiles so memory stays bounded at any resolution.
/// </summary>
public sealed class SuperResolution : IDisposable
{
    public const string ModelFile = "realesr-animevideov3.onnx";
    public const string Method = "lanczos + Real-ESRGAN animevideov3 の細部（輝度の高域）";
    /// <summary>Detail transfer: Gaussian sigma (output pixels) separating "detail" from "tone", and how much of the model's detail is added.</summary>
    public const double DetailSigma = 0.7, DetailGain = 0.4;
    public const int Scale = 4, Pad = 12;
    readonly InferenceSession _session;
    readonly string _input, _output;
    readonly int _tile;
    /// <summary>GPU (DirectML) or CPU — DirectML needs a DirectX 12 GPU; measured 0.13 s vs 6.3 s per 720x1280 frame on the dev machine.</summary>
    public string Device { get; }

    public SuperResolution(string modelPath, bool gpu = true)
    {
        if (gpu)
        {
            try
            {
                var g = Options(); g.EnableMemoryPattern = false; g.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL; g.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(modelPath, g);
                Device = "GPU (DirectML)"; _tile = 1280;   // whole frames: the GPU is fastest with one stable shape
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException) { _session = null!; }
        }
        if (_session == null) { _session = new InferenceSession(modelPath, Options()); Device = "CPU"; _tile = 256; }
        Device ??= "CPU";
        _input = _session.InputMetadata.Keys.First(); _output = _session.OutputMetadata.Keys.First();
    }

    static SessionOptions Options() => new() { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };

    /// <summary>Null when the model file is missing (registration then keeps the original file).</summary>
    public static SuperResolution? TryLoad(string? modelPath) => modelPath != null && File.Exists(modelPath) ? new SuperResolution(modelPath) : null;

    float[] _in = [], _out = [];

    /// <summary>rgb24 (w×h) → rgb24 (4w×4h). Tiles overlap by <see cref="Pad"/> input pixels; only each tile's centre is kept, so there are no seams.
    /// Buffers are reused and the model writes straight into a pinned output array (no 44M-float copy per frame); packing runs row-parallel.</summary>
    public byte[] Upscale(byte[] rgb, int w, int h)
    {
        int ow = w * Scale, oh = h * Scale;
        var dst = new byte[ow * oh * 3];
        for (int ty = 0; ty < h; ty += _tile)
            for (int tx = 0; tx < w; tx += _tile)
            {
                int x0 = Math.Max(0, tx - Pad), y0 = Math.Max(0, ty - Pad), x1 = Math.Min(w, tx + _tile + Pad), y1 = Math.Min(h, ty + _tile + Pad);
                int tw = x1 - x0, th = y1 - y0, plane = tw * th, otw = tw * Scale, oplane = otw * th * Scale;
                if (_in.Length != 3 * plane) _in = new float[3 * plane];
                if (_out.Length != 3 * oplane) _out = new float[3 * oplane];
                var inb = _in; var ob = _out;
                Parallel.For(0, th, y =>
                {
                    int row = ((y0 + y) * w + x0) * 3, o = y * tw;
                    for (int x = 0; x < tw; x++, row += 3, o++) { inb[o] = rgb[row] / 255f; inb[plane + o] = rgb[row + 1] / 255f; inb[2 * plane + o] = rgb[row + 2] / 255f; }
                });
                using (var iv = OrtValue.CreateTensorValueFromMemory(inb, [1, 3, th, tw]))
                using (var ovv = OrtValue.CreateTensorValueFromMemory(ob, [1, 3, th * Scale, otw]))
                using (var ro = new RunOptions())
                    _session.Run(ro, [_input], [iv], [_output], [ovv]);
                // keep the tile's own area (without the overlap) in output pixels
                int cx0 = (tx - x0) * Scale, cy0 = (ty - y0) * Scale, cw = Math.Min(_tile, w - tx) * Scale, ch = Math.Min(_tile, h - ty) * Scale, ox = tx * Scale, oy = ty * Scale;
                Parallel.For(0, ch, y =>
                {
                    int si = (cy0 + y) * otw + cx0, di = ((oy + y) * ow + ox) * 3;
                    for (int x = 0; x < cw; x++, si++, di += 3) { dst[di] = B(ob[si]); dst[di + 1] = B(ob[oplane + si]); dst[di + 2] = B(ob[2 * oplane + si]); }
                });
            }
        return dst;
    }

    /// <summary>Time spent in the model and waiting for the encoder (diagnostics for the registration log).</summary>
    public double ModelSeconds { get; private set; }
    public double WriteSeconds { get; private set; }

    static byte B(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    /// <summary>
    /// The encoder graph: [0] = model output (4x, rgb24), [1] = the source. Colour and low frequencies come from a lanczos upscale of the source
    /// (straight from its YUV — no RGB round trip, which alone measured −4 dB XPSNR); the luma of the model's detail, i.e. the high-pass of
    /// (model − lanczos), is added with <see cref="DetailGain"/>. Both branches are re-timed from 0 so they pair frame by frame.
    /// </summary>
    public static string DetailGraph(int outW, int outH, double sigma = DetailSigma, double gain = DetailGain)
    {
        string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        return $"[1:v]setpts=PTS-STARTPTS,scale={outW}:{outH}:flags=lanczos,format=yuv420p,setsar=1,split[lz][lz2];" +
               $"[0:v]setpts=PTS-STARTPTS,scale={outW}:{outH}:flags=area,format=yuv420p,setsar=1[sr];" +
               "[sr][lz2]blend=c0_expr='A-B+128':c1_expr=128:c2_expr=128,split[d1][d2];" +
               $"[d2]gblur=sigma={F(sigma)}:planes=1[db];[d1][db]blend=c0_expr='A-B+128':c1_expr=128:c2_expr=128[hp];" +
               $"[lz][hp]blend=c0_expr='clip(A+(B-128)*{F(gain)},0,255)':c1_expr=A:c2_expr=A,format=yuv420p[v]";
    }

    /// <summary>
    /// Video → upscaled video at exactly outW×outH (see <see cref="DetailGraph"/>), x264 CRF 10 (near-lossless, so the final render's encode is
    /// the only real generation loss). Audio is copied untouched; every source frame is kept (passthrough timing).
    /// </summary>
    public async Task UpscaleVideoAsync(FFmpeg ff, string src, int w, int h, double fps, int outW, int outH, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        string IC(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
        var tmp = dest + ".part.mp4";
        using var dec = Start(ff.FfmpegPath, ["-hide_banner", "-nostdin", "-loglevel", "error", "-i", src, "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdin: false);
        using var enc = Start(ff.FfmpegPath, ["-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", $"{w * Scale}x{h * Scale}", "-r", IC(fps), "-i", "-",
            "-i", src, "-filter_complex", DetailGraph(outW, outH), "-map", "[v]", "-map", "1:a?", "-fps_mode", "passthrough", "-c:v", "libx264", "-preset", "slow", "-crf", "10",
            "-threads", ff.Threads, "-c:a", "copy", "-movflags", "+faststart", tmp], stdin: true);
        var decErr = dec.StandardError.ReadToEndAsync(CancellationToken.None); var encErr = enc.StandardError.ReadToEndAsync(CancellationToken.None);
        using var reg = ct.Register(() => { foreach (var p in new[] { dec, enc }) try { if (!p.HasExited) p.Kill(true); } catch { } });
        var frame = new byte[w * h * 3]; var input = dec.StandardOutput.BaseStream; var output = enc.StandardInput.BaseStream;
        int n = 0, expected = Math.Max(1, (int)Math.Round(fps * await DurationAsync(ff, src, ct)));
        try
        {
            while (true)
            {
                int got = 0;
                while (got < frame.Length) { int r = await input.ReadAsync(frame.AsMemory(got), ct); if (r == 0) break; got += r; }
                if (got < frame.Length) break;
                var t0 = Stopwatch.GetTimestamp();
                var up = await Task.Run(() => Upscale(frame, w, h), ct);
                var t1 = Stopwatch.GetTimestamp();
                await output.WriteAsync(up, ct);
                ModelSeconds += Stopwatch.GetElapsedTime(t0, t1).TotalSeconds; WriteSeconds += Stopwatch.GetElapsedTime(t1).TotalSeconds;
                n++;
                progress?.Report(Math.Min(1, n / (double)expected));
            }
        }
        finally { try { output.Close(); } catch (IOException) { } }
        await Task.WhenAll(dec.WaitForExitAsync(CancellationToken.None), enc.WaitForExitAsync(CancellationToken.None));
        ct.ThrowIfCancellationRequested();
        if (dec.ExitCode != 0 || enc.ExitCode != 0 || n == 0)
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw new FFmpegException($"高画質化に失敗しました（decode {dec.ExitCode} / encode {enc.ExitCode} / {n} frames）: " + FFmpeg.Tail(await decErr + "\n" + await encErr));
        }
        File.Move(tmp, dest, true);
    }

    static async Task<double> DurationAsync(FFmpeg ff, string file, CancellationToken ct)
    {
        var r = await ff.RunProbeAsync(["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=duration", "-of", "csv=p=0", file], ct);
        return double.TryParse(r.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    static Process Start(string exe, IEnumerable<string> args, bool stdin)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = !stdin, RedirectStandardInput = stdin, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new FFmpegException($"failed to start {exe}");
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>Full-reference video quality with FFmpeg's own implementations: VMAF (NEG model, so sharpening cannot inflate it), XPSNR (luma), SSIM.</summary>
public static class VideoQuality
{
    public static async Task<(double Vmaf, double Xpsnr, double Ssim)> ScoreAsync(FFmpeg ff, string candidate, string reference, CancellationToken ct)
    {
        async Task<double> Run(string filter, string pattern)
        {
            var r = await ff.RunFfmpegAsync(["-hide_banner", "-nostdin", "-i", candidate, "-i", reference, "-filter_complex", $"[0:v]setsar=1[c];[1:v]setsar=1[r];[c][r]{filter}[o]", "-map", "[o]", "-f", "null", "-"], ct);
            var m = System.Text.RegularExpressions.Regex.Match(r.StdErr, pattern);
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }
        return (await Run("libvmaf=n_threads=8:model=version=vmaf_v0.6.1neg", @"VMAF score: ([\d.]+)"), await Run("xpsnr", @"XPSNR\s+y:\s*([\d.]+)"), await Run("ssim", @"All:([\d.]+)"));
    }
}
