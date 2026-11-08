using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class ProjectProperNounTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-proper-noun-" + Guid.NewGuid().ToString("N"));
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
        using var response = await _http.PostAsJsonAsync("/api/projects", new { name = "用語" }, TestContext.Current.CancellationToken);
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
    public async Task Proper_nouns_are_normalized_and_persisted_on_the_project()
    {
        var ct = TestContext.Current.CancellationToken;
        using var saved = await _http.PutAsJsonAsync($"/api/projects/{_id}/proper-nouns", new
        {
            proper_nouns = new[]
            {
                new { display_text = " 東京太郎 ", reading = " とうきょうたろう ", variants = new[] { "東京たろう", " 東京たろう " }, type = "person" },
                new { display_text = "青森", reading = "", variants = Array.Empty<string>(), type = "place" },
            },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var project = await _http.GetFromJsonAsync<JsonElement>($"/api/projects/{_id}", ct);
        var names = project.GetProperty("project").GetProperty("proper_nouns").EnumerateArray().ToArray();
        Assert.Equal(2, names.Length);
        Assert.Equal("東京太郎", names[0].GetProperty("display_text").GetString());
        Assert.Equal("とうきょうたろう", names[0].GetProperty("reading").GetString());
        Assert.Equal("person", names[0].GetProperty("type").GetString());
        Assert.Equal(new[] { "東京たろう" }, names[0].GetProperty("variants").EnumerateArray().Select(v => v.GetString()).ToArray());
        Assert.Equal("place", names[1].GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Display_text_is_required(string? displayText)
    {
        using var response = await _http.PutAsJsonAsync($"/api/projects/{_id}/proper-nouns", new
        {
            proper_nouns = new[] { new { display_text = displayText, reading = "", variants = Array.Empty<string>(), type = "person" } },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Project_phrases_are_sent_to_asr_and_phrase_changes_invalidate_only_that_transcript_cache()
    {
        var root = Path.Combine(_root, "pipeline");
        var data = Path.Combine(root, "data");
        var sha = new string('a', 64);
        var project = new Project
        {
            Id = "p_glossary",
            Name = "辞書",
            Directory = Path.Combine(root, "project"),
            ProperNouns =
            [
                new ProjectProperNoun { DisplayText = "東京太郎", Reading = "とうきょうたろう", Variants = ["東京たろう"], Type = ProperNounType.Person },
            ],
        };
        using var store = new Store(Path.Combine(data, "db.sqlite"));
        store.Put(Kinds.Project, project.Id, null, project);
        var asset = new SourceAsset
        {
            Id = "a1", ProjectId = project.Id, Path = "unused", Sha256 = sha, Media = Fixtures.Landscape(),
        };
        Directory.CreateDirectory(Path.Combine(data, "cache", sha[..16]));
        var transcriber = new RecordingTranscriber();
        var runner = new PipelineRunner(new PipelineServices
        {
            Ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1"),
            Store = store,
            Presets = Fixtures.Presets,
            DataDir = data,
            Asr = transcriber,
            Dictionary = ["共有用語"],
        });

        var first = await runner.TranscriptAsync(asset, "unused", TestContext.Current.CancellationToken);
        await runner.TranscriptAsync(asset, "unused", TestContext.Current.CancellationToken);
        Assert.Equal(1, transcriber.Calls);
        Assert.Equal(new[] { "共有用語", "東京太郎", "とうきょうたろう", "東京たろう" }, transcriber.Phrases);
        Assert.Equal(3, first.Words[0].T0);

        store.Put(Kinds.Project, project.Id, null, project with
        {
            ProperNouns = [project.ProperNouns[0] with { Type = ProperNounType.Place }],
        });
        await runner.TranscriptAsync(asset, "unused", TestContext.Current.CancellationToken);
        Assert.Equal(1, transcriber.Calls);

        store.Put(Kinds.Project, project.Id, null, project with
        {
            ProperNouns = [project.ProperNouns[0] with { Variants = ["東京たろう", "とうきょう太郎"] }],
        });
        await runner.TranscriptAsync(asset, "unused", TestContext.Current.CancellationToken);
        Assert.Equal(2, transcriber.Calls);
        Assert.Equal(new[] { "共有用語", "東京太郎", "とうきょうたろう", "東京たろう", "とうきょう太郎" }, transcriber.Phrases);
    }

    sealed class RecordingTranscriber : ITranscriber
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> Phrases { get; private set; } = [];
        public string Name => "recording";

        public Task<Transcript> TranscribeAsync(string audioFile, double duration, IReadOnlyList<string> phrases, int maxSpeakers, CancellationToken ct)
        {
            Calls++;
            Phrases = phrases.ToArray();
            return Task.FromResult(new Transcript
            {
                Provider = Name,
                Duration = duration,
                Words = [new Word("w0", 3, 4, "東京太郎", 1)],
                Sentences = [new Sentence(0, 3, 4, "東京太郎", 1, 0, 0)],
            });
        }
    }
}
