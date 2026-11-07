using System.Runtime.InteropServices;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>Removes a folder tree. Production sends it to the Recycle Bin so a mistaken delete can be undone.</summary>
public delegate void RemoveFolder(string path);

public sealed class FolderInUseException(string message) : Exception(message);

public sealed record ProjectDeletionResult(string ProjectId, string Name, int Documents, string Folder, string? FolderPath, int CacheDirs);

/// <summary>
/// Deletes a project and everything derived from it:
/// DB tree (project → asset/rights/job/bgm/editplan, job → candidate, editplan → render),
/// the project folder (uploads, previews, exports, rejected, user BGM), per-render QA dirs,
/// and per-source analysis caches no other project still uses. Source files registered by path are never touched.
/// Files go first: if the folder cannot be removed nothing in the DB changes.
/// </summary>
public static class ProjectDeletion
{
    static readonly string[] ProjectChildren = [Kinds.Asset, Kinds.Rights, Kinds.Job, Kinds.Bgm, Kinds.EditPlan];

    public static ProjectDeletionResult Delete(Store store, Project p, string projectsRoot, string dataDir, RemoveFolder removeFolder, out List<string> jobIds, out List<string> renderIds)
    {
        jobIds = store.Ids(Kinds.Job, p.Id);
        var planIds = store.Ids(Kinds.EditPlan, p.Id);
        renderIds = planIds.SelectMany(e => store.Ids(Kinds.Render, e)).ToList();
        var docs = new List<(string, string)>();
        foreach (var k in ProjectChildren) docs.AddRange(store.Ids(k, p.Id).Select(id => (k, id)));
        foreach (var j in jobIds) docs.AddRange(store.Ids(Kinds.Candidate, j).Select(id => (Kinds.Candidate, id)));
        docs.AddRange(renderIds.Select(id => (Kinds.Render, id)));
        docs.Add((Kinds.Project, p.Id));
        var shas = store.List<SourceAsset>(Kinds.Asset, p.Id).Select(a => CacheKey(a.Sha256)).Where(s => s != null).ToHashSet();

        string folder; string? folderPath = null;
        if (!OwnsFolder(p, projectsRoot)) { folder = "kept"; folderPath = p.Directory; }
        else if (!Directory.Exists(p.Directory)) folder = "missing";
        else
        {
            try { removeFolder(p.Directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FolderInUseException)
            {
                throw new FolderInUseException($"プロジェクトのフォルダを削除できませんでした。動画を開いているアプリ（プレーヤーやエクスプローラー）を閉じてから、もう一度お試しください（{ex.Message}）");
            }
            if (Directory.Exists(p.Directory)) throw new FolderInUseException("プロジェクトのフォルダを削除できませんでした。開いているファイルを閉じてから、もう一度お試しください");
            folder = "removed";
        }

        var n = store.Delete(docs);

        // derived data outside the project folder (best effort: the DB is already consistent)
        foreach (var r in renderIds) TryDeleteInside(Path.Combine(dataDir, "qa"), r);
        var stillUsed = store.List<SourceAsset>(Kinds.Asset).Select(a => CacheKey(a.Sha256)).ToHashSet();
        var caches = 0;
        foreach (var s in shas.Where(s => !stillUsed.Contains(s))) if (TryDeleteInside(Path.Combine(dataDir, "cache"), s!)) caches++;
        return new ProjectDeletionResult(p.Id, p.Name, n, folder, folderPath, caches);
    }

    static string? CacheKey(string? sha) => sha is { Length: >= 16 } ? sha[..16] : null;

    /// <summary>Only folders the app created: directly under the projects root and named "&lt;name&gt;_&lt;id[2..8]&gt;".</summary>
    public static bool OwnsFolder(Project p, string projectsRoot)
    {
        if (string.IsNullOrWhiteSpace(p.Directory) || p.Id.Length < 8) return false;
        var dir = Path.GetFullPath(p.Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(projectsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetDirectoryName(dir), root, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(dir).EndsWith("_" + p.Id[2..8], StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryDeleteInside(string root, string name)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(path)) return false;
        try { Directory.Delete(path, true); return true; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void DeletePermanently(string path) => Directory.Delete(path, true);

    /// <summary>Recycle Bin via SHFileOperation with no UI (this runs inside a background web server).</summary>
    public static void SendToRecycleBin(string path)
    {
        if (!OperatingSystem.IsWindows()) { DeletePermanently(path); return; }
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };
        var rc = SHFileOperation(ref op);
        if (rc != 0 || op.fAnyOperationsAborted) throw new FolderInUseException($"ごみ箱への移動に失敗しました（コード 0x{rc:X}）");
    }

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
}
