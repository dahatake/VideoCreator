using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.App;

public sealed record JobDeletionResult(string JobId, int Documents, int Renders, int Files);

/// <summary>
/// Deletes one generation (パターン) of a project: the job, its candidates, their edit plans and every render of those plans,
/// with the render files (exports folder to the Recycle Bin, previews, rejected copies) and per-render QA dirs.
/// The project's assets, rights, BGM, other jobs and the shared analysis caches are never touched.
/// Files go first: a folder that cannot be removed leaves the DB unchanged.
/// </summary>
public static class JobDeletion
{
    /// <summary>Edit plans that belong to the job: referenced by a candidate or stamped with the job id.</summary>
    public static List<string> PlanIds(Store store, Job job)
    {
        var ids = store.List<Candidate>(Kinds.Candidate, job.Id).Select(c => c.EditPlanId).OfType<string>().ToHashSet();
        foreach (var p in store.List<EditPlan>(Kinds.EditPlan, job.ProjectId)) if (p.Provenance.JobId == job.Id) ids.Add(p.EditplanId);
        return [.. ids];
    }

    public static JobDeletionResult Delete(Store store, Project project, Job job, string dataDir, RemoveFolder removeFolder, out List<string> renderIds)
    {
        var planIds = PlanIds(store, job);
        var renders = planIds.SelectMany(e => store.List<Render>(Kinds.Render, e)).ToList();
        renderIds = renders.Select(r => r.Id).ToList();

        var files = 0;
        try
        {
            foreach (var r in renders)
            {
                files += RemoveFolderInside(project.Directory, Path.Combine(project.Directory, "exports", r.Id), removeFolder);
                files += RemoveFolderInside(project.Directory, Path.Combine(project.Directory, "rejected", r.Id), removeFolder);
                foreach (var f in new[] { r.Path }.Concat(r.Outputs.Values))
                    if (IsInside(project.Directory, f) && File.Exists(f)) { File.Delete(f); files++; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FolderInUseException)
        {
            throw new FolderInUseException($"このパターンのファイルを削除できませんでした。動画を開いているアプリ（プレーヤーやエクスプローラー）を閉じてから、もう一度お試しください（{ex.Message}）");
        }

        var docs = new List<(string, string)>();
        docs.AddRange(store.Ids(Kinds.Candidate, job.Id).Select(id => (Kinds.Candidate, id)));
        docs.AddRange(renderIds.Select(id => (Kinds.Render, id)));
        docs.AddRange(planIds.SelectMany(e => store.Ids(Kinds.QaRepair, e)).Select(id => (Kinds.QaRepair, id)));
        docs.AddRange(planIds.Select(id => (Kinds.EditPlan, id)));
        docs.Add((Kinds.Job, job.Id));
        var n = store.Delete(docs);

        foreach (var r in renderIds) ProjectDeletion.TryDeleteInside(Path.Combine(dataDir, "qa"), r);
        return new JobDeletionResult(job.Id, n, renderIds.Count, files);
    }

    static bool IsInside(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    static int RemoveFolderInside(string root, string dir, RemoveFolder remove)
    {
        if (!IsInside(root, dir) || !Directory.Exists(dir)) return 0;
        var count = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
        remove(dir);
        if (Directory.Exists(dir)) throw new FolderInUseException("フォルダを削除できませんでした");
        return count;
    }
}
