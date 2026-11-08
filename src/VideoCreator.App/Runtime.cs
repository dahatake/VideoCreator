using Azure.Core;
using Azure.Identity;
using VideoCreator.Asr;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Pipeline;
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
        if (LocalOnly && provider != "none") throw new InvalidOperationException("ローカルのみモードでは選定用の AI（LLM）を使えません。字幕のための書き起こしだけは使います");
        if (provider is not ("azure-openai" or "copilot" or "none")) throw new ArgumentException("provider must be azure-openai | copilot | none");
        if (provider == "azure-openai" && string.IsNullOrWhiteSpace(_o.Llm.Endpoint)) throw new InvalidOperationException("Azure OpenAI のエンドポイント（VideoCreator:Llm:Endpoint）が設定されていません。scripts/setup.ps1 を実行してください");
        if (provider == "copilot" && CopilotCli == null) throw new InvalidOperationException("GitHub Copilot CLI が見つかりません。winget install GitHub.Copilot でインストールしてください");
        LlmProvider = provider;
    }

    public ITranscriber? Asr => _o.Asr.Provider != "azure" || string.IsNullOrWhiteSpace(_o.Asr.Endpoint)
        ? null : new AzureFastTranscriber(new Uri(_o.Asr.Endpoint), _cred.Value, _http, _o.Asr.Locale);

    public string? CopilotCli => _o.Llm.CopilotCliPath ?? CopilotSelector.FindInstalledCli();

    public IHighlightSelector? Llm => LlmProvider switch
    {
        "azure-openai" when !string.IsNullOrWhiteSpace(_o.Llm.Endpoint) => new AzureOpenAISelector(new Uri(_o.Llm.Endpoint), _o.Llm.Deployment, _cred.Value),
        "copilot" when CopilotCli != null => new CopilotSelector(CopilotCli, Path.Combine(_o.ResolvedDataDir, "copilot"), _o.Llm.CopilotModel),
        _ => null,
    };

    public PipelineRunner CreateRunner() => new(new PipelineServices
    {
        Ff = Ff, Store = Store, Presets = Presets, DataDir = _o.ResolvedDataDir, Asr = Asr, Llm = Llm, Dictionary = _o.Dictionary, SelectionCharBudget = _o.Llm.SingleCallCharBudget,
        Log = s => _log.LogInformation("{Msg}", s), Audio = Audio, Faces = Faces,
    });
}
