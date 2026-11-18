using System.Diagnostics;
using VideoCreator.Asr;
using VideoCreator.Selection;

namespace VideoCreator.App;

/// <summary>
/// Settings screen: enter → test → save the Microsoft Foundry and GitHub Copilot connections. A connection is saved only after the same values pass
/// the test, secrets are stored protected (<see cref="AiSettingsStore"/>) and never returned or logged.
/// </summary>
public static class AiConnectionApi
{
    /// <summary>Empty <c>api_key</c> = keep the saved key (only for the same endpoint host).</summary>
    public sealed record FoundryRequest(string? Endpoint, string? Deployment, string? EmbeddingDeployment, string? Auth, string? TenantId, string? ApiKey);
    public sealed record SpeechRequest(string? Endpoint, string? Auth, string? TenantId, string? ApiKey);
    /// <summary>Empty <c>github_token</c> = keep the saved token. Null model / effort / tier = the runtime's default.</summary>
    public sealed record CopilotRequest(string? Auth, [property: System.Text.Json.Serialization.JsonPropertyName("github_token")] string? GitHubToken, string? Model = null, string? ReasoningEffort = null, string? ContextTier = null);
    public sealed record Step(string Id, bool Ok, long Ms, AiDiagnosis? Diagnosis = null);
    public sealed record TestResult(bool Ok, IReadOnlyList<Step> Steps, AiDiagnosis? Diagnosis, string? Endpoint = null, string? Login = null);
    /// <summary>Lists resources in the given (or default) tenant; <c>account_id</c> selects whose deployments to list.</summary>
    public sealed record AzureCatalogRequest(string? TenantId, string? AccountId = null);

    static readonly TimeSpan FoundryTimeout = TimeSpan.FromSeconds(60), CopilotTimeout = TimeSpan.FromMinutes(2);

    public static object FoundryView(Runtime rt)
    {
        var saved = rt.AiSettings.Load().Foundry;
        if (saved != null)
            return new
            {
                Source = "app", saved.Endpoint, saved.Deployment, saved.EmbeddingDeployment, saved.Auth, saved.TenantId, saved.TestedAt,
                HasApiKey = saved.ApiKeyProtected != null,
                ApiKeyUnreadable = saved.ApiKeyProtected != null && rt.AiSettings.Reveal(saved.ApiKeyProtected) == null,
            };
        var f = rt.Foundry;
        return f == null ? new { Source = "none", Auth = "entra" } : new { Source = "config", f.Endpoint, f.Deployment, f.EmbeddingDeployment, f.Auth, f.TenantId };
    }

    public static object SpeechView(Runtime rt)
    {
        var saved = rt.AiSettings.Load().Speech;
        if (saved != null)
            return new
            {
                Source = "app", saved.Endpoint, saved.Auth, saved.TenantId, saved.TestedAt,
                HasApiKey = saved.ApiKeyProtected != null,
                ApiKeyUnreadable = saved.ApiKeyProtected != null && rt.AiSettings.Reveal(saved.ApiKeyProtected) == null,
            };
        if (rt.Speech is not { } s) return new { Source = "none", Auth = "entra" };
        var endpoint = new UriBuilder(s.Endpoint) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.ToString();
        return new { Source = "config", Endpoint = endpoint, s.Auth, s.TenantId };
    }

    public static object CopilotView(Runtime rt)
    {
        var saved = rt.AiSettings.Load().Copilot;
        if (saved != null)
            return new
            {
                Source = "app", saved.Auth, saved.Model, saved.ReasoningEffort, saved.ContextTier, saved.Login, saved.TestedAt,
                HasToken = saved.TokenProtected != null,
                TokenUnreadable = saved.TokenProtected != null && rt.AiSettings.Reveal(saved.TokenProtected) == null,
            };
        var c = rt.Copilot;
        return new { Source = "config", c.Auth, c.Model };
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/settings/ai/azure/resources", async (Runtime rt, AzureCatalogRequest req, CancellationToken ct) =>
        {
            try
            {
                var catalog = rt.AzureCatalog(Tenant(req.TenantId));
                using var cts = Timeout(ct, FoundryTimeout);
                return Results.Json(await catalog.ListAccountsAsync(cts.Token), Json.Options);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Results.Json(Fail(AzureDiagnosis(e)), Json.Options, statusCode: 422);
            }
        }).WithSummary("Azure Speech and Microsoft Foundry resources the signed-in Entra ID identity can see in Azure Resource Manager, with its effective data permissions; never reads keys");
        api.MapPost("/settings/ai/azure/deployments", async (Runtime rt, AzureCatalogRequest req, CancellationToken ct) =>
        {
            try
            {
                var catalog = rt.AzureCatalog(Tenant(req.TenantId));
                using var cts = Timeout(ct, FoundryTimeout);
                return Results.Json(new { deployments = await catalog.ListDeploymentsAsync(req.AccountId ?? "", cts.Token) }, Json.Options);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Results.Json(Fail(AzureDiagnosis(e)), Json.Options, statusCode: 422);
            }
        }).WithSummary("Model deployments of one Azure AI account (chat / embedding capability from ARM)");

