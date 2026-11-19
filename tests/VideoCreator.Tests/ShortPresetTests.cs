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

    // ---- the shorts look (telop.look = "short", docs/IMPLEMENTATION.md §24) ----

    static readonly MediaInfo Vertical = Fixtures.Landscape(8) with { Width = 1080, Height = 1920 };
    static EditPlan ShortPlan(List<Clip> clips, List<Telop> telops, Func<double, FaceBox[]> faces) => new()
    {
        EditplanId = "e", ProjectId = "p", Preset = P12.Ref, Audio = new() { Master = "a" }, Timeline = clips, Telops = telops,
        TelopFaces = new() { ["a"] = new(Vertical, Enumerable.Range(0, 18).Select(i => new FaceSample(i * 0.5, [.. faces(i * 0.5)])).ToList()) },
    };

    [Fact] // measured: the title bar is on in 93 % of QuizKnock short frames; the hook is a big title over the opening seconds, not instead of the bar
    public void The_title_bar_runs_from_the_first_frame_in_the_short_look_only()
    {
        var tr = new Transcript { Provider = "t", Duration = 10, Words = [new("w1", 0.2, 1, "帰れません", 1), new("w2", 1, 2, "。", 1), new("w3", 3, 4, "百円だけ", 2), new("w4", 4, 5, "。", 2)] };
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 8, Reframe = new() { Mode = "fit" } } };
        Telop Bar(Preset p) => TelopBuilder.Build(clips, tr, p, "帰れません", "百円だけ", [], []).Single(t => t.Type == TelopType.Headline);
        Assert.Equal(0, Bar(P12).Start);
        Assert.Equal(TelopBuilder.HookSeconds, Bar(Fixtures.Presets["P4"]).Start);
    }

    [Fact] // the bar never moves: it is left out only in the cut where a face is under it
    public void The_title_bar_is_hidden_only_in_cuts_where_a_face_is_under_it()
    {
        var clips = new List<Clip>
        {
            new() { ClipId = "c1", AssetId = "a", SrcIn = 0, SrcOut = 4, Reframe = new() { Mode = "fit" } },
            new() { ClipId = "c2", AssetId = "a", SrcIn = 4, SrcOut = 8, Reframe = new() { Mode = "fit" } },
        };
        var bar = new Telop { Id = "h", Type = TelopType.Headline, Text = "百円だけ", Start = 0, End = 8, Style = "Headline" };
        // a close-up whose head reaches the top in the first cut, a lower face in the second (detector samples within 0.5 s of a cut count for it)
        var plan = ShortPlan(clips, [bar], t => [t < 3 ? new FaceBox(0.3, 0.1, 0.4, 0.15, 1) : new FaceBox(0.3, 0.55, 0.4, 0.15, 1)]);
        var placed = VideoCreator.Rendering.TelopLayout.Resolve(plan, P12)["h"];
        Assert.NotNull(placed.Bounds);
        Assert.Equal([(0.0, 4.0)], placed.Hidden);
        var events = VideoCreator.Rendering.AssWriter.Write(plan, P12).Split('\n').Where(l => l.StartsWith("Dialogue:") && l.Contains(",Headline,")).ToList();
        Assert.Equal("0:00:04.00,0:00:08.00", Assert.Single(events).Split(',')[1] + "," + events[0].Split(',')[2]);

        var everywhere = ShortPlan(clips, [bar], _ => [new FaceBox(0.3, 0.1, 0.4, 0.15, 1)]);
        Assert.Null(VideoCreator.Rendering.TelopLayout.Resolve(everywhere, P12)["h"].Bounds);
    }

    [Fact] // a big face leaves no room for a 200 px punch telop: the short look shrinks it (down to 48 px) instead of dropping it
    public void A_punch_telop_shrinks_to_fit_beside_a_big_face_in_the_short_look()
    {
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 4, Reframe = new() { Mode = "fit" } } };
        var emph = new Telop { Id = "e", Type = TelopType.Emphasis, Text = "見せ場です", Start = 0, End = 3, Style = "Emph", Animation = "pop" };
        var face = new FaceBox(0.1, 0.12, 0.8, 0.3, 1);
        var shortPlan = ShortPlan(clips, [emph], _ => [face]);
        var placed = VideoCreator.Rendering.TelopLayout.Resolve(shortPlan, P12)["e"];
        var bounds = Assert.IsType<VideoCreator.Rendering.TelopLayout.Rect>(placed.Bounds);
        Assert.InRange(placed.FontSize, VideoCreator.Rendering.TelopLayout.MinShrunkPx, VideoCreator.Rendering.AssWriter.Short.EmphPx - 1);
        Assert.False(bounds.Intersects(VideoCreator.Rendering.TelopLayout.Project(face, clips[0], Vertical, shortPlan.Canvas, 1)));
        // a face over the whole free height: nothing fits even at the smallest size
        Assert.Null(VideoCreator.Rendering.TelopLayout.Resolve(ShortPlan(clips, [emph], _ => [new FaceBox(0.1, 0.12, 0.8, 0.55, 1)]), P12)["e"].Bounds);
    }

    [Fact] // one fill colour per speaker (QuizKnock's editors: 「1人1色」); no red keyword inside the caption (OCR misread it at feed size, §24)
    public void Captions_are_filled_per_speaker_without_keyword_colours()
    {
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 4, Reframe = new() { Mode = "fit" } } };
        var cap = new Telop { Id = "c1", Type = TelopType.Caption, Text = "一万円なんて", Start = 0, End = 2, Speaker = 2 };
        var emph = new Telop { Id = "e", Type = TelopType.Emphasis, Text = "一万円", Start = 0, End = 1.5, Style = "Emph", Animation = "pop" };
        var ass = VideoCreator.Rendering.AssWriter.Write(ShortPlan(clips, [cap, emph], _ => []), P12);
        var line = ass.Split('\n').Single(l => l.StartsWith("Dialogue:") && l.Contains(",CapS2,"));
        Assert.EndsWith(",,一万円なんて", line.TrimEnd());
        Assert.Contains($"Style: CapS2,{VideoCreator.Rendering.AssWriter.FontName},112,{VideoCreator.Rendering.AssWriter.Short.SpeakerFills[1]},", ass);
        Assert.Single(ass.Split('\n'), l => l.StartsWith("Dialogue:") && l.Contains(",CapEdge,") && l.TrimEnd().EndsWith("一万円なんて"));
        Assert.Equal(11, VideoCreator.Rendering.AssWriter.CaptionLineChars(P12));
    }

    [Fact] // the bands and the title-bar rows used for face avoidance and QA must contain what libass actually draws, inside the safe zone
    public async Task Rendered_short_telops_stay_inside_their_bands_and_the_safe_zone()
    {
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        const int W = 1080, H = 1920;
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 4, Reframe = new() { Mode = "fit" } } };
        var g = VideoCreator.Rendering.AssWriter.Geo(P12, W, H);
        var cases = new (TelopType Type, string Style, string Text)[]
        {
            (TelopType.Headline, "Headline", "ガチ反省会"), (TelopType.Headline, "Headline", "100円のものだけで1万円"), (TelopType.Hook, "Hook", "帰れません"),
            (TelopType.Caption, "Caption", "芝が言ってたんですけど"), (TelopType.Emphasis, "Emph", "アウトです"),
        };
        foreach (var (type, style, text) in cases)
        {
            var plan = ShortPlan(clips, [new Telop { Id = "x", Type = type, Text = text, Start = 0, End = 3, Style = style }], _ => []);
            var ass = Path.Combine(_root, $"short_{style}_{text.Length}.ass");
            await File.WriteAllTextAsync(ass, VideoCreator.Rendering.AssWriter.Write(plan, P12), new System.Text.UTF8Encoding(false), Ct);
            using var ms = new MemoryStream();
            await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=black:s={W}x{H}:r=10:d=4",
                "-vf", $"subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(ff.FontsDir)},select='eq(n,10)'", "-fps_mode", "passthrough", "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", "-"], Ct, stdout: ms);
            var buf = ms.ToArray(); Assert.Equal(W * H, buf.Length);
            int x1 = W, x2 = -1, y1 = H, y2 = -1;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (buf[y * W + x] > 48) { x1 = Math.Min(x1, x); x2 = Math.Max(x2, x); y1 = Math.Min(y1, y); y2 = Math.Max(y2, y); }
            Assert.True(x2 > 0, $"{style}: nothing drawn");
            var placed = VideoCreator.Rendering.TelopLayout.Resolve(plan, P12)["x"];
            var (top, bottom) = type == TelopType.Headline ? (placed.Bounds!.Top, placed.Bounds.Bottom) : VideoCreator.Rendering.AssWriter.Band(type, P12, H)!.Value;
            Assert.True(y1 >= top && y2 <= bottom, $"{style} 「{text}」: drawn y{y1}-{y2}, band {top:F0}-{bottom:F0}");
            Assert.True(y1 >= g.Safe.Top && y2 <= H - g.Safe.Bottom && x1 >= g.Safe.Left && x2 <= W - g.Safe.Right, $"{style}: drawn x{x1}-{x2} y{y1}-{y2} outside the safe zone");
        }
    }

    [Fact] // found in the rendered selfie: an emphasis pushed up by the face was drawn under the title bar
    public void A_telop_moved_up_by_a_face_never_lands_under_the_title_bar()
    {
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 8, Reframe = new() { Mode = "fit" } } };
        var bar = new Telop { Id = "h", Type = TelopType.Headline, Text = "百円だけ", Start = 0, End = 8, Style = "Headline" };
        var emph = new Telop { Id = "e", Type = TelopType.Emphasis, Text = "日本語", Start = 0, End = 2, Style = "Emph", Animation = "pop" };
        // the face fills the middle: the only free rows are at the top, where the bar is
        var plan = ShortPlan(clips, [emph, bar], _ => [new FaceBox(0.15, 0.3, 0.7, 0.42, 1)]);
        var layout = VideoCreator.Rendering.TelopLayout.Resolve(plan, P12);
        var barRect = Assert.IsType<VideoCreator.Rendering.TelopLayout.Rect>(layout["h"].Bounds);
        Assert.True(layout["e"].Bounds is not { } e || !e.Intersects(barRect), $"emphasis {layout["e"].Bounds} under the bar {barRect}");
    }
}
