using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VideoCreator.Models;

namespace VideoCreator.Storage;

public sealed record SnapshotDoc(string Kind, string Id, string? Parent, string Updated, JsonNode Json);

/// <summary>Everything the app knows about one project, written into its own folder so that copying the folder to another device is enough to continue there.</summary>
public sealed record ProjectSnapshotFile(int Format, string ProjectId, string ProjectName, string SavedAt, List<SnapshotDoc> Docs);

public sealed record RestoredProject(string Id, string Name, string Folder);
public sealed record RestoreFailure(string Folder, string Reason);
public sealed record MissingSource(string Project, string Path);
public sealed record RestoreResult(int Scanned, List<RestoredProject> Restored, int AlreadyPresent, List<RestoreFailure> Failed, List<MissingSource> MissingSources);

/// <summary>
/// A project's documents (project, assets, rights, finished jobs, candidates, edit plans, renders, QA repair history, its own BGM) as one JSON file
/// at <c>&lt;project folder&gt;\.videocreator\state.json</c>, plus the small analysis results (transcripts, signals, faces) from the data folder.
/// Paths inside the project folder are stored as <c>{project}/relative</c>, so the folder can be copied anywhere. Jobs still in progress are not saved.
/// </summary>
public static class ProjectSnapshot
{
    public const string Folder = ".videocreator", FileName = "state.json", Token = "{project}";
    public const int Format = 1;
    const long MaxCacheFileBytes = 64L * 1024 * 1024;
    static readonly HashSet<string> RestorableKinds = [Kinds.Project, Kinds.Asset, Kinds.Rights, Kinds.Job, Kinds.Bgm, Kinds.EditPlan, Kinds.Candidate, Kinds.Render, Kinds.QaRepair];

    public static string StatePath(string projectDir) => Path.Combine(projectDir, Folder, FileName);

    /// <summary>The snapshot of one project, or null when the project is unknown or unreadable.</summary>
    public static ProjectSnapshotFile? Build(IReadOnlyList<DocRow> rows, string projectId)
    {
        var projectRow = rows.FirstOrDefault(r => r.Kind == Kinds.Project && r.Id == projectId);
        if (projectRow == null) return null;
        Project project;
        try { project = Json.Deserialize<Project>(projectRow.Json); } catch (JsonException) { return null; }
        if (string.IsNullOrWhiteSpace(project.Directory)) return null;
        var dir = StorePaths.Normalize(project.Directory);

        var running = new HashSet<string>();
        var finishedJobs = new List<DocRow>();
        foreach (var r in rows.Where(r => r.Kind == Kinds.Job && r.Parent == projectId))
        {
            try
            {
                if (Json.Deserialize<Job>(r.Json).Status is JobStatus.Ready or JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled) finishedJobs.Add(r);
                else running.Add(r.Id);
            }
            catch (JsonException) { running.Add(r.Id); }
        }
        var jobIds = finishedJobs.Select(r => r.Id).ToHashSet();
        var plans = rows.Where(r => r.Kind == Kinds.EditPlan && r.Parent == projectId && !PlanOfRunningJob(r, running)).ToList();
        var planIds = plans.Select(r => r.Id).ToHashSet();

        var chosen = new List<DocRow> { projectRow };
        chosen.AddRange(rows.Where(r => r.Parent == projectId && r.Kind is Kinds.Asset or Kinds.Rights or Kinds.Bgm));
        chosen.AddRange(finishedJobs);
        chosen.AddRange(plans);
        chosen.AddRange(rows.Where(r => r.Kind == Kinds.Candidate && r.Parent is { } p && jobIds.Contains(p)));
        chosen.AddRange(rows.Where(r => r.Kind is Kinds.Render or Kinds.QaRepair && r.Parent is { } p && planIds.Contains(p)));

        var docs = new List<SnapshotDoc>();
        foreach (var r in chosen.OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var node = JsonNode.Parse(r.Json);
            if (node == null) continue;
            MapStrings(node, s => StorePaths.Reroot(s, dir, Token) is { } t ? Token + t[Token.Length..].Replace('\\', '/') : null);
            docs.Add(new(r.Kind, r.Id, r.Parent, r.Updated, node));
        }
        var savedAt = docs.Select(d => d.Updated).Max(StringComparer.Ordinal) ?? "";
        return new(Format, projectId, project.Name, savedAt, docs);
    }

