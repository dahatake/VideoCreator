using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>App-wide BGM: registered once, offered to every project that keeps it on, never breaking a plan that already uses a track.</summary>
public sealed class SharedBgmTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-sbgm-" + Guid.NewGuid().ToString("N")[..8]);
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    string _tone = "";
    string _a = "", _b = "";

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
        _tone = Path.Combine(_root, "tone.wav");
        await Rt.Ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-f", "lavfi", "-i", "sine=f=440:d=6", _tone], Ct);
        _a = await NewProject("A");
        _b = await NewProject("B");
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    async Task<string> NewProject(string name)
    {
        using var r = await _http.PostAsJsonAsync("/api/projects", new { name }, Ct);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    async Task<JsonElement> Upload(string url, string fileName, HttpStatusCode expect = HttpStatusCode.Created)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(_tone, Ct)), "file", fileName);
        form.Add(new StringContent("true"), "rights_confirmed");
        using var r = await _http.PostAsync(url, form, Ct);
        Assert.Equal(expect, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    async Task<List<(string Id, string Source)>> Listed(string projectId)
    {
        var list = await _http.GetFromJsonAsync<JsonElement>($"/api/bgm?project_id={projectId}", Ct);
        return [.. list.EnumerateArray().Where(t => t.GetProperty("source").GetString() != "library").Select(t => (t.GetProperty("id").GetString()!, t.GetProperty("source").GetString()!))];
    }

    async Task UseShared(string projectId, bool on)
    {
        using var r = await _http.PutAsJsonAsync($"/api/projects/{projectId}/generation-settings", new { bgm = "scene", use_shared_bgm = on, mask_words = Array.Empty<string>() }, Ct);
        r.EnsureSuccessStatusCode();
    }

    void PlanUsing(string projectId, string trackId) =>
        Rt.Store.Put(Kinds.EditPlan, "ep_" + projectId, projectId, new EditPlan
        {
            EditplanId = "ep_" + projectId, ProjectId = projectId, Preset = Fixtures.Presets["P4"].Ref, Timeline = [],
            Audio = new AudioSpec { Master = "a", BgmCues = [new BgmCue { Id = "c1", Track = trackId }] },
        });

    [Fact]
    public async Task Shared_track_is_offered_to_every_project_until_one_opts_out()
    {
        var shared = (await Upload("/api/bgm/shared", "共通の曲.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        // stored once, in the app data folder, outside any project
        var t = Rt.Store.Get<BgmTrack>(Kinds.Bgm, shared)!;
        Assert.Equal(("shared", (string?)null), (t.Source, t.ProjectId));
        Assert.StartsWith(Path.Combine(_root, "data", "bgm"), t.Path);
        Assert.Contains((shared, "shared"), await Listed(_a));
        Assert.Contains((shared, "shared"), await Listed(_b));

        await UseShared(_b, false);
        Assert.DoesNotContain(shared, (await Listed(_b)).Select(x => x.Id));
        Assert.Contains((shared, "shared"), await Listed(_a));
        var runner = Rt.CreateRunner();
        Assert.Empty(runner.RegisteredBgm(_b));
        Assert.Null(runner.SelectableBgm(_b, shared));
        Assert.NotNull(runner.SelectableBgm(_a, shared));
        // "scene" for B falls back to the synthesised tracks, never to the shared one
        Assert.DoesNotContain((await runner.BgmPoolAsync(_b, Ct)), x => x.Id == shared);
        using var job = await _http.PostAsJsonAsync($"/api/projects/{_b}/jobs", new { preset_ids = new[] { "P4" }, options = new { bgm = shared } }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, job.StatusCode);
        Assert.Contains("E-BGM-NOTFOUND", await job.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Another_projects_track_is_never_selectable()
    {
        var own = (await Upload($"/api/projects/{_a}/bgm", "a-only.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        Assert.Contains((own, "user"), await Listed(_a));
        Assert.DoesNotContain(own, (await Listed(_b)).Select(x => x.Id));
        Assert.Null(Rt.CreateRunner().SelectableBgm(_b, own));
        using var job = await _http.PostAsJsonAsync($"/api/projects/{_b}/jobs", new { preset_ids = new[] { "P4" }, options = new { bgm = own } }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, job.StatusCode);
    }

    [Fact]
    public async Task Project_track_wins_over_a_shared_track_of_the_same_name()
    {
        var shared = (await Upload("/api/bgm/shared", "Theme.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        var own = (await Upload($"/api/projects/{_a}/bgm", "theme.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        var pool = Rt.CreateRunner().RegisteredBgm(_a).Select(t => t.Id).ToList();
        Assert.Equal([own], pool);
        Assert.Equal([shared], Rt.CreateRunner().RegisteredBgm(_b).Select(t => t.Id));
    }

    [Fact]
    public async Task Shared_track_used_by_a_plan_cannot_be_removed_and_stays_listed_after_opting_out()
    {
        var shared = (await Upload("/api/bgm/shared", "used.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        PlanUsing(_b, shared);
        using (var del = await _http.DeleteAsync($"/api/bgm/shared/{shared}", Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);
            Assert.Contains("E-BGM-IN-USE", await del.Content.ReadAsStringAsync(Ct));
        }
        // the plan keeps resolving (and naming) its track after the project stops using app-wide tracks
        await UseShared(_b, false);
        Assert.Contains((shared, "shared"), await Listed(_b));
        Assert.NotNull((await Rt.CreateRunner().BgmContextAsync(_b, Ct)).Find(shared));

        Rt.Store.Delete([(Kinds.EditPlan, "ep_" + _b)]);
        using (var del = await _http.DeleteAsync($"/api/bgm/shared/{shared}", Ct)) Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Null(Rt.Store.Get<BgmTrack>(Kinds.Bgm, shared));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "data", "bgm")));
    }

    [Fact]
    public async Task Promoting_a_project_track_keeps_its_id_so_existing_plans_keep_working()
    {
        var id = (await Upload($"/api/projects/{_a}/bgm", "promote.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        var oldPath = Rt.Store.Get<BgmTrack>(Kinds.Bgm, id)!.Path;
        PlanUsing(_a, id);
        using (var r = await _http.PostAsync($"/api/projects/{_a}/bgm/{id}/share", null, Ct)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = Rt.Store.Get<BgmTrack>(Kinds.Bgm, id)!;
        Assert.Equal("shared", t.Source);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(t.Path));
        Assert.Contains((id, "shared"), await Listed(_a));
        Assert.Contains((id, "shared"), await Listed(_b));
        Assert.NotNull((await Rt.CreateRunner().BgmContextAsync(_a, Ct)).Find(id));
        // the project no longer owns it, and a second track of that name cannot be promoted over it
        Assert.Empty(Rt.Store.List<BgmTrack>(Kinds.Bgm, _a));
        var again = (await Upload($"/api/projects/{_b}/bgm", "promote.wav")).GetProperty("tracks")[0].GetProperty("id").GetString()!;
        using (var r = await _http.PostAsync($"/api/projects/{_b}/bgm/{again}/share", null, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("E-BGM-EXISTS", await r.Content.ReadAsStringAsync(Ct));
        }
    }
}
