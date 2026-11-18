using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Rendering;
using VideoCreator.Selection;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class ExportArtifactTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-artifacts-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    EditPlan _plan = null!;
    Render _render = null!;
    readonly ImageHandler _images = new();

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
            b.ConfigureServices(services => services.AddSingleton(new HttpClient(_images)));
        });
        _app.UseKestrel();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        _plan = new EditPlan
        {
            EditplanId = "ep-artifacts", ProjectId = "p-artifacts", Preset = Fixtures.Presets["P4"].Ref,
            Audio = new AudioSpec { Master = "a" },
        };
        Rt.Store.Put(Kinds.EditPlan, _plan.EditplanId, _plan.ProjectId, _plan);
        var path = Path.Combine(_root, "完成 動画.mp4");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), Ct);
        var text = Path.ChangeExtension(path, ".metadata.md");
        await File.WriteAllTextAsync(text, "# 投稿文\n<script>alert('not HTML')</script>\n日本語", Ct);
        var post = Path.ChangeExtension(path, ".post.json");
        await File.WriteAllTextAsync(post, """{"youtube":"保存済み","engage":"投稿文","chapters":"","source":"template","notes":[]}""", Ct);
        var hash = EditPlanFingerprint.Of(_plan);
        _render = new Render
        {
            Id = "r-artifacts", EditplanId = _plan.EditplanId, Kind = "final", Path = path, PlanHash = hash,
            Qa = [new("H-11", QaStatus.Pass, "", "", "current") { PlanHash = hash }],
            Outputs = new()
            {
                ["mp4"] = path, ["metadata"] = text, ["post"] = post,
                ["thumb1"] = Path.ChangeExtension(path, ".png"),
                ["other"] = Path.ChangeExtension(path, ".bin"),
            },
        };
        Rt.Store.Put(Kinds.Render, _render.Id, _plan.EditplanId, _render);
    }

    public async ValueTask DisposeAsync()
    {
        var store = Rt.Store;
        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Artifacts_report_recorded_paths_types_and_missing_files_without_generating_posts()
    {
        var before = File.GetLastWriteTimeUtc(_render.Outputs["post"]);
        var body = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/artifacts", Ct);
        Assert.Equal(_render.Path, body.GetProperty("path").GetString());
        Assert.True(body.GetProperty("available").GetBoolean());
        var files = body.GetProperty("files").EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!);
        Assert.Equal(5, files.Count);
        Assert.Equal("video", files["mp4"].GetProperty("preview_type").GetString());
        Assert.Equal("text", files["metadata"].GetProperty("preview_type").GetString());
        Assert.Equal("image", files["thumb1"].GetProperty("preview_type").GetString());
        Assert.False(files["thumb1"].GetProperty("available").GetBoolean());
        Assert.False(files["other"].TryGetProperty("preview_type", out _));
        Assert.Equal(before, File.GetLastWriteTimeUtc(_render.Outputs["post"]));
        var post = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/post", Ct);
        Assert.Equal("保存済み", post.GetProperty("youtube").GetString());
    }

    [Fact]
    public async Task Text_previews_are_literal_and_bounded_while_full_downloads_and_video_ranges_are_preserved()
    {
        var preview = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/files/metadata/preview", Ct);
        Assert.Contains("<script>", preview.GetProperty("text").GetString());
        Assert.False(preview.GetProperty("truncated").GetBoolean());
        var longText = new string('あ', 65537);
        await File.WriteAllTextAsync(_render.Outputs["metadata"], longText, Ct);
        preview = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/files/metadata/preview", Ct);
        Assert.Equal(longText[..65536], preview.GetProperty("text").GetString());
        Assert.True(preview.GetProperty("truncated").GetBoolean());
        Assert.Equal(longText, await _http.GetStringAsync("/api/renders/r-artifacts/files/metadata", Ct));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/media/r-artifacts");
        request.Headers.Range = new RangeHeaderValue(10, 19);
        using var response = await _http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 10-19/256", response.Content.Headers.ContentRange!.ToString());
        Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), await response.Content.ReadAsByteArrayAsync(Ct));
        using var unsupported = await _http.GetAsync("/api/renders/r-artifacts/files/mp4/preview", Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupported.StatusCode);
    }

    [Fact]
    public async Task Missing_files_and_unknown_keys_fail_explicitly()
    {
        foreach (var url in new[] { "/api/renders/unknown/artifacts", "/api/renders/r-artifacts/files/unknown/preview", "/api/renders/r-artifacts/files/thumb1/preview" })
        {
            using var response = await _http.GetAsync(url, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        File.Delete(_render.Path);
        using var open = await _http.PostAsync("/api/renders/r-artifacts/open-folder", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);
        var body = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/artifacts", Ct);
        Assert.False(body.GetProperty("available").GetBoolean());
        using var media = await _http.GetAsync("/api/media/r-artifacts", Ct);
        Assert.Equal(HttpStatusCode.NotFound, media.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(65537)]
    public async Task Text_preview_limit_is_exact_including_empty_files(int length)
    {
        await File.WriteAllTextAsync(_render.Outputs["metadata"], new string('あ', length), Ct);
        var preview = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/files/metadata/preview", Ct);
        Assert.Equal(Math.Min(length, 65536), preview.GetProperty("text").GetString()!.Length);
        Assert.Equal(length > 65536, preview.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Posting_text_regeneration_creates_a_downloadable_image_and_keeps_previous_outputs_on_model_failure()
    {
        var source = Path.Combine(_root, "model.png");
        await Rt.Ff.RunFfmpegCheckedAsync(["-v", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "color=blue:s=1536x1024",
            "-frames:v", "1", "-threads", "1", source], Ct);
        _images.Bytes = await File.ReadAllBytesAsync(source, Ct);
        Rt.SaveFoundry(new FoundryConnection("https://image.fixture.openai.azure.com/", "unused-chat", null, "key", ApiKey: "test-key"), DateTimeOffset.UtcNow);
        Rt.Store.Put(Kinds.Project, _plan.ProjectId, null, new Project
        {
            Id = _plan.ProjectId, Name = "画像テスト", Directory = _root,
            GenerationSettings = new() { MakeEngageImage = true, EngageImageDeployment = "my-gpt-image" },
        });
        Rt.Store.Put(Kinds.Asset, "a", _plan.ProjectId, new SourceAsset
        {
            Id = "a", ProjectId = _plan.ProjectId, Path = "not-used.mp4", Sha256 = new string('a', 64), Media = Fixtures.Landscape(),
        });
        Rt.Store.Put(Kinds.Job, "j", _plan.ProjectId, new Job
        {
            Id = "j", ProjectId = _plan.ProjectId, PresetIds = ["P4"],
        });
        Rt.Store.Put(Kinds.Candidate, "c", "j", new Candidate { Id = "c", Rank = 1, Headline = "新しい取り組み", EditPlanId = _plan.EditplanId, Segments = [] });
        _plan = _plan with { Provenance = new Provenance { JobId = "j", CandidateId = "c" } };
        Rt.Store.Put(Kinds.EditPlan, _plan.EditplanId, _plan.ProjectId, _plan);
        var hash = EditPlanFingerprint.Of(_plan);
        _render = _render with { PlanHash = hash, Qa = _render.Qa.Select(q => q with { PlanHash = hash }).ToList() };
        Rt.Store.Put(Kinds.Render, _render.Id, _plan.EditplanId, _render);
        using var created = await _http.PostAsync("/api/renders/r-artifacts/post", null, Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var post = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var engage = post.GetProperty("engage").GetString();
        Assert.StartsWith(engage + "\n\n" + EngageImage.Scene, Assert.Single(_images.Prompts));
        using var image = await _http.GetAsync("/api/renders/r-artifacts/files/engage_image", Ct);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        var bytes = await image.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(1000, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(200, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        var provenance = await _http.GetFromJsonAsync<JsonElement>("/api/renders/r-artifacts/files/engage_image_prompt", Ct);
        Assert.Equal(engage, provenance.GetProperty("post").GetString());
        Assert.Equal("my-gpt-image", provenance.GetProperty("deployment").GetString());
        await _http.GetStringAsync("/api/renders/r-artifacts/post", Ct);
        Assert.Single(_images.Prompts);

        using var regenerated = await _http.PostAsync("/api/renders/r-artifacts/post", null, Ct);
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        Assert.Equal(2, _images.Prompts.Count);
        var previousPost = await _http.GetStringAsync("/api/renders/r-artifacts/post", Ct);
        _images.Fail = true;
        using var failed = await _http.PostAsync("/api/renders/r-artifacts/post", null, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        var error = await failed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("E-IMAGE-GENERATION", error.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(previousPost, await _http.GetStringAsync("/api/renders/r-artifacts/post", Ct));
        Assert.Equal(bytes, await _http.GetByteArrayAsync("/api/renders/r-artifacts/files/engage_image", Ct));
    }

    [Fact]
    public async Task Changed_plan_invalidates_all_artifact_surfaces()
    {
        Rt.Store.Put(Kinds.EditPlan, _plan.EditplanId, _plan.ProjectId, _plan with { Audio = _plan.Audio with { SfxEnabled = false } });
        foreach (var url in new[] { "/api/renders/r-artifacts/artifacts", "/api/renders/r-artifacts/files/metadata/preview", "/api/renders/r-artifacts/files/metadata", "/api/renders/r-artifacts/post", "/api/media/r-artifacts" })
        {
            using var response = await _http.GetAsync(url, Ct);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("E-PLAN-STALE", error.GetProperty("error").GetProperty("code").GetString());
        }
        using var open = await _http.PostAsync("/api/renders/r-artifacts/open-folder", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
    }

    sealed class ImageHandler : HttpMessageHandler
    {
        public byte[] Bytes = [];
        public bool Fail;
        public List<string> Prompts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/openai/v1/images/generations", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Prompts.Add(body.RootElement.GetProperty("prompt").GetString()!);
            return Fail
                ? new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = new { code = "content_policy_violation", message = "rejected" } }) }
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(new { created = 1, data = new[] { new { b64_json = Convert.ToBase64String(Bytes) } } }) };
        }
    }
}
