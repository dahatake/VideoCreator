using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace VideoCreator.Selection;

/// <summary>Microsoft Foundry connection to an existing resource and deployments. <c>Auth</c> = "entra" (Microsoft Entra ID) | "key" (API key).</summary>
public sealed record FoundryConnection(string Endpoint, string Deployment, string? EmbeddingDeployment, string Auth = "entra", string? TenantId = null, string? ApiKey = null)
{
    public bool UsesKey => Auth == "key";
}

/// <summary>GitHub Copilot SDK connection. <c>Auth</c> = "user" (the user signed in to the Copilot CLI / gh) | "token" (a GitHub token).
/// Null model / effort / tier = the runtime's default. <c>ContextTier</c> = "default" | "long_context".</summary>
public sealed record CopilotConnection(string Auth = "user", string? GitHubToken = null, string? Model = null, string? ReasoningEffort = null, string? ContextTier = null);

public static class FoundryEndpoint
{
    static readonly string[] Hosts = [".cognitiveservices.azure.com", ".openai.azure.com", ".services.ai.azure.com"];

    /// <summary>
    /// Any form a user may copy from the Azure / Foundry portal — resource endpoint, Azure OpenAI endpoint, Foundry project endpoint
    /// (<c>/api/projects/x</c>), with or without <c>/openai/v1</c> — becomes the OpenAI v1 base <c>https://host/openai/v1/</c>.
    /// </summary>
    public static Uri Normalize(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("エンドポイントを入力してください（例: https://<リソース名>.cognitiveservices.azure.com/）");
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            throw new ArgumentException("エンドポイントの形式が正しくありません（例: https://<リソース名>.cognitiveservices.azure.com/）");
        if (uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("エンドポイントは https:// で始まる必要があります（キーやトークンを暗号化せずに送らないため）");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException("エンドポイントにユーザー名やクエリ（?以降）は含めないでください。キーは「API キー」欄に入力します");
        var host = uri.Host.ToLowerInvariant();
        // Private endpoints / API Management keep their own host; only the Microsoft hosts get the path rewritten.
        if (!Hosts.Any(h => host.EndsWith(h, StringComparison.Ordinal)))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) path += "/openai/v1";
            return new Uri($"https://{uri.Authority}{path}/");
        }
        return new Uri($"https://{uri.Authority.ToLowerInvariant()}/openai/v1/");
    }
}

public static class FoundryAuth
{
    public const string Scope = "https://ai.azure.com/.default";

    /// <summary>API key → the documented <c>api-key</c> header; otherwise Entra ID bearer tokens.</summary>
    public static AuthenticationPolicy Policy(FoundryConnection c, Func<string?, TokenCredential> credential) => c.UsesKey
        ? ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(new ApiKeyCredential(string.IsNullOrWhiteSpace(c.ApiKey) ? throw new ArgumentException("API キーを入力してください") : c.ApiKey.Trim()), "api-key")
        : new BearerTokenPolicy(credential(string.IsNullOrWhiteSpace(c.TenantId) ? null : c.TenantId.Trim()), Scope);

    /// <summary>Entra ID through the developer sign-ins (az login, azd, Visual Studio, VS Code, environment, managed identity); no browser pop-up from a background server.</summary>
    public static TokenCredential DefaultCredential(string? tenantId) => new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true,
        TenantId = tenantId,
    });
}

/// <summary>Why a connection test failed, in words the user can act on. <c>Code</c> is stable (the UI localizes by it); <c>Detail</c> is the provider's own text.</summary>
public sealed record AiDiagnosis(string Code, string Message, string? Detail = null);

