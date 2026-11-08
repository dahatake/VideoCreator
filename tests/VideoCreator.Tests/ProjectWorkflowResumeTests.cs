using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class ProjectWorkflowResumeTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-workflow-resume-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    string _id = "";

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _app = CreateApp();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        using var response = await _http.PostAsJsonAsync("/api/projects", new { name = "再開テスト" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        _id = project.GetProperty("id").GetString()!;
    }

    WebApplicationFactory<Program> CreateApp() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
        b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
        b.UseSetting("VideoCreator:OpenBrowser", "false");
        b.UseSetting("VideoCreator:Port", "0");
        b.UseSetting("VideoCreator:Asr:Provider", "none");
        b.UseSetting("VideoCreator:Llm:Provider", "none");
    });

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        var store = _app.Services.GetRequiredService<Runtime>().Store;
        await _app.DisposeAsync();
        store.Dispose();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Reopening_a_project_resumes_completed_generation_at_candidates_after_app_restart()
    {
        const string jobId = "job_resume";
        const string candidateId = "cand_resume";
        var store = _app.Services.GetRequiredService<Runtime>().Store;
        store.Put(Kinds.Job, jobId, _id, new Job
        {
            Id = jobId,
            ProjectId = _id,
            PresetIds = ["P4"],
            Status = JobStatus.Ready,
            CandidateIds = [candidateId],
        });
        store.Put(Kinds.Candidate, candidateId, jobId, new Candidate
        {
            Id = candidateId,
            Rank = 1,
            Headline = "保存された候補",
            Segments = [new Interval(1, 5)],
        });

        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        _app = CreateApp();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");

        using var projectResponse = await _http.GetAsync($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        var project = await projectResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(jobId, project.GetProperty("resume_job_id").GetString());

        using var candidatesResponse = await _http.GetAsync($"/api/jobs/{jobId}/candidates", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, candidatesResponse.StatusCode);
        var candidates = await candidatesResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(candidateId, candidates[0].GetProperty("candidate").GetProperty("id").GetString());
        Assert.Equal("保存された候補", candidates[0].GetProperty("candidate").GetProperty("headline").GetString());

        var restartedStore = _app.Services.GetRequiredService<Runtime>().Store;
        restartedStore.Put(Kinds.Job, jobId, _id, new Job
        {
            Id = jobId,
            ProjectId = _id,
            PresetIds = ["P4"],
            Status = JobStatus.Failed,
            CandidateIds = [candidateId],
        });
        var failedProject = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.False(failedProject.TryGetProperty("resume_job_id", out _));

        restartedStore.Put(Kinds.Job, jobId, _id, new Job
        {
            Id = jobId,
            ProjectId = _id,
            PresetIds = ["P4"],
            Status = JobStatus.Transcribing,
            CandidateIds = [candidateId],
        });
        var activeProject = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal(jobId, activeProject.GetProperty("resume_job_id").GetString());
    }
}
