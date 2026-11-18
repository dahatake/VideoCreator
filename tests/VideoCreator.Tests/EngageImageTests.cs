using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Rendering;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class EngageImageTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-engage-image-" + Guid.NewGuid().ToString("N"));
    readonly Store _store;
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    public EngageImageTests()
    {
        Directory.CreateDirectory(_root);
        _store = new Store(Path.Combine(_root, "test.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_root, true);
    }

    PipelineRunner Runner(Func<string, IImageGenerator>? generator) => new(new PipelineServices
    {
        Ff = Fixtures.Ff!, Store = _store, Presets = Fixtures.Presets, DataDir = _root, ImageGenerator = generator,
    });

    (Render, EditPlan) Prepare(bool enabled)
    {
        var plan = new EditPlan
        {
            EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P4"].Ref,
            Audio = new AudioSpec { Master = "a" }, Provenance = new Provenance { JobId = "j" },
        };
        _store.Put(Kinds.EditPlan, "ep", "p", plan);
        _store.Put(Kinds.Job, "j", "p", new Job
        {
            Id = "j", ProjectId = "p", PresetIds = ["P4"],
            Options = new() { MakeEngageImage = enabled, EngageImageDeployment = "my-gpt-image" },
        });
        var thumb = Path.Combine(_root, "thumb.png");
        File.WriteAllText(thumb, "existing thumbnail");
        return (new Render
        {
            Id = "r", EditplanId = "ep", Kind = "final", Path = Path.Combine(_root, "video.mp4"),
            Outputs = new() { ["thumb1"] = thumb },
        }, plan);
    }

    [Theory]
    [InlineData(1536, 1024)]
    [InlineData(1024, 1536)]
    public async Task Generated_image_uses_exact_post_and_is_a_separate_1000_by_200_png(int width, int height)
    {
        Assert.NotNull(Fixtures.Ff);
        var ff = Fixtures.Ff;
        var source = Path.Combine(_root, "model.png");
        await ff.RunFfmpegCheckedAsync(["-v", "error", "-nostdin", "-y", "-f", "lavfi", "-i",
            $"color=red:s={width}x{height},drawbox=x=0:y=(ih-iw/5)/2:w=iw:h=iw/5:color=lime:t=fill",
            "-frames:v", "1", "-threads", "1", source], Ct);
        using var handler = new ImageHandler(await File.ReadAllBytesAsync(source, Ct));
        using var http = new HttpClient(handler);
        var generator = new FoundryImageGenerator(new Uri("https://images.example/openai/v1/"), "my-gpt-image",
            ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(new ApiKeyCredential("test-key"), "api-key"), http);
        var (render, plan) = Prepare(true);
        var post = new PostKit { Engage = "社内の新しい取り組み\n#共同作業" };
        var updated = await Runner(_ => generator).EngageImageAsync(render, plan, post, Ct);
        Assert.Equal(1, handler.Calls);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal("my-gpt-image", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("1536x1024", request.RootElement.GetProperty("size").GetString());
        var prompt = request.RootElement.GetProperty("prompt").GetString();
        Assert.StartsWith(post.Engage + "\n\n" + EngageImage.Scene, prompt);
        Assert.Contains("アニメ調", prompt);
        Assert.Equal("test-key", handler.Key);
        var imagePath = updated.Outputs["engage_image"];
        var probe = await ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=width,height", "-of", "json", imagePath], Ct);
        Assert.Equal(0, probe.ExitCode);
        using var info = JsonDocument.Parse(probe.StdOut);
        var stream = info.RootElement.GetProperty("streams")[0];
        Assert.Equal(1000, stream.GetProperty("width").GetInt32());
        Assert.Equal(200, stream.GetProperty("height").GetInt32());
        using var pixels = new MemoryStream();
        var decode = await ff.RunFfmpegAsync(["-v", "error", "-i", imagePath, "-vf", "crop=1:1:500:100",
            "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: pixels);
        Assert.Equal(0, decode.ExitCode);
        var rgb = pixels.ToArray();
        Assert.True(rgb[1] > 220 && rgb[0] < 30 && rgb[2] < 30, "The central banner is kept, not the red outer area");
        var provenance = VideoCreator.Json.Deserialize<EngageImageProvenance>(await File.ReadAllTextAsync(updated.Outputs["engage_image_prompt"], Ct));
        Assert.Equal(post.Engage, provenance.Post);
        Assert.Equal(prompt, provenance.Prompt);
        Assert.Equal("existing thumbnail", await File.ReadAllTextAsync(updated.Outputs["thumb1"], Ct));
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.Contains(".source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_image_generation_does_not_create_files_or_call_the_model()
    {
        var (render, plan) = Prepare(false);
        var files = Directory.GetFiles(_root).Order().ToArray();
        var updated = await Runner(_ => throw new InvalidOperationException("must not be called"))
            .EngageImageAsync(render, plan, new PostKit(), Ct);
        Assert.Same(render, updated);
        Assert.Equal(files, Directory.GetFiles(_root).Order().ToArray());
    }

    [Fact]
    public async Task Missing_model_and_invalid_image_fail_explicitly_without_overwriting_existing_outputs()
    {
        Assert.NotNull(Fixtures.Ff);
        var (render, plan) = Prepare(true);
        var error = await Assert.ThrowsAsync<PipelineException>(() => Runner(null).EngageImageAsync(render, plan, new PostKit { Engage = "投稿" }, Ct));
        Assert.Equal("E-IMAGE-CONFIG", error.Code);
        var path = Path.ChangeExtension(render.Path, ".engage.png");
        await File.WriteAllTextAsync(path, "previous image", Ct);
        using var handler = new ImageHandler([1, 2, 3]);
        using var http = new HttpClient(handler);
        var model = new FoundryImageGenerator(new Uri("https://images.example/openai/v1/"), "image",
            ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(new ApiKeyCredential("test"), "api-key"), http);
        error = await Assert.ThrowsAsync<PipelineException>(() => Runner(_ => model).EngageImageAsync(render, plan, new PostKit { Engage = "投稿" }, Ct));
        Assert.Equal("E-IMAGE-GENERATION", error.Code);
        Assert.Equal("previous image", await File.ReadAllTextAsync(path, Ct));
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.Contains(".source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Provider_rejection_is_not_reported_as_a_success_or_a_video_encoding_error()
    {
        using var handler = new ImageHandler([]) { Status = HttpStatusCode.BadRequest };
        using var http = new HttpClient(handler);
        var model = new FoundryImageGenerator(new Uri("https://images.example/openai/v1/"), "image",
            ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(new ApiKeyCredential("test"), "api-key"), http);
        var error = await Assert.ThrowsAsync<PipelineException>(() => model.GenerateAsync("prompt", Ct));
        Assert.Equal("E-IMAGE-GENERATION", error.Code);
        Assert.Contains("400", error.Message);
    }

    sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls;
        public string? Body, Key;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(ct);
            Key = request.Headers.GetValues("api-key").Single();
            Assert.EndsWith("/openai/v1/images/generations", request.RequestUri!.AbsoluteUri);
            return new(Status)
            {
                Content = Status == HttpStatusCode.OK
                    ? JsonContent.Create(new { created = 1, data = new[] { new { b64_json = Convert.ToBase64String(bytes) } } })
                    : JsonContent.Create(new { error = new { code = "content_policy_violation", message = "rejected" } }),
            };
        }
    }
}
