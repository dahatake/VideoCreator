namespace VideoCreator.App;

public sealed class AppOptions
{
    public const string Section = "VideoCreator";
    public int Port { get; set; } = 8765;
    public string? DataDir { get; set; }
    public string? ProjectsDir { get; set; }
    public string? FfmpegBin { get; set; }
    public string? FontsDir { get; set; }
    public string? PresetsDir { get; set; }
    public bool OpenBrowser { get; set; } = true;
    /// <summary>US-11 / NFR-SEC-04 (v1.1): no LLM calls. Speech-to-text (Azure AI Speech) is still used because captions are mandatory.</summary>
    public bool LocalOnly { get; set; }
    public string? FaceModel { get; set; }
    public AsrOptions Asr { get; set; } = new();
    public LlmOptions Llm { get; set; } = new();
    public List<string> Dictionary { get; set; } = [];

    public string ResolvedDataDir => string.IsNullOrWhiteSpace(DataDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoCreator") : DataDir;
    public string ResolvedProjectsDir => string.IsNullOrWhiteSpace(ProjectsDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VideoCreator") : ProjectsDir;
}

public sealed class AsrOptions
{
    public string Provider { get; set; } = "azure";   // azure | none
    public string Endpoint { get; set; } = "";
    public string Locale { get; set; } = "ja-JP";
}

public sealed class LlmOptions
{
    public string Provider { get; set; } = "azure-openai";   // azure-openai | copilot | none
    public string Endpoint { get; set; } = "";               // https://<resource>.cognitiveservices.azure.com/openai/v1
    public string Deployment { get; set; } = "gpt-5.4-mini";
    public string? CopilotCliPath { get; set; }
    public string? CopilotModel { get; set; }
    public int SingleCallCharBudget { get; set; } = VideoCreator.Selection.SelectionService.DefaultCharBudget;
}
