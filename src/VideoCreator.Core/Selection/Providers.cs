using System.ClientModel.Primitives;
using System.Text;
using Azure.Core;
using GitHub.Copilot;
using OpenAI;
using OpenAI.Chat;

namespace VideoCreator.Selection;

#pragma warning disable OPENAI001

/// <summary>Microsoft Foundry with strict structured outputs and Entra ID (verified in spikes/05-llm-select.cs).</summary>
public sealed class AzureOpenAISelector : IHighlightSelector
{
    readonly ChatClient _client;
    public string Name => "azure-openai";
    public string Model { get; }

    public AzureOpenAISelector(Uri v1Endpoint, string deployment, TokenCredential credential)
        : this(v1Endpoint, deployment, new BearerTokenPolicy(credential, FoundryAuth.Scope)) { }

    /// <summary>Entra ID (<see cref="BearerTokenPolicy"/>) or API key (<see cref="FoundryAuth.Policy"/>).</summary>
    public AzureOpenAISelector(Uri v1Endpoint, string deployment, AuthenticationPolicy auth, TimeSpan? timeout = null, HttpClient? http = null)
    {
        Model = deployment;
        _client = new ChatClient(deployment, auth, new OpenAIClientOptions
        {
            Endpoint = v1Endpoint, NetworkTimeout = timeout ?? TimeSpan.FromMinutes(3),
            Transport = http == null ? null : new HttpClientPipelineTransport(http),
        });
    }

    public async Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct)
    {
        var opts = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("highlight_candidates", BinaryData.FromString(SelectionPrompt.WireSchema), jsonSchemaIsStrict: true) };
        ChatCompletion c = await _client.CompleteChatAsync([new SystemChatMessage(system), new UserChatMessage(user)], opts, ct);
        var text = c.Content.Count > 0 ? c.Content[0].Text : "";
        return new LlmResult(SelectionPrompt.ParseCandidates(text), Model, new LlmUsage(c.Usage?.InputTokenCount ?? 0, c.Usage?.OutputTokenCount ?? 0));
    }

    public async Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
    {
        var opts = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(schemaName, BinaryData.FromString(wireSchema), jsonSchemaIsStrict: true) };
        ChatCompletion c = await _client.CompleteChatAsync([new SystemChatMessage(system), new UserChatMessage(user)], opts, ct);
        return c.Content.Count > 0 ? c.Content[0].Text : "{}";
    }
}

/// <summary>GitHub Copilot SDK driving the OS-installed Copilot CLI (verified in spikes/06-copilot-sdk.cs).</summary>
public sealed class CopilotSelector(string cliPath, string workDir, CopilotConnection? connection = null) : IHighlightSelector
{
    readonly CopilotConnection _c = connection ?? new();
    public CopilotSelector(string cliPath, string workDir, string? model) : this(cliPath, workDir, new CopilotConnection(Model: model)) { }

    public string Name => "github-copilot";
    public string Model => _c.Model ?? "copilot-default";

    /// <summary>Signed-in user (stored OAuth / gh) or only the explicit token — never a token from the environment when one is given.</summary>
    public static CopilotClientOptions ClientOptions(string cliPath, string workDir, CopilotConnection c) => new()
    {
        Connection = RuntimeConnection.ForStdio(cliPath),
        WorkingDirectory = workDir,
        GitHubToken = c.Auth == "token" ? (string.IsNullOrWhiteSpace(c.GitHubToken) ? throw new ArgumentException("GitHub トークンを入力してください") : c.GitHubToken.Trim()) : null,
        UseLoggedInUser = c.Auth != "token",
    };

    public static ContextTier? Tier(string? tier) => tier switch { "long_context" => ContextTier.LongContext, "default" => ContextTier.Default, _ => null };

    public static string? FindInstalledCli()
    {
        var env = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        var exe = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        var list = new List<string>();
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        if (OperatingSystem.IsWindows() && Directory.Exists(winget))
            foreach (var d in Directory.GetDirectories(winget, "GitHub.Copilot*")) list.AddRange(Directory.GetFiles(d, exe, SearchOption.AllDirectories));
        list.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => p.Length > 0).Select(p => Path.Combine(p.Trim(), exe)).Where(File.Exists));
        return list.FirstOrDefault();
    }

    async Task<List<string>> AskAsync(string prompt, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        await using var client = new CopilotClient(ClientOptions(cliPath, workDir, _c));
        await client.StartAsync(ct);
        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = _c.Model,
            ReasoningEffort = _c.ReasoningEffort,
            ContextTier = Tier(_c.ContextTier),
            OnPermissionRequest = PermissionHandler.ApproveAll,
            AvailableTools = [],   // pure text generation; the agent gets no shell/file tools
        }, ct);
        var messages = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.On<SessionEvent>(evt =>
        {
            if (evt is AssistantMessageEvent m) lock (messages) messages.Add(m.Data.Content ?? "");
            else if (evt is SessionIdleEvent) done.TrySetResult();
            else if (evt is SessionErrorEvent e) done.TrySetException(new InvalidOperationException(e.Data.Message));
        });
        await session.SendAsync(new MessageOptions { Prompt = prompt }, ct);
        await done.Task.WaitAsync(TimeSpan.FromMinutes(4), ct);
        return messages;
    }

    public async Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct)
    {
        var prompt = system + "\n出力形式（JSONのみ。説明文やコードフェンスは不要）:\n" + SelectionPrompt.WireSchema + "\n\n" + user;
        var messages = await AskAsync(prompt, ct);
        var json = SelectionPrompt.ExtractJsonObject(messages, "candidates") ?? throw new LlmSchemaException("Copilot reply contained no candidates JSON");
        return new LlmResult(SelectionPrompt.ParseCandidates(json), Model, new LlmUsage(Encoding.UTF8.GetByteCount(prompt) / 3, Encoding.UTF8.GetByteCount(json) / 3));
    }

    public async Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
    {
        var messages = await AskAsync(system + "\n出力形式（JSONのみ）:\n" + wireSchema + "\n\n" + user, ct);
        var top = System.Text.Json.JsonDocument.Parse(wireSchema).RootElement.GetProperty("required")[0].GetString()!;
        return SelectionPrompt.ExtractJsonObject(messages, top) ?? throw new LlmSchemaException("Copilot reply contained no JSON");
    }
}
