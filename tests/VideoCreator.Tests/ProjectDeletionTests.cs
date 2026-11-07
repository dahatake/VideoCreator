using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>Project delete: the whole document tree and derived files go, user-owned files and other projects stay.</summary>
public sealed class ProjectDeletionTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-del-" + Guid.NewGuid().ToString("N")[..8]);
    string Projects => Path.Combine(_root, "projects");
    string Data => Path.Combine(_root, "data");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    static void Touch(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "x"); }

    /// <summary>Seeds a project with one of every child kind plus its files. Returns the project.</summary>
    Project Seed(Store store, string id, string sha, string? dir = null)
    {
        var p = new Project { Id = id, Name = "名前" + id, Directory = dir ?? Path.Combine(Projects, $"名前_{id[2..8]}") };
        store.Put(Kinds.Project, p.Id, null, p);
        Touch(Path.Combine(p.Directory, "exports", "final.mp4"));
        Touch(Path.Combine(p.Directory, "assets", "up.mp4"));
        var asset = new SourceAsset { Id = "a_" + id, ProjectId = id, Path = Path.Combine(_root, "external", id + ".mp4"), Sha256 = sha, Media = Fixtures.Landscape() };
        Touch(asset.Path);
        store.Put(Kinds.Asset, asset.Id, id, asset);
        store.Put(Kinds.Rights, "rt_" + id, id, new { id = "rt_" + id });
        store.Put(Kinds.Bgm, "bgm_" + id, id, new { id = "bgm_" + id });
        store.Put(Kinds.Job, "job_" + id, id, new Job { Id = "job_" + id, ProjectId = id, PresetIds = ["P4"], Status = JobStatus.Ready });
        store.Put(Kinds.Candidate, "c_" + id, "job_" + id, new { id = "c_" + id });
        store.Put(Kinds.EditPlan, "ep_" + id, id, new EditPlan { EditplanId = "ep_" + id, ProjectId = id, Preset = Fixtures.Presets["P4"].Ref, Audio = new AudioSpec { Master = asset.Id } });
        store.Put(Kinds.Render, "r_" + id, "ep_" + id, new { id = "r_" + id });
        Touch(Path.Combine(Data, "qa", "r_" + id, "frame.png"));
        Touch(Path.Combine(Data, "cache", sha[..16], "proxy.mp4"));
        return p;
    }

    static int Count(Store s) => new[] { Kinds.Project, Kinds.Asset, Kinds.Rights, Kinds.Job, Kinds.Candidate, Kinds.EditPlan, Kinds.Render, Kinds.Bgm }
        .Sum(k => s.List<JsonElement>(k).Count);

    [Fact]
    public void Deletes_tree_and_derived_files_but_keeps_sources_and_other_projects()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var shared = new string('a', 64); var own = new string('b', 64);
        var p1 = Seed(store, "p_1111111111", own);
        var p2 = Seed(store, "p_2222222222", shared);
        // p1 also uses the same source file as p2 (same hash, different asset id per project)
        store.Put(Kinds.Asset, "a2_p1", p1.Id, new SourceAsset { Id = "a2_p1", ProjectId = p1.Id, Path = "x", Sha256 = shared, Media = Fixtures.Landscape() });
        var before = Count(store);

        var r = ProjectDeletion.Delete(store, p1, Projects, Data, ProjectDeletion.DeletePermanently, out var jobs, out var renders);

        Assert.Equal("removed", r.Folder);
        Assert.Equal(9, r.Documents);   // project + 2 assets + rights + bgm + job + candidate + editplan + render
        Assert.Equal(before - 9, Count(store));
        Assert.Equal(["job_p_1111111111"], jobs);
        Assert.Equal(["r_p_1111111111"], renders);
        Assert.False(Directory.Exists(p1.Directory));
        Assert.False(Directory.Exists(Path.Combine(Data, "qa", "r_" + p1.Id)));
        Assert.False(Directory.Exists(Path.Combine(Data, "cache", own[..16])));
        Assert.Equal(1, r.CacheDirs);
        Assert.True(Directory.Exists(Path.Combine(Data, "cache", shared[..16])), "cache still used by p2");
        Assert.True(File.Exists(Path.Combine(_root, "external", p1.Id + ".mp4")), "source registered by path must survive");

        // p2 untouched
        Assert.NotNull(store.Get<Project>(Kinds.Project, p2.Id));
        Assert.Single(store.List<JsonElement>(Kinds.Candidate, "job_" + p2.Id));
        Assert.Single(store.List<JsonElement>(Kinds.Render, "ep_" + p2.Id));
        Assert.True(File.Exists(Path.Combine(p2.Directory, "exports", "final.mp4")));
        Assert.True(Directory.Exists(Path.Combine(Data, "qa", "r_" + p2.Id)));
    }

    [Fact]
    public void Folder_outside_the_projects_root_is_kept()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var p = Seed(store, "p_3333333333", new string('c', 64), dir: Path.Combine(_root, "elsewhere", "名前_333333"));
        var r = ProjectDeletion.Delete(store, p, Projects, Data, _ => throw new InvalidOperationException("must not be called"), out _, out _);
        Assert.Equal("kept", r.Folder);
        Assert.Equal(p.Directory, r.FolderPath);
        Assert.True(Directory.Exists(p.Directory));
        Assert.Equal(0, Count(store));
    }

    [Theory]
    [InlineData("名前_111111", true)]
    [InlineData("名前_999999", false)]            // someone else's folder
    [InlineData("", false)]                       // the projects root itself
    [InlineData("sub/名前_111111", false)]        // not a direct child
    public void Owns_only_its_own_direct_child_folder(string rel, bool expected)
    {
        var p = new Project { Id = "p_1111111111", Name = "n", Directory = Path.Combine(Projects, rel) };
        Assert.Equal(expected, ProjectDeletion.OwnsFolder(p, Projects));
    }

    [Fact]
    public void When_the_folder_cannot_be_removed_nothing_is_deleted()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var p = Seed(store, "p_4444444444", new string('d', 64));
        var before = Count(store);
        Assert.Throws<FolderInUseException>(() => ProjectDeletion.Delete(store, p, Projects, Data, _ => throw new IOException("locked"), out _, out _));
        Assert.Throws<FolderInUseException>(() => ProjectDeletion.Delete(store, p, Projects, Data, _ => { }, out _, out _));   // remover "succeeded" but folder still there
        Assert.Equal(before, Count(store));
        Assert.True(Directory.Exists(Path.Combine(Data, "cache", new string('d', 16))));
    }

    [Fact]
    public void Missing_folder_is_not_an_error()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var p = Seed(store, "p_5555555555", new string('e', 64));
        Directory.Delete(p.Directory, true);
        Assert.Equal("missing", ProjectDeletion.Delete(store, p, Projects, Data, ProjectDeletion.DeletePermanently, out _, out _).Folder);
        Assert.Equal(0, Count(store));
    }

    [Fact]
    public void Queue_reports_busy_for_queued_jobs_and_exports_of_that_project_only()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var p = Seed(store, "p_6666666666", new string('f', 64));
        var q = new JobQueue(store, () => throw new NotSupportedException(), new JobEvents(), NullLogger<JobQueue>.Instance);   // not started: work stays queued
        Assert.False(q.IsBusy(p.Id));
        q.EnqueueJob(new Job { Id = "job_q", ProjectId = p.Id, PresetIds = ["P4"] });
        Assert.True(q.IsBusy(p.Id));
        Assert.False(q.IsBusy("p_other"));

        var q2 = new JobQueue(store, () => throw new NotSupportedException(), new JobEvents(), NullLogger<JobQueue>.Instance);
        q2.EnqueueExport("r_x", store.Get<EditPlan>(Kinds.EditPlan, "ep_" + p.Id)!, 1);
        Assert.True(q2.IsBusy(p.Id));
        q2.Forget([], ["r_x"]);
        Assert.False(q2.IsBusy(p.Id));
    }

    [Fact]
    public async Task Delete_endpoint_end_to_end()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("VideoCreator:DataDir", Data);
            b.UseSetting("VideoCreator:ProjectsDir", Projects);
            b.UseSetting("VideoCreator:OpenBrowser", "false");
            b.ConfigureServices(s => s.AddSingleton<RemoveFolder>(ProjectDeletion.DeletePermanently));
        });
        using var http = app.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1") });
        http.DefaultRequestHeaders.Add("X-VideoCreator", "1");

        var created = await (await http.PostAsJsonAsync("/api/projects", new { name = "消す" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = created.GetProperty("id").GetString()!;
        var dir = created.GetProperty("directory").GetString()!;
        Assert.True(Directory.Exists(dir));

        using (var noHeader = new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{id}"))
        {
            using var raw = app.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1") });
            Assert.Equal(HttpStatusCode.Forbidden, (await raw.SendAsync(noHeader, ct)).StatusCode);   // CSRF guard covers DELETE
        }

        var res = await http.DeleteAsync($"/api/projects/{id}", ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("removed", body.GetProperty("folder").GetString());
        Assert.Equal("消す", body.GetProperty("name").GetString());
        Assert.False(Directory.Exists(dir));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/projects/{id}", ct)).StatusCode);
        Assert.Equal(0, (await http.GetFromJsonAsync<JsonElement>("/api/projects", ct)).GetArrayLength());

        var again = await http.DeleteAsync($"/api/projects/{id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("E-NOT-FOUND", (await again.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error").GetProperty("code").GetString());
    }
}
