using Microsoft.Extensions.Logging.Abstractions;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>A final export is stored once, under the id the UI polls; legacy duplicates are cleaned up.</summary>
public sealed class RenderRecordTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-rr-" + Guid.NewGuid().ToString("N")[..8]);
    string Data => Path.Combine(_dir, "data");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task Final_export_creates_exactly_one_render_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        Directory.CreateDirectory(_dir);
        var src = Path.Combine(_dir, "land.mp4");
        await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=130", "-f", "lavfi", "-i", "sine=f=330:d=130,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", src], ct);

        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var project = new Project { Id = "p_rr00000000", Name = "rr", Directory = Path.Combine(_dir, "rr_rr0000") };
        store.Put(Kinds.Project, project.Id, null, project);
        var media = new MediaInfo { Duration = 130, Width = 1280, Height = 720, Fps = 30, AudioChannels = 1 };
        var sha = new string('9', 64);
        store.Put(Kinds.Asset, "a1", project.Id, new SourceAsset { Id = "a1", ProjectId = project.Id, Path = src, Sha256 = sha, Media = media });
        store.Put(Kinds.Rights, "rt1", project.Id, new RightsConfirmation { Id = "rt1", ProjectId = project.Id, AssetId = "a1" });
        var cache = Path.Combine(Data, "cache", sha[..16]); Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "transcript_fixture.json"), VideoCreator.Json.Serialize(Fixtures.Talk), ct);

        var p = Fixtures.Presets["P4"]; var tr = Fixtures.Talk;
        var res = Solver.Plan([new Interval(74, 103)], tr, Fixtures.Signals(tr), p, media, "a1", ["300円の商品"]);
        var plan = new EditPlan
        {
            EditplanId = "ep_rr", ProjectId = project.Id, Preset = p.Ref, Timeline = res.Clips,
            Telops = TelopBuilder.Build(res.Clips, tr, p, "あと2個", "300円の商品", ["300円の商品"], []),
            Audio = new AudioSpec { Master = "a1" },
        };
        store.Put(Kinds.EditPlan, plan.EditplanId, project.Id, plan);

        var runner = () => new PipelineRunner(new PipelineServices { Ff = ff, Store = store, Presets = Fixtures.Presets, DataDir = Data });
        var q = new JobQueue(store, runner, new JobEvents(), NullLogger<JobQueue>.Instance);
        await q.StartAsync(ct);
        try
        {
            q.EnqueueExport("r_export00001", plan, 1);
            var until = DateTime.UtcNow.AddMinutes(3);
            while (q.Exports["r_export00001"].State is "queued" or "rendering" && DateTime.UtcNow < until) await Task.Delay(200, ct);
            var st = q.Exports["r_export00001"];
            Assert.True(st.State == "done", $"{st.State}: {st.ErrorCode} {st.Message}");
        }
        finally { await q.StopAsync(CancellationToken.None); }

        Assert.Equal(["r_export00001"], store.Ids(Kinds.Render, plan.EditplanId));
        var r = store.Get<Render>(Kinds.Render, "r_export00001")!;
        Assert.Equal("final", r.Kind);
        Assert.True(File.Exists(r.Path));
        Assert.NotNull(r.Reproducibility);
        Assert.Equal(EditPlanFingerprint.Of(plan), r.Reproducibility.PlanHash);
        Assert.Equal(p.Ref, r.Reproducibility.Preset);
        Assert.Equal(64, Assert.Single(r.Reproducibility.SourceSha256, f => f.Path == src).Sha256.Length);
        Assert.All(r.Qa, check => Assert.Equal(r.Reproducibility.PlanHash, check.PlanHash));
        // QA scratch is keyed by the same id, so deleting the render can find it
        Assert.Equal(["r_export00001"], Directory.GetDirectories(Path.Combine(Data, "qa")).Select(Path.GetFileName));
    }

    [Fact]
    public void Legacy_preview_does_not_certify_final_only_checks()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        var preview = new Render
        {
            Id = "r_old", EditplanId = "ep_old", Kind = "preview", Path = Path.Combine(_dir, "old.mp4"),
            Qa = [new("H-02", QaStatus.Pass, "540x960", "540x960", "resolution"), new("H-05", QaStatus.Pass, "safe", "safe", "safe zone")],
        };
        store.Put(Kinds.Render, preview.Id, preview.EditplanId, preview);
        var final = preview with { Id = "r_final", Kind = "final" };
        store.Put(Kinds.Render, final.Id, final.EditplanId, final);
        Assert.Equal(1, RenderMaintenance.MarkLegacyPreviewChecksPending(store));
        var changed = store.Get<Render>(Kinds.Render, preview.Id)!;
        Assert.Equal(QaStatus.NotRun, changed.Qa[0].Status);
        Assert.Equal("final", changed.Qa[0].Stage);
        Assert.Null(changed.Qa[0].PlanHash);
        Assert.Equal(QaStatus.Pass, changed.Qa[1].Status);
        Assert.Equal(QaStatus.Pass, store.Get<Render>(Kinds.Render, final.Id)!.Qa[0].Status);
        Assert.Equal(0, RenderMaintenance.MarkLegacyPreviewChecksPending(store));
    }

    [Fact]
    public void Legacy_duplicate_final_records_are_removed_keeping_the_export_id()
    {
        using var store = new Store(Path.Combine(Data, "db.sqlite"));
        Render R(string id, string ep, string path, string kind = "final", bool metadata = false) => new()
        {
            Id = id, EditplanId = ep, Kind = kind, Path = path, FfmpegVersion = "x",
            Outputs = metadata ? new() { ["mp4"] = path, ["metadata"] = path + ".md" } : new() { ["mp4"] = path },
        };
        var f1 = Path.Combine(_dir, "a.mp4"); var f2 = Path.Combine(_dir, "b.mp4");
        // old builds: runner record first, then the export record (with metadata) for the same file
        store.Put(Kinds.Render, "r_internal01", "ep1", R("r_internal01", "ep1", f1));
        store.Put(Kinds.Render, "r_export0001", "ep1", R("r_export0001", "ep1", f1, metadata: true));
        // no candidate → no metadata: the later write (export id) wins
        store.Put(Kinds.Render, "r_internal02", "ep1", R("r_internal02", "ep1", f2));
        Thread.Sleep(5);
        store.Put(Kinds.Render, "r_export0002", "ep1", R("r_export0002", "ep1", f2));
        // untouched: previews and a single final in another plan
        store.Put(Kinds.Render, "r_preview001", "ep1", R("r_preview001", "ep1", Path.Combine(_dir, "p.mp4"), "preview"));
        store.Put(Kinds.Render, "r_single0001", "ep2", R("r_single0001", "ep2", f1));
        foreach (var id in new[] { "r_internal01", "r_internal02", "r_export0001" }) Directory.CreateDirectory(Path.Combine(Data, "qa", id));

        Assert.Equal(2, RenderMaintenance.RemoveDuplicateFinals(store, Data));
        Assert.Equal(["r_export0001", "r_export0002", "r_preview001"], store.Ids(Kinds.Render, "ep1").Order());
        Assert.Equal(["r_single0001"], store.Ids(Kinds.Render, "ep2"));
        Assert.False(Directory.Exists(Path.Combine(Data, "qa", "r_internal01")));
        Assert.False(Directory.Exists(Path.Combine(Data, "qa", "r_internal02")));
        Assert.True(Directory.Exists(Path.Combine(Data, "qa", "r_export0001")));
        Assert.Equal(0, RenderMaintenance.RemoveDuplicateFinals(store, Data));   // idempotent
    }
}
