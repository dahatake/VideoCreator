using System.Globalization;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VideoCreator.Media;
using VideoCreator.Models;

namespace VideoCreator.Faces;

/// <summary>UltraFace (version-RFB-320) via ONNX Runtime — verified in spikes/09-faces.cs (counts 1,1,1,4; error ≈ 23 px on a moving face).</summary>
public sealed class FaceDetector : IDisposable
{
    public const int W = 320, H = 240;
    readonly InferenceSession _session;
    readonly object _lock = new();

    public FaceDetector(string modelPath)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException($"顔検出モデルがありません: {modelPath}（scripts/get-tools.ps1 を実行してください）");
        _session = new InferenceSession(modelPath, new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR });
    }

    /// <summary>rgb24 frame at 320x240 starting at offset → boxes in normalised coordinates.</summary>
    public List<FaceBox> Detect(byte[] rgb, int offset, float minScore = 0.7f)
    {
        var t = new DenseTensor<float>(new[] { 1, 3, H, W });
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) for (int c = 0; c < 3; c++) t[0, c, y, x] = (rgb[offset + (y * W + x) * 3 + c] - 127f) / 128f;
        float[] scores, boxes; int n;
        lock (_lock)
        {
            using var res = _session.Run([NamedOnnxValue.CreateFromTensor("input", t)]);
            var s = res.First(r => r.Name == "scores").AsTensor<float>(); var b = res.First(r => r.Name == "boxes").AsTensor<float>();
            n = s.Dimensions[1]; scores = new float[n]; boxes = new float[n * 4];
            for (int i = 0; i < n; i++) { scores[i] = s[0, i, 1]; for (int k = 0; k < 4; k++) boxes[i * 4 + k] = b[0, i, k]; }
        }
        var cand = Enumerable.Range(0, n).Where(i => scores[i] > minScore).OrderByDescending(i => scores[i]).ToList();
        var keep = new List<FaceBox>();
        foreach (var i in cand)
        {
            float x1 = boxes[i * 4], y1 = boxes[i * 4 + 1], x2 = boxes[i * 4 + 2], y2 = boxes[i * 4 + 3];
            bool dup = keep.Any(k =>
            {
                double ix = Math.Max(0, Math.Min(k.X + k.W, x2) - Math.Max(k.X, x1)), iy = Math.Max(0, Math.Min(k.Y + k.H, y2) - Math.Max(k.Y, y1));
                double inter = ix * iy, u = k.W * k.H + (x2 - x1) * (y2 - y1) - inter;
                return inter / u > 0.3;
            });
            if (!dup) keep.Add(new FaceBox(x1, y1, x2 - x1, y2 - y1, scores[i]));
        }
        return keep;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>Samples faces at 2 fps from the analysis proxy, only inside the ranges that are actually used; cached per asset.</summary>
public sealed class FaceService(FFmpeg ff, string modelPath)
{
    public const double Fps = 2;
    FaceDetector? _det;
    readonly SemaphoreSlim _gate = new(1, 1);

    public bool Available => File.Exists(modelPath);

    sealed record Cache(List<Interval> Covered, List<FaceSample> Samples);

