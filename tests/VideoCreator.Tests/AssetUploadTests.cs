using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoCreator.App;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Storage;

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
        _http.Timeout = TimeSpan.FromMinutes(10);   // a 128 MiB upload is slow while the rest of the suite is hammering the disk and the thread pool
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

    [Fact]
    public async Task Pip_scenes_can_be_reviewed_saved_and_reject_invalid_ranges_and_main_changes_reset_alignment()
    {
        var ct = TestContext.Current.CancellationToken;
        var rt = _app.Services.GetRequiredService<Runtime>();
        var file = Path.Combine(_root, "scenes.mp4");
        await rt.Ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=640x360:r=30:d=12",
            "-vf", "drawbox=x=0:y=0:w=iw:h=ih:c=blue:t=fill:enable='gte(t,6)'", "-c:v", "libx264", "-preset", "ultrafast", file], ct);
        SourceAsset Asset(string id, AssetRole role) => new()
        {
            Id = id, ProjectId = _id, Path = file, Sha256 = id, Role = role, Media = Fixtures.Landscape(12),
            SyncOffset = 5, SyncMethod = "manual", SyncConfidence = 9999,
        };
        rt.Store.Put(Kinds.Asset, "main", _id, Asset("main", AssetRole.Main));
        rt.Store.Put(Kinds.Asset, "screen", _id, Asset("screen", AssetRole.Screen));
        using var scan = await _http.PostAsync("/api/assets/screen/pip-scenes", null, ct);
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var scenes = (await scan.Content.ReadFromJsonAsync<List<Interval>>(VideoCreator.Json.Options, ct))!;
        Assert.Equal(2, scenes.Count);
        Assert.InRange(scenes[0].T1, 5.5, 6.5);
        using var frame = await _http.GetAsync("/api/assets/screen/frame?t=8", ct);
        Assert.Equal("image/jpeg", frame.Content.Headers.ContentType!.MediaType);
        Assert.True((await frame.Content.ReadAsByteArrayAsync(ct)).Length > 100);
        foreach (var ranges in new object[] { new[] { new { t0 = 5, t1 = 13 } }, new[] { new { t0 = 5, t1 = 9 }, new { t0 = 8, t1 = 10 } }, new object?[] { null } })
        {
            using var invalid = await _http.PatchAsJsonAsync("/api/assets/screen", new { pip_scenes = ranges }, ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        }
        using var save = await _http.PatchAsJsonAsync("/api/assets/screen", new { pip_scenes = new[] { new { t0 = 6.5, t1 = 11 } } }, ct);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(new Interval(6.5, 11), Assert.Single(rt.Store.Get<SourceAsset>(Kinds.Asset, "screen")!.PipScenes));
        using var change = await _http.PatchAsJsonAsync("/api/assets/screen", new { role = "main" }, ct);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var oldMain = rt.Store.Get<SourceAsset>(Kinds.Asset, "main")!;
        var newMain = rt.Store.Get<SourceAsset>(Kinds.Asset, "screen")!;
        Assert.Equal(AssetRole.CamB, oldMain.Role);
        Assert.Equal(AssetRole.Main, newMain.Role);
        Assert.Null(oldMain.SyncMethod);
        Assert.Null(newMain.SyncConfidence);
        Assert.Equal(0, oldMain.SyncOffset);
        Assert.Single(newMain.PipScenes);

        // the audio source is a separate mark: moving it re-bases the sync but leaves every picture role alone
        rt.Store.Put(Kinds.Asset, "main", _id, oldMain with { SyncOffset = 5, SyncMethod = "manual", SyncConfidence = 9999 });
        using var mark = await _http.PatchAsJsonAsync("/api/assets/main", new { audio_ref = true }, ct);
        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        oldMain = rt.Store.Get<SourceAsset>(Kinds.Asset, "main")!;
        newMain = rt.Store.Get<SourceAsset>(Kinds.Asset, "screen")!;
        Assert.True(oldMain.AudioRef);
        Assert.False(newMain.AudioRef);
        Assert.Equal((AssetRole.CamB, AssetRole.Main), (oldMain.Role, newMain.Role));
        Assert.Equal("main", AssetRoles.Master(rt.Store.List<SourceAsset>(Kinds.Asset, _id))!.Id);
        Assert.Null(oldMain.SyncMethod);
    }
}
