using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoCreator.App;
using VideoCreator.Media;

namespace VideoCreator.Tests;

public sealed class AssetUploadTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-upload-" + Guid.NewGuid().ToString("N"));
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    string _id = "";

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
        using var response = await _http.PostAsJsonAsync("/api/projects", new { name = "upload" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        _id = project.GetProperty("id").GetString()!;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        var store = _app.Services.GetRequiredService<Runtime>().Store;
        await _app.DisposeAsync();
        store.Dispose();
        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData(1024 * 1024)]
    [InlineData(128 * 1024 * 1024 - 1)]
    [InlineData(128 * 1024 * 1024)]
    [InlineData(128 * 1024 * 1024 + 1)]
    public async Task Upload_preserves_video_bytes_across_the_default_multipart_limit(long length)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Path.Combine(_root, "source.mp4");
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", "color=s=360x360:r=30:d=11",
            "-c:v", "libx264", "-preset", "ultrafast", source], ct);
        // Trailing padding leaves the MP4 valid while exercising exact multipart file sizes.
        await using (var padding = new FileStream(source, FileMode.Open, FileAccess.Write)) padding.SetLength(length);
        var sha = await MediaProbe.Sha256Async(source, ct);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(File.OpenRead(source)), "file", "source.MP4");

        using var response = await _http.PostAsync($"/api/projects/{_id}/assets/upload", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var doc = JsonDocument.Parse(body);
        var asset = doc.RootElement;
        var saved = asset.GetProperty("path").GetString()!;
        Assert.Equal("source.mp4", Path.GetFileName(saved));
        Assert.Equal(length, new FileInfo(saved).Length);
        Assert.Equal(sha, asset.GetProperty("sha256").GetString());
        Assert.Equal(sha, await MediaProbe.Sha256Async(saved, ct));
        var project = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", ct);
        Assert.Single(project.GetProperty("assets").EnumerateArray());
        Assert.Equal(FormOptions.DefaultMultipartBodyLengthLimit,
            _app.Services.GetRequiredService<IOptions<FormOptions>>().Value.MultipartBodyLengthLimit);
    }

    [Fact]
    public async Task Missing_file_remains_a_validation_error()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("not a file"), "note");
        using var response = await _http.PostAsync($"/api/projects/{_id}/assets/upload", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("E-VALIDATION", body.GetProperty("error").GetProperty("code").GetString());
    }
}
