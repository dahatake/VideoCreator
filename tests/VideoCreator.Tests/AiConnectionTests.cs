using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
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

    WebApplicationFactory<Program> App(bool localOnly = false) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("VideoCreator:DataDir", Path.Combine(_root, "data"));
        b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
        b.UseSetting("VideoCreator:OpenBrowser", "false");
        b.UseSetting("VideoCreator:LocalOnly", localOnly ? "true" : "false");
        b.UseSetting("VideoCreator:Llm:Endpoint", "");
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
        rt.SaveFoundry(new FoundryConnection("https://r1.openai.azure.com/openai/v1/", "gpt", null, "key", ApiKey: secret), DateTimeOffset.Now);
        rt.SaveCopilot(new CopilotConnection("token", "github_pat_must_not_leave_0123"), "me", DateTimeOffset.Now);

        var settings = await http.GetStringAsync("/api/settings", ct);
        Assert.DoesNotContain(secret, settings);
        Assert.DoesNotContain("github_pat_must_not_leave", settings);
        using var doc = JsonDocument.Parse(settings);
        Assert.True(doc.RootElement.GetProperty("foundry").GetProperty("has_api_key").GetBoolean());
        Assert.Equal("app", doc.RootElement.GetProperty("foundry").GetProperty("source").GetString());
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
}
