using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoCreator.App;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class SceneCardTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-cards-" + Guid.NewGuid().ToString("N"));
    readonly FakePicker _picker = new();
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    string _id = "";

    sealed class FakePicker : IFolderPicker
    {
        public string? Result, Initial;
        public Exception? Error;
        public Task<string?> PickAsync(string? initialPath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Initial = initialPath;
            return Error == null ? Task.FromResult(Result) : Task.FromException<string?>(Error);
        }
    }

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
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IFolderPicker>(); s.AddSingleton<IFolderPicker>(_picker);
                s.RemoveAll<RemoveFolder>(); s.AddSingleton<RemoveFolder>(ProjectDeletion.DeletePermanently);
            });
        });
        _app.UseKestrel();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        using var r = await _http.PostAsJsonAsync("/api/projects", new { name = "cards" }, Ct);
        r.EnsureSuccessStatusCode();
        _id = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    public async ValueTask DisposeAsync()
    {
        var store = Rt.Store;
        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        Directory.Delete(_root, true);
    }

    async Task<SourceAsset> SourceAsync(string name = "source.mp4", bool black = false)
    {
        var path = Path.Combine(_root, name);
        await Rt.Ff.RunFfmpegCheckedAsync(["-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i",
            black ? "color=c=black:s=640x360:r=10:d=12" : "testsrc2=s=640x360:r=10:d=12",
            "-vf", black ? "null" : "hue=h=30*t,drawbox=color=black:t=fill:enable='lt(t,2)'",
            "-c:v", "libx264", "-preset", "ultrafast", path], Ct);
        var a = new SourceAsset { Id = name, ProjectId = _id, Path = path, Sha256 = await MediaProbe.Sha256Async(path, Ct), Media = await new MediaProbe(Rt.Ff).ProbeAsync(path, Ct) };
        Rt.Store.Put(Kinds.Asset, a.Id, _id, a);
        return a;
    }

    void Confirm(SourceAsset a) => Rt.Store.Put(Kinds.Rights, "rights-" + a.Id, _id,
        new RightsConfirmation { Id = "rights-" + a.Id, ProjectId = _id, AssetId = a.Id });

    [Fact]
    public async Task Folder_dialog_returns_selected_path_or_null_and_errors_are_explicit()
    {
        const string initial = "C:\\素材\\OpeningClosing";
        using var cancel = await _http.PostAsJsonAsync("/api/dialogs/folder", new { initial_path = initial }, Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await cancel.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("path").ValueKind);
        Assert.Equal(initial, _picker.Initial);
        _picker.Result = _root;
        using var select = await _http.PostAsJsonAsync("/api/dialogs/folder", new { initial_path = initial }, Ct);
        Assert.Equal(_root, (await select.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("path").GetString());
        _picker.Error = new IOException("test failure");
        using var fail = await _http.PostAsJsonAsync("/api/dialogs/folder", new { initial_path = initial }, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, fail.StatusCode);
        Assert.Equal("E-FOLDER-PICKER", (await fail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetProperty("code").GetString());
        _http.DefaultRequestHeaders.Remove("X-VideoCreator");
        using var csrf = await _http.PostAsJsonAsync("/api/dialogs/folder", new { initial_path = initial }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, csrf.StatusCode);
    }

    [Fact]
    public async Task Generated_images_persist_are_used_in_cards_and_render_with_source_rights()
    {
        var source = await SourceAsync();
        Confirm(source);
        using var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = source.Id, count = 4 }, Ct);
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == HttpStatusCode.Created, body);
        Assert.Equal($"/api/projects/{_id}", r.Headers.Location?.OriginalString);
        var project = Rt.Store.Get<Project>(Kinds.Project, _id)!;
        var images = project.SceneCardImages;
        Assert.Equal(4, images.Count);
        Assert.All(images, i => { Assert.Equal(source.Id, i.AssetId); Assert.InRange(i.SourceT, 2, 12); Assert.Equal((640, 360), (i.Width, i.Height)); Assert.True(File.Exists(i.Path)); });
        Assert.True(images.Select(i => i.SourceT).SequenceEqual(images.Select(i => i.SourceT).OrderBy(t => t)));
        Assert.True(images[^1].SourceT - images[0].SourceT > 6);
        var hashes = new List<string>();
        foreach (var i in images) hashes.Add(await MediaProbe.Sha256Async(i.Path, Ct));
        Assert.Equal(images.Count, hashes.Distinct().Count());
        using var preview = await _http.GetAsync($"/api/projects/{_id}/scene-cards/0", Ct);
        Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(images[0].Path, Ct), await preview.Content.ReadAsByteArrayAsync(Ct));
        var persisted = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", Ct);
        Assert.Equal(4, persisted.GetProperty("project").GetProperty("scene_card_images").GetArrayLength());
        using var again = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = source.Id, count = 4 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single(Directory.GetDirectories(Path.Combine(project.Directory, "scene-cards")));

        var runner = Rt.CreateRunner();
        var kit = runner.BrandKitFor(_id)!;
        Assert.Equal(4, kit.Files.Count);
        var clips = new List<Clip>
        {
            new() { ClipId = "a", AssetId = source.Id, SrcIn = 2, SrcOut = 5 },
            new() { ClipId = "b", AssetId = source.Id, SrcIn = 9, SrcOut = 12 },
        };
        var canvas = new Canvas(640, 360, 30);
        var (packed, inserts) = InsertPlanner.Plan(clips, new JobOptions { SceneCards = true }, kit, canvas, -14, null, []);
        var card = Assert.Single(inserts);
        Assert.Equal(images[0].Path, card.Path);
        Assert.Equal(source.Id, card.ImageAssetId);
        Assert.Equal(InsertPlanner.FixedPhrases[0], card.Text);   // pause cards always carry text over the image
        var preset = Fixtures.Presets["P9"];
        // Clear the text so only the image itself is compared.
        var plan = new EditPlan { EditplanId = "ep", ProjectId = _id, Preset = preset.Ref, Canvas = canvas,
            Timeline = packed, Inserts = [card with { Text = "" }], Audio = new AudioSpec { Master = source.Id, SfxEnabled = false } };
        var transcript = new Transcript { Provider = "none", Duration = 12 };
        var qa = QaEngine.PlanChecks(new(plan, preset, transcript, [source.Id], []));
        Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "H-09").Status);
        Assert.Equal(QaStatus.Fail, QaEngine.PlanChecks(new(plan, preset, transcript, [], [])).Single(q => q.CheckId == "H-09").Status);
        // Even when the card came from a different camera, revoking that camera's rights blocks export.
        var other = plan with { Timeline = packed.Select(c => c with { AssetId = "other" }).ToList() };
        Assert.Equal(QaStatus.Fail, QaEngine.PlanChecks(new(other, preset, transcript, ["other"], [])).Single(q => q.CheckId == "H-09").Status);

        var render = await new Renderer(Rt.Ff).RenderAsync(plan, preset, new Dictionary<string, SourceAsset> { [source.Id] = source }, Path.Combine(_root, "render.mp4"), true, Ct);
        double cardT = InsertMap.StartOf(InsertMap.Active(plan), plan.Inserts[0]) + 0.5;
        var expected = await PixelsAsync(images[0].Path, 0);
        // A same-aspect image card covers the whole screen (no partial overlay).
        Assert.True(Renderer.CardCovers(plan.Inserts[0], canvas.Width, canvas.Height));
        var actual = await PixelsAsync(render.Path, cardT);
        Assert.Equal(expected.Length, actual.Length);
        double error = expected.Zip(actual).Average(p => Math.Abs(p.First - p.Second));
        Assert.True(error < 12, $"Rendered card must match the extracted source image: MAE={error}");
        Rt.Store.Delete([(Kinds.Rights, "rights-" + source.Id)]);
        Assert.Null(runner.BrandKitFor(_id));
        using var delete = await _http.DeleteAsync($"/api/projects/{_id}", Ct);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.False(Directory.Exists(project.Directory));
        Assert.True(File.Exists(source.Path));
        Assert.Null(Rt.Store.Get<Project>(Kinds.Project, _id));
    }

    async Task<byte[]> PixelsAsync(string path, double t, string filter = "")
    {
        using var buffer = new MemoryStream();
        var r = await Rt.Ff.RunFfmpegAsync(["-v", "error", "-ss", t.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", path,
            "-frames:v", "1", "-vf", filter + "scale=32:18:flags=area", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], Ct, stdout: buffer);
        Assert.Equal(0, r.ExitCode);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Invalid_input_rights_and_black_video_do_not_leave_partial_images()
    {
        var source = await SourceAsync(black: true);
        using var rights = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = source.Id }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rights.StatusCode);
        Assert.Equal("E-RIGHTS-REQUIRED", (await rights.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetProperty("code").GetString());
        Confirm(source);
        using var count = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = source.Id, count = 7 }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, count.StatusCode);
        using var missing = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = "not-in-project" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var empty = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = "" }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        using var black = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = source.Id, count = 2 }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, black.StatusCode);
        var p = Rt.Store.Get<Project>(Kinds.Project, _id)!;
        Assert.Empty(p.SceneCardImages);
        Assert.Empty(Directory.GetDirectories(Path.Combine(p.Directory, "scene-cards")));
    }

    [Fact]
    public async Task Registered_cards_take_precedence_and_prevent_generation()
    {
        var source = await SourceAsync();
        Confirm(source);
        var kit = new BrandKit { RightsConfirmed = true, Files = [new() { Path = "registered.png", Kind = "card", Width = 640, Height = 360 }] };
        Rt.Store.Put(Kinds.BrandKit, "default", null, kit);
        using var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = source.Id }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("registered.png", Assert.Single(Rt.CreateRunner().BrandKitFor(_id)!.Files).Path);
        Assert.Empty(Rt.Store.Get<Project>(Kinds.Project, _id)!.SceneCardImages);
    }

    [Fact]
    public async Task Opening_video_is_a_source_and_make_choice_controls_use_without_deleting()
    {
        var kitDir = Path.Combine(_root, "kit");
        Directory.CreateDirectory(kitDir);
        var opening = Path.Combine(kitDir, "Opening.mp4");
        // Fades from/to black like a typical title sequence: the black ends must not become cards.
        await Rt.Ff.RunFfmpegCheckedAsync(["-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=s=360x640:r=10:d=4",
            "-vf", "hue=h=60*t,fade=in:d=1,fade=out:st=3:d=1", "-c:v", "libx264", "-preset", "ultrafast", opening], Ct);
        using (var reg = await _http.PutAsJsonAsync("/api/brandkit", new { paths = new[] { kitDir }, rights_confirmed = true, upscale = false }, Ct))
            Assert.True(reg.IsSuccessStatusCode, await reg.Content.ReadAsStringAsync(Ct));
        var project = Rt.Store.Get<Project>(Kinds.Project, _id)!;
        Assert.Null(project.MakeSceneCardImages);
        Assert.False(project.UsesSceneCardImages());

        var stranger = Path.Combine(_root, "Opening-not-registered.mp4");
        File.Copy(opening, stranger);
        using (var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { brand_path = stranger, count = 3 }, Ct))
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        using (var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { asset_id = "x", brand_path = opening }, Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(project.Directory, "scene-cards")) && Directory.EnumerateFileSystemEntries(Path.Combine(project.Directory, "scene-cards")).Any());

        using (var r = await _http.PostAsJsonAsync($"/api/projects/{_id}/scene-cards", new { brand_path = opening.ToUpperInvariant(), count = 3 }, Ct))
            Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync(Ct));
        project = Rt.Store.Get<Project>(Kinds.Project, _id)!;
        Assert.True(project.MakeSceneCardImages);
        var images = project.SceneCardImages;
        Assert.InRange(images.Count, 2, 3);
        Assert.All(images, i =>
        {
            Assert.Null(i.AssetId);
            Assert.Equal(opening, i.BrandPath);
            Assert.Equal((360, 640), (i.Width, i.Height));
            Assert.InRange(i.SourceT, 0.5, 3.5);
            Assert.StartsWith(project.Directory, i.Path);
        });
        foreach (var i in images)
        {
            var px = await PixelsAsync(i.Path, 0);
            Assert.True(px.Average(b => (double)b) > 30, $"{i.Path} must not be a fade-to-black frame");
        }
        using (var preview = await _http.GetAsync($"/api/projects/{_id}/scene-cards/0", Ct))
            Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);

        var runner = Rt.CreateRunner();
        string[] Cards() => runner.BrandKitFor(_id)?.Files.Where(f => f.Kind == "card").Select(f => f.Path).ToArray() ?? [];
        Assert.Equal(images.Select(i => i.Path), Cards());

        using (var off = await _http.PutAsJsonAsync($"/api/projects/{_id}/scene-cards/settings", new { make = false }, Ct))
            Assert.False((await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("make_scene_card_images").GetBoolean());
        Assert.Empty(Cards());
        Assert.Contains(runner.BrandKitFor(_id)!.Files, f => f.Kind == "opening");
        Assert.Equal(images.Count, Rt.Store.Get<Project>(Kinds.Project, _id)!.SceneCardImages.Count);
        Assert.All(images, i => Assert.True(File.Exists(i.Path)));

        using (var on = await _http.PutAsJsonAsync($"/api/projects/{_id}/scene-cards/settings", new { make = true }, Ct))
            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.Equal(images.Select(i => i.Path), Cards());
        using (var missing = await _http.PutAsJsonAsync("/api/projects/p_missing/scene-cards/settings", new { make = true }, Ct))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // The rights basis is the kit registration: unregistering the opening stops its images being used.
        using (var clear = await _http.DeleteAsync("/api/brandkit", Ct)) clear.EnsureSuccessStatusCode();
        Assert.Empty(Cards());
        Assert.True(File.Exists(opening));
    }

    [Fact]
    public void Older_projects_with_images_keep_using_them_until_the_user_chooses()
    {
        var img = new SceneCardImage("a.png", 1, 1, "asset", 1);
        Assert.True(new Project { Id = "p", Name = "n", Directory = "d", SceneCardImages = [img] }.UsesSceneCardImages());
        Assert.False(new Project { Id = "p", Name = "n", Directory = "d" }.UsesSceneCardImages());
        Assert.False(new Project { Id = "p", Name = "n", Directory = "d", SceneCardImages = [img], MakeSceneCardImages = false }.UsesSceneCardImages());
    }

    [Fact]
    public async Task Static_video_returns_one_distinct_image_instead_of_duplicates()
    {
        var path = Path.Combine(_root, "still.mp4");
        await Rt.Ff.RunFfmpegCheckedAsync(["-v", "error", "-f", "lavfi", "-i", "color=c=green:s=360x640:r=10:d=2",
            "-c:v", "libx264", "-preset", "ultrafast", path], Ct);
        var source = new SourceAsset { Id = "still", ProjectId = _id, Path = path, Sha256 = "hash", Media = await new MediaProbe(Rt.Ff).ProbeAsync(path, Ct) };
        var images = await new SceneCardExtractor(Rt.Ff).ExtractAsync(source, Path.Combine(_root, "static"), 4, Ct);
        Assert.Single(images);
        Assert.Equal((360, 640), (images[0].Width, images[0].Height));
    }
}
