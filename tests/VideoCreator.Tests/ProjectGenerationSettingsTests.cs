using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class ProjectGenerationSettingsTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-generation-settings-" + Guid.NewGuid().ToString("N"));
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
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        using var response = await _http.PostAsJsonAsync("/api/projects", new { name = "保存設定" }, TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task Generation_settings_are_saved_and_survive_reopening_the_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var response = await _http.PutAsJsonAsync($"/api/projects/{_id}/generation-settings", new
        {
            preset_id = "P3",
            scope = "economy",
            bgm = "none",
            sfx = false,
            tsukkomi = true,
            series = true,
            series_by = "minutes",
            parts = 3,
            part_minutes = 12.5,
            opening = false,
            closing = true,
            scene_cards = true,
            mask_words = new[] { "秘密の語" },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var reopened = new Store(Path.Combine(_root, "data", "videocreator.db"));
        var saved = reopened.Get<Project>(Kinds.Project, _id)!.GenerationSettings;
        Assert.Equal("P3", saved.PresetId);
        Assert.Equal("economy", saved.Scope);
        Assert.Equal("none", saved.Bgm);
        Assert.False(saved.Sfx);
        Assert.True(saved.Tsukkomi);
        Assert.True(saved.Series);
        Assert.Equal("minutes", saved.SeriesBy);
        Assert.Equal(3, saved.Parts);
        Assert.Equal(12.5, saved.PartMinutes);
        Assert.False(saved.Opening);
        Assert.True(saved.Closing);
        Assert.True(saved.SceneCards);
        Assert.Equal(["秘密の語"], saved.MaskWords);
    }

    [Fact]
    public async Task Invalid_generation_settings_are_rejected_without_overwriting_saved_settings()
    {
        using var response = await _http.PutAsJsonAsync($"/api/projects/{_id}/generation-settings", new
        {
            preset_id = "P4",
            scope = "invalid",
            series_by = "parts",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var project = _app.Services.GetRequiredService<Runtime>().Store.Get<Project>(Kinds.Project, _id);
        Assert.Equal("P4", project!.GenerationSettings.PresetId);
        Assert.Equal("balanced", project.GenerationSettings.Scope);
    }

    [Fact]
    public void Existing_project_documents_use_default_generation_settings()
    {
        var project = VideoCreator.Json.Deserialize<Project>("""
            {"id":"p_legacy","name":"以前のプロジェクト","directory":"C:\\Videos\\legacy"}
            """);

        Assert.Equal("P4", project.GenerationSettings.PresetId);
        Assert.Equal("balanced", project.GenerationSettings.Scope);
    }
}
