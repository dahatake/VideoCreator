using Azure.Core;
using Azure.Identity;
using VideoCreator.Asr;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Selection;
using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>Holds configured services; the LLM provider can be switched at runtime from the settings UI.</summary>
public sealed class Runtime
{
    readonly AppOptions _o;
    readonly ILogger _log;
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, TokenCredential> _creds = new();
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    TokenCredential Credential(string? tenantId) => _creds.GetOrAdd(tenantId ?? "", t => FoundryAuth.DefaultCredential(t.Length == 0 ? null : t));
    public Func<string?, TokenCredential> CredentialFor => Credential;

    public FFmpeg Ff { get; }
    public Store Store { get; }
    public IReadOnlyDictionary<string, Preset> Presets { get; }
    public string LlmProvider { get; private set; }
    public bool LocalOnly => _o.LocalOnly;
    public AppOptions Options => _o;
    public AiSettingsStore AiSettings { get; internal set; }
    /// <summary>The Foundry connection in use: the one saved in the settings screen, else appsettings.json (Entra ID). Null = not configured.</summary>
    public FoundryConnection? Foundry => AiSettings.Load().Foundry is { } f ? f.ToConnection(AiSettings)
        : string.IsNullOrWhiteSpace(_o.Llm.Endpoint) ? null : new FoundryConnection(_o.Llm.Endpoint, _o.Llm.Deployment, _o.Llm.EmbeddingDeployment);
    public CopilotConnection Copilot => AiSettings.Load().Copilot is { } c ? c.ToConnection(AiSettings) : new CopilotConnection(Model: _o.Llm.CopilotModel);
    public string CopilotWorkDir => Path.Combine(_o.ResolvedDataDir, "copilot");

    public AudioLibrary Audio { get; }
    public FaceService Faces { get; }
    public string UpscaleModel { get; }

    public Runtime(AppOptions o, ILogger<Runtime> log)
    {
        _o = o; _log = log;
        // AzureCliCredential inherits this process setting; token acquisition must not emit CLI telemetry.
        Environment.SetEnvironmentVariable("AZURE_CORE_COLLECT_TELEMETRY", "false", EnvironmentVariableTarget.Process);
        Directory.CreateDirectory(o.ResolvedDataDir);
        Ff = FFmpeg.Locate(o.FfmpegBin, o.FontsDir, AppContext.BaseDirectory);
        Ff.Log = s => _log.LogDebug("{Cmd}", s);
        Audio = new AudioLibrary(Ff, Path.Combine(o.ResolvedDataDir, "audio"));
        Faces = new FaceService(Ff, o.FaceModel ?? Path.Combine(FindUp(Path.Combine("tools", "models")) ?? "", "ultraface.onnx"));
        UpscaleModel = o.UpscaleModel ?? Path.Combine(FindUp(Path.Combine("tools", "models")) ?? "", SuperResolution.ModelFile);
        Store = new Store(Path.Combine(o.ResolvedDataDir, "videocreator.db"));
        var presetsDir = o.PresetsDir ?? FindUp("presets") ?? throw new DirectoryNotFoundException("presets folder not found");
        Presets = PresetStore.LoadDirectory(presetsDir).ToDictionary(p => p.Id);
        AiSettings = new AiSettingsStore(Path.Combine(o.ResolvedDataDir, "ai-settings.json"), AiSettingsStore.DefaultProtector());
        var saved = AiSettings.Load().Provider;
        LlmProvider = o.LocalOnly ? "none" : saved is "azure-openai" or "copilot" or "none" ? saved : o.Llm.Provider;
    }

