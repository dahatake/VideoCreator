using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Asr;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>Settings → AI connections: endpoint normalization, the api-key path against a local OpenAI-v1 server, failure diagnosis,
/// secret storage at rest, and the API's test-before-save and no-secret-echo guarantees.</summary>
public sealed class AiConnectionTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-ai-" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Theory]
    [InlineData("https://res.cognitiveservices.azure.com/", "https://res.cognitiveservices.azure.com/openai/v1/")]
    [InlineData("res.openai.azure.com", "https://res.openai.azure.com/openai/v1/")]
    [InlineData("https://RES.services.ai.azure.com/api/projects/proj1", "https://res.services.ai.azure.com/openai/v1/")]
    [InlineData("https://res.cognitiveservices.azure.com/openai/v1", "https://res.cognitiveservices.azure.com/openai/v1/")]
    [InlineData("https://apim.contoso.com/ai", "https://apim.contoso.com/ai/openai/v1/")]
    public void Endpoint_forms_copied_from_the_portals_become_the_v1_base(string input, string expected) =>
        Assert.Equal(expected, FoundryEndpoint.Normalize(input).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("http://res.openai.azure.com/")]
    [InlineData("https://res.openai.azure.com/?api-key=abc")]
    [InlineData("https://user:pw@res.openai.azure.com/")]
    public void Endpoints_that_would_leak_or_send_secrets_in_clear_are_rejected(string input) =>
        Assert.Throws<ArgumentException>(() => FoundryEndpoint.Normalize(input));

    /// <summary>Minimal OpenAI v1 chat endpoint: records each request's path and api-key header, answers with the given status/body.</summary>
    sealed class FakeOpenAI : IDisposable
    {
        readonly HttpListener _l = new();
        public readonly List<(string Path, string? Key)> Requests = [];
        public Uri V1 { get; }
        public FakeOpenAI(int status, string body)
        {
            var port = FreePort();
            _l.Prefixes.Add($"http://127.0.0.1:{port}/");
            _l.Start();
            V1 = new Uri($"http://127.0.0.1:{port}/openai/v1/");
            _ = Task.Run(async () =>
            {
                while (_l.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _l.GetContextAsync(); } catch { return; }
                    lock (Requests) Requests.Add((ctx.Request.Url!.AbsolutePath, ctx.Request.Headers["api-key"]));
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = status; ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
                }
            });
        }
        static int FreePort() { var t = new TcpListener(IPAddress.Loopback, 0); t.Start(); var p = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop(); return p; }
        public void Dispose() => _l.Close();
    }

    static IHighlightSelector KeySelector(Uri v1, string deployment, string key) =>
        new AzureOpenAISelector(v1, deployment, FoundryAuth.Policy(new FoundryConnection(v1.ToString(), deployment, null, "key", ApiKey: key), _ => throw new InvalidOperationException()), TimeSpan.FromSeconds(10));

    const string ChatOk = """{"id":"x","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{\"reply\":\"OK\"}"}}]}""";

    [Fact]
    public async Task Key_auth_sends_the_api_key_header_to_the_deployment_and_the_probe_passes()
    {
        using var server = new FakeOpenAI(200, ChatOk);
        await ConnectionProbe.ChatAsync(KeySelector(server.V1, "gpt-x", "secret-key-123"), TestContext.Current.CancellationToken);
        var (path, key) = Assert.Single(server.Requests);
        Assert.Equal("/openai/v1/chat/completions", path);
        Assert.Equal("secret-key-123", key);
    }

    [Theory]   // raw bodies captured from a real resource (spikes/22-ai-connection.cs)
    [InlineData(401, """{"error":{"code":"401","message":"Access denied due to invalid subscription key or wrong API endpoint."}}""", "key", "key_rejected")]
    [InlineData(401, """{"error":{"code":"401","message":"Access denied due to invalid subscription key or wrong API endpoint."}}""", "entra", "entra_rejected")]
    [InlineData(403, """{"error":{"code":"PermissionDenied","message":"The principal lacks the required data action."}}""", "entra", "forbidden")]
    [InlineData(404, """{"error":{"code":"DeploymentNotFound","message":"The API deployment for this resource does not exist."}}""", "key", "deployment_not_found")]
    [InlineData(404, """{"error":{"code":"404","message":"Resource not found"}}""", "key", "endpoint_not_found")]
    [InlineData(400, """{"error":{"code":"OperationNotSupported","message":"The requested operation is unsupported."}}""", "key", "wrong_model_kind")]
    public async Task Provider_failures_are_diagnosed_from_the_real_response(int status, string body, string auth, string code)
    {
        using var server = new FakeOpenAI(status, body);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ConnectionProbe.ChatAsync(KeySelector(server.V1, "gpt-x", "k"), TestContext.Current.CancellationToken));
        var d = AiDiagnostics.Classify(ex, "foundry", auth, "gpt-x");
        Assert.Equal(code, d.Code);
        Assert.False(string.IsNullOrWhiteSpace(d.Detail));
    }

    [Fact]
    public async Task A_reply_that_is_not_the_requested_json_is_a_bad_reply_not_a_pass()
    {
        using var server = new FakeOpenAI(200, ChatOk.Replace("""{\"reply\":\"OK\"}""", "hello"));
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ConnectionProbe.ChatAsync(KeySelector(server.V1, "gpt-x", "k"), TestContext.Current.CancellationToken));
        Assert.Equal("bad_reply", AiDiagnostics.Classify(ex, "foundry", "key").Code);
    }

    static CopilotModel Sonnet => new("claude-sonnet", "Claude Sonnet", "enabled", ["low", "medium", "high"], "medium", 200_000, 1_000_000, 1, null, null);

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData(null, "high", null, false)]
    [InlineData("claude-sonnet", "high", "long_context", true)]
    [InlineData("claude-sonnet", "max", null, false)]
    [InlineData("other", null, null, false)]
    public void Copilot_choices_are_checked_against_what_the_model_offers(string? model, string? effort, string? tier, bool valid)
    {
        var problem = CopilotCatalog.Validate(new CopilotConnection(Model: model, ReasoningEffort: effort, ContextTier: tier), [Sonnet]);
        Assert.Equal(valid, problem == null);
        Assert.NotNull(CopilotCatalog.Validate(new CopilotConnection(Model: "m2", ContextTier: "long_context"), [Sonnet with { Id = "m2", LongContextTokens = null }]));
    }

    [Fact]
    public void Secrets_are_stored_protected_and_unreadable_blobs_are_reported_not_thrown()
    {
        var store = new AiSettingsStore(Path.Combine(_root, "ai-settings.json"), AiSettingsStore.DefaultProtector());
        const string secret = "sk-very-secret-value-0123456789";
        store.Update(s => s with { Foundry = new SavedFoundry("https://r.openai.azure.com/openai/v1/", "d", null, "key", null, store.Protect(secret), DateTimeOffset.Now) });
        var file = File.ReadAllText(store.Path);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("dpapi", store.Protector.Kind);
            Assert.DoesNotContain(secret, file);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), file);
        }
        Assert.Equal(secret, store.Load().Foundry!.ToConnection(store).ApiKey);
        Assert.Null(store.Reveal(Convert.ToBase64String(new byte[64])));   // e.g. a file copied from another Windows user
        Assert.Null(store.Reveal("not base64!"));
    }

    WebApplicationFactory<Program> App(bool localOnly = false, HttpMessageHandler? speechHandler = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
        b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
        b.UseSetting("VideoCreator:OpenBrowser", "false");
        b.UseSetting("VideoCreator:LocalOnly", localOnly ? "true" : "false");
        b.UseSetting("VideoCreator:Llm:Endpoint", "");
        if (speechHandler != null) b.ConfigureServices(services => services.AddSingleton(new HttpClient(speechHandler)));
    });

    static HttpClient Client(WebApplicationFactory<Program> app)
    {
        var http = app.CreateClient(new() { BaseAddress = new Uri("http://127.0.0.1") });
        http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        return http;
    }

    [Fact]
    public async Task Settings_never_return_a_saved_secret_and_a_saved_key_is_not_sent_to_another_host()
    {
        await using var app = App();
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        const string secret = "key-that-must-not-leave-0123456789";
        rt.SaveFoundry(new FoundryConnection("https://r1.openai.azure.com/openai/v1/", "gpt", null, "key", ApiKey: secret, ImageDeployment: "flux-image"), DateTimeOffset.Now);
        rt.SaveCopilot(new CopilotConnection("token", "github_pat_must_not_leave_0123"), "me", DateTimeOffset.Now);

        var settings = await http.GetStringAsync("/api/settings", ct);
        Assert.DoesNotContain(secret, settings);
        Assert.DoesNotContain("github_pat_must_not_leave", settings);
        using var doc = JsonDocument.Parse(settings);
        Assert.True(doc.RootElement.GetProperty("foundry").GetProperty("has_api_key").GetBoolean());
        Assert.Equal("app", doc.RootElement.GetProperty("foundry").GetProperty("source").GetString());
        Assert.Equal("flux-image", doc.RootElement.GetProperty("foundry").GetProperty("image_deployment").GetString());
        Assert.True(doc.RootElement.GetProperty("copilot").GetProperty("has_token").GetBoolean());
        Assert.Equal(rt.AiSettings.Protector.Kind, doc.RootElement.GetProperty("secret_protection").GetString());

        // empty api_key = reuse the saved one, but never for a different host
        var moved = await http.PostAsJsonAsync("/api/settings/ai/foundry/test", new { endpoint = "https://attacker.example.com", deployment = "gpt", auth = "key", api_key = "" }, ct);
        var body = await moved.Content.ReadAsStringAsync(ct);
        Assert.Contains("\"invalid_input\"", body);
        Assert.Equal(secret, rt.Foundry!.ApiKey);
    }

    [Fact]
    public async Task Where_secrets_cannot_be_encrypted_a_key_or_token_is_never_saved_but_keyless_sign_in_is()
    {
        await using var app = App();
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        rt.AiSettings = new AiSettingsStore(rt.AiSettings.Path, new NoSecretStorage());

        var r = await http.PutAsJsonAsync("/api/settings/ai/foundry", new { endpoint = "https://does-not-exist.invalid", deployment = "gpt", auth = "key", api_key = "k" }, ct);
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        using (var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)))
        {
            Assert.Equal("secret_storage_unavailable", doc.RootElement.GetProperty("diagnosis").GetProperty("code").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("steps").GetArrayLength());   // refused before any request is sent
        }
        r = await http.PutAsJsonAsync("/api/settings/ai/copilot", new { auth = "token", github_token = "github_pat_x" }, ct);
        Assert.Contains("\"secret_storage_unavailable\"", await r.Content.ReadAsStringAsync(ct));
        Assert.False(File.Exists(rt.AiSettings.Path));

        rt.SaveFoundry(new FoundryConnection("https://r1.openai.azure.com/openai/v1/", "gpt", null, "entra"), DateTimeOffset.Now);
        Assert.Equal("entra", rt.AiSettings.Load().Foundry!.Auth);
        Assert.Contains("\"secret_protection\":\"unavailable\"", await http.GetStringAsync("/api/settings", ct));
    }

    [Fact]
    public async Task The_github_token_the_ui_sends_reaches_the_connection()
    {
        await using var app = App();
        var rt = app.Services.GetRequiredService<Runtime>();
        var req = JsonSerializer.Deserialize<AiConnectionApi.CopilotRequest>("""{"auth":"token","github_token":"github_pat_abc"}""", Json.Options)!;
        Assert.Equal("github_pat_abc", AiConnectionApi.CopilotInput(rt, req).GitHubToken);
    }

    [Fact]
    public async Task Save_happens_only_after_a_passing_test()
    {
        await using var app = App();
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();

        var r = await http.PutAsJsonAsync("/api/settings/ai/foundry", new { endpoint = "http://res.openai.azure.com", deployment = "gpt", auth = "key", api_key = "k" }, ct);
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        Assert.Null(rt.AiSettings.Load().Foundry);
        Assert.False(File.Exists(rt.AiSettings.Path));

        // a host that cannot resolve: the test fails as unreachable and nothing is saved
        r = await http.PutAsJsonAsync("/api/settings/ai/foundry", new { endpoint = "https://does-not-exist.invalid", deployment = "gpt", auth = "key", api_key = "k" }, ct);
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("chat", doc.RootElement.GetProperty("steps")[0].GetProperty("id").GetString());
        Assert.Null(rt.AiSettings.Load().Foundry);
    }

    [Fact]
    public async Task Local_only_mode_sends_no_connection_test()
    {
        await using var app = App(localOnly: true);
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var r = await http.PostAsJsonAsync("/api/settings/ai/foundry/test", new { endpoint = "https://does-not-exist.invalid", deployment = "gpt" }, ct);
        Assert.Contains("\"local_only\"", await r.Content.ReadAsStringAsync(ct));
        r = await http.PostAsJsonAsync("/api/settings/ai/copilot/test", new { auth = "user" }, ct);
        Assert.Contains("\"local_only\"", await r.Content.ReadAsStringAsync(ct));
    }

    sealed class SpeechHandler : HttpMessageHandler
    {
        public readonly List<(Uri Uri, string? Key, string? Authorization, string Body)> Requests = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = """{"durationMilliseconds":1000,"phrases":[]}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys) ? keys.Single() : null,
                request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }

    sealed class SpeechCredential : TokenCredential
    {
        public string[]? Scopes;
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return ValueTask.FromResult(new AccessToken("speech-test-token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    [Theory]
    [InlineData("key")]
    [InlineData("entra")]
    public async Task Verified_Foundry_is_used_for_Speech_only_without_a_saved_Speech_override(string auth)
    {
        using var handler = new SpeechHandler();
        await using var app = App(speechHandler: handler);
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        rt.CredentialFactory = _ => new SpeechCredential();
        var original = rt.Speech;
        const string endpoint = "https://shared.cognitiveservices.azure.com/";
        var foundry = new FoundryConnection(endpoint + "openai/v1/", "gpt", null, auth, ApiKey: auth == "key" ? "shared-key" : null);
        rt.SaveFoundry(foundry, DateTimeOffset.UtcNow);
        Assert.Equal(original, rt.Speech);

        rt.SaveFoundry(foundry, DateTimeOffset.UtcNow, speechVerified: true);
        Assert.Equal(original, rt.Speech);
        rt.AiSettings.Update(s => s with { UseFoundrySpeech = null }); // Legacy settings had no explicit source.
        Assert.Equal(endpoint, rt.Speech!.Endpoint);
        var file = Path.Combine(_root, "probe.wav");
        await File.WriteAllBytesAsync(file, [0], ct); // The fake transport only checks routing and authentication.
        await rt.Asr!.TranscribeAsync(file, 1, [], 1, ct);
        // the MAI-Transcribe region check (one second of silence) may precede the upload; every request must take the same route and authentication
        Assert.Single(handler.Requests, r => r.Body.Contains("probe.wav"));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(endpoint + $"speechtotext/transcriptions:transcribe?api-version={AzureFastTranscriber.ApiVersion}", request.Uri.AbsoluteUri);
            Assert.Equal(auth == "key" ? "shared-key" : null, request.Key);
            Assert.Equal(auth == "entra" ? "Bearer speech-test-token" : null, request.Authorization);
        });
        using var settings = JsonDocument.Parse(await http.GetStringAsync("/api/settings", ct));
        Assert.Equal("foundry", settings.RootElement.GetProperty("speech").GetProperty("source").GetString());
        Assert.Equal(endpoint.TrimEnd('/'), settings.RootElement.GetProperty("transmission").GetProperty("asr_endpoint").GetString());
        Assert.DoesNotContain("shared-key", settings.RootElement.GetRawText());

        var dedicated = new SpeechConnection("https://dedicated.cognitiveservices.azure.com/");
        rt.SaveSpeech(dedicated, DateTimeOffset.UtcNow);
        Assert.Equal(dedicated, rt.Speech);
        rt.AiSettings.Update(s => s with { Speech = null, Foundry = null });
        Assert.Equal(original, rt.Speech);
    }

    [Fact]
    public async Task Speech_source_choice_preserves_separate_credentials_and_never_switches_away_from_selected_foundry()
    {
        await using var app = App();
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        const string url = "/api/settings/ai/speech/source";
        const string shared = "https://shared.cognitiveservices.azure.com/";
        const string separate = "https://dedicated.cognitiveservices.azure.com/";
        var foundry = new FoundryConnection(shared + "openai/v1/", "gpt", null);
        rt.SaveSpeech(new SpeechConnection(separate, "key", ApiKey: "test-dedicated-key"), DateTimeOffset.UtcNow);
        var savedSpeech = rt.AiSettings.Load().Speech;

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync(url, new { }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync(url, new { use_foundry = true }, ct)).StatusCode);
        Assert.Null(rt.AiSettings.Load().UseFoundrySpeech);
        rt.SaveFoundry(foundry, DateTimeOffset.UtcNow, speechVerified: true);
        Assert.Equal(separate, rt.Speech!.Endpoint);

        Assert.Equal(HttpStatusCode.NoContent, (await http.PutAsJsonAsync(url, new { use_foundry = true }, ct)).StatusCode);
        var reloaded = new AiSettingsStore(rt.AiSettings.Path, rt.AiSettings.Protector).Load();
        Assert.True(reloaded.UseFoundrySpeech);
        Assert.Equal(savedSpeech, reloaded.Speech);
        Assert.Equal(shared, rt.Speech!.Endpoint);
        using var settings = JsonDocument.Parse(await http.GetStringAsync("/api/settings", ct));
        var speech = settings.RootElement.GetProperty("speech");
        Assert.True(speech.GetProperty("foundry_available").GetBoolean());
        Assert.Equal(separate, speech.GetProperty("separate").GetProperty("endpoint").GetString());
        Assert.Equal(shared.TrimEnd('/'), settings.RootElement.GetProperty("transmission").GetProperty("asr_endpoint").GetString());
        Assert.DoesNotContain("test-dedicated-key", settings.RootElement.GetRawText());

        rt.SaveFoundry(foundry, DateTimeOffset.UtcNow, speechVerified: false);
        Assert.Null(rt.Speech);
        Assert.Null(rt.Asr);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PutAsJsonAsync(url, new { use_foundry = false }, ct)).StatusCode);
        Assert.Equal("test-dedicated-key", rt.Speech!.ApiKey);
        rt.SaveFoundry(foundry, DateTimeOffset.UtcNow, speechVerified: true);
        Assert.Equal(separate, rt.Speech!.Endpoint);
    }

    sealed class MaiRegionHandler(bool regionServesMai) : HttpMessageHandler
    {
        public readonly List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Bodies.Add(body);
            // exact reply of a japaneast resource (2026-11); MAI regions answer the same request with 200
            return !regionServesMai && body.Contains("enhancedMode")
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"code":"InvalidRequest","message":"Enhanced mode with model is currently not supported yet."}""") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"durationMilliseconds":1000,"phrases":[]}""") };
        }
    }

    [Fact]
    public async Task Mai_transcription_sends_verbatim_enhanced_mode_without_maxSpeakers_and_reports_the_model()
    {
        using var handler = new MaiRegionHandler(regionServesMai: true);
        using var http = new HttpClient(handler);
        var asr = new AzureFastTranscriber(new Uri("https://mai-ok.cognitiveservices.azure.com/"), "k", http, "ja-JP", AzureFastTranscriber.MaiModel);
        Assert.Equal(AzureFastTranscriber.MaiName, asr.Name);
        Assert.Equal(AzureFastTranscriber.MaiName, (await asr.ProbeAsync(TestContext.Current.CancellationToken)).Provider);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("\"enhancedMode\":{\"enabled\":true,\"model\":\"MAI-Transcribe-2\",\"modelOptions\":{\"timestamps\":\"word\",\"transcribeStyle\":\"verbatim\"}}", body);
        Assert.DoesNotContain("maxSpeakers", body);   // MAI rejects it with 400
        Assert.Contains("\"diarization\":{\"enabled\":true}", body);
    }

    [Fact]
    public async Task Regions_without_MAI_fall_back_to_the_standard_model_after_one_silent_probe()
    {
        using var handler = new MaiRegionHandler(regionServesMai: false);
        using var http = new HttpClient(handler);
        var asr = new AzureFastTranscriber(new Uri("https://mai-unsupported.cognitiveservices.azure.com/"), "k", http, "ja-JP", AzureFastTranscriber.MaiModel);
        var audio = Path.Combine(Path.GetTempPath(), $"vc-mai-{Guid.NewGuid():N}.flac");
        await File.WriteAllBytesAsync(audio, new byte[64], TestContext.Current.CancellationToken);
        try
        {
            var tr = await asr.TranscribeAsync(audio, 10, [], 4, TestContext.Current.CancellationToken);
            Assert.Equal(AzureFastTranscriber.StandardName, tr.Provider);
            Assert.Equal(2, handler.Bodies.Count);
            Assert.Contains("connection-test.wav", handler.Bodies[0]);   // the recording itself is uploaded once, to the standard model
            Assert.DoesNotContain("enhancedMode", handler.Bodies[1]);
            Assert.Contains("\"maxSpeakers\":4", handler.Bodies[1]);
            await asr.TranscribeAsync(audio, 10, [], 4, TestContext.Current.CancellationToken);
            Assert.Equal(3, handler.Bodies.Count);   // the region answer is remembered
            Assert.Equal("standard", asr.ExpectedModel);
            Assert.Equal(AzureFastTranscriber.StandardName, (await asr.ProbeAsync(TestContext.Current.CancellationToken)).Provider);
        }
        finally { File.Delete(audio); }
    }

    [Fact]
    public void Mai_word_onsets_are_held_until_the_next_word_at_most_300_ms_within_the_phrase()
    {
        // MAI returns one character per word with a fixed 80 ms placeholder length
        const string json = """
            {"durationMilliseconds":5000,"phrases":[{"offsetMilliseconds":1000,"durationMilliseconds":1500,"text":"あい。","speaker":1,"words":[
              {"text":"あ","offsetMilliseconds":1000,"durationMilliseconds":80},
              {"text":"い","offsetMilliseconds":1150,"durationMilliseconds":80},
              {"text":"。","offsetMilliseconds":2400,"durationMilliseconds":80}]}]}
            """;
        var mai = AzureFastTranscriber.Parse(json, 5, AzureFastTranscriber.MaiName).Words;
        Assert.Equal(1.15, mai[0].T1, 3);   // next onset
        Assert.Equal(1.45, mai[1].T1, 3);   // 300 ms cap keeps the pause before the next word visible
        Assert.Equal(2.5, mai[2].T1, 3);    // phrase end
        Assert.Equal(1.08, AzureFastTranscriber.Parse(json, 5, AzureFastTranscriber.StandardName).Words[0].T1, 3);   // standard word times are real
    }

    [Theory]
    [InlineData("key")]
    [InlineData("entra")]
    public async Task Speech_probe_uses_exactly_the_selected_authentication_and_the_generation_API(string auth)
    {
        using var handler = new SpeechHandler();
        using var http = new HttpClient(handler);
        var credential = new SpeechCredential();
        var endpoint = new Uri("https://speech.cognitiveservices.azure.com/");
        var transcriber = auth == "key" ? new AzureFastTranscriber(endpoint, "speech-secret", http, "en-US")
            : new AzureFastTranscriber(endpoint, credential, http, "en-US");
        var transcript = await transcriber.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Empty(transcript.Words);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/speechtotext/transcriptions:transcribe", request.Uri.AbsolutePath);
        Assert.Equal("?api-version=" + AzureFastTranscriber.ApiVersion, request.Uri.Query);
        Assert.Contains("connection-test.wav", request.Body);
        Assert.Contains("audio/wav", request.Body);
        Assert.Contains("\"locales\":[\"en-US\"]", request.Body);
        if (auth == "key")
        {
            Assert.Equal("speech-secret", request.Key);
            Assert.Null(request.Authorization);
            Assert.Null(credential.Scopes);
        }
        else
        {
            Assert.Null(request.Key);
            Assert.Equal("Bearer speech-test-token", request.Authorization);
            Assert.NotNull(credential.Scopes);
            Assert.Equal(["https://cognitiveservices.azure.com/.default"], credential.Scopes);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://speech.cognitiveservices.azure.com/")]
    [InlineData("https://user:password@speech.cognitiveservices.azure.com/")]
    [InlineData("https://speech.cognitiveservices.azure.com/?key=secret")]
    [InlineData("https://speech.cognitiveservices.azure.com/#secret")]
    [InlineData("https://speech.cognitiveservices.azure.com/speechtotext")]
    public void Unsafe_or_non_root_Speech_endpoints_are_rejected(string endpoint) =>
        Assert.Throws<ArgumentException>(() => SpeechConnection.NormalizeEndpoint(endpoint));

    [Fact]
    public async Task Speech_tests_before_saving_protects_keys_and_prevents_reuse_on_a_different_destination()
    {
        using var handler = new SpeechHandler();
        await using var app = App(speechHandler: handler);
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        const string endpoint = "https://speech.cognitiveservices.azure.com/";
        const string secret = "speech-secret-not-for-display";
        var body = new { endpoint, auth = "key", api_key = secret };

        var test = await http.PostAsJsonAsync("/api/settings/ai/speech/test", body, ct);
        Assert.Contains("\"ok\":true", await test.Content.ReadAsStringAsync(ct));
        Assert.Null(rt.AiSettings.Load().Speech);
        var saved = await http.PutAsJsonAsync("/api/settings/ai/speech", body, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(secret, await saved.Content.ReadAsStringAsync(ct));
        Assert.DoesNotContain(secret, File.ReadAllText(rt.AiSettings.Path));
        Assert.Equal(secret, rt.Speech!.ApiKey);
        Assert.Equal("key", rt.Speech.Auth);
        Assert.Equal("key", new AiSettingsStore(rt.AiSettings.Path, AiSettingsStore.DefaultProtector()).Load().Speech!.Auth);
        var audioPath = Path.Combine(_root, "generation.flac");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3], ct);
        await rt.AsrForLanguage("ko-KR")!.TranscribeAsync(audioPath, 1, ["project phrase"], 3, ct);
        Assert.Equal(secret, handler.Requests[^1].Key);
        Assert.Null(handler.Requests[^1].Authorization);
        Assert.Contains("\"locales\":[\"ko-KR\"]", handler.Requests[^1].Body);
        Assert.Contains("project phrase", handler.Requests[^1].Body);
        Assert.Contains("generation.flac", handler.Requests[^1].Body);

        var count = handler.Requests.Count;
        foreach (var other in new[] { "https://other.cognitiveservices.azure.com/", "https://speech.cognitiveservices.azure.com:8443/" })
        {
            var rejected = await http.PutAsJsonAsync("/api/settings/ai/speech", new { endpoint = other, auth = "key", api_key = "" }, ct);
            Assert.Equal((HttpStatusCode)422, rejected.StatusCode);
            Assert.Contains("invalid_input", await rejected.Content.ReadAsStringAsync(ct));
        }
        Assert.Equal(count, handler.Requests.Count);
        var reused = AiConnectionApi.SpeechInput(rt, new(endpoint, "key", null, ""));
        Assert.Equal(secret, reused.ApiKey);

        handler.Status = HttpStatusCode.Forbidden;
        handler.Response = """{"error":{"message":"rejected speech-secret-not-for-display"}}""";
        var failure = await http.PutAsJsonAsync("/api/settings/ai/speech", body, ct);
        Assert.Equal((HttpStatusCode)422, failure.StatusCode);
        var failedBody = await failure.Content.ReadAsStringAsync(ct);
        Assert.Contains("speech_forbidden", failedBody);
        Assert.DoesNotContain(secret, failedBody);
        Assert.Equal(secret, rt.Speech.ApiKey);

        rt.SaveSpeech(new SpeechConnection(endpoint, "entra", "16b3c013-d300-468d-ac64-7eda0820b6d3"), DateTimeOffset.Now);
        Assert.Null(rt.AiSettings.Load().Speech!.ApiKeyProtected);
        Assert.Null(rt.Speech.ApiKey);
        Assert.Equal("16b3c013-d300-468d-ac64-7eda0820b6d3", rt.Speech.TenantId);
        Assert.DoesNotContain(secret, await http.GetStringAsync("/api/settings", ct));

        await http.DeleteAsync("/api/settings/ai/speech", ct);
        Assert.Null(rt.AiSettings.Load().Speech);
        Assert.Equal(rt.Options.Asr.Endpoint, rt.Speech!.Endpoint);
    }

    [Fact]
    public async Task Settings_drafts_survive_a_restart_carry_no_secret_are_never_used_and_end_when_saved()
    {
        const string secret = "draft-secret-that-must-not-be-kept";
        const string draftEndpoint = "https://draft-only.cognitiveservices.azure.com/";
        var ct = TestContext.Current.CancellationToken;
        await using (var first = App())
        {
            using var http = Client(first);
            var rt = first.Services.GetRequiredService<Runtime>();
            var before = rt.Speech?.Endpoint;
            var put = await http.PutAsJsonAsync("/api/settings/drafts/speech", new { endpoint = draftEndpoint, auth = "key", tenant_id = "", api_key = secret }, ct);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            (await http.PutAsJsonAsync("/api/settings/drafts/copilot", new { auth = "token", github_token = secret, model = "gpt-5", reasoning_effort = "high", context_tier = "huge" }, ct)).EnsureSuccessStatusCode();
            (await http.PutAsJsonAsync("/api/settings/drafts/projects_dir", new { path = @"D:\Videos", move = "keep" }, ct)).EnsureSuccessStatusCode();
            Assert.Equal((HttpStatusCode)422, (await http.PutAsJsonAsync("/api/settings/drafts/foundry", new { endpoint = new string('x', 3000) }, ct)).StatusCode);
            Assert.Equal(before, rt.Speech?.Endpoint);   // a draft is not a connection
            Assert.Null(rt.AiSettings.Load().Speech);
            Assert.All(rt.Store.RawAll(), d => Assert.DoesNotContain(secret, d.Json));
        }

        using var handler = new SpeechHandler();
        await using var second = App(speechHandler: handler);
        second.Services.GetRequiredService<Runtime>().CredentialFactory = _ => new SpeechCredential();
        using (var http = Client(second))
        {
            using (var drafts = JsonDocument.Parse(await http.GetStringAsync("/api/settings/drafts", ct)))
            {
                var root = drafts.RootElement;
                Assert.Equal(draftEndpoint, root.GetProperty("speech").GetProperty("endpoint").GetString());
                Assert.Equal("key", root.GetProperty("speech").GetProperty("auth").GetString());
                Assert.False(root.GetProperty("speech").TryGetProperty("tenant_id", out _));
                Assert.False(root.GetProperty("speech").TryGetProperty("api_key", out _));
                Assert.Equal("gpt-5", root.GetProperty("copilot").GetProperty("model").GetString());
                Assert.False(root.GetProperty("copilot").TryGetProperty("context_tier", out _));   // not a known tier
                Assert.Equal("keep", root.GetProperty("projects_dir").GetProperty("move").GetString());
                Assert.False(root.TryGetProperty("foundry", out _));   // rejected as too long
                Assert.DoesNotContain(secret, root.GetRawText());
            }

            var saved = await http.PutAsJsonAsync("/api/settings/ai/speech", new { endpoint = "https://speech.cognitiveservices.azure.com/", auth = "entra" }, ct);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync("/api/settings/drafts/copilot", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync("/api/settings/drafts/other", ct)).StatusCode);
            using var after = JsonDocument.Parse(await http.GetStringAsync("/api/settings/drafts", ct));
            Assert.False(after.RootElement.TryGetProperty("speech", out _));   // saved → the draft is done
            Assert.False(after.RootElement.TryGetProperty("copilot", out _));
            Assert.Equal(JsonValueKind.Object, after.RootElement.GetProperty("projects_dir").ValueKind);
        }
    }

    [Fact]
    public async Task Speech_remains_usable_without_an_LLM_and_secret_storage_limitations_are_explicit()
    {
        using var handler = new SpeechHandler();
        await using var app = App(localOnly: true, speechHandler: handler);
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        var body = new { endpoint = "https://speech.cognitiveservices.azure.com/", auth = "key", api_key = "speech-secret" };
        var success = await http.PostAsJsonAsync("/api/settings/ai/speech/test", body, ct);
        Assert.Contains("\"ok\":true", await success.Content.ReadAsStringAsync(ct));
        Assert.Single(handler.Requests);
        rt.AiSettings = new AiSettingsStore(rt.AiSettings.Path, new NoSecretStorage());
        var refused = await http.PutAsJsonAsync("/api/settings/ai/speech", body, ct);
        Assert.Equal((HttpStatusCode)422, refused.StatusCode);
        Assert.Contains("secret_storage_unavailable", await refused.Content.ReadAsStringAsync(ct));
        Assert.Null(rt.AiSettings.Load().Speech);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_unreadable_Speech_key_does_not_break_Settings_and_fails_transcription_visibly()
    {
        await using var app = App();
        using var http = Client(app);
        var rt = app.Services.GetRequiredService<Runtime>();
        rt.AiSettings.Update(s => s with { Speech = new("https://speech.cognitiveservices.azure.com/", "key", null, "unreadable", DateTimeOffset.Now) });
        using var settings = JsonDocument.Parse(await http.GetStringAsync("/api/settings", TestContext.Current.CancellationToken));
        Assert.True(settings.RootElement.GetProperty("speech").GetProperty("api_key_unreadable").GetBoolean());
        var asr = rt.AsrForLanguage("ja-JP");
        Assert.NotNull(asr);
        var error = await Assert.ThrowsAsync<AsrException>(() => asr.TranscribeAsync("missing.flac", 1, [], 2, TestContext.Current.CancellationToken));
        Assert.Contains("Speech の API キー", error.Message);
    }

    /// <summary>ARM as observed live: an empty first page with a nextLink, rich <c>properties.endpoints</c>, per-scope permissions.</summary>
    sealed class FakeArm : HttpMessageHandler
    {
        public readonly List<(Uri Uri, string? Authorization)> Requests = [];
        public readonly Dictionary<string, (HttpStatusCode Status, string Body)> Routes = new(StringComparer.OrdinalIgnoreCase);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            var (status, body) = Routes.TryGetValue(request.RequestUri!.PathAndQuery, out var r) ? r : (HttpStatusCode.NotFound, """{"error":{"code":"NotFound","message":"no route"}}""");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    const string Sub1 = "11111111-1111-1111-1111-111111111111", Sub2 = "22222222-2222-2222-2222-222222222222";
    static string AccountJson(string sub, string rg, string name, string kind, string? domain, bool disableLocalAuth, params string[] apis) => JsonSerializer.Serialize(new
    {
        id = $"/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{name}",
        name, kind, location = "japaneast",
        properties = new
        {
            endpoint = domain != null ? $"https://{domain}.cognitiveservices.azure.com/" : "https://japaneast.api.cognitive.microsoft.com/",
            customSubDomainName = domain, disableLocalAuth, provisioningState = "Succeeded",
            endpoints = apis.ToDictionary(a => a, _ => "https://x/"),
        },
    });

    static FakeArm Arm()
    {
        var arm = new FakeArm();
        var accounts = $"/subscriptions/{Sub1}/providers/Microsoft.CognitiveServices/accounts?api-version={AzureResourceCatalog.AccountsApiVersion}";
        var ai = AccountJson(Sub1, "rg-a", "ai-both", "AIServices", "ai-both", true, "Speech Services Speech to Text v3.2", "OpenAI Language Model Instance API");
        arm.Routes["/subscriptions?api-version=2022-12-01"] = (HttpStatusCode.OK, $$"""{"value":[{"subscriptionId":"{{Sub1}}","displayName":"Main","state":"Enabled","tenantId":"t"},{"subscriptionId":"{{Sub2}}","displayName":"Locked","state":"Enabled","tenantId":"t"}]}""");
        arm.Routes[accounts] = (HttpStatusCode.OK, $$"""{"value":[],"nextLink":"https://management.azure.com{{accounts}}&%24skiptoken=p2"}""");
        arm.Routes[accounts + "&%24skiptoken=p2"] = (HttpStatusCode.OK, $$"""{"value":[{{ai}},{{AccountJson(Sub1, "rg-b", "speech-regional", "SpeechServices", null, false, "Speech Services Speech to Text (Standard)")}},{{AccountJson(Sub1, "rg-c", "vision", "ComputerVision", "vision", true, "Computer Vision")}}],"nextLink":"https://attacker.example.com/steal"}""");
        arm.Routes[$"/subscriptions/{Sub2}/providers/Microsoft.CognitiveServices/accounts?api-version={AzureResourceCatalog.AccountsApiVersion}"] =
            (HttpStatusCode.Forbidden, """{"error":{"code":"AuthorizationFailed","message":"no read"}}""");
        // Speech User only (its real dataActions) on ai-both; nothing readable on speech-regional
        arm.Routes[$"/subscriptions/{Sub1}/resourceGroups/rg-a/providers/Microsoft.CognitiveServices/accounts/ai-both/providers/Microsoft.Authorization/permissions?api-version=2022-04-01"] =
            (HttpStatusCode.OK, """{"value":[{"actions":["*/read"],"dataActions":[]},{"actions":[],"dataActions":["Microsoft.CognitiveServices/accounts/SpeechServices/*/transcriptions/action"],"notDataActions":[]},{"actions":[],"dataActions":["Microsoft.CognitiveServices/*"],"notDataActions":["Microsoft.CognitiveServices/accounts/OpenAI/*"]}]}""");
        arm.Routes[$"/subscriptions/{Sub1}/resourceGroups/rg-a/providers/Microsoft.CognitiveServices/accounts/ai-both/deployments?api-version={AzureResourceCatalog.AccountsApiVersion}"] =
            (HttpStatusCode.OK, """{"value":[{"name":"emb","sku":{"name":"GlobalStandard"},"properties":{"model":{"name":"text-embedding-3-large","version":"1"},"provisioningState":"Succeeded","capabilities":{"embeddings":"true"}}},{"name":"chat","sku":{"name":"GlobalStandard"},"properties":{"model":{"name":"gpt-4.1","version":"2025-04-14"},"provisioningState":"Succeeded","capabilities":{"chatCompletion":"true","responses":"true"}}}]}""");
        return arm;
    }

    [Fact]
    public async Task Azure_catalog_pages_through_ARM_keeps_only_Speech_or_Foundry_and_reports_effective_permissions()
    {
        using var arm = Arm();
        var credential = new SpeechCredential();
        var catalog = new AzureResourceCatalog(credential, new HttpClient(arm));
        var ct = TestContext.Current.CancellationToken;

        var r = await catalog.ListAccountsAsync(ct);

        Assert.NotNull(credential.Scopes);
        Assert.Equal([AzureResourceCatalog.Scope], credential.Scopes);
        Assert.Equal(2, r.Subscriptions.Count);
        Assert.Equal(["ai-both", "speech-regional"], r.Accounts.Select(a => a.Name));   // ComputerVision has no Speech / OpenAI API
        var ai = r.Accounts[0];
        Assert.True(ai.Speech && ai.Foundry && ai.KeyAuthDisabled && ai.CustomDomain);
        Assert.Equal("https://ai-both.cognitiveservices.azure.com/", ai.Endpoint);
        Assert.Equal((true, false, false), (ai.CanTranscribe, ai.CanChat, ai.CanEmbed));   // notDataActions removes OpenAI
        var regional = r.Accounts[1];
        Assert.True(regional.Speech && !regional.Foundry && !regional.CustomDomain);
        Assert.Null(regional.CanTranscribe);   // permissions unreadable → unknown, not "denied"
        var issue = Assert.Single(r.Issues);
        Assert.Equal(("Locked", "forbidden"), (issue.Scope, issue.Code));
        Assert.All(arm.Requests, q => Assert.Equal("management.azure.com", q.Uri.Host));   // the attacker nextLink was not followed
        Assert.All(arm.Requests, q => Assert.Equal("Bearer speech-test-token", q.Authorization));

        var deployments = await catalog.ListDeploymentsAsync(ai.Id, ct);
        Assert.Equal(["chat", "emb"], deployments.Select(d => d.Name));
        Assert.True(deployments[0].Chat && !deployments[0].Embeddings);
        Assert.True(deployments[1].Embeddings && !deployments[1].Chat);
        Assert.Equal("gpt-4.1", deployments[0].Model);
    }

    [Fact]
    public async Task Azure_catalog_API_uses_the_entered_tenant_and_rejects_resource_ids_outside_Cognitive_Services()
    {
        using var arm = Arm();
        await using var app = App(speechHandler: arm);
        using var http = Client(app);
        var ct = TestContext.Current.CancellationToken;
        var rt = app.Services.GetRequiredService<Runtime>();
        var tenants = new List<string?>();
        rt.CredentialFactory = t => { tenants.Add(t); return new SpeechCredential(); };

        var r = await http.PostAsJsonAsync("/api/settings/ai/azure/resources", new { tenant_id = "contoso.onmicrosoft.com" }, ct);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)))
        {
            var first = doc.RootElement.GetProperty("accounts")[0];
            Assert.Equal("https://ai-both.cognitiveservices.azure.com/", first.GetProperty("endpoint").GetString());
            Assert.True(first.GetProperty("can_transcribe").GetBoolean());
            Assert.True(first.GetProperty("key_auth_disabled").GetBoolean());
        }
        Assert.Equal(["contoso.onmicrosoft.com"], tenants);

        var sent = arm.Requests.Count;
        r = await http.PostAsJsonAsync("/api/settings/ai/azure/deployments", new { account_id = $"/subscriptions/{Sub1}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/x" }, ct);
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        Assert.Contains("\"invalid_input\"", await r.Content.ReadAsStringAsync(ct));
        Assert.Equal(sent, arm.Requests.Count);
    }
}
