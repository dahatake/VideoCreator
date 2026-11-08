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
    public bool AnyBusy => _projectId != null;

    /// <summary>Extracts from a project source (<paramref name="assetId"/>) or a registered opening / closing video (<paramref name="brandPath"/>).</summary>
    public async Task<List<SceneCardImage>> GenerateAsync(Project project, string? assetId, string? brandPath, int count, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new InvalidOperationException("カード画像を作成中です。完了を待ってください");
        _projectId = project.Id;
        string? output = null;
        bool saved = false;
        try
        {
            var (_, source, bumper) = Check(project.Id, assetId, brandPath);
            output = Path.Combine(project.Directory, "scene-cards", Guid.NewGuid().ToString("N"));
            PipelineRunner.EnsureInside(project.Directory, output);
            var extractor = new SceneCardExtractor(rt.Ff);
            var images = bumper != null ? await extractor.ExtractAsync(bumper, output, count, ct) : await extractor.ExtractAsync(source!, output, count, ct);
            ct.ThrowIfCancellationRequested();
            var (current, _, _) = Check(project.Id, assetId, brandPath);
            rt.Store.Put(Kinds.Project, project.Id, null, current with { SceneCardImages = images, MakeSceneCardImages = true });
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

    /// <summary>作る / 作らない. Existing images are kept either way; 作らない only stops plans from using them.</summary>
    public Project? SetMake(string projectId, bool make)
    {
        var project = rt.Store.Get<Project>(Kinds.Project, projectId);
        if (project == null) return null;
        if (project.MakeSceneCardImages != make) rt.Store.Put(Kinds.Project, projectId, null, project = project with { MakeSceneCardImages = make });
        return project;
    }

    (Project Project, SourceAsset? Source, BrandFile? Bumper) Check(string projectId, string? assetId, string? brandPath)
    {
        var project = rt.Store.Get<Project>(Kinds.Project, projectId) ?? throw new KeyNotFoundException("プロジェクトがありません");
        if (project.SceneCardImages.Count > 0) throw new InvalidOperationException("このプロジェクトのカード画像は作成済みです。既存の画像は上書きしません");
        var kit = rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId) is { RightsConfirmed: true } k ? k : null;
        if (kit != null && kit.Files.Any(f => f.Kind == "card"))
            throw new InvalidOperationException("登録済みのカード画像があります。既存の画像を使用します");
        if ((assetId == null) == (brandPath == null)) throw new ArgumentException("画像を取り出す元動画を1つ選んでください");
        if (brandPath != null)
        {
            // Only files of the registered (rights-confirmed) kit: never an arbitrary path sent by the client.
            var bumper = kit?.Files.FirstOrDefault(f => f.Kind is "opening" or "closing" && string.Equals(f.Path, brandPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("登録済みのオープニング・クロージング動画がありません");
            if (!File.Exists(bumper.Path)) throw new IngestException("E-CARD-IMAGES", $"登録した動画が見つかりません: {bumper.Path}");
            return (project, null, bumper);
        }
        var source = rt.Store.Get<SourceAsset>(Kinds.Asset, assetId!);
        if (source == null || source.ProjectId != projectId) throw new KeyNotFoundException("このプロジェクトの元動画がありません");
        if (!rt.Store.List<RightsConfirmation>(Kinds.Rights, projectId).Any(r => r.AssetId == assetId))
            throw new IngestException("E-RIGHTS-REQUIRED", "元動画を使う権利の確認にチェックしてください");
        return (project, source, null);
    }
}
