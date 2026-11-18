using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;

namespace VideoCreator.Selection;

public sealed record AzureSubscriptionInfo(string Id, string Name, string TenantId);

/// <summary>
/// A Cognitive Services account the signed-in identity can see in Azure Resource Manager. <c>Speech</c> / <c>Foundry</c> come from the
/// account's advertised APIs (<c>properties.endpoints</c>), not its kind alone. <c>CanTranscribe</c> / <c>CanChat</c> / <c>CanEmbed</c> are the
/// caller's effective data actions at the account (null = could not be read); deny assignments, conditions and RBAC propagation can still differ,
/// so the connection test remains the proof.
/// </summary>
public sealed record AzureAiAccount(string Id, string Name, string Kind, string Location, string SubscriptionId, string SubscriptionName, string ResourceGroup,
    string Endpoint, bool CustomDomain, bool KeyAuthDisabled, string ProvisioningState, bool Speech, bool Foundry, bool? CanTranscribe, bool? CanChat, bool? CanEmbed);

/// <summary>A model deployment; <c>Chat</c> / <c>Embeddings</c> from the deployment's ARM capabilities.</summary>
public sealed record AzureAiDeployment(string Name, string? Model, string? ModelVersion, string? Sku, string ProvisioningState, bool Chat, bool Embeddings);

/// <summary>Something that could not be listed (one subscription or account); the rest of the catalog is still valid.</summary>
public sealed record AzureCatalogIssue(string Scope, string Code, string Message);

public sealed record AzureAiCatalog(IReadOnlyList<AzureSubscriptionInfo> Subscriptions, IReadOnlyList<AzureAiAccount> Accounts, IReadOnlyList<AzureCatalogIssue> Issues);

/// <summary>An ARM call failed as a whole (for example no access to list subscriptions).</summary>
public sealed class AzureArmException(HttpStatusCode status, string? code, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string? Code { get; } = code;
}

/// <summary>
/// Lists the Azure Speech and Microsoft Foundry resources the signed-in Microsoft Entra ID identity can use, through Azure Resource Manager only
/// (management-plane read; never <c>listKeys</c>). The token is sent only to the ARM host, including when following <c>nextLink</c>.
/// </summary>
public sealed class AzureResourceCatalog
{
    public const string Scope = "https://management.azure.com/.default";
    public const string AccountsApiVersion = "2024-10-01";
    const string SubscriptionsApiVersion = "2022-12-01", PermissionsApiVersion = "2022-04-01";
    const int MaxPages = 50, Parallelism = 6;

    public const string TranscribeAction = "Microsoft.CognitiveServices/accounts/SpeechServices/speechrest/transcriptions/action";
    public const string ChatAction = "Microsoft.CognitiveServices/accounts/OpenAI/deployments/chat/completions/action";
    public const string EmbedAction = "Microsoft.CognitiveServices/accounts/OpenAI/deployments/embeddings/action";

    static readonly Regex AccountId = new(@"^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[-\w\._\(\)]{1,90}/providers/Microsoft\.CognitiveServices/accounts/[A-Za-z0-9][A-Za-z0-9_.-]{1,63}$", RegexOptions.CultureInvariant);

    readonly TokenCredential _credential;
    readonly HttpClient _http;
    readonly Uri _arm;

    public AzureResourceCatalog(TokenCredential credential, HttpClient http, Uri? arm = null)
    {
        _credential = credential; _http = http; _arm = arm ?? new Uri("https://management.azure.com/");
    }

    public static bool IsAccountId(string? id) => id != null && AccountId.IsMatch(id);

