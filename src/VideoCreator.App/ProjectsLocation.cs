using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>The projects folder chosen in the settings screen, and every earlier one (folders the app created there are still its own).</summary>
public sealed record ProjectsDirSetting(string Root, List<string> PreviousRoots);

/// <summary>One top-level item of the current projects folder that "move" would carry over.</summary>
public sealed record MoveItem(string Name, bool IsDirectory, long Bytes, int Files, string? ProjectId, string? ProjectName);

/// <summary>What changing the projects folder would do; nothing has changed yet. <see cref="Problems"/> non-empty = it will be refused.</summary>
public sealed record MovePlan(string From, string To, bool Move, List<MoveItem> Items, long TotalBytes, bool SameVolume, long? FreeBytes,
    int ProjectsElsewhere, List<string> Problems);

public sealed record MoveFailure(string Name, string Reason);

public sealed record MoveResult(string ProjectsDir, bool Moved, List<string> MovedItems, List<MoveFailure> Failed, List<string> LeftBehind, int DocumentsUpdated);

/// <summary>Byte-measured progress of a move (copy across drives); a rename on the same drive completes an item at once.</summary>
public sealed record MoveProgress(bool Running, long DoneBytes, long TotalBytes, int DoneItems, int TotalItems, string? Current);

public sealed class ProjectsBusyException(string message) : Exception(message);

/// <summary>
/// Changes the projects folder. "Move" carries every top-level item of the current folder to the new one, one item at a time:
/// rename on the same drive, otherwise copy → verify (files and bytes) → switch; then every stored absolute path under the item is
/// re-rooted in one transaction, and only then the original is removed. A journal in the data folder records the item in flight,
/// so an interrupted move is completed (or cleaned up) at the next start. "Don't move" only changes where new projects go.
/// </summary>
public sealed class ProjectsLocation(Runtime rt, JobQueue q, SceneCardService cards, ILogger<ProjectsLocation> log)
{
    const string SettingId = "projects_dir";
    readonly SemaphoreSlim _gate = new(1);
    volatile MoveProgress _progress = new(false, 0, 0, 0, 0, null);
    public MoveProgress Progress => _progress;
    public bool IsMoving => _gate.CurrentCount == 0;

    string Journal => Path.Combine(rt.Options.ResolvedDataDir, "projects-move.json");
    ProjectsDirSetting? Saved => rt.Store.Get<ProjectsDirSetting>(Kinds.Setting, SettingId);

    /// <summary>Where new projects are created: the folder chosen in the app, else the configured / default one.</summary>
    public string Root => Saved?.Root ?? rt.Options.ResolvedProjectsDir;
    public string Source => Saved != null ? "app" : string.IsNullOrWhiteSpace(rt.Options.ProjectsDir) ? "default" : "config";
    /// <summary>The current folder first, then earlier ones (and the configured one): project folders created there are owned by the app.</summary>
    public IReadOnlyList<string> KnownRoots => [Root, .. (Saved?.PreviousRoots ?? []), rt.Options.ResolvedProjectsDir];

    sealed record JournalEntry(string From, string To, string Item, string Mode);

