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
    public async Task Same_file_name_from_two_cameras_keeps_both_videos_and_a_re_upload_or_rejected_file_leaves_nothing_behind()
    {
        var ct = TestContext.Current.CancellationToken;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        async Task<string> Make(string color)
        {
            var file = Path.Combine(_root, color, "GX010001.MP4");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={color}:s=640x360:r=30:d=11", "-c:v", "libx264", "-preset", "ultrafast", file], ct);
            return file;
        }
        async Task<HttpResponseMessage> Upload(string file, string name)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(File.OpenRead(file)), "file", name);
            return await _http.PostAsync($"/api/projects/{_id}/assets/upload", form, ct);
        }
        var red = await Make("red"); var blue = await Make("blue");
        var redSha = await MediaProbe.Sha256Async(red, ct); var blueSha = await MediaProbe.Sha256Async(blue, ct);

        using (var a = await Upload(red, "GX010001.MP4")) Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        using (var b = await Upload(blue, "GX010001.MP4")) Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        using (var again = await Upload(red, "GX010001.MP4")) Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var assets = _app.Services.GetRequiredService<Runtime>().Store.Assets(_id);
        Assert.Equal(2, assets.Count);
        foreach (var asset in assets) Assert.Equal(asset.Sha256, await MediaProbe.Sha256Async(asset.Path, ct));
        Assert.Equal([redSha, blueSha], assets.Select(a => a.Sha256));
        var folder = Path.GetDirectoryName(assets[0].Path)!;
        Assert.Equal(2, Directory.GetFiles(folder).Length);

        var text = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(text, "not a video", ct);
        using (var bad = await Upload(text, "notes.txt")) Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public async Task Each_video_keeps_its_own_slides_and_a_failed_replaced_or_removed_deck_leaves_the_right_files()
    {
        var ct = TestContext.Current.CancellationToken;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        async Task<string> Register(string color)
        {
            var file = Path.Combine(_root, "cam-" + color, "GX010001.MP4");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={color}:s=640x360:r=30:d=11", "-c:v", "libx264", "-preset", "ultrafast", file], ct);
            using var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/assets", new { path = file }, ct);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        }
        async Task<string> Zip(string name, params string[] colors)
        {
            var dir = Path.Combine(_root, "zip-" + name);
            Directory.CreateDirectory(dir);
            for (int i = 0; i < colors.Length; i++)
                await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", $"color=c={colors[i]}:s=1280x720", "-frames:v", "1", Path.Combine(dir, $"Slide{i + 1}.PNG")], ct);
            var zip = Path.Combine(_root, name + ".zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip);
            return zip;
        }
        async Task<HttpResponseMessage> AddDeck(string asset, string zip)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StreamContent(File.OpenRead(zip)), "file", "slides.zip");
            return await _http.PostAsync($"/api/assets/{asset}/deck", form, ct);
        }
        async Task<(string Deck, string Slides)> Added(HttpResponseMessage r)
        {
            var body = await r.Content.ReadAsStringAsync(ct);
            Assert.True(r.StatusCode == HttpStatusCode.OK, body);
            var deck = JsonDocument.Parse(body).RootElement.GetProperty("deck");
            var slide = deck.GetProperty("slides")[0].GetProperty("path").GetString()!;
            Assert.True(File.Exists(slide));
            return (deck.GetProperty("path").GetString()!, Path.GetDirectoryName(slide)!);
        }

        var a = await Register("red"); var b = await Register("blue");
        (string Deck, string Slides) deckA, deckB;
        using (var r = await AddDeck(a, await Zip("a", "yellow", "green"))) deckA = await Added(r);
        using (var r = await AddDeck(b, await Zip("b", "white"))) deckB = await Added(r);
        // same file name, different videos: neither deck replaces the other's files
        Assert.NotEqual(deckA.Deck, deckB.Deck);
        Assert.NotEqual(deckA.Slides, deckB.Slides);
        Assert.Equal(2, Directory.GetFiles(deckA.Slides, "slide-*.png").Length);
        Assert.Single(Directory.GetFiles(deckB.Slides, "slide-*.png"));

        // a replacement that cannot be rendered leaves the current slides as they are
        var empty = Path.Combine(_root, "empty.zip");
        using (var z = System.IO.Compression.ZipFile.Open(empty, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("notes.txt").Open())) w.Write("no slides");
        using (var r = await AddDeck(a, empty)) Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.True(File.Exists(deckA.Deck));
        Assert.Equal(2, Directory.GetFiles(deckA.Slides, "slide-*.png").Length);

        // a replacement removes the files of the deck it replaces, and only those
        (string Deck, string Slides) next;
        using (var r = await AddDeck(a, await Zip("a2", "gray"))) next = await Added(r);
        Assert.False(File.Exists(deckA.Deck));
        Assert.False(Directory.Exists(deckA.Slides));
        Assert.True(File.Exists(deckB.Deck));

        using (var r = await _http.DeleteAsync($"/api/assets/{b}/deck", ct)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(File.Exists(deckB.Deck));
        Assert.False(Directory.Exists(deckB.Slides));
        Assert.True(File.Exists(next.Deck));

        // slides an already generated edit plan shows stay, so that video still renders; the deck file itself goes
        var store = _app.Services.GetRequiredService<Runtime>().Store;
        var slide = Directory.GetFiles(next.Slides, "slide-*.png")[0];
        store.Put(Kinds.EditPlan, "ep_deck", _id, new EditPlan
        {
            EditplanId = "ep_deck", ProjectId = _id, Preset = Fixtures.Presets["P4"].Ref, Audio = new AudioSpec { Master = a },
            Timeline = [new Clip { ClipId = "c1", AssetId = a, SrcIn = 0, SrcOut = 5, Role = "body", Reframe = new ReframeSpec { Mode = "screen", Image = slide } }],
        });

        // while the project generates or exports, its decks stay as they are (a running job holds them in memory, not yet in a stored plan)
        var queue = _app.Services.GetRequiredService<JobQueue>();
        queue.Exports["r_busy"] = new ExportStatus("r_busy", "ep_deck", "rendering");
        using (var r = await AddDeck(a, await Zip("busy", "black"))) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await _http.DeleteAsync($"/api/assets/{a}/deck", ct)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.True(File.Exists(next.Deck));
        queue.Exports.TryRemove("r_busy", out _);

        using (var r = await _http.DeleteAsync($"/api/assets/{a}/deck", ct)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(File.Exists(next.Deck));
        Assert.True(File.Exists(slide));
        var assets = Path.GetDirectoryName(next.Deck)!;
        Assert.Empty(Directory.GetFiles(assets));
        Assert.Equal([Path.GetFileName(next.Slides)], Directory.GetDirectories(assets).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_zip_whose_image_inflates_beyond_the_limit_is_refused_without_writing_it_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        var video = Path.Combine(_root, "screen.mp4");
        await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=640x360:r=30:d=11", "-c:v", "libx264", "-preset", "ultrafast", video], ct);
        string asset;
        using (var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/assets", new { path = video }, ct))
            asset = (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        // ~65 KB on disk, 65 MiB when inflated
        var bomb = Path.Combine(_root, "bomb.zip");
        using (var z = System.IO.Compression.ZipFile.Open(bomb, System.IO.Compression.ZipArchiveMode.Create))
        await using (var s = z.CreateEntry("Slide1.png", System.IO.Compression.CompressionLevel.SmallestSize).Open())
        {
            var zeros = new byte[1 << 20];
            for (int i = 0; i < 65; i++) await s.WriteAsync(zeros, ct);
        }
        Assert.True(new FileInfo(bomb).Length < 1 << 20);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(File.OpenRead(bomb)), "file", "slides.zip");
        using var response = await _http.PostAsync($"/api/assets/{asset}/deck", form, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("error");
        Assert.Equal("E-DECK", error.GetProperty("code").GetString());
        Assert.Contains("大きすぎます", error.GetProperty("message").GetString());
        var projectDir = (await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", ct)).GetProperty("project").GetProperty("directory").GetString()!;
        var assets = Path.Combine(projectDir, "assets");
        Assert.True(!Directory.Exists(assets) || Directory.GetFileSystemEntries(assets).Length == 0, string.Join(", ", Directory.GetFileSystemEntries(assets)));
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
        Assert.Equal(AssetRole.Screen, oldMain.Role);   // "main" is exclusive: the previous holder takes the role the picked asset gave up
        Assert.Equal(AssetRole.Main, newMain.Role);
        Assert.Null(oldMain.SyncMethod);
        Assert.Null(newMain.SyncConfidence);
        Assert.Equal(0, oldMain.SyncOffset);
        Assert.Single(newMain.PipScenes);

        // a role nobody else holds changes only the picked asset; screens may be shared
        using (var free = await _http.PatchAsJsonAsync("/api/assets/screen", new { role = "cam_a" }, ct)) Assert.Equal(HttpStatusCode.OK, free.StatusCode);
        Assert.Equal(AssetRole.Screen, rt.Store.Get<SourceAsset>(Kinds.Asset, "main")!.Role);
        using (var shared = await _http.PatchAsJsonAsync("/api/assets/screen", new { role = "screen" }, ct)) Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal((AssetRole.Screen, AssetRole.Screen), (rt.Store.Get<SourceAsset>(Kinds.Asset, "main")!.Role, rt.Store.Get<SourceAsset>(Kinds.Asset, "screen")!.Role));
        // editing an asset never moves it in the project's list
        Assert.Equal(["main", "screen"], rt.Store.Assets(_id).Select(a => a.Id));
        rt.Store.Put(Kinds.Asset, "main", _id, oldMain with { Role = AssetRole.CamB });
        rt.Store.Put(Kinds.Asset, "screen", _id, newMain);
        oldMain = rt.Store.Get<SourceAsset>(Kinds.Asset, "main")!;

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
