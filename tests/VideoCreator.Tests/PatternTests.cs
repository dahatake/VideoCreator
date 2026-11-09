using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>A project keeps several patterns (jobs) side by side; one can be deleted without touching the others.</summary>
public sealed class PatternTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-pat-" + Guid.NewGuid().ToString("N")[..8]);
    string Data => Path.Combine(_root, "data");
    string Projects => Path.Combine(_root, "projects");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    static void Touch(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "x"); }

    /// <summary>One job → candidate → plan → (preview, final) with files on disk.</summary>
    static (Job Job, string Plan, string Preview, string Final) SeedPattern(Store store, Project p, string tag, string dataDir, string preset = "P4")
    {
        var job = new Job { Id = "job_" + tag, ProjectId = p.Id, PresetIds = [preset], Status = JobStatus.Ready, CandidateIds = ["c_" + tag] };
        store.Put(Kinds.Job, job.Id, p.Id, job);
        var plan = new EditPlan
        {
            EditplanId = "ep_" + tag, ProjectId = p.Id, Preset = Fixtures.Presets[preset].Ref, Audio = new AudioSpec { Master = "a_x" },
            Provenance = new Provenance { JobId = job.Id, CandidateId = "c_" + tag },
        };
        store.Put(Kinds.EditPlan, plan.EditplanId, p.Id, plan);
        store.Put(Kinds.Candidate, "c_" + tag, job.Id, new Candidate { Id = "c_" + tag, Segments = [new Interval(0, 30)], EditPlanId = plan.EditplanId, Approval = new CandidateApproval(DateTimeOffset.Now, "me", "h") });
        var preview = Path.Combine(p.Directory, "previews", $"r_prev_{tag}.mp4");
        var final = Path.Combine(p.Directory, "exports", "r_fin_" + tag, "out.mp4");
        Touch(preview); Touch(final); Touch(Path.Combine(p.Directory, "exports", "r_fin_" + tag, "out.srt"));
        store.Put(Kinds.Render, "r_prev_" + tag, plan.EditplanId, new Render { Id = "r_prev_" + tag, EditplanId = plan.EditplanId, Kind = "preview", Path = preview });
        store.Put(Kinds.Render, "r_fin_" + tag, plan.EditplanId, new Render { Id = "r_fin_" + tag, EditplanId = plan.EditplanId, Kind = "final", Path = final, Outputs = new() { ["srt"] = Path.ChangeExtension(final, ".srt") } });
        Touch(Path.Combine(dataDir, "qa", "r_fin_" + tag, "f.png"));
        return (job, plan.EditplanId, preview, final);
    }

    [Fact]
    public async Task Deleting_one_pattern_leaves_the_others_and_the_sources()
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
        var store = app.Services.GetRequiredService<Runtime>().Store;

        var created = await (await http.PostAsJsonAsync("/api/projects", new { name = "複数パターン" }, ct)).Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = created.GetProperty("id").GetString()!;
        var p = store.Get<Project>(Kinds.Project, id)!;
        var asset = new SourceAsset { Id = "a_x", ProjectId = id, Path = Path.Combine(_root, "src.mp4"), Sha256 = new string('9', 64), Media = Fixtures.Landscape() };
        Touch(asset.Path); store.Put(Kinds.Asset, asset.Id, id, asset);
        var a = SeedPattern(store, p, "aaa", Data);
        var b = SeedPattern(store, p, "bbb", Data, "P1");
        store.Put(Kinds.Project, id, null, p with { WorkflowState = new ProjectWorkflowState { Step = "review", EditPlanId = a.Plan } });

        var list = await http.GetFromJsonAsync<JsonElement>($"/api/projects/{id}/patterns", ct);
        Assert.Equal(2, list.GetArrayLength());
        var first = list.EnumerateArray().First(x => x.GetProperty("job").GetProperty("id").GetString() == a.Job.Id);
        Assert.Equal(1, first.GetProperty("candidates").GetInt32());
        Assert.Equal(1, first.GetProperty("approved").GetInt32());
        Assert.False(first.GetProperty("busy").GetBoolean());

        var res = await http.DeleteAsync($"/api/jobs/{a.Job.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, body.GetProperty("renders").GetInt32());

        // pattern A: documents and files are gone
        Assert.Null(store.Get<Job>(Kinds.Job, a.Job.Id));
        Assert.Null(store.Get<EditPlan>(Kinds.EditPlan, a.Plan));
        Assert.Empty(store.List<Candidate>(Kinds.Candidate, a.Job.Id));
        Assert.Empty(store.List<Render>(Kinds.Render, a.Plan));
        Assert.False(File.Exists(a.Preview));
        Assert.False(Directory.Exists(Path.GetDirectoryName(a.Final)));
        Assert.False(Directory.Exists(Path.Combine(Data, "qa", "r_fin_aaa")));

        // pattern B, the source and the project survive
        Assert.NotNull(store.Get<Job>(Kinds.Job, b.Job.Id));
        Assert.NotNull(store.Get<EditPlan>(Kinds.EditPlan, b.Plan));
        Assert.Equal(2, store.List<Render>(Kinds.Render, b.Plan).Count);
        Assert.True(File.Exists(b.Preview)); Assert.True(File.Exists(b.Final));
        Assert.True(Directory.Exists(Path.Combine(Data, "qa", "r_fin_bbb")));
        Assert.True(File.Exists(asset.Path));
        Assert.NotNull(store.Get<SourceAsset>(Kinds.Asset, "a_x"));

        // the saved workflow position pointed into the deleted pattern → back to the settings screen
        var state = (await http.GetFromJsonAsync<JsonElement>($"/api/projects/{id}", ct)).GetProperty("resume_state");
        Assert.Equal("settings", state.GetProperty("step").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync($"/api/jobs/{a.Job.Id}", ct)).StatusCode);
        Assert.Single((await http.GetFromJsonAsync<JsonElement>($"/api/projects/{id}/patterns", ct)).EnumerateArray());
    }

    [Fact]
    public void A_running_pattern_cannot_be_deleted()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var p = new Project { Id = "p_7777777777", Name = "n", Directory = Path.Combine(Projects, "n_777777") };
        store.Put(Kinds.Project, p.Id, null, p);
        var a = SeedPattern(store, p, "ccc", Data);
        var q = new VideoCreator.App.JobQueue(store, () => throw new NotSupportedException(), new JobEvents(), Microsoft.Extensions.Logging.Abstractions.NullLogger<VideoCreator.App.JobQueue>.Instance);
        Assert.False(q.IsJobBusy(a.Job.Id, [a.Plan]));
        q.EnqueueExport("r_new", store.Get<EditPlan>(Kinds.EditPlan, a.Plan)!, 1);
        Assert.True(q.IsJobBusy(a.Job.Id, [a.Plan]));
        Assert.False(q.IsJobBusy("job_other", ["ep_other"]));
        q.Previews[a.Plan] = new PreviewProgress(true, 0, null);
        q.Forget([], ["r_new"]);
        Assert.True(q.IsJobBusy(a.Job.Id, [a.Plan]), "a preview re-render also holds the files");
    }

    [Fact]
    public void A_locked_file_leaves_the_pattern_intact()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var p = new Project { Id = "p_8888888888", Name = "n", Directory = Path.Combine(Projects, "n_888888") };
        store.Put(Kinds.Project, p.Id, null, p);
        var a = SeedPattern(store, p, "ddd", Data);
        Assert.Throws<FolderInUseException>(() => JobDeletion.Delete(store, p, a.Job, Data, _ => throw new IOException("locked"), out _));
        Assert.NotNull(store.Get<Job>(Kinds.Job, a.Job.Id));
        Assert.Equal(2, store.List<Render>(Kinds.Render, a.Plan).Count);
    }
}