    public MovePlan Plan(string? path, bool move)
    {
        var problems = new List<string>();
        var from = StorePaths.Normalize(Root);
        string to = "";
        var raw = path?.Trim().Trim('"') ?? "";
        if (raw.Length == 0 || !Path.IsPathFullyQualified(raw)) problems.Add("保存先のフォルダーをフルパスで指定してください（例: D:\\Videos\\VideoCreator）");
        else
        {
            to = StorePaths.Normalize(raw);
            if (to.Equals(from, StringComparison.OrdinalIgnoreCase)) problems.Add("いまと同じフォルダーです");
            else if (StorePaths.Reroot(to, from, from) != null && move) problems.Add("いまの保存先の中へは移動できません。別の場所を選んでください");
            if (StorePaths.Reroot(to, StorePaths.Normalize(rt.Options.ResolvedDataDir), "x") != null)
                problems.Add("アプリのデータフォルダーの中は選べません。別の場所を選んでください");
            var inside = rt.Store.List<Project>(Kinds.Project).FirstOrDefault(p => StorePaths.Reroot(to, StorePaths.Normalize(p.Directory), "x") != null);
            if (inside != null) problems.Add($"プロジェクト「{inside.Name}」のフォルダーの中は選べません");
            if (File.Exists(to)) problems.Add("同じ名前のファイルがあります。フォルダーを選んでください");
        }
        var items = new List<MoveItem>();
        long total = 0;
        if (move && Directory.Exists(from))
        {
            var byFolder = rt.Store.List<Project>(Kinds.Project).GroupBy(p => StorePaths.Normalize(p.Directory), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var e in new DirectoryInfo(from).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                var (bytes, files) = e is DirectoryInfo d ? Measure(d) : (((FileInfo)e).Length, 1);
                byFolder.TryGetValue(StorePaths.Normalize(e.FullName), out var proj);
                items.Add(new(e.Name, e is DirectoryInfo, bytes, files, proj?.Id, proj?.Name));
                total += bytes;
            }
        }
        bool same = to.Length > 0 && string.Equals(Path.GetPathRoot(to), Path.GetPathRoot(from), StringComparison.OrdinalIgnoreCase);
        long? free = null;
        if (to.Length > 0 && Path.GetPathRoot(to) is { } r)
        {
            try { free = new DriveInfo(r).AvailableFreeSpace; } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { problems.Add($"ドライブ {r} を使えません"); }
        }
        if (move && to.Length > 0 && problems.Count == 0)
        {
            var clash = items.Where(i => Path.Exists(Path.Combine(to, i.Name))).Select(i => i.Name).ToList();
            if (clash.Count > 0) problems.Add($"移動先に同じ名前があるため、上書きしないよう中止します: {string.Join("、", clash.Take(5))}{(clash.Count > 5 ? $" ほか {clash.Count - 5} 件" : "")}");
            if (!same && free is long f && total > f - 256L * 1024 * 1024) problems.Add($"移動先の空き容量が足りません（必要 {Size(total)}・空き {Size(f)}）");
        }
        var elsewhere = rt.Store.List<Project>(Kinds.Project).Count(p => StorePaths.Reroot(StorePaths.Normalize(p.Directory), from, from) == null);
        return new MovePlan(from, to, move, items, total, same, free, elsewhere, problems);
    }

