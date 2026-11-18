using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>P12: a vertical short cut from the same source, alone or as a second, linked job next to another template.</summary>
public sealed class ShortPresetTests : IAsyncLifetime
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static Preset P12 => Fixtures.Presets["P12"];

    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-short-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    string _id = "";
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();

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
            b.UseSetting("VideoCreator:Llm:Endpoint", "");
        });
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        using var response = await _http.PostAsJsonAsync("/api/projects", new { name = "ショート" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        _id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    public async ValueTask DisposeAsync()
    {
        // jobs queued by a test fail fast (no ASR, no file); let them finish before the store goes away
        for (int i = 0; i < 100 && Rt.Store.List<Job>(Kinds.Job, _id).Any(j => j.Status is not (JobStatus.Failed or JobStatus.Cancelled or JobStatus.Ready or JobStatus.Done)); i++)
            await Task.Delay(100);
        _http.Dispose();
        var store = Rt.Store;
        await _app.DisposeAsync();
        store.Dispose();
        Directory.Delete(_root, true);
    }

    void AddLandscapeSource(double duration = 600)
    {
        var asset = new SourceAsset { Id = "a1", ProjectId = _id, Path = Path.Combine(_root, "missing.mp4"), Sha256 = new string('5', 64), Role = AssetRole.Main, Media = Fixtures.Landscape() with { Duration = duration } };
        Rt.Store.Put(Kinds.Asset, asset.Id, _id, asset);
        Rt.Store.Put(Kinds.Rights, "rt1", _id, new RightsConfirmation { Id = "rt1", ProjectId = _id, AssetId = "a1" });
    }

    Task<HttpResponseMessage> Start(string[] presets, object options) =>
        _http.PostAsJsonAsync($"/api/projects/{_id}/jobs", new { preset_ids = presets, options }, Ct);

    [Fact]
    public void A_short_preset_must_be_vertical_and_at_most_sixty_seconds()
    {
        Assert.True(P12.Short);
        Assert.Equal("9:16", P12.Target.Aspect);
        Assert.True(P12.Target.Max <= 60);
        Assert.Throws<PresetValidationException>(() => PresetStore.Validate(P12 with { Target = P12.Target with { Aspect = "16:9" } }));
        Assert.Throws<PresetValidationException>(() => PresetStore.Validate(P12 with { Target = P12.Target with { DurationSec = [15, 90] } }));
        PresetStore.Validate(P12);
        // the whole file (body + teaser) must fit the short's max, not the general 180 s cap
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = P12.Ref, Audio = new AudioSpec { Master = "a1" } };
        Assert.Equal(60, VideoCreator.Qa.QaEngine.DurationRange(plan, P12).Cap);
        Assert.Equal(180, VideoCreator.Qa.QaEngine.DurationRange(plan, Fixtures.Presets["P6"]).Cap);
    }

    [Fact]
    public void Apply_forces_a_vertical_single_short_and_leaves_other_presets_alone()
    {
        var o = new JobOptions { Orientation = "landscape", Opening = true, Closing = true, SceneCards = true, Parts = 3, Tsukkomi = true, TsukkomiStyle = "card" };
        var s = P12.Apply(o);
        Assert.Equal(("portrait", false, false, false, null, "telop"), (s.Orientation, s.Opening, s.Closing, s.SceneCards, s.Parts, s.TsukkomiStyle));
        Assert.False(s.IsSeries);
        Assert.Same(o, Fixtures.Presets["P6"].Apply(o));
    }

    [Fact] // even a landscape source with "landscape" chosen for the main template gives a 1080×1920 short without bumpers
    public async Task Plan_for_a_landscape_source_is_vertical_without_inserts()
    {
        var kit = new BrandKit
        {
            RightsConfirmed = true,
            Files = [new() { Path = "op916.mp4", Kind = "opening", Width = 1080, Height = 1920, Duration = 3, HasAudio = true, Lufs = -14 },
                     new() { Path = "ed916.mp4", Kind = "closing", Width = 1080, Height = 1920, Duration = 3, HasAudio = true, Lufs = -14 }],
        };
        var assets = new List<SourceAsset> { new() { Id = "a1", ProjectId = "p", Path = "x.mp4", Sha256 = "x", Role = AssetRole.Main, Media = Fixtures.Landscape() } };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P12"], Options = new JobOptions { Opening = true, Closing = true, SceneCards = true, Orientation = "landscape" } };
        var cand = new Candidate { Id = "c1", Segments = [new Interval(8.8, 40)], HookText = "1万円で帰れない", Headline = "100円だけで1万円" };
        var (plan, _) = await PlanBuilder.BuildAsync(new PlanRequest(cand, Fixtures.Talk, Fixtures.Signals(Fixtures.Talk), P12, assets, assets[0], "p", job,
            new Dictionary<string, List<FaceSample>>(), null, null, Kit: kit), Ct);
        Assert.Equal((1080, 1920), (plan.Canvas.Width, plan.Canvas.Height));
        Assert.Empty(plan.Inserts);
        Assert.DoesNotContain(plan.Timeline, c => c.Reframe.Mode == "fit");
    }

    [Fact]
    public async Task Choosing_a_template_and_the_short_queues_two_linked_jobs()
    {
        AddLandscapeSource();
        using var response = await Start(["P6", "P12"], new { opening = false, closing = false, orientation = "landscape", bgm = "none", reuse_analysis = false });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var mainId = body.GetProperty("job_id").GetString()!;
        var shortId = body.GetProperty("companion_job_id").GetString()!;
        Assert.Equal([mainId, shortId], body.GetProperty("job_ids").EnumerateArray().Select(e => e.GetString()));

        var main = Rt.Store.Get<Job>(Kinds.Job, mainId)!;
        var shorts = Rt.Store.Get<Job>(Kinds.Job, shortId)!;
        Assert.Equal(["P6"], main.PresetIds);
        Assert.Equal(shortId, main.CompanionJobId);
        Assert.Equal(["P12"], shorts.PresetIds);
        Assert.Equal(mainId, shorts.CompanionJobId);
        Assert.Equal("landscape", main.Options.Orientation);
        Assert.False(main.Options.ReuseAnalysis);
        Assert.Equal("portrait", shorts.Options.Orientation);
        Assert.True(shorts.Options.ReuseAnalysis);
        Assert.Equal(0, shorts.Cost!.AsrMinutes); // the source is transcribed once, by the first job
    }

    [Fact] // the short drops opening/closing itself, so a missing brand kit does not block it
    public async Task Short_alone_ignores_bumper_options_and_queues_one_job()
    {
        AddLandscapeSource();
        using var response = await Start(["P12"], new { opening = true, closing = true, bgm = "none" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(body.TryGetProperty("companion_job_id", out _));
        var job = Rt.Store.Get<Job>(Kinds.Job, body.GetProperty("job_id").GetString()!)!;
        Assert.Equal(("portrait", false, false, null), (job.Options.Orientation, job.Options.Opening, job.Options.Closing, job.CompanionJobId));
    }

    [Fact] // validation of either group fails the whole request: nothing is queued
    public async Task A_failing_group_queues_neither_job()
    {
        AddLandscapeSource();
        using var noKit = await Start(["P6", "P12"], new { opening = true, closing = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noKit.StatusCode);
        Assert.Equal("E-BRANDKIT", (await noKit.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(Rt.Store.List<Job>(Kinds.Job, _id));

        var asset = Rt.Store.Get<SourceAsset>(Kinds.Asset, "a1")!;
        Rt.Store.Put(Kinds.Asset, "a1", _id, asset with { Media = asset.Media with { Duration = 10 } });
        using var tooShort = await Start(["P6", "P12"], new { opening = false, closing = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
        Assert.Empty(Rt.Store.List<Job>(Kinds.Job, _id));
    }

    [Fact]
    public async Task Generation_settings_keep_the_short_choice_separate_from_the_main_template()
    {
        using (var asMain = await _http.PutAsJsonAsync($"/api/projects/{_id}/generation-settings", new { preset_id = "P12" }, Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, asMain.StatusCode);
        using (var bad = await _http.PutAsJsonAsync($"/api/projects/{_id}/generation-settings", new { preset_id = "P6", @short = "sometimes" }, Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        using (var ok = await _http.PutAsJsonAsync($"/api/projects/{_id}/generation-settings", new { preset_id = "P6", @short = "with" }, Ct))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var saved = Rt.Store.Get<Project>(Kinds.Project, _id)!.GenerationSettings;
        Assert.Equal(("P6", "with"), (saved.PresetId, saved.Short));
    }
}