    static string? FindUp(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, name))) return Path.Combine(d.FullName, name);
        return null;
    }

    public void SetLlmProvider(string provider)
    {
        if (LocalOnly && provider != "none") throw new InvalidOperationException("LLM不使用モードでは選定用の AI（LLM）を使えません。字幕のための音声は ASR に送信します");
        if (provider is not ("azure-openai" or "copilot" or "none")) throw new ArgumentException("provider must be azure-openai | copilot | none");
        if (provider == "azure-openai" && Foundry == null) throw new InvalidOperationException("Microsoft Foundry の接続が設定されていません。設定の「AI の接続」で Microsoft Foundry を入力し、接続テストをして保存してください");
        if (provider == "copilot" && CopilotCli == null) throw new InvalidOperationException("GitHub Copilot CLI が見つかりません。winget install GitHub.Copilot でインストールしてください");
        LlmProvider = provider;
        AiSettings.Update(s => s with { Provider = provider });
    }

    public void SaveFoundry(FoundryConnection c, DateTimeOffset testedAt)
    {
        var keep = AiSettings.Load().Foundry;
        // switching to Entra ID deletes the stored key; an empty key field keeps the stored one
        var key = c.UsesKey ? (string.IsNullOrWhiteSpace(c.ApiKey) ? keep?.ApiKeyProtected : AiSettings.Protect(c.ApiKey)) : null;
        AiSettings.Update(s => s with { Foundry = new SavedFoundry(c.Endpoint, c.Deployment, c.EmbeddingDeployment, c.Auth, c.TenantId, key, testedAt) });
    }

    public void SaveCopilot(CopilotConnection c, string? login, DateTimeOffset testedAt)
    {
        var keep = AiSettings.Load().Copilot;
        var token = c.Auth == "token" ? (string.IsNullOrWhiteSpace(c.GitHubToken) ? keep?.TokenProtected : AiSettings.Protect(c.GitHubToken)) : null;
        AiSettings.Update(s => s with { Copilot = new SavedCopilot(c.Auth, token, c.Model, c.ReasoningEffort, c.ContextTier, login, testedAt) });
    }

    public IHighlightSelector FoundrySelector(FoundryConnection c, TimeSpan? timeout = null) =>
        new AzureOpenAISelector(FoundryEndpoint.Normalize(c.Endpoint), c.Deployment.Trim(), FoundryAuth.Policy(c, Credential), timeout);

    public ITextEmbedder FoundryEmbedder(FoundryConnection c, TimeSpan? timeout = null) =>
        new AzureOpenAIEmbedder(FoundryEndpoint.Normalize(c.Endpoint), c.EmbeddingDeployment!.Trim(), FoundryAuth.Policy(c, Credential), timeout);

    public ITranscriber? Asr => _o.Asr.Provider != "azure" || string.IsNullOrWhiteSpace(_o.Asr.Endpoint)
        ? null : new AzureFastTranscriber(new Uri(_o.Asr.Endpoint), Credential(null), _http, _o.Asr.Locale);

    public ITranscriber? AsrForLanguage(string language) => _o.Asr.Provider != "azure" || string.IsNullOrWhiteSpace(_o.Asr.Endpoint)
        ? null : new AzureFastTranscriber(new Uri(_o.Asr.Endpoint), Credential(null), _http, language);

    public string? CopilotCli => _o.Llm.CopilotCliPath ?? CopilotSelector.FindInstalledCli();

    static string Origin(Uri endpoint) => new UriBuilder(endpoint) { UserName = "", Password = "", Path = "", Query = "", Fragment = "" }
        .Uri.GetLeftPart(UriPartial.Authority);

    public object Transmission => new
    {
        llm_disabled = LlmProvider == "none",
        asr_endpoint = _o.Asr.Provider == "azure" && Uri.TryCreate(_o.Asr.Endpoint, UriKind.Absolute, out var endpoint)
            ? Origin(endpoint) : null,
        authentication = _o.Asr.Provider == "azure" && !string.IsNullOrWhiteSpace(_o.Asr.Endpoint)
            ? "Microsoft Entra ID（Azure Identity の認証先。音声・書き起こしは送りません）" : null,
        llm_destination = LlmDestination,
    };

    public string? LlmDestination => LlmProvider switch
    {
        "azure-openai" when Foundry is { } f && TryEndpoint(f) is { } llmEndpoint => Origin(llmEndpoint),
        "copilot" => "GitHub Copilot",
        _ => null,
    };

    static Uri? TryEndpoint(FoundryConnection f) { try { return FoundryEndpoint.Normalize(f.Endpoint); } catch (ArgumentException) { return null; } }

    /// <summary>Null when the provider is off or its stored connection cannot be used (e.g. a key saved by another Windows user).</summary>
    public IHighlightSelector? Llm => LlmProvider switch
    {
        "azure-openai" when Foundry is { } f && TryEndpoint(f) != null && (!f.UsesKey || f.ApiKey != null) => FoundrySelector(f),
        "copilot" when CopilotCli != null && (Copilot is var c && (c.Auth != "token" || c.GitHubToken != null)) => new CopilotSelector(CopilotCli, CopilotWorkDir, Copilot),
        _ => null,
    };

    /// <summary>Meaning-level topic detection; same Microsoft Foundry resource as the chat model, so nothing new leaves the machine's configured destination.</summary>
    public ITextEmbedder? Embedder => LlmProvider == "azure-openai" && Foundry is { } f && TryEndpoint(f) != null && !string.IsNullOrWhiteSpace(f.EmbeddingDeployment) && (!f.UsesKey || f.ApiKey != null)
        ? FoundryEmbedder(f) : null;

    public PipelineRunner CreateRunner() => new(new PipelineServices
    {
        Ff = Ff, Store = Store, Presets = Presets, DataDir = _o.ResolvedDataDir, Asr = Asr, AsrForLanguage = AsrForLanguage, Llm = Llm, Embedder = Embedder, Dictionary = _o.Dictionary, SelectionCharBudget = _o.Llm.SingleCallCharBudget,
        Log = s => _log.LogInformation("{Msg}", s), Audio = Audio, Faces = Faces, UpscaleModel = UpscaleModel,
    });
}