        api.MapPost("/settings/ai/speech/test", async (Runtime rt, SpeechRequest req, CancellationToken ct) =>
            Results.Json((await RunSpeechAsync(rt, req, ct)).Result, Json.Options))
            .WithSummary("Test Speech fast transcription using one second of synthetic silence; saves nothing");
        api.MapPut("/settings/ai/speech", async (Runtime rt, SpeechRequest req, CancellationToken ct) =>
        {
            if (req.Auth == "key" && !rt.AiSettings.Protector.StoresSecrets) return Results.Json(Fail(NoSecretStorageCode, NoSecretStorageMessage), Json.Options, statusCode: 422);
            var (result, conn) = await RunSpeechAsync(rt, req, ct);
            if (!result.Ok || conn == null) return Results.Json(result, Json.Options, statusCode: 422);
            rt.SaveSpeech(conn, DateTimeOffset.Now);
            return Results.Json(new { test = result, speech = SpeechView(rt) }, Json.Options);
        }).WithSummary("Test, then save the Speech connection only when fast transcription succeeds");
        api.MapDelete("/settings/ai/speech", (Runtime rt) =>
        {
            rt.AiSettings.Update(s => s with { Speech = null });
            return Results.Json(new { speech = SpeechView(rt) }, Json.Options);
        }).WithSummary("Forget the saved Speech connection and key; appsettings.json applies again");

        api.MapPost("/settings/ai/foundry/test", async (Runtime rt, FoundryRequest req, CancellationToken ct) =>
            Results.Json(await TestFoundryAsync(rt, req, ct), Json.Options))
            .WithSummary("Test a Microsoft Foundry connection (chat with the strict JSON schema, and the embedding deployment if given); saves nothing");
        api.MapPut("/settings/ai/foundry", async (Runtime rt, FoundryRequest req, CancellationToken ct) =>
        {
            if (req.Auth == "key" && !rt.AiSettings.Protector.StoresSecrets) return Results.Json(Fail(NoSecretStorageCode, NoSecretStorageMessage), Json.Options, statusCode: 422);
            var (result, conn) = await RunFoundryAsync(rt, req, ct);
            if (!result.Ok || conn == null) return Results.Json(result, Json.Options, statusCode: 422);
            rt.SaveFoundry(conn with { Endpoint = result.Endpoint! }, DateTimeOffset.Now);
            return Results.Json(new { test = result, foundry = FoundryView(rt) }, Json.Options);
        }).WithSummary("Test, then save the Microsoft Foundry connection (only when the test passes)");
        api.MapDelete("/settings/ai/foundry", (Runtime rt) =>
        {
            rt.AiSettings.Update(s => s with { Foundry = null });
            return Results.Json(new { foundry = FoundryView(rt) }, Json.Options);
        }).WithSummary("Forget the saved Foundry connection and its key (appsettings.json applies again)");