    public async Task<List<FaceSample>> AnalyzeAsync(string proxy, IReadOnlyList<Interval> ranges, string cacheFile, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var cache = File.Exists(cacheFile) ? Json.Deserialize<Cache>(await File.ReadAllTextAsync(cacheFile, ct)) : new Cache([], []);
            bool changed = false;
            foreach (var r0 in ranges)
            {
                var r = new Interval(Math.Max(0, r0.T0 - 0.5), r0.T1 + 0.5);
                if (cache.Covered.Any(c => c.T0 <= r.T0 + 1e-6 && c.T1 >= r.T1 - 1e-6)) continue;
                _det ??= new FaceDetector(modelPath);
                using var ms = new MemoryStream();
                var res = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", r.T0.ToString("0.###", CultureInfo.InvariantCulture), "-t", r.Length.ToString("0.###", CultureInfo.InvariantCulture),
                    "-i", proxy, "-an", "-vf", $"fps={Fps},scale={FaceDetector.W}:{FaceDetector.H}", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], ct, stdout: ms);
                if (res.ExitCode != 0) throw new FFmpegException("顔検出用のフレーム抽出に失敗しました: " + FFmpeg.Tail(res.StdErr));
                var buf = ms.ToArray(); int fsz = FaceDetector.W * FaceDetector.H * 3, n = buf.Length / fsz;
                var add = new List<FaceSample>();
                for (int k = 0; k < n; k++) add.Add(new FaceSample(Math.Round(r.T0 + k / Fps, 3), _det.Detect(buf, k * fsz)));
                cache.Samples.RemoveAll(s => s.T >= r.T0 - 1e-6 && s.T <= r.T1 + 1e-6);
                cache.Samples.AddRange(add);
                cache.Covered.Add(r); changed = true;
            }
            if (changed)
            {
                cache.Samples.Sort((a, b) => a.T.CompareTo(b.T));
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                await File.WriteAllTextAsync(cacheFile, Json.Serialize(cache), ct);
            }
            return cache.Samples;
        }
        finally { _gate.Release(); }
    }

    public static List<FaceSample> Load(string cacheFile) =>
        File.Exists(cacheFile) ? Json.Deserialize<Cache>(File.ReadAllText(cacheFile)).Samples : [];

    /// <summary>Faces in one frame of any video (normalised boxes), e.g. a rendered output for QA S-05.</summary>
    public async Task<List<FaceBox>> DetectAtAsync(string video, double t, CancellationToken ct)
    {
        _det ??= new FaceDetector(modelPath);
        using var ms = new MemoryStream();
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", t.ToString("0.###", CultureInfo.InvariantCulture), "-i", video, "-frames:v", "1", "-vf", $"scale={FaceDetector.W}:{FaceDetector.H}", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], ct, stdout: ms);
        var buf = ms.ToArray();
        return r.ExitCode == 0 && buf.Length >= FaceDetector.W * FaceDetector.H * 3 ? _det.Detect(buf, 0) : [];
    }
}

/// <summary>Turns face samples into a smoothed 9:16 crop path (FR-RFM-02/03). Rules verified in spike 09.</summary>
public static class FaceTrack
{
    public const double MinSingleFaceCoverage = 0.7, SmoothSec = 1.0, MaxSpeedPxPerSec = 400, SimplifyTol = 6;

    public static (int CropW, int CropH) CropSize(MediaInfo m)
    {
        int cropH = m.Height & ~1, cropW = (int)Math.Round(cropH * 9.0 / 16) & ~1;
        return (cropW, cropH);
    }

    /// <summary>Returns a face-follow ReframeSpec for source range [srcIn,srcOut] (output clip time = (t−srcIn)/speed), or null when blur-fit is the better choice.</summary>
    public static ReframeSpec? Plan(IReadOnlyList<FaceSample> samples, MediaInfo m, double srcIn, double srcOut, double speed, out string reason)
    {
        reason = "";
        var (cropW, cropH) = CropSize(m);
        if (cropW >= m.Width) { reason = "素材がすでに縦長"; return null; }
        var s = samples.Where(x => x.T >= srcIn - 0.5 && x.T <= srcOut + 0.5).OrderBy(x => x.T).ToList();
        if (s.Count == 0) { reason = "顔を検出できませんでした"; return null; }
        double single = s.Count(x => x.Boxes.Count == 1) / (double)s.Count, multi = s.Count(x => x.Boxes.Count >= 2) / (double)s.Count;
        if (multi > 0.3) { reason = "複数の顔が映っているため全体を表示"; return null; }
        if (single < MinSingleFaceCoverage) { reason = $"顔の検出率が低い（{single:P0}）"; return null; }

        // centre of the single face per sample; gaps are bridged by interpolation
        var pts = s.Where(x => x.Boxes.Count == 1).Select(x => (t: x.T, x: Math.Clamp(x.Boxes[0].Cx * m.Width - cropW / 2.0, 0, m.Width - cropW))).ToList();
        int n = Math.Max(2, (int)Math.Ceiling((srcOut - srcIn) * FaceService.Fps) + 1);
        var grid = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = srcIn + i / FaceService.Fps; if (t > srcOut) t = srcOut;
            grid[i] = Interp(pts, t);
        }
        // centred moving average ≈ SmoothSec, then a speed limit
        int half = Math.Max(1, (int)Math.Round(SmoothSec * FaceService.Fps / 2));
        var sm = new double[n];
        for (int i = 0; i < n; i++) { int a = Math.Max(0, i - half), b = Math.Min(n - 1, i + half); sm[i] = grid[a..(b + 1)].Average(); }
        double maxStep = MaxSpeedPxPerSec / FaceService.Fps;
        for (int i = 1; i < n; i++) sm[i] = sm[i - 1] + Math.Clamp(sm[i] - sm[i - 1], -maxStep, maxStep);

