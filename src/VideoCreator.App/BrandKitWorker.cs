using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>
/// Upscales the registered opening / closing in the background after registration (about 1–2 minutes per 12 s video on a GPU,
/// much longer on CPU), so the registration request returns at once. Only the variants a portrait (1080x1920) or landscape (1920x1080) output will actually use are processed, each against the canvas of its own shape.
/// Generation can run meanwhile: until a file is done, renders use the registered file (scaled with lanczos as before).
/// </summary>
public sealed class BrandKitWorker(Runtime rt, ILogger<BrandKitWorker> log)
{
    public sealed record Status(string State, string? File = null, string Stage = "", double Progress = 0, string Message = "");   // idle | running | done | failed
    volatile Status _status = new("idle");
    CancellationTokenSource? _cts;
    readonly Lock _gate = new();

    public Status Current => _status;
    public string CacheDir => Path.Combine(rt.Options.ResolvedDataDir, "brandkit");

    /// <summary>
    /// Files of the kit that a portrait or landscape output uses and that are smaller than the canvas of their own shape
    /// (a 1280x720 opening against 1920x1080). A portrait file shown on a landscape canvas is pillarboxed at that height, so it is judged as portrait.
    /// </summary>
    public static List<(BrandFile File, Canvas Canvas)> Targets(BrandKit kit) =>
        new[] { new Canvas(), Canvas.Landscape() }
            .SelectMany(c => new[] { "opening", "closing" }.Select(k => BrandKitService.Pick(kit.Files, k, c)))
            .OfType<BrandFile>().DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: f, Canvas: Canvas.For(f.Width, f.Height)))
            .Where(x => BrandKitEnhancer.Needs(x.File, x.Canvas)).ToList();

    public void Start()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            _status = new("running", Stage: "準備", Message: "高画質化の準備をしています");
            _ = Task.Run(() => RunAsync(cts.Token));
        }
    }

    public void Cancel() { lock (_gate) { _cts?.Cancel(); _status = new("idle"); } }

    async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var kit = rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId);
            if (kit == null || !kit.Upscale) { _status = new("idle"); return; }
            var targets = Targets(kit);
            if (targets.Count == 0) { _status = new("done", Message: "高画質化が必要な動画はありません（出力と同じか大きい解像度です）"); return; }
            if (!File.Exists(rt.UpscaleModel)) { _status = new("failed", Message: $"高画質化のモデルがありません（{rt.UpscaleModel}）。scripts/get-tools.ps1 を実行してください。元の動画のまま使います"); return; }
            using var sr = new SuperResolution(rt.UpscaleModel);
            log.LogInformation("Upscaling {Count} brand file(s) on {Device}", targets.Count, sr.Device);
            var notes = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                var (f, canvas) = targets[i]; var name = Path.GetFileName(f.Path); int k = i;
                var p = new Progress<(string Stage, double Progress)>(x => _status = new("running", name, x.Stage, (k + x.Progress) / targets.Count, $"{name}: {x.Stage}（{sr.Device}）"));
                var done = await BrandKitEnhancer.EnhanceAsync(rt.Ff, sr, f, canvas, CacheDir, p, ct);
                ct.ThrowIfCancellationRequested();
                // the kit may have been re-registered meanwhile: update only the same file of the current kit
                var now = rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId);
                if (now == null) return;
                rt.Store.Put(Kinds.BrandKit, PipelineRunner.BrandKitId, null, now with { Files = now.Files.Select(x => string.Equals(x.Path, f.Path, StringComparison.OrdinalIgnoreCase) ? x with { Enhancement = done.Enhancement } : x).ToList() });
                if (done.Enhancement is { } e) notes.Add($"{name}: {e.Note}");
            }
            _status = new("done", Progress: 1, Message: string.Join(" / ", notes));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "brand kit upscale failed");
            _status = new("failed", Message: "高画質化に失敗しました（元の動画のまま使います）: " + ex.Message);
        }
    }
}