public static class AiDiagnostics
{
    /// <summary>Maps the raw failures observed from Azure (spikes/22-ai-connection.cs) and the Copilot runtime to a cause and a next step.</summary>
    public static AiDiagnosis Classify(Exception ex, string provider, string? auth = null, string? deployment = null)
    {
        var detail = Truncate(ex is ClientResultException cr ? ProviderMessage(cr) : ex.Message);
        if (ex is AuthenticationFailedException or CredentialUnavailableException)
            return new("entra_signin", "Microsoft Entra ID のトークンを取得できませんでした。このPCで az login（テナントが複数あるときは az login --tenant <テナント ID>）を実行してから、もう一度テストしてください", detail);
        if (ex is LlmSchemaException)
            return new("bad_reply", "応答は返りましたが、決められた JSON 形式ではありませんでした。別のモデルを選ぶか、もう一度テストしてください", detail);
        if (ex is TimeoutException || ex is TaskCanceledException { InnerException: TimeoutException } || ex is OperationCanceledException)
            return new("timeout", "応答が時間内に返りませんでした。ネットワークとプロキシを確認し、もう一度テストしてください", detail);
        if (ex is HttpRequestException { InnerException: SocketException } || ex.InnerException is HttpRequestException { InnerException: SocketException } || ex is HttpRequestException { StatusCode: null })
            return new("unreachable", "エンドポイントに接続できません。リソース名（ホスト名）のつづりと、ネットワーク・プロキシ・ファイアウォールを確認してください", detail);
        if (provider == "speech" && ex is Asr.AsrException speech)
            return speech.StatusCode switch
            {
                HttpStatusCode.Unauthorized when auth == "key" => new("key_rejected", "API キーが拒否されました。キーが正しいか、またはこのリソースでキー認証が無効（disableLocalAuth）になっていないか確認してください。無効なら「Microsoft Entra ID」を選んでください", detail),
                HttpStatusCode.Unauthorized => new("entra_rejected", "Microsoft Entra ID token was rejected. Sign in to the resource's tenant.", detail),
                HttpStatusCode.Forbidden => new("speech_forbidden", "Assign Cognitive Services Speech User at the resource group or resource scope, then allow time for RBAC propagation.", detail),
                HttpStatusCode.NotFound => new("endpoint_not_found", "Speech API was not found. Use the resource's custom-domain endpoint.", detail),
                HttpStatusCode.TooManyRequests => new("rate_limited", "Speech quota exceeded. Retry later.", detail),
                >= HttpStatusCode.InternalServerError => new("service_error", "Speech service is temporarily unavailable.", detail),
                _ => new("speech_failed", "Speech fast transcription failed. Verify the resource supports fast transcription and check the response details.", detail),
            };
        if (ex is ClientResultException r)
        {
            var code = ProviderCode(r);
            return r.Status switch
            {
                401 when auth == "key" => new("key_rejected", "API キーが拒否されました。キーが正しいか、またはこのリソースでキー認証が無効（disableLocalAuth）になっていないか確認してください。無効なら「Microsoft Entra ID」を選んでください", detail),
                401 or 403 when code is "PermissionDenied" || r.Status == 403 => new("forbidden", "サインインしたアカウントに、このリソースを使う権限がありません。Azure portal でリソースの「アクセス制御 (IAM)」から「Cognitive Services OpenAI User」ロールを付与してください（反映まで数分かかります）", detail),
                401 => new("entra_rejected", "Microsoft Entra ID のトークンが拒否されました。リソースと同じテナントにサインインしているか確認し、必要ならテナント ID を入力してください", detail),
                404 when code == "DeploymentNotFound" => new("deployment_not_found", $"デプロイ「{deployment}」がこのリソースに見つかりません。Foundry ポータルの「デプロイ」に表示される名前（モデル名ではありません）を入力してください", detail),
                404 => new("endpoint_not_found", "エンドポイントは応答しましたが、API が見つかりません。Azure portal の「キーとエンドポイント」に表示されるエンドポイントを貼り付けてください", detail),
                400 when Mentions(detail, "response_format", "json_schema", "structured") => new("no_structured_output", "このデプロイのモデルは JSON スキーマ出力（Structured Outputs）に対応していません。gpt-4.1 以降などの対応モデルのデプロイを指定してください", detail),
                400 when Mentions(detail, "unsupported", "operation") => new("wrong_model_kind", "このデプロイはチャット用ではありません（埋め込み用など）。チャット用モデルのデプロイ名を指定してください", detail),
                429 => new("rate_limited", "クォータ（1 分あたりのトークン数）の上限に達しています。少し待つか、Foundry ポータルでデプロイのレート制限を引き上げてください", detail),
                >= 500 => new("service_error", "サービス側で一時的なエラーが発生しました。しばらくしてからもう一度テストしてください", detail),
                _ => new("http_" + r.Status, $"接続テストに失敗しました（HTTP {r.Status}）", detail),
            };
        }
        if (provider == "copilot")
        {
            if (Mentions(detail, "auth", "login", "token", "401", "unauthorized"))
                return new("copilot_auth", "GitHub の認証に失敗しました。サインイン済みのユーザーを使う場合は、ターミナルで copilot を起動して /login を実行してください。トークンを使う場合は、Copilot Requests 権限のある fine-grained トークンか確認してください", detail);
            if (Mentions(detail, "model") && Mentions(detail, "not", "unavailable", "invalid", "unknown"))
                return new("copilot_model", "選んだモデルはこのアカウントでは使えません。モデル一覧を読み込み直し、別のモデルを選んでください（組織のポリシーで無効な場合があります）", detail);
            return new("copilot_failed", "GitHub Copilot に接続できませんでした。Copilot CLI がインストールされ、最新版か確認してください（winget upgrade GitHub.Copilot）", detail);
        }
        return new("failed", "接続テストに失敗しました", detail);
    }

    static bool Mentions(string? text, params string[] words) => text != null && words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    static string? ProviderCode(ClientResultException r)
    {
        try { using var d = JsonDocument.Parse(r.GetRawResponse()?.Content?.ToString() ?? ""); return d.RootElement.GetProperty("error").TryGetProperty("code", out var c) ? c.ToString() : null; }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    static string ProviderMessage(ClientResultException r)
    {
        try { using var d = JsonDocument.Parse(r.GetRawResponse()?.Content?.ToString() ?? ""); return d.RootElement.GetProperty("error").GetProperty("message").GetString() ?? r.Message; }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return r.Message; }
    }

