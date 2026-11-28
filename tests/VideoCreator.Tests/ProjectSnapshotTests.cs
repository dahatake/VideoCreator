using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>A project folder carries its own state: copy it to another device and the projects come back as they were.</summary>
public sealed class ProjectSnapshotTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-snap-" + Guid.NewGuid().ToString("N")[..8]);
    string RootA => Path.Combine(_root, "deviceA", "プロジェクト");
    string RootB => Path.Combine(_root, "deviceB", "Videos");
    Store _a = null!, _b = null!;
    string _outside = "";

    public void Dispose()
    {
        _a?.Dispose(); _b?.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    static readonly string Sha = new('a', 64);

    (Project Project, string Dir) Seed(Store store, string root, string workflowJob = "job_done")
    {
        var dir = Path.Combine(root, "動画_abc123");
        Directory.CreateDirectory(Path.Combine(dir, "assets")); Directory.CreateDirectory(Path.Combine(dir, "exports"));
        var upload = Path.Combine(dir, "assets", "clip.mp4"); File.WriteAllBytes(upload, [1, 2, 3]);
        var final = Path.Combine(dir, "exports", "out.mp4"); File.WriteAllBytes(final, [4, 5]);
        _outside = Path.Combine(_root, "elsewhere", "registered.mp4");
        var media = new MediaInfo { Duration = 10, Width = 640, Height = 360 };
        var p = new Project { Id = "p_abc123def0", Name = "動画", Directory = dir, WorkflowState = new ProjectWorkflowState { Step = "exports", JobId = workflowJob } };
        store.Put(Kinds.Project, p.Id, null, p);
        store.Put(Kinds.Asset, "a_up", p.Id, new SourceAsset { Id = "a_up", ProjectId = p.Id, Path = upload, Sha256 = Sha, Media = media });
        store.Put(Kinds.Asset, "a_out", p.Id, new SourceAsset { Id = "a_out", ProjectId = p.Id, Path = _outside, Sha256 = new string('b', 64), Media = media });
        store.Put(Kinds.Rights, "rt1", p.Id, new { id = "rt1", asset_id = "a_up" });
        store.Put(Kinds.Job, "job_done", p.Id, new Job { Id = "job_done", ProjectId = p.Id, PresetIds = ["P4"], Status = JobStatus.Done });
        store.Put(Kinds.Job, "job_run", p.Id, new Job { Id = "job_run", ProjectId = p.Id, PresetIds = ["P4"], Status = JobStatus.Analyzing });
        store.Put(Kinds.Candidate, "c_done", "job_done", new { id = "c_done" });
        store.Put(Kinds.Candidate, "c_run", "job_run", new { id = "c_run" });
        EditPlan Plan(string id, string job) => new()
        {
            EditplanId = id, ProjectId = p.Id, Preset = Fixtures.Presets["P4"].Ref, Timeline = [], Audio = new AudioSpec { Master = "a_up" },
            Provenance = new Provenance { JobId = job },
        };
        store.Put(Kinds.EditPlan, "ep_done", p.Id, Plan("ep_done", "job_done"));
        store.Put(Kinds.EditPlan, "ep_run", p.Id, Plan("ep_run", "job_run"));
        store.Put(Kinds.Render, "r_final", "ep_done", new Render { Id = "r_final", EditplanId = "ep_done", Kind = "final", Path = final, Outputs = new() { ["mp4"] = final } });
        store.Put(Kinds.QaRepair, "qr1", "ep_done", new { id = "qr1" });
        store.Put(Kinds.Bgm, "bgm_user", p.Id, new BgmTrack { Id = "bgm_user", Name = "song", Path = Path.Combine(dir, "bgm", "song.mp3"), Source = "user", ProjectId = p.Id });
        store.Put(Kinds.Bgm, "bgm_shared", "app", new BgmTrack { Id = "bgm_shared", Name = "shared", Path = Path.Combine(_root, "shared.mp3"), Source = "shared" });
        return (p, dir);
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)));
    }

    /// <summary>Device A saves the project; its folder is the only thing that reaches device B.</summary>
    string SaveAndCopy(out string dirA)
    {
        _a = new Store(Path.Combine(_root, "deviceA", "data", "db.sqlite"));
        var (_, dir) = Seed(_a, RootA);
        dirA = dir;
        var cache = Path.Combine(_root, "deviceA", "data", "cache", Sha[..16]); Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "transcript_x.json"), "{\"words\":[]}");
        File.WriteAllBytes(Path.Combine(cache, "proxy.mp4"), new byte[100]);
        var rows = _a.Rows();
        var snap = ProjectSnapshot.Build(rows, "p_abc123def0")!;
        ProjectSnapshot.Write(dir, ProjectSnapshot.ToText(snap));
        Assert.Equal(1, ProjectSnapshot.MirrorCache(dir, Path.Combine(_root, "deviceA", "data"), [Sha]));
        var copy = Path.Combine(RootB, Path.GetFileName(dir));
        CopyDir(dir, copy);
        _b = new Store(Path.Combine(_root, "deviceB", "data", "db.sqlite"));
        return copy;
    }

    [Fact]
    public void A_copied_project_folder_restores_the_project_on_another_device()
    {
        var copy = SaveAndCopy(out var dirA);
        var dataB = Path.Combine(_root, "deviceB", "data");

        var result = ProjectSnapshot.Restore(_b, [RootB], dataB);

        Assert.Single(result.Restored);
        Assert.Empty(result.Failed);
        var p = _b.Get<Project>(Kinds.Project, "p_abc123def0")!;
        Assert.Equal(copy, p.Directory);
        var assets = _b.List<SourceAsset>(Kinds.Asset, p.Id).ToDictionary(a => a.Id);
        Assert.Equal(Path.Combine(copy, "assets", "clip.mp4"), assets["a_up"].Path);
        Assert.True(File.Exists(assets["a_up"].Path));
        Assert.Equal(_outside, assets["a_out"].Path);   // not inside the folder: stays as registered, and is reported
        var missing = Assert.Single(result.MissingSources);
        Assert.Equal(_outside, missing.Path);
        var render = _b.Get<Render>(Kinds.Render, "r_final")!;
        Assert.Equal(Path.Combine(copy, "exports", "out.mp4"), render.Path);
        Assert.Equal(Path.Combine(copy, "exports", "out.mp4"), render.Outputs["mp4"]);
        Assert.Equal(Path.Combine(copy, "bgm", "song.mp3"), _b.Get<BgmTrack>(Kinds.Bgm, "bgm_user")!.Path);
        Assert.NotNull(_b.Get<EditPlan>(Kinds.EditPlan, "ep_done"));
        Assert.NotNull(_b.Get<Job>(Kinds.Job, "job_done"));
        Assert.Single(_b.Ids(Kinds.Candidate, "job_done"));
        Assert.Single(_b.Ids(Kinds.QaRepair, "ep_done"));
        Assert.Single(_b.Ids(Kinds.Rights, p.Id));
        // not saved: the unfinished job, its candidate and edit plan, and the app-wide BGM
        Assert.Null(_b.Get<Job>(Kinds.Job, "job_run"));
        Assert.Empty(_b.Ids(Kinds.Candidate, "job_run"));
        Assert.Null(_b.Get<EditPlan>(Kinds.EditPlan, "ep_run"));
        Assert.Null(_b.Get<BgmTrack>(Kinds.Bgm, "bgm_shared"));
        // the transcript is not paid for again; the big proxy is rebuilt locally
        Assert.True(File.Exists(Path.Combine(dataB, "cache", Sha[..16], "transcript_x.json")));
        Assert.False(File.Exists(Path.Combine(dataB, "cache", Sha[..16], "proxy.mp4")));
        // update times survive, so "newest first" lists keep their order
        Assert.Equal(_a.Rows().First(r => r.Id == "r_final").Updated, _b.Rows().First(r => r.Id == "r_final").Updated);
        Assert.True(Directory.Exists(dirA));
    }

    [Fact]
    public void Restoring_twice_changes_nothing_and_never_overwrites_this_devices_project()
    {
        SaveAndCopy(out _);
        var dataB = Path.Combine(_root, "deviceB", "data");
        ProjectSnapshot.Restore(_b, [RootB], dataB);
        var p = _b.Get<Project>(Kinds.Project, "p_abc123def0")!;
        _b.Put(Kinds.Project, p.Id, null, p with { Name = "この端末で改名" });

        var again = ProjectSnapshot.Restore(_b, [RootB], dataB);

        Assert.Empty(again.Restored);
        Assert.Equal(1, again.AlreadyPresent);
        Assert.Equal("この端末で改名", _b.Get<Project>(Kinds.Project, p.Id)!.Name);
    }

    [Fact]
    public void A_current_step_pointing_at_an_unsaved_job_goes_back_to_settings()
    {
        _a = new Store(Path.Combine(_root, "deviceA", "data", "db.sqlite"));
        var (_, dir) = Seed(_a, RootA, workflowJob: "job_run");
        ProjectSnapshot.Write(dir, ProjectSnapshot.ToText(ProjectSnapshot.Build(_a.Rows(), "p_abc123def0")!));
        _b = new Store(Path.Combine(_root, "deviceB", "data", "db.sqlite"));

        ProjectSnapshot.Restore(_b, [RootA], Path.Combine(_root, "deviceB", "data"));

        // same id exists nowhere in B, so it is restored from A's folder; its step no longer names the unfinished job
        var step = _b.Get<Project>(Kinds.Project, "p_abc123def0")!.WorkflowState!;
        Assert.Equal("settings", step.Step);
        Assert.Null(step.JobId);
    }

    [Fact]
    public void A_snapshot_cannot_bring_in_settings_or_clash_with_another_projects_documents()
    {
        var copy = SaveAndCopy(out _);
        var file = ProjectSnapshot.StatePath(copy);
        var text = File.ReadAllText(file).Replace("\"kind\": \"rights\"", "\"kind\": \"setting\"");
        File.WriteAllText(file, text);
        var dataB = Path.Combine(_root, "deviceB", "data");

        var rejected = ProjectSnapshot.Restore(_b, [RootB], dataB);
        Assert.Empty(rejected.Restored);
        Assert.Single(rejected.Failed);
        Assert.Null(_b.Get<Project>(Kinds.Project, "p_abc123def0"));

        File.WriteAllText(file, File.ReadAllText(file).Replace("\"kind\": \"setting\"", "\"kind\": \"rights\""));
        _b.Put(Kinds.Asset, "a_up", "p_other", new { id = "a_up" });
        var clash = ProjectSnapshot.Restore(_b, [RootB], dataB);
        Assert.Empty(clash.Restored);
        Assert.Single(clash.Failed);
    }

    [Fact]
    public void An_unreadable_snapshot_is_reported_and_does_not_stop_the_others()
    {
        var copy = SaveAndCopy(out _);
        var broken = Path.Combine(RootB, "broken_000001");
        Directory.CreateDirectory(Path.Combine(broken, ProjectSnapshot.Folder));
        File.WriteAllText(ProjectSnapshot.StatePath(broken), "{ not json");

        var result = ProjectSnapshot.Restore(_b, [RootB], Path.Combine(_root, "deviceB", "data"));

        Assert.Single(result.Restored);
        Assert.Equal(broken, Assert.Single(result.Failed).Folder);
        Assert.Equal(2, result.Scanned);
        Assert.True(Directory.Exists(copy));
    }
}

public sealed class ProjectSnapshotServiceTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-snapsvc-" + Guid.NewGuid().ToString("N")[..8]);
    Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
            b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
            b.UseSetting("VideoCreator:OpenBrowser", "false");
            b.UseSetting("VideoCreator:Port", "0");
            b.UseSetting("VideoCreator:Asr:Provider", "none");
            b.UseSetting("VideoCreator:Llm:Provider", "none");
        });
        _app.UseKestrel();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_new_project_writes_its_snapshot_and_the_restore_endpoint_reports_it_as_known()
    {
        using var created = await _http.PostAsJsonAsync("/api/projects", new { name = "自動保存" }, Ct);
        created.EnsureSuccessStatusCode();
        var dir = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("directory").GetString()!;
        var state = ProjectSnapshot.StatePath(dir);
        for (var i = 0; i < 100 && !File.Exists(state); i++) await Task.Delay(100, Ct);
        Assert.True(File.Exists(state), "the snapshot is written shortly after the change");
        Assert.Contains("自動保存", File.ReadAllText(state));

        using var r = await _http.PostAsync("/api/projects/restore", null, Ct);
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        Assert.Equal(1, body.GetProperty("scanned").GetInt32());
        Assert.Equal(1, body.GetProperty("already_present").GetInt32());
        Assert.Equal(0, body.GetProperty("restored").GetArrayLength());
    }

    [Fact]
    public async Task A_store_that_keeps_changing_is_saved_once_per_max_delay_and_its_last_state_once_quiet()
    {
        var snapshots = _app.Services.GetRequiredService<ProjectSnapshots>();
        snapshots.Debounce = TimeSpan.FromMilliseconds(200);
        snapshots.MaxDelay = TimeSpan.FromSeconds(1);
        using var created = await _http.PostAsJsonAsync("/api/projects", new { name = "連続変更" }, Ct);
        created.EnsureSuccessStatusCode();
        var project = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        var id = project.GetProperty("id").GetString()!;
        var state = ProjectSnapshot.StatePath(project.GetProperty("directory").GetString()!);
        for (var i = 0; i < 100 && !File.Exists(state); i++) await Task.Delay(100, Ct);
        await Task.Delay(500, Ct);

        // like a running job reporting progress: a change every 50 ms for 3 seconds
        var store = _app.Services.GetRequiredService<Runtime>().Store;
        var before = snapshots.Flushes;
        var n = 0;
        for (var until = DateTime.UtcNow.AddSeconds(3); DateTime.UtcNow < until; n++)
        {
            store.Put(Kinds.Job, "job_burst", id, new Job { Id = "job_burst", ProjectId = id, PresetIds = ["P4"], Status = JobStatus.Done, Message = $"step {n}" });
            await Task.Delay(50, Ct);
        }
        var during = snapshots.Flushes - before;
        Assert.InRange(during, 2, 4);   // about every MaxDelay, not every Debounce (that would be ~15)

        var last = $"step {n - 1}";
        for (var i = 0; i < 50 && !File.ReadAllText(state).Contains(last); i++) await Task.Delay(100, Ct);
        Assert.Contains(last, File.ReadAllText(state));
    }
}
