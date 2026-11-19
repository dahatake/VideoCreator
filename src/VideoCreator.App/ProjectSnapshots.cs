using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>
/// Keeps each project's snapshot (<see cref="ProjectSnapshot"/>) in its folder up to date, and brings projects back from snapshots found in the projects folder.
/// Writes are batched a couple of seconds after the last change and paused while the projects folder is being moved.
/// </summary>
public sealed class ProjectSnapshots : BackgroundService
{
    readonly Runtime _rt;
    readonly ProjectsLocation _loc;
    readonly ILogger<ProjectSnapshots> _log;
    readonly SemaphoreSlim _wake = new(0);
    readonly SemaphoreSlim _gate = new(1);
    readonly Dictionary<string, string> _written = [];

    /// <summary>How long the store must be quiet before snapshots are written.</summary>
    internal TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(2);

    public ProjectSnapshots(Runtime rt, ProjectsLocation loc, ILogger<ProjectSnapshots> log)
    {
        _rt = rt; _loc = loc; _log = log;
        rt.Store.Changed += Signal;
        Signal();
    }

    void Signal() { if (_wake.CurrentCount == 0) { try { _wake.Release(); } catch (SemaphoreFullException) { } } }

    /// <summary>Brings back projects saved in the projects folders (current and earlier) that this device does not know yet.</summary>
    public RestoreResult Restore()
    {
        _gate.Wait();
        try
        {
            var result = ProjectSnapshot.Restore(_rt.Store, _loc.KnownRoots, _rt.Options.ResolvedDataDir);
            if (result.Restored.Count > 0) _log.LogInformation("Restored {Count} project(s) from the projects folder", result.Restored.Count);
            foreach (var f in result.Failed) _log.LogWarning("Could not restore {Folder}: {Reason}", f.Folder, f.Reason);
            return result;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Writes the snapshots that changed. Returns the number of projects written.</summary>
    public int Flush()
    {
        _gate.Wait();
        try
        {
            var rows = _rt.Store.Rows();
            var written = 0;
            foreach (var project in rows.Where(r => r.Kind == Kinds.Project))
            {
                try
                {
                    var snap = ProjectSnapshot.Build(rows, project.Id);
                    var dir = snap == null ? null : Json.Deserialize<Project>(project.Json).Directory;
                    if (snap == null || !Directory.Exists(dir)) continue;
                    var text = ProjectSnapshot.ToText(snap);
                    var shas = rows.Where(r => r.Kind == Kinds.Asset && r.Parent == project.Id).Select(r => Json.Deserialize<SourceAsset>(r.Json).Sha256);
                    ProjectSnapshot.MirrorCache(dir, _rt.Options.ResolvedDataDir, shas);
                    if (_written.TryGetValue(project.Id, out var last) && last == text && File.Exists(ProjectSnapshot.StatePath(dir))) continue;
                    ProjectSnapshot.Write(dir, text);
                    _written[project.Id] = text;
                    written++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    _log.LogWarning(ex, "Could not save the snapshot of project {Project}", project.Id);
                }
            }
            return written;
        }
        finally { _gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync(stoppingToken);
                await Task.Delay(Debounce, stoppingToken);
                while (_loc.IsMoving) await Task.Delay(500, stoppingToken);
                Flush();
            }
        }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _rt.Store.Changed -= Signal;
        try { if (!_loc.IsMoving) Flush(); } catch (Exception ex) { _log.LogWarning(ex, "Final snapshot failed"); }
    }
}