    public async Task<AzureAiCatalog> ListAccountsAsync(CancellationToken ct)
    {
        var subscriptions = new List<AzureSubscriptionInfo>();
        foreach (var s in await GetAllAsync($"subscriptions?api-version={SubscriptionsApiVersion}", ct))
            if (Str(s, "state") is null or "Enabled" or "Warning" or "PastDue")
                subscriptions.Add(new(Str(s, "subscriptionId") ?? "", Str(s, "displayName") ?? Str(s, "subscriptionId") ?? "", Str(s, "tenantId") ?? ""));
        subscriptions.RemoveAll(s => s.Id.Length == 0);

        var issues = new System.Collections.Concurrent.ConcurrentBag<AzureCatalogIssue>();
        var accounts = new System.Collections.Concurrent.ConcurrentBag<AzureAiAccount>();
        await Parallel.ForEachAsync(subscriptions, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (sub, t) =>
        {
            try
            {
                foreach (var a in await GetAllAsync($"subscriptions/{sub.Id}/providers/Microsoft.CognitiveServices/accounts?api-version={AccountsApiVersion}", t))
                    if (ParseAccount(a, sub) is { } acc && (acc.Speech || acc.Foundry)) accounts.Add(acc);
            }
            catch (AzureArmException e) { issues.Add(new(sub.Name, ArmCode(e), e.Message)); }
        });

        var withAccess = new System.Collections.Concurrent.ConcurrentBag<AzureAiAccount>();
        await Parallel.ForEachAsync(accounts, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (acc, t) =>
        {
            try
            {
                var perms = await GetAllAsync($"{acc.Id.TrimStart('/')}/providers/Microsoft.Authorization/permissions?api-version={PermissionsApiVersion}", t);
                withAccess.Add(acc with
                {
                    CanTranscribe = acc.Speech ? Allows(perms, TranscribeAction) : null,
                    CanChat = acc.Foundry ? Allows(perms, ChatAction) : null,
                    CanEmbed = acc.Foundry ? Allows(perms, EmbedAction) : null,
                });
            }
            catch (AzureArmException) { withAccess.Add(acc); }
        });

        return new(subscriptions.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            withAccess.OrderBy(a => a.SubscriptionName, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            issues.OrderBy(i => i.Scope, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    public async Task<IReadOnlyList<AzureAiDeployment>> ListDeploymentsAsync(string accountId, CancellationToken ct)
    {
        if (!IsAccountId(accountId)) throw new ArgumentException("Azure リソース ID の形式が正しくありません（/subscriptions/…/providers/Microsoft.CognitiveServices/accounts/…）");
        return (await GetAllAsync($"{accountId.TrimStart('/')}/deployments?api-version={AccountsApiVersion}", ct))
            .Select(ParseDeployment).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static AzureAiAccount? ParseAccount(JsonElement a, AzureSubscriptionInfo sub)
    {
        var id = Str(a, "id");
        if (id == null || !IsAccountId(id) || !a.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        var kind = Str(a, "kind") ?? "";
        var apis = p.TryGetProperty("endpoints", out var eps) && eps.ValueKind == JsonValueKind.Object
            ? eps.EnumerateObject().Select(e => e.Name).ToList() : [];
        bool speech, foundry;
        if (apis.Count > 0)
        {
            speech = apis.Any(n => n.StartsWith("Speech Services Speech to Text", StringComparison.OrdinalIgnoreCase));
            foundry = apis.Any(n => n.Equals("OpenAI Language Model Instance API", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            speech = kind is "SpeechServices" or "AIServices" or "CognitiveServices";
            foundry = kind is "OpenAI" or "AIServices";
        }
        var endpoint = Str(p, "endpoint") ?? "";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var ep) || ep.Scheme != Uri.UriSchemeHttps) return null;
        var segments = id.Split('/');
        return new(id, Str(a, "name") ?? segments[^1], kind, Str(a, "location") ?? "", sub.Id, sub.Name, segments[4],
            $"https://{ep.Authority.ToLowerInvariant()}/", !string.IsNullOrEmpty(Str(p, "customSubDomainName")),
            p.TryGetProperty("disableLocalAuth", out var dla) && dla.ValueKind == JsonValueKind.True,
            Str(p, "provisioningState") ?? "", speech, foundry, null, null, null);
    }

    public static AzureAiDeployment ParseDeployment(JsonElement d)
    {
        var p = d.TryGetProperty("properties", out var pp) && pp.ValueKind == JsonValueKind.Object ? pp : default;
        var model = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        var modelName = model.ValueKind == JsonValueKind.Object ? Str(model, "name") : null;
        bool chat, embeddings;
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object)
        {
            chat = Str(caps, "chatCompletion") == "true";
            embeddings = Str(caps, "embeddings") == "true";
        }
        else
        {
            embeddings = modelName?.Contains("embedding", StringComparison.OrdinalIgnoreCase) == true;
            chat = !embeddings;
        }
        return new(Str(d, "name") ?? "", modelName, model.ValueKind == JsonValueKind.Object ? Str(model, "version") : null,
            d.TryGetProperty("sku", out var sku) && sku.ValueKind == JsonValueKind.Object ? Str(sku, "name") : null,
            p.ValueKind == JsonValueKind.Object ? Str(p, "provisioningState") ?? "" : "", chat, embeddings);
    }

    /// <summary>Azure RBAC data-action evaluation over the caller's permissions: allowed by some entry's dataActions and not excluded by that same entry's notDataActions.</summary>
    public static bool Allows(IEnumerable<JsonElement> permissions, string action) => permissions.Any(p =>
        Patterns(p, "dataActions").Any(x => Matches(x, action)) && !Patterns(p, "notDataActions").Any(x => Matches(x, action)));

    static IEnumerable<string> Patterns(JsonElement p, string name) =>
        p.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString()).OfType<string>() : [];

    static bool Matches(string pattern, string action) =>
        Regex.IsMatch(action, "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    static string ArmCode(AzureArmException e) => e.Status switch
    {
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "forbidden",
        HttpStatusCode.TooManyRequests => "rate_limited",
        _ => e.Code ?? "http_" + (int)e.Status,
    };

    /// <summary>GET with every page; a <c>nextLink</c> outside the ARM host is not followed so the token cannot leave it.</summary>
    async Task<List<JsonElement>> GetAllAsync(string relative, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        var url = new Uri(_arm, relative);
        for (var page = 0; url != null && page < MaxPages; page++)
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext([Scope]), ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                string? code = null, message = null;
                try
                {
                    using var err = JsonDocument.Parse(body);
                    if (err.RootElement.TryGetProperty("error", out var e)) { code = Str(e, "code"); message = Str(e, "message"); }
                }
                catch (JsonException) { }
                throw new AzureArmException(res.StatusCode, code, Truncate(message ?? $"HTTP {(int)res.StatusCode}"));
            }
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                items.AddRange(value.EnumerateArray().Select(v => v.Clone()));
            url = Str(doc.RootElement, "nextLink") is { Length: > 0 } next && Uri.TryCreate(next, UriKind.Absolute, out var n)
                && n.Scheme == Uri.UriSchemeHttps && n.Authority.Equals(_arm.Authority, StringComparison.OrdinalIgnoreCase) ? n : null;
        }
        return items;
    }

    static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";
}
