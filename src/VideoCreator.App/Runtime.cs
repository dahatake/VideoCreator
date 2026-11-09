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
    readonly Lazy<TokenCredential> _cred = new(() => new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true }));
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public FFmpeg Ff { get; }
    public Store Store { get; }
    public IReadOnlyDictionary<string, Preset> Presets { get; }
    public string LlmProvider { get; private set; }
    public bool LocalOnly => _o.LocalOnly;
    public AppOptions Options => _o;

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
        LlmProvider = o.LocalOnly ? "none" : o.Llm.Provider;
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
        if (provider == "azure-openai" && string.IsNullOrWhiteSpace(_o.Llm.Endpoint)) throw new InvalidOperationException("Azure OpenAI のエンドポイント（VideoCreator:Llm:Endpoint）が設定されていません。scripts/setup.ps1 を実行してください");
        if (provider == "copilot" && CopilotCli == null) throw new InvalidOperationException("GitHub Copilot CLI が見つかりません。winget install GitHub.Copilot でインストールしてください");
        LlmProvider = provider;
    }

    public ITranscriber? Asr => _o.Asr.Provider != "azure" || string.IsNullOrWhiteSpace(_o.Asr.Endpoint)
        ? null : new AzureFastTranscriber(new Uri(_o.Asr.Endpoint), _cred.Value, _http, _o.Asr.Locale);

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
        llm_destination = LlmProvider switch
        {
            "azure-openai" when Uri.TryCreate(_o.Llm.Endpoint, UriKind.Absolute, out var llmEndpoint) => Origin(llmEndpoint),
            "copilot" => "GitHub Copilot",
            _ => null,
        },
    };

    public IHighlightSelector? Llm => LlmProvider switch
    {
        "azure-openai" when !string.IsNullOrWhiteSpace(_o.Llm.Endpoint) => new AzureOpenAISelector(new Uri(_o.Llm.Endpoint), _o.Llm.Deployment, _cred.Value),
        "copilot" when CopilotCli != null => new CopilotSelector(CopilotCli, Path.Combine(_o.ResolvedDataDir, "copilot"), _o.Llm.CopilotModel),
        _ => null,
    };

    /// <summary>Meaning-level topic detection; same Azure OpenAI resource as the chat model, so nothing new leaves the machine's configured destination.</summary>
    public ITextEmbedder? Embedder => LlmProvider == "azure-openai" && !string.IsNullOrWhiteSpace(_o.Llm.Endpoint) && !string.IsNullOrWhiteSpace(_o.Llm.EmbeddingDeployment)
        ? new AzureOpenAIEmbedder(new Uri(_o.Llm.Endpoint), _o.Llm.EmbeddingDeployment, _cred.Value) : null;

    public PipelineRunner CreateRunner() => new(new PipelineServices
    {
        Ff = Ff, Store = Store, Presets = Presets, DataDir = _o.ResolvedDataDir, Asr = Asr, Llm = Llm, Embedder = Embedder, Dictionary = _o.Dictionary, SelectionCharBudget = _o.Llm.SingleCallCharBudget,
        Log = s => _log.LogInformation("{Msg}", s), Audio = Audio, Faces = Faces,
    });
}