        api.MapPost("/settings/ai/copilot/models", async (Runtime rt, CopilotRequest req, CancellationToken ct) =>
        {
            if (rt.LocalOnly) return Results.Json(Fail(LocalOnlyCode, LocalOnlyMessage), Json.Options, statusCode: 422);
            if (rt.CopilotCli is not { } cli) return Results.Json(Fail("copilot_cli", "GitHub Copilot CLI が見つかりません。winget install GitHub.Copilot でインストールしてください"), Json.Options, statusCode: 422);
            CopilotConnection c;
            try { c = CopilotInput(rt, req); }
            catch (ArgumentException e) { return Results.Json(Fail("invalid_input", e.Message), Json.Options, statusCode: 422); }
            try
            {
                using var cts = Timeout(ct, CopilotTimeout);
                var cat = await CopilotCatalog.LoadAsync(cli, rt.CopilotWorkDir, c, cts.Token);
                return Results.Json(new { account = cat.Account, models = cat.Models }, Json.Options);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Results.Json(Fail(Scrub(AiDiagnostics.Classify(e, "copilot"), c.GitHubToken)), Json.Options, statusCode: 422);
            }
        }).WithSummary("Sign-in state and the models this GitHub account may use (reasoning efforts, context tiers, credit prices)");
        api.MapPost("/settings/ai/copilot/test", async (Runtime rt, CopilotRequest req, CancellationToken ct) =>
            Results.Json((await RunCopilotAsync(rt, req, ct)).Result, Json.Options))
            .WithSummary("Test the GitHub Copilot connection with the chosen model, reasoning effort and context size; saves nothing");
        api.MapPut("/settings/ai/copilot", async (Runtime rt, CopilotRequest req, CancellationToken ct) =>
        {
            if (req.Auth == "token" && !rt.AiSettings.Protector.StoresSecrets) return Results.Json(Fail(NoSecretStorageCode, NoSecretStorageMessage), Json.Options, statusCode: 422);
            var (result, conn) = await RunCopilotAsync(rt, req, ct);
            if (!result.Ok || conn == null) return Results.Json(result, Json.Options, statusCode: 422);
            rt.SaveCopilot(conn, result.Login, DateTimeOffset.Now);
            return Results.Json(new { test = result, copilot = CopilotView(rt) }, Json.Options);
        }).WithSummary("Test, then save the GitHub Copilot connection (only when the test passes)");
        api.MapDelete("/settings/ai/copilot", (Runtime rt) =>
        {
            rt.AiSettings.Update(s => s with { Copilot = null });
            return Results.Json(new { copilot = CopilotView(rt) }, Json.Options);
        }).WithSummary("Forget the saved Copilot connection and its token");
    }

    const string LocalOnlyCode = "local_only", LocalOnlyMessage = "LLM不使用モードでは AI に接続しません（接続テストも送信しません）";
    const string NoSecretStorageCode = "secret_storage_unavailable", NoSecretStorageMessage = "この OS では API キーとトークンを暗号化して保存できないため、保存しません。Microsoft Entra ID またはサインイン済みの GitHub ユーザーを選んでください";
    static TestResult Fail(string code, string message) => new(false, [], new AiDiagnosis(code, message));
    static TestResult Fail(AiDiagnosis d) => new(false, [], d);

    static AiDiagnosis AzureDiagnosis(Exception e) => e switch
    {
        ArgumentException => new("invalid_input", e.Message),
        AzureArmException { Status: System.Net.HttpStatusCode.Unauthorized } arm => new("arm_unauthorized", "Azure Resource Manager がトークンを受け付けませんでした。リソースのあるテナントに az login し、必要ならテナント ID を入力してください", arm.Message),
        AzureArmException { Status: System.Net.HttpStatusCode.Forbidden } arm => new("arm_forbidden", "このアカウントにはサブスクリプションやリソースを一覧する権限がありません。エンドポイントを直接入力するか、管理者に閲覧者ロールを依頼してください", arm.Message),
        AzureArmException arm => new("http_" + (int)arm.Status, $"Azure Resource Manager の呼び出しに失敗しました（HTTP {(int)arm.Status}）", arm.Message),
        _ => AiDiagnostics.Classify(e, "azure"),
    };

    static CancellationTokenSource Timeout(CancellationToken ct, TimeSpan after)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(after);
        return cts;
    }

    /// <summary>A provider message must never echo a secret back to the browser.</summary>
    static AiDiagnosis Scrub(AiDiagnosis d, params string?[] secrets) =>
        d with { Detail = secrets.Where(s => !string.IsNullOrWhiteSpace(s) && s.Length >= 4).Aggregate(d.Detail, (t, s) => t?.Replace(s!.Trim(), "***", StringComparison.Ordinal)) };

    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static string? Tenant(string? input)
    {
        var tenant = Clean(input);
        if (tenant != null && !Guid.TryParse(tenant, out _) && !System.Text.RegularExpressions.Regex.IsMatch(tenant, @"^[a-z0-9-]+(\.[a-z0-9-]+)+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("テナント ID は GUID（例: 72f988bf-…）か、ドメイン名（例: contoso.onmicrosoft.com）で入力してください");
        return tenant;
    }

    public static SpeechConnection SpeechInput(Runtime rt, SpeechRequest req)
    {
        var endpoint = SpeechConnection.NormalizeEndpoint(req.Endpoint).ToString();
        var auth = req.Auth is "key" ? "key" : req.Auth is null or "entra" ? "entra" : throw new ArgumentException("認証方式は entra | key です");
        var tenant = auth == "entra" ? Tenant(req.TenantId) : null;
        var key = auth == "key" ? Clean(req.ApiKey) : null;
        if (auth == "key" && key == null)
        {
            var saved = rt.AiSettings.Load().Speech;
            if (saved is { Auth: "key", ApiKeyProtected: not null }
                && new Uri(saved.Endpoint).GetLeftPart(UriPartial.Authority).Equals(new Uri(endpoint).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                key = rt.AiSettings.Reveal(saved.ApiKeyProtected) ?? throw new ArgumentException("保存した API キーをこの Windows ユーザーでは読めません。キーを入力し直してください");
            else throw new ArgumentException("API キーを入力してください。別の接続先に保存済みのキーは送信しません。");
        }
        return new SpeechConnection(endpoint, auth, tenant, key);
    }

    public static FoundryConnection FoundryInput(Runtime rt, FoundryRequest req)
    {
        var endpoint = FoundryEndpoint.Normalize(req.Endpoint).ToString();
        var deployment = Clean(req.Deployment) ?? throw new ArgumentException("チャット用モデルのデプロイ名を入力してください（Foundry ポータルの「デプロイ」に表示される名前）");
        var auth = req.Auth is "key" ? "key" : req.Auth is null or "entra" ? "entra" : throw new ArgumentException("認証方式は entra | key です");
        var tenant = Tenant(req.TenantId);
        var key = auth == "key" ? Clean(req.ApiKey) : null;
        if (auth == "key" && key == null)
        {
            // the stored key is reused only for the host it was saved for, so it is never sent to a different server
            var saved = rt.AiSettings.Load().Foundry;
            if (saved?.ApiKeyProtected != null && saved.Auth == "key" && new Uri(saved.Endpoint).Host == new Uri(endpoint).Host)
                key = rt.AiSettings.Reveal(saved.ApiKeyProtected) ?? throw new ArgumentException("保存した API キーをこの Windows ユーザーでは読めません。キーを入力し直してください");
            else throw new ArgumentException(saved?.ApiKeyProtected != null ? "エンドポイントが変わったため、保存した API キーは使いません。キーを入力してください" : "API キーを入力してください（Azure portal の「キーとエンドポイント」）");
        }
        return new FoundryConnection(endpoint, deployment, Clean(req.EmbeddingDeployment), auth, auth == "entra" ? tenant : null, key);
    }

    public static CopilotConnection CopilotInput(Runtime rt, CopilotRequest req)
    {
        var auth = req.Auth is "token" ? "token" : req.Auth is null or "user" ? "user" : throw new ArgumentException("認証方式は user | token です");
        var token = auth == "token" ? Clean(req.GitHubToken) : null;
        if (auth == "token" && token == null)
        {
            var saved = rt.AiSettings.Load().Copilot;
            token = saved?.TokenProtected != null && saved.Auth == "token"
                ? rt.AiSettings.Reveal(saved.TokenProtected) ?? throw new ArgumentException("保存したトークンをこの Windows ユーザーでは読めません。トークンを入力し直してください")
                : throw new ArgumentException("GitHub トークンを入力してください（Copilot Requests 権限のある fine-grained personal access token）");
        }
        var tier = Clean(req.ContextTier);
        return new CopilotConnection(auth, token, Clean(req.Model), Clean(req.ReasoningEffort), tier == "default" ? null : tier);
    }

    static async Task<TestResult> TestFoundryAsync(Runtime rt, FoundryRequest req, CancellationToken ct) => (await RunFoundryAsync(rt, req, ct)).Result;

    static async Task<(TestResult Result, SpeechConnection? Conn)> RunSpeechAsync(Runtime rt, SpeechRequest req, CancellationToken ct)
    {
        SpeechConnection c;
        try { c = SpeechInput(rt, req); }
        catch (ArgumentException e) { return (Fail("invalid_input", e.Message), null); }
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = Timeout(ct, FoundryTimeout);
            await rt.SpeechTranscriber(c, rt.Options.Asr.Locale).ProbeAsync(cts.Token);
            return (new(true, [new("speech", true, sw.ElapsedMilliseconds)], null, c.Endpoint), c);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var diagnosis = Scrub(AiDiagnostics.Classify(e, "speech", c.Auth), c.ApiKey);
            return (new(false, [new("speech", false, sw.ElapsedMilliseconds, diagnosis)], diagnosis), null);
        }
    }

    static async Task<(TestResult Result, FoundryConnection? Conn)> RunFoundryAsync(Runtime rt, FoundryRequest req, CancellationToken ct)
    {
        if (rt.LocalOnly) return (Fail(LocalOnlyCode, LocalOnlyMessage), null);
        FoundryConnection c;
        try { c = FoundryInput(rt, req); }
        catch (ArgumentException e) { return (Fail("invalid_input", e.Message), null); }
        var steps = new List<Step>();
        async Task<bool> Run(string id, string? deployment, Func<CancellationToken, Task> work)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = Timeout(ct, FoundryTimeout);
                await work(cts.Token);
                steps.Add(new(id, true, sw.ElapsedMilliseconds));
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                steps.Add(new(id, false, sw.ElapsedMilliseconds, Scrub(AiDiagnostics.Classify(e, "foundry", c.Auth, deployment), c.ApiKey)));
                return false;
            }
        }
        var ok = await Run("chat", c.Deployment, t => ConnectionProbe.ChatAsync(rt.FoundrySelector(c, FoundryTimeout), t));
        if (ok && c.EmbeddingDeployment != null)
            ok = await Run("embedding", c.EmbeddingDeployment, t => rt.FoundryEmbedder(c, FoundryTimeout).EmbedAsync(["接続テスト"], t));
        return (new TestResult(ok, steps, steps.FirstOrDefault(s => !s.Ok)?.Diagnosis, c.Endpoint), ok ? c : null);
    }

    static async Task<(TestResult Result, CopilotConnection? Conn)> RunCopilotAsync(Runtime rt, CopilotRequest req, CancellationToken ct)
    {
        if (rt.LocalOnly) return (Fail(LocalOnlyCode, LocalOnlyMessage), null);
        if (rt.CopilotCli is not { } cli) return (Fail("copilot_cli", "GitHub Copilot CLI が見つかりません。winget install GitHub.Copilot でインストールしてください"), null);
        CopilotConnection c;
        try { c = CopilotInput(rt, req); }
        catch (ArgumentException e) { return (Fail("invalid_input", e.Message), null); }
        var steps = new List<Step>();
        string? login = null;
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = Timeout(ct, CopilotTimeout);
            var cat = await CopilotCatalog.LoadAsync(cli, rt.CopilotWorkDir, c, cts.Token);
            if (!cat.Account.Authenticated)
            {
                steps.Add(new("auth", false, sw.ElapsedMilliseconds, Scrub(new AiDiagnosis("copilot_auth", "GitHub にサインインしていません。サインイン済みのユーザーを使う場合は、ターミナルで copilot を起動して /login を実行してください。トークンを使う場合はトークンを確認してください", cat.Account.Message), c.GitHubToken)));
                return (new(false, steps, steps[^1].Diagnosis), null);
            }
            login = cat.Account.Login;
            steps.Add(new("auth", true, sw.ElapsedMilliseconds));
            if (CopilotCatalog.Validate(c, cat.Models) is { } problem)
            {
                steps.Add(new("model", false, 0, new AiDiagnosis("copilot_model", problem)));
                return (new(false, steps, steps[^1].Diagnosis, Login: login), null);
            }
            steps.Add(new("model", true, 0));
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            steps.Add(new("auth", false, sw.ElapsedMilliseconds, Scrub(AiDiagnostics.Classify(e, "copilot"), c.GitHubToken)));
            return (new(false, steps, steps[^1].Diagnosis), null);
        }
        sw.Restart();
        try
        {
            using var cts = Timeout(ct, CopilotTimeout);
            await ConnectionProbe.ChatAsync(new CopilotSelector(cli, rt.CopilotWorkDir, c), cts.Token);
            steps.Add(new("chat", true, sw.ElapsedMilliseconds));
            return (new(true, steps, null, Login: login), c);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            steps.Add(new("chat", false, sw.ElapsedMilliseconds, Scrub(AiDiagnostics.Classify(e, "copilot"), c.GitHubToken)));
            return (new(false, steps, steps[^1].Diagnosis, Login: login), null);
        }
    }
}