    public async Task<MoveResult> ChangeAsync(string? path, bool move, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new ProjectsBusyException("保存先を変更しています。完了を待ってください");
        try
        {
            if (!q.IsIdle || cards.AnyBusy) throw new ProjectsBusyException("生成・書き出し・プレビュー作成・カード画像の作成が終わってから変更してください（実行中のファイルを動かさないため）");
            var plan = Plan(path, move);
            if (plan.Problems.Count > 0) throw new ArgumentException(string.Join(" / ", plan.Problems));
            Directory.CreateDirectory(plan.To);
            Probe(plan.To);
            var moved = new List<string>(); var failed = new List<MoveFailure>(); var left = new List<string>();
            int docs = 0; long done = 0;
            _progress = new(true, 0, plan.TotalBytes, 0, plan.Items.Count, null);
            foreach (var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                _progress = _progress with { Current = item.ProjectName ?? item.Name };
                var src = Path.Combine(plan.From, item.Name); var dst = Path.Combine(plan.To, item.Name);
                try
                {
                    var (n, leftover) = MoveItemSafely(src, dst, item, b => _progress = _progress with { DoneBytes = done + b });
                    docs += n; moved.Add(item.Name);
                    if (leftover) left.Add(src);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.LogWarning(ex, "Could not move {Item}", src);
                    failed.Add(new(item.ProjectName ?? item.Name, ex is UnauthorizedAccessException ? "アクセスが拒否されました" : "使用中のファイルがあるか、コピーできませんでした。開いている動画を閉じてからもう一度お試しください"));
                    // stop here: what has been moved is consistent, the rest stays where it is (and is still found there)
                    break;
                }
                done += item.Bytes;
                _progress = _progress with { DoneBytes = done, DoneItems = moved.Count };
            }
            // new projects go to the new folder even after a partial move; the old one stays known so its project folders are still the app's
            var prev = KnownRoots.Skip(1).Prepend(plan.From).Where(r => !StorePaths.Normalize(r).Equals(plan.To, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            rt.Store.Put(Kinds.Setting, SettingId, null, new ProjectsDirSetting(plan.To, prev));
            return new MoveResult(plan.To, move, moved, failed, left, docs);
        }
        finally { _progress = _progress with { Running = false }; _gate.Release(); }
    }

    /// <summary>Tests: take the other-drive path (copy → verify → switch) even on one drive.</summary>
    internal bool ForceCopy { get; set; }

    /// <summary>Returns (documents updated, original left behind).</summary>
    (int Docs, bool LeftBehind) MoveItemSafely(string src, string dst, MoveItem item, Action<long> copied)
    {
        bool sameDrive = !ForceCopy && string.Equals(Path.GetPathRoot(src), Path.GetPathRoot(dst), StringComparison.OrdinalIgnoreCase);
        if (sameDrive)
        {
            WriteJournal(new(src, dst, item.Name, "rename"));
            try
            {
                if (item.IsDirectory) Directory.Move(src, dst); else File.Move(src, dst);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 17) { sameDrive = false; }   // ERROR_NOT_SAME_DEVICE (e.g. a mounted volume)
            catch { ClearJournal(); throw; }
            if (sameDrive)
            {
                var n = StorePaths.Rewrite(rt.Store, src, dst);
                ClearJournal();
                return (n, false);
            }
        }
        // another drive: copy beside the target, verify files and bytes, switch it in, re-root the paths, then remove the original
        var temp = Path.Combine(Path.GetDirectoryName(dst)!, "." + item.Name + ".vc-moving");
        WriteJournal(new(src, dst, item.Name, "copy"));
        try
        {
            if (item.IsDirectory) CopyTree(src, temp, copied); else { File.Copy(src, temp); copied(new FileInfo(temp).Length); }
            var (b, f) = item.IsDirectory ? Measure(new DirectoryInfo(temp)) : (new FileInfo(temp).Length, 1);
            var (sb, sf) = item.IsDirectory ? Measure(new DirectoryInfo(src)) : (new FileInfo(src).Length, 1);
            if (b != sb || f != sf) throw new IOException($"コピーの照合に失敗しました（{f}/{sf} ファイル）");
            if (item.IsDirectory) Directory.Move(temp, dst); else File.Move(temp, dst);
        }
        catch
        {
            TryRemove(temp);
            ClearJournal();
            throw;
        }
        WriteJournal(new(src, dst, item.Name, "copied"));
        var docs = StorePaths.Rewrite(rt.Store, src, dst);
        bool leftover = !TryRemove(src);
        ClearJournal();
        return (docs, leftover);
    }

    /// <summary>At start-up: finish a move interrupted after its files were in place, or drop a half-made copy.</summary>
    public void Recover()
    {
        if (!File.Exists(Journal)) return;
        try
        {
            var j = Json.Deserialize<JournalEntry>(File.ReadAllText(Journal));
            if (j.Mode == "copy") TryRemove(Path.Combine(Path.GetDirectoryName(j.To)!, "." + j.Item + ".vc-moving"));
            else if (Path.Exists(j.To) && (j.Mode == "copied" || !Path.Exists(j.From)))
            {
                var n = StorePaths.Rewrite(rt.Store, j.From, j.To);
                if (j.Mode == "copied") TryRemove(j.From);
                log.LogInformation("Completed an interrupted move of {Item}: {Docs} documents updated", j.To, n);
            }
            File.Delete(Journal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { log.LogWarning(ex, "Could not recover the projects move journal"); }
    }

    void WriteJournal(JournalEntry e) { Directory.CreateDirectory(Path.GetDirectoryName(Journal)!); File.WriteAllText(Journal, Json.Serialize(e)); }
    void ClearJournal() { try { File.Delete(Journal); } catch (IOException) { } }

    static void Probe(string dir)
    {
        var f = Path.Combine(dir, $".vc-write-test-{Guid.NewGuid():N}");
        try { File.WriteAllText(f, ""); File.Delete(f); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new ArgumentException("選んだフォルダーに書き込めません。別のフォルダーを選んでください"); }
    }

    static void CopyTree(string src, string dst, Action<long> copied)
    {
        long n = 0;
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, d)));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, f));
            File.Copy(f, target);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(f));
            copied(n += new FileInfo(f).Length);
        }
    }

    static (long Bytes, int Files) Measure(DirectoryInfo d)
    {
        long b = 0; int n = 0;
        foreach (var f in d.EnumerateFiles("*", SearchOption.AllDirectories)) { b += f.Length; n++; }
        return (b, n);
    }

    static bool TryRemove(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true); else if (File.Exists(path)) File.Delete(path);
            return !Path.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):F1} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):F1} MB" : $"{Math.Max(1, b / 1024)} KB";
}
