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
        Assert.Equal("candidates", project.GetProperty("resume_state").GetProperty("step").GetString());
        Assert.Equal(jobId, project.GetProperty("resume_state").GetProperty("job_id").GetString());

        using var candidatesResponse = await _http.GetAsync($"/api/jobs/{jobId}/candidates", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, candidatesResponse.StatusCode);
        var candidates = await candidatesResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(candidateId, candidates[0].GetProperty("candidate").GetProperty("id").GetString());
        Assert.Equal("保存された候補", candidates[0].GetProperty("candidate").GetProperty("headline").GetString());

        var restartedStore = _app.Services.GetRequiredService<Runtime>().Store;
        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "project" }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var savedProject = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("project", savedProject.GetProperty("resume_state").GetProperty("step").GetString());

        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "settings" }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var savedSettings = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("settings", savedSettings.GetProperty("resume_state").GetProperty("step").GetString());

        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "job", job_id = jobId }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var savedJob = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("job", savedJob.GetProperty("resume_state").GetProperty("step").GetString());
        Assert.Equal(jobId, savedJob.GetProperty("resume_state").GetProperty("job_id").GetString());

        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "candidates", job_id = jobId }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var savedCandidates = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("candidates", savedCandidates.GetProperty("resume_state").GetProperty("step").GetString());

        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "exports", job_id = jobId }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var savedExports = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("exports", savedExports.GetProperty("resume_state").GetProperty("step").GetString());
        Assert.Equal(jobId, savedExports.GetProperty("resume_state").GetProperty("job_id").GetString());
        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "exports", job_id = jobId, edit_plan_id = "ep_x" }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, stateResponse.StatusCode);

        var planId = "ep_resume";
        var plan = new EditPlan
        {
            EditplanId = planId,
            ProjectId = _id,
            Preset = Fixtures.Presets["P4"].Ref,
            Audio = new AudioSpec { Master = "asset_resume" },
        };
        restartedStore.Put(Kinds.EditPlan, planId, _id, plan);
        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new { step = "review", edit_plan_id = planId }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var savedReview = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("review", savedReview.GetProperty("resume_state").GetProperty("step").GetString());

        var planHash = EditPlanFingerprint.Of(plan);
        var renderPath = Path.Combine(_root, "render.mp4");
        await File.WriteAllTextAsync(renderPath, "render", TestContext.Current.CancellationToken);
        restartedStore.Put(Kinds.Render, "render_resume", planId, new Render
        {
            Id = "render_resume",
            EditplanId = planId,
            Kind = "final",
            Path = renderPath,
            PlanHash = planHash,
            Qa = [new QaResult("H-11", QaStatus.Pass, "pass", "pass", "valid") { PlanHash = planHash }],
        });
        using (var stateResponse = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new
        {
            step = "render",
            edit_plan_id = planId,
            render_id = "render_resume",
        }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);

        _http.Dispose();
        await _app.DisposeAsync();
        restartedStore.Dispose();
        _app = CreateApp();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        var reopenedAtRender = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("render", reopenedAtRender.GetProperty("resume_state").GetProperty("step").GetString());
        Assert.Equal("render_resume", reopenedAtRender.GetProperty("resume_state").GetProperty("render_id").GetString());

        restartedStore = _app.Services.GetRequiredService<Runtime>().Store;
        restartedStore.Delete([(Kinds.Render, "render_resume")]);
        var missingRender = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("review", missingRender.GetProperty("resume_state").GetProperty("step").GetString());

        restartedStore.Put(Kinds.Render, "render_resume", planId, new Render
        {
            Id = "render_resume",
            EditplanId = planId,
            Kind = "final",
            Path = renderPath,
            PlanHash = "outdated",
        });
        var staleRender = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("review", staleRender.GetProperty("resume_state").GetProperty("step").GetString());

        restartedStore.Put(Kinds.Job, jobId, _id, new Job
        {
            Id = jobId,
            ProjectId = _id,
            PresetIds = ["P4"],
            Status = JobStatus.Failed,
            CandidateIds = [candidateId],
        });
        var failedProject = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("review", failedProject.GetProperty("resume_state").GetProperty("step").GetString());

        restartedStore.Put(Kinds.Job, jobId, _id, new Job
        {
            Id = jobId,
            ProjectId = _id,
            PresetIds = ["P4"],
            Status = JobStatus.Transcribing,
            CandidateIds = [candidateId],
        });
        var activeProject = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", TestContext.Current.CancellationToken);
        Assert.Equal("review", activeProject.GetProperty("resume_state").GetProperty("step").GetString());

        using var invalidState = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new
        {
            step = "review",
            edit_plan_id = "ep_from_another_project",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidState.StatusCode);

        using var invalidJobState = await _http.PutAsJsonAsync($"/api/projects/{_id}/workflow-state", new
        {
            step = "candidates",
            job_id = "job_from_another_project",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidJobState.StatusCode);
    }
}