        var keys = Enumerable.Range(0, n).Select(i => (t: Math.Min(srcOut - srcIn, i / FaceService.Fps) / speed, x: sm[i])).ToList();
        if (keys.Max(k => k.x) - keys.Min(k => k.x) < 24) keys = [(0, keys.Average(k => k.x))];   // essentially still: static crop
        else keys = Simplify(keys, SimplifyTol);
        return new ReframeSpec
        {
            Mode = "face", CropW = cropW, CropH = cropH, CropY = 0,
            Track = keys.Select(k => new TrackPoint(Math.Round(k.t, 3), Math.Round(k.x, 1))).ToList(),
        };
    }

    static double Interp(List<(double t, double x)> p, double t)
    {
        if (t <= p[0].t) return p[0].x; if (t >= p[^1].t) return p[^1].x;
        for (int i = 0; i + 1 < p.Count; i++)
            if (t <= p[i + 1].t) return p[i].x + (p[i + 1].x - p[i].x) * (t - p[i].t) / Math.Max(1e-6, p[i + 1].t - p[i].t);
        return p[^1].x;
    }

    static List<(double t, double x)> Simplify(List<(double t, double x)> pts, double tol)
    {
        if (pts.Count < 3) return pts;
        double Dist(int i, int a, int b) { var (ta, xa) = pts[a]; var (tb, xb) = pts[b]; var xi = xa + (xb - xa) * (pts[i].t - ta) / Math.Max(1e-9, tb - ta); return Math.Abs(pts[i].x - xi); }
        var keep = new SortedSet<int> { 0, pts.Count - 1 }; var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop(); int far = -1; double md = tol;
            for (int i = a + 1; i < b; i++) { var d = Dist(i, a, b); if (d > md) { md = d; far = i; } }
            if (far >= 0) { keep.Add(far); stack.Push((a, far)); stack.Push((far, b)); }
        }
        return keep.Select(i => pts[i]).ToList();
    }

    /// <summary>FFmpeg expression for the crop's left edge as a function of clip time t.</summary>
    public static string XExpression(IReadOnlyList<TrackPoint> k)
    {
        var IC = CultureInfo.InvariantCulture;
        if (k.Count == 1) return k[0].X.ToString("0.#", IC);
        var sb = new StringBuilder();
        sb.Append($"lt(t,{k[0].T.ToString("0.###", IC)})*{k[0].X.ToString("0.#", IC)}+");
        for (int i = 0; i + 1 < k.Count; i++)
        {
            double t0 = k[i].T, t1 = k[i + 1].T;
            if (t1 <= t0) continue;
            sb.Append($"gte(t,{t0.ToString("0.###", IC)})*lt(t,{t1.ToString("0.###", IC)})*({k[i].X.ToString("0.#", IC)}+({(k[i + 1].X - k[i].X).ToString("0.#", IC)})*(t-{t0.ToString("0.###", IC)})/{(t1 - t0).ToString("0.###", IC)})+");
        }
        sb.Append($"gte(t,{k[^1].T.ToString("0.###", IC)})*{k[^1].X.ToString("0.#", IC)}");
        return sb.ToString();
    }

    /// <summary>Expected crop-left at clip time t (used by QA S-05 to know where the face is on the output).</summary>
    public static double XAt(IReadOnlyList<TrackPoint> k, double t)
    {
        if (k.Count == 1 || t <= k[0].T) return k[0].X; if (t >= k[^1].T) return k[^1].X;
        for (int i = 0; i + 1 < k.Count; i++) if (t < k[i + 1].T) return k[i].X + (k[i + 1].X - k[i].X) * (t - k[i].T) / Math.Max(1e-9, k[i + 1].T - k[i].T);
        return k[^1].X;
    }
}
