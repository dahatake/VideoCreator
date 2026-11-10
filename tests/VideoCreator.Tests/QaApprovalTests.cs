using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Qa;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class QaApprovalTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-qa-approval-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    const string PlanId = "ep-qa";
    EditPlan Plan => Rt.Store.Get<EditPlan>(Kinds.EditPlan, PlanId)!;

    public async ValueTask InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
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
        using var r = await _http.PostAsJsonAsync("/api/projects", new { name = "QA approval" }, Ct);
        r.EnsureSuccessStatusCode();
        var project = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        Rt.Store.Put(Kinds.Asset, "a", project, new SourceAsset
        {
            Id = "a", ProjectId = project, Path = Path.Combine(_root, "source.mp4"),
            Sha256 = new string('8', 64), Media = Fixtures.Landscape(30),
        });
        var plan = new EditPlan
        {
            EditplanId = PlanId, ProjectId = project, Preset = Fixtures.Presets["P4"].Ref,
            Audio = new AudioSpec { Master = "a" },
            Timeline = [new Clip { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 30 }],
            Telops = [new Telop { Id = "t", Type = TelopType.Caption, Text = "これはとても長い字幕なので一瞬では読めません", Start = 1, End = 1.1, Approved = false }],
            Provenance = new Provenance { CandidateId = "candidate", JobId = "job" },
        };
        Rt.Store.Put(Kinds.EditPlan, PlanId, project, plan);
        Rt.Store.Put(Kinds.Candidate, "candidate", "job", new Candidate
        {
            Id = "candidate", Rank = 1, EditPlanId = PlanId, Segments = [new Interval(0, 30)],
            Approval = new CandidateApproval(DateTimeOffset.Now, "test", EditPlanFingerprint.Of(plan)),
        });
        Rt.Store.Put(Kinds.Job, "job", project, new Job { Id = "job", ProjectId = project, Status = JobStatus.Ready, PresetIds = ["P4"] });
    }

    public async ValueTask DisposeAsync()
    {
        var store = Rt.Store;
        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    async Task<JsonElement> View() => await _http.GetFromJsonAsync<JsonElement>($"/api/editplans/{PlanId}", Ct);
    static JsonElement Check(JsonElement view, string id) => view.GetProperty("qa").EnumerateArray().Single(q => q.GetProperty("check_id").GetString() == id);
    static Api.QaApprovalTarget Target(JsonElement q) => new(q.GetProperty("check_id").GetString()!, q.GetProperty("review_hash").GetString()!);
    static bool Approved(JsonElement q) => q.TryGetProperty("review_approval", out _);
    async Task<HttpResponseMessage> Approve(JsonElement view, bool approved, params Api.QaApprovalTarget[] targets) =>
        await _http.PutAsJsonAsync($"/api/editplans/{PlanId}/qa/approval",
            new Api.QaApprovalRequest(view.GetProperty("plan_hash").GetString(), targets.ToList(), approved), Json.Options, Ct);

    [Fact]
    public async Task Individual_and_bulk_approvals_persist_without_waiving_checks_or_invalidating_content()
    {
        var before = Plan;
        // Existing fingerprints must be unchanged for plans saved before review metadata existed.
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Serialize(before)))), EditPlanFingerprint.Of(before));
        var view = await View();
        Assert.Equal("fail", Check(view, "H-08").GetProperty("status").GetString());
        Assert.Equal("warn", Check(view, "S-01").GetProperty("status").GetString());
        using (var r = await Approve(view, true, Target(Check(view, "S-01")))) r.EnsureSuccessStatusCode();
        view = await View();
        Assert.True(Approved(Check(view, "S-01")));
        Assert.False(Approved(Check(view, "H-08")));
        var firstAt = Check(view, "S-01").GetProperty("review_approval").GetProperty("at").GetString();
        var targets = view.GetProperty("qa").EnumerateArray().Where(q => q.GetProperty("status").GetString() != "not_run").Select(Target).ToArray();
        using (var r = await Approve(view, true, targets)) r.EnsureSuccessStatusCode();
        view = await View();
        Assert.All(view.GetProperty("qa").EnumerateArray().Where(q => q.GetProperty("status").GetString() != "not_run"), q => Assert.True(Approved(q)));
        Assert.Equal(firstAt, Check(view, "S-01").GetProperty("review_approval").GetProperty("at").GetString());
        Assert.Equal("fail", Check(view, "H-08").GetProperty("status").GetString());
        Assert.Equal("warn", Check(view, "S-01").GetProperty("status").GetString());
        Assert.False(Plan.Telops[0].Approved);
        Assert.Equal(EditPlanFingerprint.Of(before), EditPlanFingerprint.Of(Plan));
        Assert.False(view.GetProperty("approval").GetProperty("stale").GetBoolean());
        Assert.Equal(Json.Serialize(QaEngine.PlanChecks(Rt.CreateRunner().QaContextFor(before))),
            Json.Serialize(QaEngine.PlanChecks(Rt.CreateRunner().QaContextFor(Plan))));
        using (var r = await _http.PostAsync($"/api/editplans/{PlanId}/render", null, Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        using (var r = await Approve(view, false, Target(Check(view, "S-01")))) r.EnsureSuccessStatusCode();
        view = await View();
        Assert.False(Approved(Check(view, "S-01")));
        Assert.True(Approved(Check(view, "H-08")));
        Assert.DoesNotContain(Plan.QaApprovals!, a => a.CheckId == "S-01");
    }

    [Fact]
    public async Task Stale_plan_and_external_result_changes_require_review_again_and_fail_atomically()
    {
        var view = await View();
        using (var r = await Approve(view, true, Target(Check(view, "H-09")), Target(Check(view, "S-01")))) r.EnsureSuccessStatusCode();
        // Rights are outside the edit plan: the plan hash stays the same, but H-09 changes.
        Rt.Store.Put(Kinds.Rights, "rights", Plan.ProjectId, new RightsConfirmation { Id = "rights", ProjectId = Plan.ProjectId, AssetId = "a" });
        var changed = await View();
        Assert.Equal(view.GetProperty("plan_hash").GetString(), changed.GetProperty("plan_hash").GetString());
        Assert.False(Approved(Check(changed, "H-09")));
        Assert.True(Approved(Check(changed, "S-01")));
        var saved = Json.Serialize(Plan);
        using (var r = await Approve(view, true, Target(Check(view, "H-08")), Target(Check(view, "H-09"))))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(saved, Json.Serialize(Plan));

        using (var r = await _http.PatchAsJsonAsync($"/api/editplans/{PlanId}", new { ops = new[] { new { op = "set_sfx", enabled = false } } }, Ct))
            r.EnsureSuccessStatusCode();
        changed = await View();
        Assert.All(changed.GetProperty("qa").EnumerateArray(), q => Assert.False(Approved(q)));
        using (var r = await Approve(view, true, Target(Check(view, "S-01"))))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await Approve(changed, true, Target(Check(changed, "S-01")))) r.EnsureSuccessStatusCode();
        Assert.True(Approved(Check(await View(), "S-01")));
    }

    [Fact]
    public async Task Invalid_batches_never_partially_save()
    {
        var view = await View();
        var valid = Target(Check(view, "S-01"));
        foreach (var targets in new[] { Array.Empty<Api.QaApprovalTarget>(), new[] { valid, valid }, new[] { valid, new Api.QaApprovalTarget("missing", "hash") } })
        {
            using var r = await Approve(view, true, targets);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Null(Plan.QaApprovals);
        }
        using var missing = await _http.PutAsJsonAsync("/api/editplans/missing/qa/approval",
            new Api.QaApprovalRequest("hash", [valid], true), Json.Options, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Current_preview_measurements_can_be_approved_but_unrun_and_stale_preview_checks_cannot()
    {
        var plan = Plan;
        var hash = EditPlanFingerprint.Of(plan);
        Render Preview(string id, string measured) => new()
        {
            Id = id, EditplanId = PlanId, Kind = "preview", Path = Path.Combine(_root, "preview.mp4"), PlanHash = hash,
            Qa = [
                new QaResult("H-11", QaStatus.Pass, "0", "0", "grounding") { PlanHash = hash },
                new QaResult("H-05", QaStatus.Pass, measured, "safe", "measured") { Stage = "plan", PlanHash = hash },
                new QaResult("H-02", QaStatus.NotRun, "pending", "1080x1920", "resolution") { Stage = "final", PlanHash = hash },
            ],
        };
        Rt.Store.Put(Kinds.Render, "preview-1", PlanId, Preview("preview-1", "inside"));
        var view = await View();
        using (var r = await Approve(view, true, Target(Check(view, "H-05")))) r.EnsureSuccessStatusCode();
        view = await View();
        Assert.True(Approved(Check(view, "H-05")));
        var saved = Json.Serialize(Plan);
        using (var r = await Approve(view, true, Target(Check(view, "S-01")), Target(Check(view, "H-02"))))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(saved, Json.Serialize(Plan));
        Rt.Store.Put(Kinds.Render, "preview-2", PlanId, Preview("preview-2", "inside (remeasured)"));
        Assert.False(Approved(Check(await View(), "H-05")));
        using (var r = await Approve(view, true, Target(Check(view, "H-05"))))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await _http.PatchAsJsonAsync($"/api/editplans/{PlanId}", new { ops = new[] { new { op = "set_sfx", enabled = false } } }, Ct))
            r.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await View()).GetProperty("qa").EnumerateArray(), q => q.GetProperty("check_id").GetString() == "H-05");
    }

    [Fact]
    public void Result_identity_ignores_repair_advice_but_includes_every_finding_and_excludes_unrun_checks()
    {
        var plan = Plan;
        var result = new QaResult("S-01", QaStatus.Warn, "summary", "limit", "speed")
        {
            Items = [new QaItem("t1", "telop", "first", 0, 1), new QaItem("t2", "telop", "second", 1, 2)],
        };
        plan = plan with { QaApprovals = [new QaCheckApproval(result.CheckId, EditPlanFingerprint.Of(plan), QaApproval.ResultHash(result), DateTimeOffset.Now, "test")] };
        Assert.NotNull(QaApproval.Attach(plan, [result])[0].ReviewApproval);
        var advised = result with { Advice = new QaAdvice("different advice", "ai"), Items = result.Items.Select(i => i with { Repair = "ai", Note = "hint" }).ToList() };
        Assert.NotNull(QaApproval.Attach(plan, [advised])[0].ReviewApproval);
        var changed = result with { Items = [result.Items[0], result.Items[1] with { Detail = "changed" }] };
        Assert.Null(QaApproval.Attach(plan, [changed])[0].ReviewApproval);
        var unrun = result with { Status = QaStatus.NotRun };
        plan = plan with { QaApprovals = [new QaCheckApproval(unrun.CheckId, EditPlanFingerprint.Of(plan), QaApproval.ResultHash(unrun), DateTimeOffset.Now, "test")] };
        Assert.Null(QaApproval.Attach(plan, [unrun])[0].ReviewApproval);
    }
}
