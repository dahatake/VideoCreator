using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>Changing the projects folder: everything moved is still found (files on disk and every stored path), or nothing moves at all.</summary>
public sealed class ProjectsLocationTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-loc-" + Guid.NewGuid().ToString("N")[..8]);
    string OldRoot => Path.Combine(_root, "old projects");
    string NewRoot => Path.Combine(_root, "新しい保存先");
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    ProjectsLocation Loc => _app.Services.GetRequiredService<ProjectsLocation>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    Project _p = null!;
    string _outside = "";

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
            b.UseSetting("VideoCreator:ProjectsDir", OldRoot);
            b.UseSetting("VideoCreator:OpenBrowser", "false");
            b.UseSetting("VideoCreator:Port", "0");
            b.UseSetting("VideoCreator:Asr:Provider", "none");
            b.UseSetting("VideoCreator:Llm:Provider", "none");
            b.ConfigureServices(s => { s.RemoveAll<RemoveFolder>(); s.AddSingleton<RemoveFolder>(ProjectDeletion.DeletePermanently); });
        });
        _app.UseKestrel();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        using var r = await _http.PostAsJsonAsync("/api/projects", new { name = "移動テスト" }, Ct);
        r.EnsureSuccessStatusCode();
        _p = Rt.Store.Get<Project>(Kinds.Project, (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!)!;
        Assert.StartsWith(OldRoot + "\\", _p.Directory);

        // a realistic project tree: upload, user BGM, extracted card image, preview + export, an edit plan using the image
        var d = _p.Directory;
        string Put(string rel, int size) { var f = Path.Combine(d, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllBytes(f, Enumerable.Range(0, size).Select(i => (byte)(i * 7)).ToArray()); return f; }
        var upload = Put(@"assets\clip.mp4", 40_000);
        var bgm = Put(@"bgm\song.mp3", 3_000);
        var card = Put(@"scene-cards\abc\highlight-01.png", 2_000);
        var preview = Put(@"previews\r_prev.mp4", 9_000);
        var final = Put(@"exports\移動テスト_P4_1.mp4", 12_000);
        var srt = Put(@"exports\移動テスト_P4_1.srt", 100);
        _outside = Path.Combine(_root, "outside.mp4"); File.WriteAllBytes(_outside, [1, 2, 3]);
        var media = new MediaInfo { Duration = 10, Width = 640, Height = 360 };
        Rt.Store.Put(Kinds.Asset, "a_up", _p.Id, new SourceAsset { Id = "a_up", ProjectId = _p.Id, Path = upload, Sha256 = new string('1', 64), Media = media });
        Rt.Store.Put(Kinds.Asset, "a_out", _p.Id, new SourceAsset { Id = "a_out", ProjectId = _p.Id, Path = _outside, Sha256 = new string('2', 64), Media = media });
        Rt.Store.Put(Kinds.Bgm, "bgm_u", _p.Id, new BgmTrack { Id = "bgm_u", Name = "song", Path = bgm, Source = "user", ProjectId = _p.Id, RightsConfirmed = true });
        Rt.Store.Put(Kinds.Project, _p.Id, null, _p = _p with { SceneCardImages = [new SceneCardImage(card, 640, 360, "a_up", 1)] });
        var plan = new EditPlan
        {
            EditplanId = "ep1", ProjectId = _p.Id, Preset = Fixtures.Presets["P4"].Ref, Timeline = [], Audio = new AudioSpec { Master = "a_up" },
            Inserts = [new Insert { Id = "card1", Kind = "card", Path = card, ImageAssetId = "a_up" }],
        };
        Rt.Store.Put(Kinds.EditPlan, plan.EditplanId, _p.Id, plan);
        Rt.Store.Put(Kinds.Render, "r_prev", "ep1", new Render { Id = "r_prev", EditplanId = "ep1", Kind = "preview", Path = preview, Commands = [$"ffmpeg -i \"{upload}\" \"{preview}\""] });
        await Task.Delay(20, Ct);
        Rt.Store.Put(Kinds.Render, "r_final", "ep1", new Render { Id = "r_final", EditplanId = "ep1", Kind = "final", Path = final, Outputs = new() { ["mp4"] = final, ["srt"] = srt } });
        // something of the user's own in the projects folder, and a registered opening kept there
        File.WriteAllText(Path.Combine(OldRoot, "メモ.txt"), "user note");
        Directory.CreateDirectory(Path.Combine(OldRoot, "OpeningClosing"));
        var opening = Path.Combine(OldRoot, "OpeningClosing", "Opening.mp4"); File.WriteAllBytes(opening, [9, 9, 9]);
        Rt.Store.Put(Kinds.BrandKit, "default", null, new BrandKit { Sources = [Path.Combine(OldRoot, "OpeningClosing")], Files = [new BrandFile { Path = opening, Kind = "opening" }], RightsConfirmed = true });
    }

    public async ValueTask DisposeAsync()
    {
        var store = Rt.Store;
        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    async Task<JsonElement> PlanAsync(string path, bool move)
    {
        using var r = await _http.PostAsJsonAsync("/api/settings/projects-dir/plan", new { path, move }, Ct);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    Task<HttpResponseMessage> ChangeAsync(string path, bool move) => _http.PutAsJsonAsync("/api/settings/projects-dir", new { path, move }, Ct);

    /// <summary>No stored string still points at (or under) the old folder — except recorded command lines, which are history.</summary>
    void AssertNoStalePaths(string old)
    {
        foreach (var (kind, id, json) in Rt.Store.RawAll().Where(x => x.Kind != Kinds.Setting))   // the setting remembers earlier folders on purpose
        {
            var doc = JsonDocument.Parse(json);
            foreach (var s in Strings(doc.RootElement).Where(s => !s.StartsWith("ffmpeg ")))
                Assert.False(s.Equals(old, StringComparison.OrdinalIgnoreCase) || s.StartsWith(old + "\\", StringComparison.OrdinalIgnoreCase), $"{kind}/{id} still has {s}");
        }
    }

    static IEnumerable<string> Strings(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Strings(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Strings),
        _ => [],
    };

    async Task AssertServedAsync()
    {
        var p = Rt.Store.Get<Project>(Kinds.Project, _p.Id)!;
        using (var r = await _http.GetAsync("/api/media/r_prev", Ct)) { Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Equal(9_000, (await r.Content.ReadAsByteArrayAsync(Ct)).Length); }
        // Legacy unbound exports remain on disk after a folder move, but cannot certify the current plan.
        using (var r = await _http.GetAsync("/api/renders/r_final/files/srt", Ct)) Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await _http.GetAsync($"/api/projects/{p.Id}/scene-cards/0", Ct)) { Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Equal(2_000, (await r.Content.ReadAsByteArrayAsync(Ct)).Length); }
        Assert.True(File.Exists(Rt.Store.Get<SourceAsset>(Kinds.Asset, "a_up")!.Path));
        Assert.True(File.Exists(Rt.Store.Get<BgmTrack>(Kinds.Bgm, "bgm_u")!.Path));
        Assert.True(File.Exists(Rt.Store.Get<EditPlan>(Kinds.EditPlan, "ep1")!.Inserts[0].Path));
        Assert.True(File.Exists(Rt.Store.Get<BrandKit>(Kinds.BrandKit, "default")!.Files[0].Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // the other-drive path: copy → verify → switch → remove the original
    public async Task Move_all_carries_every_item_and_every_stored_path(bool copy)
    {
        Loc.ForceCopy = copy;
        var order = Rt.Store.List<Render>(Kinds.Render, "ep1").Select(r => r.Id).ToList();
        var plan = await PlanAsync(NewRoot, true);
        Assert.Empty(plan.GetProperty("problems").EnumerateArray());
        var items = plan.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(_p.Id, Assert.Single(items, i => i.TryGetProperty("project_id", out _)).GetProperty("project_id").GetString());
        Assert.Contains(items, i => i.GetProperty("name").GetString() == "メモ.txt");
        Assert.Equal(Path.Combine(OldRoot), plan.GetProperty("from").GetString());
        Assert.True(Directory.Exists(_p.Directory), "planning must not move anything");

        using var r = await ChangeAsync(NewRoot, true);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(r.StatusCode == HttpStatusCode.OK, body.ToString());
        Assert.Equal(3, body.GetProperty("moved_items").GetArrayLength());
        Assert.Empty(body.GetProperty("failed").EnumerateArray());
        Assert.Empty(body.GetProperty("left_behind").EnumerateArray());

        var p = Rt.Store.Get<Project>(Kinds.Project, _p.Id)!;
        Assert.Equal(Path.Combine(NewRoot, Path.GetFileName(_p.Directory)), p.Directory);
        Assert.False(Directory.Exists(_p.Directory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(OldRoot));
        Assert.Equal("user note", File.ReadAllText(Path.Combine(NewRoot, "メモ.txt")));
        AssertNoStalePaths(OldRoot);
        await AssertServedAsync();
        // a source registered by path elsewhere is never touched; recorded command lines are kept as they were
        Assert.Equal(_outside, Rt.Store.Get<SourceAsset>(Kinds.Asset, "a_out")!.Path);
        Assert.Contains(OldRoot, Rt.Store.Get<Render>(Kinds.Render, "r_prev")!.Commands[0]);
        // "newest first" is not disturbed by the rewrite
        Assert.Equal(order, Rt.Store.List<Render>(Kinds.Render, "ep1").Select(x => x.Id).ToList());

        var s = await _http.GetFromJsonAsync<JsonElement>("/api/settings/projects-dir", Ct);
        Assert.Equal(NewRoot, s.GetProperty("path").GetString());
        Assert.Equal("app", s.GetProperty("source").GetString());
        using var created = await _http.PostAsJsonAsync("/api/projects", new { name = "次" }, Ct);
        Assert.StartsWith(NewRoot + "\\", (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("directory").GetString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "projects-move.json")));
    }

    [Fact]
    public async Task Name_clash_or_bad_target_refuses_and_moves_nothing()
    {
        Directory.CreateDirectory(Path.Combine(NewRoot, "メモ.txt"));
        var before = Rt.Store.RawAll();
        var plan = await PlanAsync(NewRoot, true);
        Assert.Contains(plan.GetProperty("problems").EnumerateArray(), x => x.GetString()!.Contains("メモ.txt"));
        using (var r = await ChangeAsync(NewRoot, true)) Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.True(Directory.Exists(_p.Directory));
        Assert.Equal(before, Rt.Store.RawAll());
        foreach (var bad in new[] { "relative\\path", OldRoot, Path.Combine(OldRoot, "inside"), Path.Combine(_p.Directory, "x"), Path.Combine(_root, "data", "x") })
            using (var r = await ChangeAsync(bad, bad != Path.Combine(_p.Directory, "x")))
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(before, Rt.Store.RawAll());
        Assert.Equal(OldRoot, (await _http.GetFromJsonAsync<JsonElement>("/api/settings/projects-dir", Ct)).GetProperty("path").GetString());
    }

    [Fact]
    public async Task Busy_work_blocks_the_change()
    {
        _app.Services.GetRequiredService<JobQueue>().Previews["ep1"] = new PreviewProgress(true, 0.5, "合成");
        using var r = await ChangeAsync(NewRoot, true);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.True(Directory.Exists(_p.Directory));
    }

    [Fact]
    public async Task Keep_changes_only_new_projects_and_old_project_folders_are_still_removed_on_delete()
    {
        var before = Rt.Store.RawAll().Where(x => x.Kind != Kinds.Setting).ToList();
        using (var r = await ChangeAsync(NewRoot, false))
        {
            var body = await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.False(body.GetProperty("moved").GetBoolean());
        }
        Assert.Equal(before, Rt.Store.RawAll().Where(x => x.Kind != Kinds.Setting).ToList());
        await AssertServedAsync();
        using (var created = await _http.PostAsJsonAsync("/api/projects", new { name = "新" }, Ct))
            Assert.StartsWith(NewRoot + "\\", (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("directory").GetString());
        // the old folder is still known as the app's: deleting the old project removes its folder (not just "kept")
        using var del = await _http.DeleteAsync($"/api/projects/{_p.Id}", Ct);
        Assert.Equal("removed", (await del.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("folder").GetString());
        Assert.False(Directory.Exists(_p.Directory));
    }

    [Fact]
    public async Task Interrupted_move_is_completed_at_next_start()
    {
        // the folder was renamed but the app stopped before the paths were rewritten
        var dst = Path.Combine(NewRoot, Path.GetFileName(_p.Directory));
        Directory.CreateDirectory(NewRoot);
        File.WriteAllText(Path.Combine(_root, "data", "projects-move.json"), Json.Serialize(new { from = _p.Directory, to = dst, item = Path.GetFileName(_p.Directory), mode = "rename" }));
        Directory.Move(_p.Directory, dst);
        Loc.Recover();
        Assert.Equal(dst, Rt.Store.Get<Project>(Kinds.Project, _p.Id)!.Directory);
        AssertNoStalePaths(_p.Directory);
        await AssertServedAsync();
        Assert.False(File.Exists(Path.Combine(_root, "data", "projects-move.json")));
    }

    [Fact]
    public void Reroot_matches_whole_path_segments_only()
    {
        Assert.Equal(@"D:\New\a\b.mp4", StorePaths.Reroot(@"c:\OLD\a\b.mp4", @"C:\Old", @"D:\New"));
        Assert.Equal(@"D:\New", StorePaths.Reroot(@"C:\Old", @"C:\Old", @"D:\New"));
        Assert.Null(StorePaths.Reroot(@"C:\Older\a.mp4", @"C:\Old", @"D:\New"));
        Assert.Null(StorePaths.Reroot(@"relative\C:\Old\a", @"C:\Old", @"D:\New"));
    }
}
