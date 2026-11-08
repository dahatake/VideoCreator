using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.App;

public sealed class SceneCardService(Runtime rt, ILogger<SceneCardService> log)
{
    readonly SemaphoreSlim _gate = new(1);
    volatile string? _projectId;
    public bool IsBusy(string projectId) => _projectId == projectId;

    public async Task<List<SceneCardImage>> GenerateAsync(Project project, SourceAsset source, int count, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new InvalidOperationException("カード画像を作成中です。完了を待ってください");
        _projectId = project.Id;
        string? output = null;
        bool saved = false;
        try
        {
            Check(project.Id, source.Id);
            output = Path.Combine(project.Directory, "scene-cards", Guid.NewGuid().ToString("N"));
            PipelineRunner.EnsureInside(project.Directory, output);
            var images = await new SceneCardExtractor(rt.Ff).ExtractAsync(source, output, count, ct);
            ct.ThrowIfCancellationRequested();
            var current = Check(project.Id, source.Id);
            rt.Store.Put(Kinds.Project, project.Id, null, current with { SceneCardImages = images });
            saved = true;
            return images;
        }
        finally
        {
            if (!saved && output != null && Directory.Exists(output))
            {
                try { Directory.Delete(output, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "Could not clean up incomplete scene cards at {Path}", output); }
            }
            _projectId = null;
            _gate.Release();
        }
    }

    Project Check(string projectId, string assetId)
    {
        var project = rt.Store.Get<Project>(Kinds.Project, projectId) ?? throw new InvalidOperationException("プロジェクトがありません");
        if (project.SceneCardImages.Count > 0) throw new InvalidOperationException("このプロジェクトのカード画像は作成済みです。既存の画像は上書きしません");
        if (rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId) is { RightsConfirmed: true } kit && kit.Files.Any(f => f.Kind == "card"))
            throw new InvalidOperationException("登録済みのカード画像があります。既存の画像を使用します");
        if (!rt.Store.List<RightsConfirmation>(Kinds.Rights, projectId).Any(r => r.AssetId == assetId))
            throw new IngestException("E-RIGHTS-REQUIRED", "元動画を使う権利の確認にチェックしてください");
        return project;
    }
}
