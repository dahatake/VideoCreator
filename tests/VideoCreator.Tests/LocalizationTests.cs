using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Asr;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Selection;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class LocalizationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-languages-" + Guid.NewGuid().ToString("N"));
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData("en-US", "Hello", "world!", "Hello world!")]
    [InlineData("en-US", "He said,", "“hello!”", "He said, “hello!”")]
    [InlineData("en-US", "He said,", "\"hello!\"", "He said, \"hello!\"")]
    [InlineData("en-US", "Hello", "(world)", "Hello (world)")]
    [InlineData("en-US", "\"Hello!\"", "Again.", "\"Hello!\" Again.")]
    [InlineData("zh-CN", "你好", "世界！", "你好世界！")]
    [InlineData("ko-KR", "안녕하세요", "여러분!", "안녕하세요 여러분!")]
    [InlineData("ko-KR", "그는", "“안녕”", "그는 “안녕”")]
    public void Captions_keep_words_spacing_punctuation_and_source_ids(string language, string a, string b, string expected)
    {
        var tr = new Transcript { Provider = "test", Language = language, Duration = 5,
            Words = [new("w0", 0, 1, a, 1), new("w1", 1, 2, b, 1)] };
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a", SrcIn = 0, SrcOut = 5 } };
        var captions = TelopBuilder.Build(clips, tr, Fixtures.Presets["P4"], "", "", [], [])
            .Where(t => t.Type == TelopType.Caption).ToList();
        var caption = Assert.Single(captions);
        Assert.Equal(expected, caption.Text);
        Assert.Equal(new[] { "w0", "w1" }, caption.SourceWordIds);
        var plan = Plan(language) with { Timeline = clips, Telops = captions };
        Assert.Contains(expected, AssWriter.Srt(plan));
        Assert.Contains(expected, AssWriter.Write(plan, Fixtures.Presets["P4"]));
    }

    [Theory]
    [InlineData("en-US", "This is a very long English sentence with several words.")]
    [InlineData("ko-KR", "이것은 여러 단어가 있는 아주 긴 한국어 문장입니다.")]
    public void Captions_can_split_between_words_and_wrap_without_breaking_words(string language, string text)
    {
        var words = text.Split(' ').Select((w, i) => new TelopBuilder.Mapped(new Word($"w{i}", i, i + 0.9, w, 1), i, i + 0.9, 0)).ToList();
        var chunks = TelopBuilder.Chunk(words, 8, language);
        Assert.True(chunks.Count > 1);
        Assert.Equal(words.Select(w => w.Word.Id), chunks.SelectMany(c => c).Select(w => w.Word.Id));
        var lines = CaptionText.WrapLines(text, 16, 2, language);
        Assert.Equal(2, lines.Count);
        Assert.Equal(text, string.Join(" ", lines));
        Assert.All(lines, line => Assert.Contains(line.Split(' ')[0], text.Split(' ')));
    }

    [Theory]
    [InlineData("en-US", "John", "Smith", "John Smith")]
    [InlineData("ko-KR", "김", "민수", "김 민수")]
    public void Masking_names_across_word_boundaries_preserves_spaces_and_masks_audio(string language, string first, string last, string name)
    {
        var tr = new Transcript { Provider = "test", Language = language, Duration = 3,
            Words = [new("w0", 0, 1, first, 1), new("w1", 1, 2, last, 1)],
            Sentences = [new(0, 0, 2, name, 1, 0, 1)] };
        var (masked, spans) = Masking.Apply(tr, [name]);
        Assert.All(masked.Words, w => Assert.All(w.Text, c => Assert.Equal(Masking.Dot, c)));
        Assert.Contains(" ", Assert.Single(masked.Sentences).Text);
        Assert.Single(spans);
        Assert.Equal(0, spans[0].T0);
        Assert.Equal(2, spans[0].T1);
    }

    [Fact]
    public async Task Speech_request_and_parsed_transcript_use_project_locale()
    {
        Directory.CreateDirectory(_root);
        var audio = Path.Combine(_root, "sample.flac");
        await File.WriteAllBytesAsync(audio, [0], Ct);
        foreach (var language in new[] { "en-US", "zh-CN", "ko-KR" })
        {
            using var handler = new SpeechHandler(language);
            using var http = new HttpClient(handler);
            var asr = new AzureFastTranscriber(new Uri("https://speech.invalid/"), new TestCredential(), http, language);
            var tr = await asr.TranscribeAsync(audio, 5, [], 2, Ct);
            Assert.Equal(language, tr.Language);
            Assert.Equal("Hello.", Assert.Single(tr.Sentences).Text);
        }
    }

    [Fact]
    public void Period_sentence_boundaries_keep_decimals_and_word_timing()
    {
        Assert.Equal(new[] { "The value is 3.14.", " Next!" }, SentenceBuilder.SplitText("The value is 3.14. Next!"));
        var words = new List<Word> { new("a", 0, 0.4, "Hello", 1), new("b", 0.4, 1, "world", 1),
            new("c", 2, 2.4, "Good", 1), new("d", 2.4, 3, "day", 1) };
        var sentences = SentenceBuilder.Build([(0, 3, "Hello world. Good day.", 1, 0, 3)], words);
        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hello world.", sentences[0].Text);
        Assert.Equal(1, sentences[0].T1);
        Assert.Equal(2, sentences[1].T0);
        Assert.Equal(2, sentences[1].FirstWord);
        Assert.Equal(3, sentences[1].LastWord);
    }

    [Fact]
    public async Task Transcript_cache_is_language_isolated_and_old_plans_can_load_their_original_language()
    {
        var data = Path.Combine(_root, "data");
        using var store = new Store(Path.Combine(data, "db.sqlite"));
        var project = new Project { Id = "p", Name = "languages", Directory = _root };
        store.Put(Kinds.Project, project.Id, null, project);
        var asset = new SourceAsset { Id = "a", ProjectId = project.Id, Path = "unused", Sha256 = new string('a', 64), Media = Fixtures.Landscape(5) };
        Directory.CreateDirectory(Path.Combine(data, "cache", asset.Sha256[..16]));
        var requests = new List<string>();
        var runner = new PipelineRunner(new PipelineServices
        {
            Ff = Fixtures.Ff!, Store = store, Presets = Fixtures.Presets, DataDir = data,
            Asr = new RecordingTranscriber("ja-JP", requests),
            AsrForLanguage = language => new RecordingTranscriber(language, requests),
        });
        foreach (var language in new[] { "ja-JP", "en-US", "zh-CN", "ko-KR" })
        {
            store.Put(Kinds.Project, project.Id, null, project with { Language = language });
            Assert.False(runner.CachedAnalysis(asset).TranscriptCached);
            Assert.Empty(runner.LoadTranscript(asset).Words);
            Assert.Equal(language, (await runner.TranscriptAsync(asset, "unused", Ct)).Language);
            await runner.TranscriptAsync(asset, "unused", Ct);
            Assert.Equal(language, runner.LoadTranscript(asset).Language);
        }
        Assert.Equal(new[] { "ja-JP", "en-US", "zh-CN", "ko-KR" }, requests);
        Assert.Equal("en-US", runner.LoadTranscript(asset, "en-US").Language);
        Assert.Equal("en-US", Assert.Single(runner.LoadTranscript(asset, "en-US").Words).Text);
        var edited = runner.EditTranscript(asset, [("w0", "수정")]);
        Assert.Equal("수정", Assert.Single(edited.Words).Text);
        Assert.Equal("en-US", Assert.Single(runner.LoadTranscript(asset, "en-US").Words).Text);
    }

    [Theory]
    [InlineData("en-US", "English", "Up next!", "M PLUS 1p ExtraBold")]
    [InlineData("zh-CN", "Simplified Chinese", "接下来！", "Noto Sans CJK SC")]
    [InlineData("ko-KR", "Korean", "다음은!", "Noto Sans CJK KR")]
    public async Task Generated_text_prompts_fixed_cards_fonts_and_post_copy_use_video_language(string language, string name, string card, string font)
    {
        var tr = new Transcript { Provider = "test", Language = language, Duration = 30,
            Sentences = [new(0, 0, 30, "Hello world.", 1, 0, 0)], Words = [new("w0", 0, 1, "Hello", 1)] };
        var llm = new LanguageSelector(name);
        await new SelectionService(llm).SelectAsync(tr, Fixtures.Signals(tr), Fixtures.Presets["P4"], 30, null, [], Ct);
        Assert.True(llm.Selected);
        var captions = Enumerable.Range(0, 3).Select(i => new Telop { Id = $"t{i}", Type = TelopType.Caption, Start = i, End = i + 1, Text = "Hello" }).ToList();
        await TsukkomiService.GenerateAsync(llm, captions, "Hello", Ct, language);
        var plan = Plan(language) with { Telops = captions };
        var inserts = new List<Insert> { new() { Id = "card1", Kind = "card", At = 3, Duration = 2, TextSource = "fixed" } };
        await InsertPlanner.AiTextAsync(llm, inserts, captions, "Hello", Ct, language);
        await SeriesPlanner.OutlineAsync(llm, tr, null, Ct);
        await PostCopy.BuildAsync(llm, plan, new Candidate { Id = "c", Segments = [new(0, 30)] }, tr, [], [], null, Ct);
        Assert.Contains("tsukkomi", llm.Schemas);
        Assert.Contains("scene_cards", llm.Schemas);
        Assert.Contains("series_outline", llm.Schemas);
        Assert.Contains("post_copy", llm.Schemas);
        Assert.Equal(card, InsertPlanner.FixedPhrase(language, 0));
        Assert.Contains("," + font + ",", AssWriter.Write(plan, Fixtures.Presets["P4"]));
        var post = PostCopy.Compose(plan, "Title", [], [], null, []);
        Assert.DoesNotContain("見どころ", post.Youtube);
        Assert.DoesNotContain("公開しました", post.Engage);
    }

    [Fact]
    public async Task Project_api_persists_language_rejects_unsupported_and_preserves_existing_defaults()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
            b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
            b.UseSetting("VideoCreator:OpenBrowser", "false");
            b.UseSetting("VideoCreator:Port", "0");
            b.UseSetting("VideoCreator:Asr:Provider", "none");
            b.UseSetting("VideoCreator:Llm:Provider", "none");
        });
        using var http = app.CreateClient();
        http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        try
        {
            using var original = await http.PostAsJsonAsync("/api/projects", new { name = "default" }, Ct);
            Assert.Equal("ja-JP", (await original.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("language").GetString());
            using var invalid = await http.PostAsJsonAsync("/api/projects", new { name = "invalid", language = "fr-FR" }, Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
            foreach (var language in new[] { "en-US", "zh-CN", "ko-KR" })
            {
                using var created = await http.PostAsJsonAsync("/api/projects", new { name = "video", language }, Ct);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var json = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
                var id = json.GetProperty("id").GetString();
                Assert.Equal(language, json.GetProperty("language").GetString());
                using var updated = await http.PatchAsJsonAsync($"/api/projects/{id}", new { language = "ja-JP" }, Ct);
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
                var saved = await http.GetFromJsonAsync<JsonElement>($"/api/projects/{id}", Ct);
                Assert.Equal("ja-JP", saved.GetProperty("project").GetProperty("language").GetString());
                Assert.Equal("video", saved.GetProperty("project").GetProperty("name").GetString());
                using var badPatch = await http.PatchAsJsonAsync($"/api/projects/{id}", new { language = "en" }, Ct);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, badPatch.StatusCode);
            }
        }
        finally
        {
            var store = app.Services.GetRequiredService<Runtime>().Store;
            await app.DisposeAsync();
            store.Dispose();
        }
    }

    [Theory]
    [InlineData("en-US", "Hello world!", "M PLUS 1p ExtraBold")]
    [InlineData("zh-CN", "欢迎观看，视频字幕测试", "Noto Sans CJK SC")]
    [InlineData("ko-KR", "안녕하세요 영상 자막 테스트", "Noto Sans CJK KR")]
    public async Task Libass_renders_each_language_with_bundled_font_and_no_missing_glyphs(string language, string text, string font)
    {
        Directory.CreateDirectory(_root);
        var ff = Fixtures.Ff!;
        var plan = Plan(language) with { Telops = [new() { Id = "t", Type = TelopType.Caption, Start = 0, End = 1, Text = text }] };
        var ass = Path.Combine(_root, "render.ass");
        await File.WriteAllTextAsync(ass, AssWriter.Write(plan, Fixtures.Presets["P4"]), new UTF8Encoding(false), Ct);
        var filter = $"subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(ff.FontsDir)}";
        var result = await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "verbose", "-f", "lavfi", "-i", "color=c=black:s=1080x1920:d=0.1",
            "-vf", filter, "-frames:v", "1", "-f", "md5", "-"], Ct);
        Assert.Contains("MD5=", result.StdOut);
        Assert.Contains(font, result.StdErr);
        Assert.Contains(language switch { "zh-CN" => "NotoSansCJKsc-Bold", "ko-KR" => "NotoSansCJKkr-Bold", _ => "MPLUS1p-ExtraBold" }, result.StdErr);
        Assert.DoesNotContain("not found", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed to find", result.StdErr, StringComparison.OrdinalIgnoreCase);
        var blank = await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=1080x1920:d=0.1",
            "-frames:v", "1", "-f", "md5", "-"], Ct);
        Assert.NotEqual(blank.StdOut, result.StdOut);

        var source = Path.Combine(_root, "source.mp4");
        await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=360x640:r=30:d=1",
            "-f", "lavfi", "-i", "sine=f=330:d=1", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", source], Ct);
        var asset = new SourceAsset { Id = "a", ProjectId = "p", Path = source, Sha256 = new string('a', 64),
            Media = new() { Width = 360, Height = 640, Duration = 1, Fps = 30, AudioChannels = 1 } };
        plan = plan with { Timeline = [plan.Timeline[0] with { SrcOut = 1 }] };
        var rendered = await new Renderer(ff).RenderAsync(plan, Fixtures.Presets["P4"], new Dictionary<string, SourceAsset> { ["a"] = asset },
            Path.Combine(_root, "preview.mp4"), true, Ct);
        var probe = await ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", rendered.Path], Ct);
        Assert.Contains("h264", probe.StdOut);
        Assert.Contains("aac", probe.StdOut);
        var subtitles = await File.ReadAllTextAsync(Path.ChangeExtension(rendered.Path, ".ass"), Ct);
        Assert.Contains(text, subtitles.Replace(@"\N", language == "zh-CN" ? "" : " "));
    }

    [Theory]
    [InlineData("zh-CN", "NotoSansCJKsc-Bold.otf")]
    [InlineData("ko-KR", "NotoSansCJKkr-Bold.otf")]
    public async Task Missing_bundled_font_fails_explicitly_before_encoding(string language, string fontFile)
    {
        var ff = new FFmpeg(Path.GetDirectoryName(Fixtures.Ff!.FfmpegPath)!, Path.Combine(_root, "empty-fonts"));
        var plan = Plan(language) with { Telops = [new() { Id = "t", Type = TelopType.Caption, Start = 0, End = 1, Text = "字幕 자막" }] };
        var error = await Assert.ThrowsAsync<FFmpegException>(() => new Renderer(ff).RenderAsync(plan, Fixtures.Presets["P4"],
            new Dictionary<string, SourceAsset>(), Path.Combine(_root, "output.mp4"), true, Ct));
        Assert.Contains(fontFile, error.Message);
        Assert.Contains("get-tools.ps1", error.Message);
    }

    [Theory]
    [InlineData("en-US", "WWWWWWWWWWWWWWWWWWWWWWWWWWWW")]
    [InlineData("en-US", "EEEEEEEEEEEEEEEEEEEEEEEEEEEE")]
    [InlineData("en-US", "Every word must stay readable and inside the frame.")]
    [InlineData("zh-CN", "视频字幕测试欢迎观看完整内容")]
    [InlineData("ko-KR", "영상자막테스트시청해주셔서감사합니다")]
    public async Task Language_fonts_fit_long_captions_inside_measured_safe_zone(string language, string text)
    {
        var plan = Plan(language) with
        {
            Timeline = [new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 2 }],
            Telops = [new() { Id = "t", Type = TelopType.Caption, Start = 0, End = 1.5, Text = text }],
        };
        var result = await new QaEngine(Fixtures.Ff!).SafeZoneAsync(
            new QaContext(plan, Fixtures.Presets["P4"], new Transcript { Provider = "test", Language = language }, [], []), _root, Ct);
        Assert.True(result.Status == QaStatus.Pass, result.Measured);
    }

    static EditPlan Plan(string language) => new()
    {
        EditplanId = "ep", ProjectId = "p", Language = language, Preset = Fixtures.Presets["P4"].Ref,
        Audio = new() { Master = "a" }, Timeline = [new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 30 }],
    };

    sealed class RecordingTranscriber(string language, List<string> requests) : ITranscriber
    {
        public string Name => "recording";
        public Task<Transcript> TranscribeAsync(string file, double duration, IReadOnlyList<string> phrases, int speakers, CancellationToken ct)
        {
            requests.Add(language);
            return Task.FromResult(new Transcript { Provider = Name, Language = language, Duration = duration,
                Words = [new("w0", 0, 1, language, 1)], Sentences = [new(0, 0, 1, language, 1, 0, 0)] });
        }
    }

    sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) => new("test", DateTimeOffset.UtcNow.AddMinutes(5));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) => ValueTask.FromResult(GetToken(context, ct));
    }

    sealed class SpeechHandler(string language) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Contains($"[\"{language}\"]", await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"durationMilliseconds":5000,"phrases":[{"offsetMilliseconds":0,"durationMilliseconds":1000,"text":"Hello.","words":[{"offsetMilliseconds":0,"durationMilliseconds":1000,"text":"Hello"}]}]}
                """) };
        }
    }

    sealed class LanguageSelector(string name) : IHighlightSelector
    {
        public string Name => "language-test";
        public string Model => Name;
        public bool Selected;
        public List<string> Schemas { get; } = [];
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct)
        {
            Assert.Contains($"language is {name}", system);
            Selected = true;
            return Task.FromResult(new LlmResult([], Name, new(0, 0)));
        }
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string schema, CancellationToken ct)
        {
            Assert.Contains($"language is {name}", system);
            Schemas.Add(schemaName);
            return Task.FromResult(schemaName switch
            {
                "series_outline" => """{"series_title":"","chapters":[{"start":0,"title":"Hello","split_score":0,"highlight":5,"emphasis_words":[]}]}""",
                "post_copy" => """{"hook":"","engage_hook":"","summary":"","highlights":[],"chapter_titles":[],"hashtags":[]}""",
                _ => """{"items":[]}""",
            });
        }
    }
}