    static string? Truncate(string? s) => s == null ? null : s.Length <= 400 ? s : s[..400] + "…";
}

/// <summary>The smallest request through the same call the pipeline makes (CompleteJsonAsync), so a passing test means generation can work.</summary>
public static class ConnectionProbe
{
    public const string SchemaName = "connection_test";
    public const string Schema = """{"type":"object","properties":{"reply":{"type":"string"}},"required":["reply"],"additionalProperties":false}""";

    public static async Task ChatAsync(IHighlightSelector llm, CancellationToken ct)
    {
        var json = await llm.CompleteJsonAsync("You are a connection test. Answer only with the requested JSON.", "Return {\"reply\":\"OK\"}.", SchemaName, Schema, ct);
        try
        {
            using var d = JsonDocument.Parse(json);
            if (!d.RootElement.TryGetProperty("reply", out var reply) || reply.ValueKind != JsonValueKind.String) throw new LlmSchemaException("reply missing");
        }
        catch (JsonException e) { throw new LlmSchemaException("not JSON: " + e.Message); }
    }
}

/// <summary>Credits per 1M tokens as the Copilot runtime reports them.</summary>
public sealed record CopilotPrices(double? Input, double? Output, double? CacheRead, double? CacheWrite);

/// <summary>One selectable model, straight from <c>ListModelsAsync</c>: the efforts and context tiers offered are exactly what the runtime accepts for it.</summary>
public sealed record CopilotModel(string Id, string Name, string? Policy, IReadOnlyList<string> ReasoningEfforts, string? DefaultReasoningEffort,
    long? DefaultContextTokens, long? LongContextTokens, double? Multiplier, CopilotPrices? Prices, CopilotPrices? LongContextPrices);

public sealed record CopilotAccount(bool Authenticated, string? Login, string? Host, string? AuthType, string? Message);

public sealed record CopilotCatalogResult(CopilotAccount Account, IReadOnlyList<CopilotModel> Models);

public static class CopilotCatalog
{
    public static async Task<CopilotCatalogResult> LoadAsync(string cliPath, string workDir, CopilotConnection c, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        await using var client = new GitHub.Copilot.CopilotClient(CopilotSelector.ClientOptions(cliPath, workDir, c));
        await client.StartAsync(ct);
        var auth = await client.GetAuthStatusAsync(ct);
        var account = new CopilotAccount(auth.IsAuthenticated, auth.Login, auth.Host, auth.AuthType, auth.StatusMessage);
        if (!auth.IsAuthenticated) return new(account, []);
        var models = await client.ListModelsAsync(ct);
        return new(account, [.. models.Where(m => m.Policy?.State != "disabled").Select(ToModel)]);
    }

    public static CopilotModel ToModel(GitHub.Copilot.ModelInfo m)
    {
        var p = m.Billing?.TokenPrices;
        var l = p?.LongContext;
        long? window = m.Capabilities?.Limits?.MaxContextWindowTokens is int w and > 0 ? w : null;
        return new(m.Id, string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name, m.Policy?.State, [.. m.SupportedReasoningEfforts ?? []], m.DefaultReasoningEffort,
            p?.MaxPromptTokens ?? m.Capabilities?.Limits?.MaxPromptTokens ?? window, l == null ? null : window ?? l.MaxPromptTokens, m.Billing?.Multiplier,
            p == null ? null : new(p.InputPrice, p.OutputPrice, p.CacheReadPrice, p.CacheWritePrice),
            l == null ? null : new(l.InputPrice, l.OutputPrice, l.CacheReadPrice, l.CacheWritePrice));
    }

    /// <summary>Null when the choice is valid for the model; otherwise why not (the server re-checks what the UI offered).</summary>
    public static string? Validate(CopilotConnection c, IReadOnlyList<CopilotModel> models)
    {
        if (c.Model == null) return c.ReasoningEffort != null || c.ContextTier is "long_context" ? "思考レベルと Context Size は、モデルを選んだときだけ指定できます" : null;
        var m = models.FirstOrDefault(x => x.Id == c.Model);
        if (m == null) return $"モデル「{c.Model}」はこのアカウントでは使えません。モデル一覧を読み込み直してください";
        if (c.ReasoningEffort != null && !m.ReasoningEfforts.Contains(c.ReasoningEffort))
            return m.ReasoningEfforts.Count == 0 ? $"{m.Name} は思考レベルを指定できません" : $"{m.Name} の思考レベルは {string.Join(" / ", m.ReasoningEfforts)} から選んでください";
        if (c.ContextTier is "long_context" && m.LongContextTokens == null) return $"{m.Name} は長いコンテキスト（Long context）に対応していません";
        if (c.ContextTier is not (null or "default" or "long_context")) return "Context Size は default | long_context です";
        return null;
    }
}
