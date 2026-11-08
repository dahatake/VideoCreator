using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>Candidates are approved one by one (bound to the plan as it was approved) and one or more approved ones are exported together.</summary>
public sealed class ApprovalExportTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-appr-" + Guid.NewGuid().ToString("N")[..8]);
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    JobQueue Q => _app.Services.GetRequiredService<JobQueue>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    string _project = "";
    const string JobId = "job_appr";

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
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
        using var r = await _http.PostAsJsonAsync("/api/projects", new { name = "appr" }, Ct);
        r.EnsureSuccessStatusCode();
        _project = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

        var src = Path.Combine(_root, "land.mp4");
        await Rt.Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=130", "-f", "lavfi", "-i", "sine=f=330:d=130,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", src], Ct);
        var media = new MediaInfo { Duration = 130, Width = 1280, Height = 720, Fps = 30, AudioChannels = 1 };
        var asset = new SourceAsset { Id = "a1", ProjectId = _project, Path = src, Sha256 = new string('7', 64), Media = media };
        Rt.Store.Put(Kinds.Asset, asset.Id, _project, asset);
        ConfirmRights(true);
        var p = Fixtures.Presets["P4"]; var tr = Fixtures.Talk;
        var res = Solver.Plan([new Interval(74, 103)], tr, Fixtures.Signals(tr), p, asset.Media, asset.Id, ["300円の商品"]);
        var ids = new List<string>();
        for (int rank = 1; rank <= 3; rank++)
        {
            var cid = $"c{rank}"; ids.Add(cid);
            var plan = new EditPlan
            {
                EditplanId = $"ep{rank}", ProjectId = _project, Preset = p.Ref, Timeline = res.Clips, Audio = new AudioSpec { Master = asset.Id },
                Provenance = new Provenance { JobId = JobId, CandidateId = cid },
            };
            Rt.Store.Put(Kinds.EditPlan, plan.EditplanId, _project, plan);
            Rt.Store.Put(Kinds.Candidate, cid, JobId, new Candidate { Id = cid, Rank = rank, Segments = [new Interval(74, 103)], EditPlanId = plan.EditplanId });
        }
        Rt.Store.Put(Kinds.Job, JobId, _project, new Job { Id = JobId, ProjectId = _project, PresetIds = ["P4"], Status = JobStatus.Ready, CandidateIds = ids });
    }

    public async ValueTask DisposeAsync()
    {
        var store = Rt.Store;
        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    void ConfirmRights(bool on)
    {
        if (on) Rt.Store.Put(Kinds.Rights, "rt1", _project, new RightsConfirmation { Id = "rt1", ProjectId = _project, AssetId = "a1" });
        else Rt.Store.Delete([(Kinds.Rights, "rt1")]);
    }

    async Task<HttpResponseMessage> ApproveAsync(string cid, bool on) =>
        await _http.PutAsJsonAsync($"/api/jobs/{JobId}/candidates/{cid}/approval", new { approved = on }, Ct);

    async Task<HttpResponseMessage> ExportAsync(params string[] ids) =>
        await _http.PostAsJsonAsync($"/api/jobs/{JobId}/exports", new { candidate_ids = ids }, Ct);

    async Task<Dictionary<string, JsonElement>> ListAsync() =>
        (await _http.GetFromJsonAsync<JsonElement>($"/api/jobs/{JobId}/candidates", Ct)).EnumerateArray()
            .ToDictionary(x => x.GetProperty("candidate").GetProperty("id").GetString()!);

    static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    [Fact]
    public async Task Approval_is_bound_to_the_plan_and_approved_candidates_export_together()
    {
        var list = await ListAsync();
        Assert.All(list.Values, x => Assert.False(x.TryGetProperty("approval", out _)));

        // nothing is approved yet → refused, and nothing is queued
        using (var r = await ExportAsync("c1"))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("E-NOT-APPROVED", Code(await r.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        Assert.Empty(Q.Exports);

        foreach (var c in new[] { "c1", "c2" })
        {
            using var r = await ApproveAsync(c, true);
            var a = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("approval");
            Assert.False(a.GetProperty("stale").GetBoolean());
        }
        // withdraw works
        using (var r = await ApproveAsync("c3", true)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await ApproveAsync("c3", false)) Assert.False((await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).TryGetProperty("approval", out _));
        Assert.Null(Rt.Store.Get<Candidate>(Kinds.Candidate, "c3")!.Approval);

        // editing after approval makes it stale; a batch containing it starts nothing at all
        using (var r = await _http.PatchAsJsonAsync("/api/editplans/ep2", new { ops = new[] { new { op = "set_sfx", enabled = false } } }, Ct)) r.EnsureSuccessStatusCode();
        list = await ListAsync();
        Assert.True(list["c2"].GetProperty("approval").GetProperty("stale").GetBoolean());
        Assert.False(list["c1"].GetProperty("approval").GetProperty("stale").GetBoolean());
        using (var r = await ExportAsync("c1", "c2"))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var detail = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetProperty("detail");
            Assert.Equal("c2", Assert.Single(detail.EnumerateArray()).GetProperty("candidate_id").GetString());
        }
        Assert.Empty(Q.Exports);

        // a plan that fails a hard QA check (rights withdrawn) cannot be approved
        ConfirmRights(false);
        using (var r = await ApproveAsync("c3", true))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("E-QA-HARDFAIL", Code(await r.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        ConfirmRights(true);

        using (var r = await ApproveAsync("c2", true)) r.EnsureSuccessStatusCode();
        using (var r = await ApproveAsync("c3", true)) r.EnsureSuccessStatusCode();
        using (var r = await _http.PostAsJsonAsync($"/api/jobs/{JobId}/exports", new { candidate_ids = Array.Empty<string>() }, Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        using (var r = await ExportAsync("c1", "nope")) Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);

        // three approved, given out of order → exported in rank order
        List<(string Cid, string Rid)> started;
        using (var r = await ExportAsync("c3", "c2", "c1"))
        {
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            started = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("exports").EnumerateArray()
                .Select(x => (x.GetProperty("candidate_id").GetString()!, x.GetProperty("render_id").GetString()!)).ToList();
        }
        Assert.Equal(["c1", "c2", "c3"], started.Select(x => x.Cid));
        // the third is still waiting behind two full renders: withdraw it
        using (var r = await _http.PostAsync($"/api/renders/{started[2].Rid}/cancel", null, Ct)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        // a second request for something already exporting is refused, and so is the single-export route
        using (var r = await ExportAsync("c1")) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await _http.PostAsync("/api/editplans/ep1/render", null, Ct)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        // queued plans cannot change under an approved export (AT-15).
        using (var r = await _http.PatchAsJsonAsync("/api/editplans/ep2", new { ops = new[] { new { op = "set_sfx", enabled = true } } }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("E-PLAN-STALE", Code(await r.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }

        var until = DateTime.UtcNow.AddMinutes(6);
        while (started.Take(2).Any(s => Q.Exports[s.Rid].State is "queued" or "rendering") && DateTime.UtcNow < until) await Task.Delay(200, Ct);
        foreach (var (cid, rid) in started.Take(2))
        {
            var st = Q.Exports[rid];
            Assert.True(st.State == "done", $"{cid}: {st.State} {st.ErrorCode} {st.Message}");
        }
        Assert.Equal("cancelled", Q.Exports[started[2].Rid].State);
        using (var r = await _http.PostAsync($"/api/renders/{started[0].Rid}/cancel", null, Ct)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

        var renders = started.Take(2).Select(s => Rt.Store.Get<Render>(Kinds.Render, s.Rid)!).ToList();
        Assert.All(renders, r => { Assert.Equal("final", r.Kind); Assert.True(File.Exists(r.Path), r.Path); Assert.True(r.Outputs.ContainsKey("metadata")); });
        Assert.NotEqual(renders[0].Path, renders[1].Path);
        Assert.Equal(["ep1", "ep2"], renders.Select(r => r.EditplanId));
        var exported = Json.Deserialize<EditPlan>(await File.ReadAllTextAsync(renders[1].Outputs["editplan"], Ct));
        Assert.False(exported.Audio.SfxEnabled);
        Assert.False(Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep2")!.Audio.SfxEnabled);
        var probe = await new MediaProbe(Rt.Ff).ProbeAsync(renders[0].Path, Ct);
        Assert.Equal((1080, 1920), (probe.Width, probe.Height));

        list = await ListAsync();
        Assert.Equal(started[0].Rid, list["c1"].GetProperty("final_render_id").GetString());
        Assert.Equal("done", list["c1"].GetProperty("export").GetProperty("state").GetString());
        Assert.Equal("cancelled", list["c3"].GetProperty("export").GetProperty("state").GetString());
        Assert.False(list["c3"].TryGetProperty("final_render_id", out _));
        Assert.False(list["c2"].GetProperty("approval").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task AT15_money_edit_invalidates_approval_final_QA_and_every_download_route()
    {
        var cache = Path.Combine(Rt.Options.ResolvedDataDir, "cache", new string('7', 16));
        Directory.CreateDirectory(cache);
        var tr = new Transcript
        {
            Provider = "fixed-at15", Duration = 130,
            Words = [new Word("amount", 75, 79, "A社は100万円、B社は500万円", 1)],
            Sentences = [new Sentence(0, 75, 79, "A社は100万円、B社は500万円", 1, 0, 0)],
        };
        await File.WriteAllTextAsync(Path.Combine(cache, "transcript_fixed.json"), Json.Serialize(tr), Ct);
        var original = Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep1")!;
        original = original with
        {
            Timeline = [new Clip { ClipId = "amount-clip", AssetId = "a1", SrcIn = 74, SrcOut = 103 }],
            Telops = [new Telop { Id = "amount", Type = TelopType.Emphasis, Start = 1, End = 3, Text = "A社100万円", Style = "Emph", SourceWordIds = ["amount"] }],
        };
        Rt.Store.Put(Kinds.EditPlan, "ep1", _project, original);
        using (var unapproved = await _http.PostAsync("/api/editplans/ep1/render", null, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, unapproved.StatusCode);
            Assert.Equal("E-NOT-APPROVED", Code(await unapproved.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        using (var approved = await ApproveAsync("c1", true)) approved.EnsureSuccessStatusCode();
        string rid;
        using (var export = await ExportAsync("c1"))
        {
            Assert.Equal(HttpStatusCode.Accepted, export.StatusCode);
            rid = (await export.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("exports")[0].GetProperty("render_id").GetString()!;
        }
        var until = DateTime.UtcNow.AddMinutes(3);
        while (Q.Exports[rid].State == "queued" && DateTime.UtcNow < until) await Task.Delay(20, Ct);
        Assert.Equal("rendering", Q.Exports[rid].State);
        using (var patch = await _http.PatchAsJsonAsync("/api/editplans/ep1", new { ops = new[] { new { op = "replace_text", target = "amount", text = "A社500万円" } } }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
            Assert.Equal("E-PLAN-STALE", Code(await patch.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        Assert.Equal(EditPlanFingerprint.Of(original), EditPlanFingerprint.Of(Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep1")!));
        using (var transcriptPatch = await _http.PatchAsJsonAsync("/api/assets/a1/transcript",
            new { edits = new[] { new { word_id = "w1", text = "訂正" } } }, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, transcriptPatch.StatusCode);
            Assert.Equal("E-PLAN-STALE", Code(await transcriptPatch.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        while (Q.Exports[rid].State is "queued" or "rendering" && DateTime.UtcNow < until) await Task.Delay(200, Ct);
        Assert.True(Q.Exports[rid].State == "done", Q.Exports[rid].Message);
        var final = Rt.Store.Get<Render>(Kinds.Render, rid)!;
        Assert.Equal(EditPlanFingerprint.Of(original), final.PlanHash);
        Assert.All(final.Qa.Where(q => q.Hard), q => Assert.Equal(QaStatus.Pass, q.Status));
        using (var download = await _http.GetAsync($"/api/renders/{rid}/files/mp4", Ct)) Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        using (var patch = await _http.PatchAsJsonAsync("/api/editplans/ep1", new { ops = new[] { new { op = "replace_text", target = "amount", text = "A社500万円" } } }, Ct))
        {
            patch.EnsureSuccessStatusCode();
            var body = await patch.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.NotEqual(final.PlanHash, body.GetProperty("plan_hash").GetString());
            Assert.True(body.GetProperty("approval").GetProperty("stale").GetBoolean());
            Assert.True(body.GetProperty("qa_stale").GetBoolean());
            Assert.True(body.GetProperty("final_render_stale").GetBoolean());
            Assert.Equal("fail", body.GetProperty("qa").EnumerateArray().Single(q => q.GetProperty("check_id").GetString() == "H-11").GetProperty("status").GetString());
        }
        var status = await _http.GetFromJsonAsync<JsonElement>($"/api/renders/{rid}", Ct);
        Assert.Equal("stale", status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("qa_stale").GetBoolean());
        foreach (var path in final.Outputs.Keys.Select(k => $"/api/renders/{rid}/files/{k}").Append($"/api/media/{rid}"))
        {
            using var download = await _http.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
            Assert.Equal("E-PLAN-STALE", Code(await download.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        using (var open = await _http.PostAsync($"/api/renders/{rid}/open-folder", null, Ct)) Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        using (var direct = await _http.PostAsync("/api/editplans/ep1/render", null, Ct))
            Assert.Equal("E-PLAN-STALE", Code(await direct.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        using (var batch = await ExportAsync("c1"))
            Assert.Equal("E-PLAN-STALE", Code(await batch.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        Assert.Single(Q.Exports);
        var list = await ListAsync();
        Assert.True(list["c1"].GetProperty("final_render_stale").GetBoolean());
        Assert.False(list["c1"].TryGetProperty("final_render_id", out _));
        Assert.True(list["c1"].GetProperty("approval").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task AT15_out_of_band_plan_change_during_render_is_not_published()
    {
        using (var approved = await ApproveAsync("c1", true)) approved.EnsureSuccessStatusCode();
        string rid;
        using (var export = await ExportAsync("c1"))
            rid = (await export.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("exports")[0].GetProperty("render_id").GetString()!;
        var until = DateTime.UtcNow.AddMinutes(3);
        while ((Q.Exports[rid].State == "queued" || Q.Exports[rid].Progress < 0.1) && DateTime.UtcNow < until) await Task.Delay(20, Ct);
        Assert.Equal("rendering", Q.Exports[rid].State);
        var plan = Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep1")!;
        Rt.Store.Put(Kinds.EditPlan, "ep1", _project, PlanEditor.Apply(plan, [new PlanOp("set_sfx", Enabled: false)]));
        while (Q.Exports[rid].State is "queued" or "rendering" && DateTime.UtcNow < until) await Task.Delay(200, Ct);
        Assert.Equal("failed", Q.Exports[rid].State);
        Assert.Equal("E-PLAN-STALE", Q.Exports[rid].ErrorCode);
        Assert.Null(Rt.Store.Get<Render>(Kinds.Render, rid));
        Assert.Empty(Directory.GetFiles(Path.Combine(Rt.Store.Get<Project>(Kinds.Project, _project)!.Directory, "exports"), "*.mp4", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(Rt.Store.Get<Project>(Kinds.Project, _project)!.Directory, "rejected"), "*.mp4", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AT14_API_rejects_ten_seconds_before_queueing_or_ASR()
    {
        var asset = Rt.Store.Get<SourceAsset>(Kinds.Asset, "a1")!;
        Rt.Store.Put(Kinds.Asset, asset.Id, _project, asset with { Media = asset.Media with { Duration = 10 } });
        using var request = await _http.PostAsJsonAsync($"/api/projects/{_project}/jobs", new { preset_ids = new[] { "P4" }, options = new { bgm = "none" } }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, request.StatusCode);
        var body = await request.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("E-SOL-INFEASIBLE", Code(body));
        Assert.Equal("素材がP4の尺の下限（25秒）より短い", body.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal([JobId], Rt.Store.Ids(Kinds.Job, _project));
    }

    [Fact]
    public async Task Current_final_is_not_labeled_stale_because_an_older_export_is_stale()
    {
        var plan = Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep1")!;
        var hash = EditPlanFingerprint.Of(plan);
        var old = new Render { Id = "old", EditplanId = plan.EditplanId, Kind = "final", Path = "unused.mp4", PlanHash = "old" };
        Rt.Store.Put(Kinds.Render, old.Id, plan.EditplanId, old);
        var current = old with
        {
            Id = "current", PlanHash = hash,
            Qa = new[] { "H-01", "H-05", "H-06", "H-07", "H-08", "H-09", "H-11" }
                .Select(id => new QaResult(id, QaStatus.Pass, "0", "0", "fixture") { PlanHash = hash, Stage = "plan" })
                .Concat(new[] { "H-01", "H-02", "H-03", "H-04", "H-05" }
                    .Select(id => new QaResult(id, QaStatus.Pass, "0", "0", "fixture") { PlanHash = hash, Stage = "final" })).ToList(),
        };
        Rt.Store.Put(Kinds.Render, current.Id, plan.EditplanId, current);
        var list = await ListAsync();
        Assert.False(list["c1"].GetProperty("final_render_stale").GetBoolean());
        Assert.Equal("current", list["c1"].GetProperty("final_render_id").GetString());
        var view = await _http.GetFromJsonAsync<JsonElement>("/api/editplans/ep1", Ct);
        Assert.False(view.GetProperty("final_render_stale").GetBoolean());
        using var download = await _http.GetAsync("/api/renders/old/files/mp4", Ct);
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
    }

    [Fact]
    public async Task Transcript_edits_invalidate_all_plans_sharing_the_cache_including_other_projects()
    {
        var asset = Rt.Store.Get<SourceAsset>(Kinds.Asset, "a1")!;
        var shared = asset with { Id = "shared", ProjectId = "other-project" };
        Rt.Store.Put(Kinds.Asset, shared.Id, shared.ProjectId, shared);
        var other = Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep1")! with
        {
            EditplanId = "ep-other", ProjectId = shared.ProjectId, Audio = new AudioSpec { Master = shared.Id },
        };
        Rt.Store.Put(Kinds.EditPlan, other.EditplanId, other.ProjectId, other);
        var dir = Path.Combine(Rt.Options.ResolvedDataDir, "cache", asset.Sha256[..16]);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "transcript_acceptance.json"), Json.Serialize(Fixtures.Talk), Ct);
        using (var approved = await ApproveAsync("c1", true)) approved.EnsureSuccessStatusCode();
        var before = Rt.Store.List<EditPlan>(Kinds.EditPlan).ToDictionary(p => p.EditplanId, EditPlanFingerprint.Of);
        using var response = await _http.PatchAsJsonAsync("/api/assets/a1/transcript",
            new { edits = new[] { new { word_id = Fixtures.Talk.Words[0].Id, text = "訂正" } } }, Ct);
        response.EnsureSuccessStatusCode();
        foreach (var plan in Rt.Store.List<EditPlan>(Kinds.EditPlan))
            Assert.NotEqual(before[plan.EditplanId], EditPlanFingerprint.Of(plan));
        Assert.Equal("訂正", Rt.CreateRunner().LoadTranscript(shared).Words[0].Text);
        var list = await ListAsync();
        Assert.True(list["c1"].GetProperty("approval").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Fingerprint_changes_with_any_edit_and_is_stable_across_storage()
    {
        var plan = new EditPlan { EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P4"].Ref, Timeline = [], Audio = new AudioSpec { Master = "a" } };
        var reloaded = Json.Deserialize<EditPlan>(Json.Serialize(plan));
        Assert.Equal(EditPlanFingerprint.Of(plan), EditPlanFingerprint.Of(reloaded));
        Assert.NotEqual(EditPlanFingerprint.Of(plan), EditPlanFingerprint.Of(plan with { Audio = plan.Audio with { SfxEnabled = false } }));
    }
}