    static bool PlanOfRunningJob(DocRow r, HashSet<string> running)
    {
        try { return Json.Deserialize<EditPlan>(r.Json).Provenance?.JobId is { } j && running.Contains(j); }
        catch (JsonException) { return true; }
    }

    public static string ToText(ProjectSnapshotFile f) => Json.Serialize(f, true);

    /// <summary>Writes the file whole or not at all (a half-written snapshot would be worse than the previous one).</summary>
    public static void Write(string projectDir, string text)
    {
        var path = StatePath(projectDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    /// <summary>Forgets a project's snapshot (the project was deleted but its folder was kept).</summary>
    public static void Remove(string projectDir)
    {
        try { var d = Path.Combine(projectDir, Folder); if (Directory.Exists(d)) Directory.Delete(d, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Keeps a copy of the small analysis files (transcripts cost money to make again) beside the snapshot. Returns the number of files copied.</summary>
    public static int MirrorCache(string projectDir, string dataDir, IEnumerable<string> shas)
    {
        var copied = 0;
        foreach (var key in shas.Where(s => s.Length >= 16).Select(s => s[..16]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var src = Path.Combine(dataDir, "cache", key);
            if (!Directory.Exists(src)) continue;
            var dst = Path.Combine(projectDir, Folder, "cache", key);
            foreach (var f in Directory.EnumerateFiles(src, "*.json"))
            {
                var info = new FileInfo(f);
                if (info.Length > MaxCacheFileBytes) continue;
                var target = Path.Combine(dst, info.Name);
                if (File.Exists(target) && new FileInfo(target).Length == info.Length && File.GetLastWriteTimeUtc(target) >= info.LastWriteTimeUtc) continue;
                Directory.CreateDirectory(dst);
                File.Copy(f, target, true);
                copied++;
            }
        }
        return copied;
    }

    /// <summary>
    /// Brings back every project found in the given folders whose id the store does not know yet. A project the store already has is left as it is
    /// (this device's data wins); an unreadable or inconsistent snapshot is reported and skipped.
    /// </summary>
    public static RestoreResult Restore(Store store, IEnumerable<string> roots, string dataDir)
    {
        var rows = store.Rows();
        var known = rows.Select(r => (r.Kind, r.Id)).ToHashSet();
        int scanned = 0, present = 0;
        var restored = new List<RestoredProject>(); var failed = new List<RestoreFailure>(); var missing = new List<MissingSource>();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(StorePaths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(root).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(new(root, "フォルダーを読めません")); continue; }
            foreach (var dir in children.Where(d => File.Exists(StatePath(d))).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                scanned++;
                try
                {
                    var file = Json.Deserialize<ProjectSnapshotFile>(File.ReadAllText(StatePath(dir)));
                    if (file.Format != Format) { failed.Add(new(dir, "このアプリより新しい形式のため読み込めません")); continue; }
                    if (known.Contains((Kinds.Project, file.ProjectId))) { present++; continue; }
                    var problem = Validate(file, known);
                    if (problem != null) { failed.Add(new(dir, problem)); continue; }
                    var folder = StorePaths.Normalize(dir);
                    var imported = file.Docs.Select(d => ToRow(d, folder)).ToList();
                    imported = FixWorkflow(imported, file.ProjectId);
                    store.Import(imported);
                    foreach (var r in imported) known.Add((r.Kind, r.Id));
                    RestoreCache(dir, dataDir);
                    var name = file.ProjectName;
                    restored.Add(new(file.ProjectId, name, folder));
                    foreach (var r in imported.Where(r => r.Kind == Kinds.Asset))
                        if (Json.Deserialize<SourceAsset>(r.Json) is { } a && !File.Exists(a.Path)) missing.Add(new(name, a.Path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                {
                    failed.Add(new(dir, "読み込めませんでした"));
                }
            }
        }
        return new(scanned, restored, present, failed, missing);
    }

    static string? Validate(ProjectSnapshotFile f, HashSet<(string, string)> known)
    {
        if (string.IsNullOrWhiteSpace(f.ProjectId) || f.ProjectId.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')) return "プロジェクト ID が正しくありません";
        if (f.Docs.Count(d => d.Kind == Kinds.Project && d.Id == f.ProjectId) != 1) return "プロジェクトの情報が見つかりません";
        foreach (var d in f.Docs)
        {
            if (!RestorableKinds.Contains(d.Kind) || string.IsNullOrWhiteSpace(d.Id) || d.Json is not JsonObject) return "読み込めない種類のデータが含まれています";
            if (known.Contains((d.Kind, d.Id))) return "別のプロジェクトのデータと ID が重複しています";
        }
        return null;
    }

    static DocRow ToRow(SnapshotDoc d, string folder)
    {
        MapStrings(d.Json, s => FromToken(s, folder));
        return new(d.Kind, d.Id, d.Parent, d.Json.ToJsonString(Json.Options), string.IsNullOrWhiteSpace(d.Updated) ? DateTimeOffset.UtcNow.ToString("O") : d.Updated);
    }

    static string? FromToken(string s, string folder) =>
        s == Token ? folder : s.StartsWith(Token + "/", StringComparison.Ordinal) ? Path.Combine(folder, s[(Token.Length + 1)..].Replace('/', Path.DirectorySeparatorChar)) : null;

    /// <summary>Jobs in progress were not saved; a saved "current step" that points at one of them would lead nowhere.</summary>
    static List<DocRow> FixWorkflow(List<DocRow> rows, string projectId)
    {
        var ids = rows.Select(r => (r.Kind, r.Id)).ToHashSet();
        var i = rows.FindIndex(r => r.Kind == Kinds.Project && r.Id == projectId);
        var p = Json.Deserialize<Project>(rows[i].Json);
        if (p.WorkflowState is not { } ws) return rows;
        bool dangling = ws.JobId != null && !ids.Contains((Kinds.Job, ws.JobId)) || ws.EditPlanId != null && !ids.Contains((Kinds.EditPlan, ws.EditPlanId))
            || ws.RenderId != null && !ids.Contains((Kinds.Render, ws.RenderId));
        if (!dangling) return rows;
        rows[i] = rows[i] with { Json = Json.Serialize(p with { WorkflowState = new ProjectWorkflowState { Step = "settings" } }) };
        return rows;
    }

    static void RestoreCache(string projectDir, string dataDir)
    {
        var src = Path.Combine(projectDir, Folder, "cache");
        if (!Directory.Exists(src)) return;
        foreach (var d in Directory.EnumerateDirectories(src))
        {
            var key = Path.GetFileName(d);
            var dst = Path.Combine(dataDir, "cache", key);
            foreach (var f in Directory.EnumerateFiles(d, "*.json"))
            {
                var target = Path.Combine(dst, Path.GetFileName(f));
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(dst);
                File.Copy(f, target);
            }
        }
    }

    /// <summary>Replaces every string value for which <paramref name="map"/> returns a replacement.</summary>
    static void MapStrings(JsonNode node, Func<string, string?> map)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (o[key] is JsonValue v && v.TryGetValue<string>(out var s)) { if (map(s) is { } r) o[key] = r; }
                    else if (o[key] is { } child) MapStrings(child, map);
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue<string>(out var s)) { if (map(s) is { } r) a[i] = r; }
                    else if (a[i] is { } child) MapStrings(child, map);
                }
                break;
        }
    }
}
