namespace VideoCreator.Models;

// ---- Project / assets (PRD §9.2) ----
public sealed record Project
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Language { get; init; } = "ja-JP";
    public required string Directory { get; init; }
}

public enum AssetRole { Main, CamA, CamB, Wide, AudioMaster }

public sealed record MediaInfo
{
    public double Duration { get; init; }
    public int Width { get; init; }            // display width (rotation applied)
    public int Height { get; init; }           // display height (rotation applied)
    public int Rotation { get; init; }
    public double Fps { get; init; }
    public bool Vfr { get; init; }
    public int AudioChannels { get; init; }
    public string VideoCodec { get; init; } = "";
    public string Container { get; init; } = "";
    public double Aspect => Height == 0 ? 0 : (double)Width / Height;
}

public sealed record SourceAsset
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required MediaInfo Media { get; init; }
    public AssetRole Role { get; init; } = AssetRole.Main;
    /// <summary>Which speaker this camera shows (1-based, order of first appearance in the transcript). Null = wide / no single speaker.</summary>
    public int? Speaker { get; init; }
    /// <summary>Seconds such that cameraTime = masterTime + SyncOffset (audio cross-correlation or manual).</summary>
    public double SyncOffset { get; init; }
    public double? SyncConfidence { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed record BgmTrack
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public double Bpm { get; init; }
    public double Phase { get; init; }
    public string Source { get; init; } = "library";   // library | user
    public string? ProjectId { get; init; }
    public bool RightsConfirmed { get; init; }
}

public sealed record FaceBox(double X, double Y, double W, double H, double Score)
{
    public double Cx => X + W / 2;
    public double Cy => Y + H / 2;
}

public sealed record FaceSample(double T, List<FaceBox> Boxes);

public sealed record RightsConfirmation
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string AssetId { get; init; }
    public string ConfirmedBy { get; init; } = Environment.UserName;
    public DateTimeOffset ConfirmedAt { get; init; } = DateTimeOffset.UtcNow;
    public string StatementVersion { get; init; } = RightsStatement.Version;
}

public static class RightsStatement
{
    public const string Version = "1.0";
    public const string Text = "この素材の著作権・肖像権などの権利を自分が持っている、または権利者から利用の許諾を得ています。";
}

// ---- Transcript (FR-ASR) ----
public sealed record Word(string Id, double T0, double T1, string Text, int Speaker, double Conf = 1);

public sealed record Sentence(int Index, double T0, double T1, string Text, int Speaker, int FirstWord, int LastWord);

public sealed record Transcript
{
    public required string Provider { get; init; }
    public string ModelVersion { get; init; } = "";
    public string Language { get; init; } = "ja-JP";
    public double Duration { get; init; }
    public List<Word> Words { get; init; } = [];
    public List<Sentence> Sentences { get; init; } = [];
    public string FullText => string.Concat(Sentences.Select(s => s.Text));
}

// ---- Signals (FR-SIG) ----
public sealed record Interval(double T0, double T1)
{
    public double Length => T1 - T0;
}

public sealed record SignalEvent(double T, string Kind, double Strength);

public sealed record SignalSet
{
    public List<double> Shots { get; init; } = [];            // shot boundary times
    public List<double> Loudness100Ms { get; init; } = [];    // dBFS per 100 ms window
    public List<Interval> Silences { get; init; } = [];
    public List<SignalEvent> Events { get; init; } = [];      // loudness peaks etc.
    public double MedianLoudness { get; init; }
}

// ---- Candidates (FR-SEL) ----
public sealed record Scores
{
    public int Hook { get; init; }
    public int Standalone { get; init; }
    public int Payoff { get; init; }
    public int Emotion { get; init; }
    public double Signal { get; init; }
    public double Final { get; init; }
    public double LlmAverage => (Hook + Standalone + Payoff + Emotion) / 4.0;
}

public sealed record Candidate
{
    public required string Id { get; init; }
    public int Rank { get; init; }
    public required List<Interval> Segments { get; init; }
    public string HookText { get; init; } = "";
    public string Headline { get; init; } = "";
    public List<string> EmphasisWords { get; init; } = [];
    public string Reason { get; init; } = "";
    public Scores Scores { get; init; } = new();
    public string Source { get; init; } = "llm";       // llm | fallback
    public List<string> Notes { get; init; } = [];     // validator adjustments, warnings
    public string? EditPlanId { get; init; }
    public string? PreviewRenderId { get; init; }
    public double TotalDuration => Segments.Sum(s => s.Length);
}

// ---- EditPlan (PRD §9.3) ----
public sealed record TrackPoint(double T, double X);

public sealed record ReframeSpec
{
    public string Mode { get; init; } = "blur_bg";   // blur_bg | fit | face
    public int FgOffsetY { get; init; } = -120;
    /// <summary>face mode: crop window (source pixels), left edge over clip time (seconds after the clip start).</summary>
    public List<TrackPoint>? Track { get; init; }
    public int CropW { get; init; }
    public int CropH { get; init; }
    public int CropY { get; init; }
}

public sealed record Clip
{
    public required string ClipId { get; init; }
    public required string AssetId { get; init; }
    /// <summary>Range in the VIDEO asset's own time.</summary>
    public double SrcIn { get; init; }
    public double SrcOut { get; init; }
    /// <summary>Start in MASTER time (transcript/audio). Null = same as SrcIn (single source).</summary>
    public double? AudioIn { get; init; }
    public double Speed { get; init; } = 1.0;
    public ReframeSpec Reframe { get; init; } = new();
    public double Zoom { get; init; } = 1.0;
    public string TransitionIn { get; init; } = "cut";
    public string Role { get; init; } = "body";      // body | teaser | reaction | wide
    /// <summary>> 0: this clip is entered with a video cross-fade of that many seconds (scene changes only, FR-SWT-05). Audio stays a hard cut; the previous clip's video is extended by the same amount, so the duration is unchanged.</summary>
    public double TransitionSec { get; init; }
    public double MasterIn => AudioIn ?? SrcIn;
    public double MasterOut => MasterIn + (SrcOut - SrcIn);
    /// <summary>Length on the OUTPUT timeline.</summary>
    public double Length => (SrcOut - SrcIn) / Speed;
}
public enum TelopType { Caption, Emphasis, Tsukkomi, Headline, Hook, Sfx, Counter }

public sealed record Telop
{
    public required string Id { get; init; }
    public TelopType Type { get; init; }
    public double Start { get; init; }
    public double End { get; init; }
    public required string Text { get; init; }
    public string Style { get; init; } = "Caption";
    public string? Animation { get; init; }
    public List<string> SourceWordIds { get; init; } = [];
    public bool Approved { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public int? Speaker { get; init; }
}

public sealed record SfxEvent(string Kind, double T, double GainDb = 0);

public sealed record AudioSpec
{
    /// <summary>Asset whose audio is the dialogue master (also the transcript timeline).</summary>
    public required string Master { get; init; }
    public string? Bgm { get; init; }                // BgmTrack id, null = none
    public double BgmGainDb { get; init; } = -16;
    public bool Duck { get; init; } = true;
    public double DialogueGainDb { get; init; }
    /// <summary>Seconds into the BGM where the first beat of the output falls (beat-synced patterns).</summary>
    public double BgmPhase { get; init; }
    public bool SfxEnabled { get; init; } = true;
    public List<SfxEvent> Sfx { get; init; } = [];
    public List<Interval> BgmMutes { get; init; } = [];
    public double TargetLufs { get; init; } = -14;
    public double TruePeak { get; init; } = -1.5;
    public double Lra { get; init; } = 11;
}
public sealed record EditRecord(DateTimeOffset At, string Op, string Target, string? From, string? To);

public sealed record Provenance
{
    public string? JobId { get; init; }
    public string? CandidateId { get; init; }
    public string? LlmModel { get; init; }
    public string? PromptVersion { get; init; }
    public List<EditRecord> Edits { get; init; } = [];
}

public sealed record Canvas(int Width = 1080, int Height = 1920, int Fps = 30);

public sealed record PresetRef(string Id, string Version);

public sealed record EditPlan
{
    public string SchemaVersion { get; init; } = "1.0";
    public required string EditplanId { get; init; }
    public required string ProjectId { get; init; }
    public required PresetRef Preset { get; init; }
    public Canvas Canvas { get; init; } = new();
    public List<Clip> Timeline { get; init; } = [];
    public List<Telop> Telops { get; init; } = [];
    public required AudioSpec Audio { get; init; }
    public Provenance Provenance { get; init; } = new();
    public double Duration => Timeline.Sum(c => c.Length);
}

// ---- Jobs / renders / QA ----
public enum JobStatus { Queued, Ingesting, Transcribing, Analyzing, Selecting, Planning, Previewing, Ready, Rendering, Done, Failed, Cancelled }

public sealed record CostEstimate(double AsrMinutes, int LlmInputTokens, int LlmOutputTokens, double EstimatedJpy, string Basis);

/// <summary>User-selectable options for one generation (PRD FR-SEL-10 and §7.9/7.6).</summary>
public sealed record JobOptions
{
    /// <summary>How much of a long transcript is sent to the AI: full | balanced | economy.</summary>
    public string Scope { get; init; } = "balanced";
    /// <summary>auto (preset default) | none | library/user track id.</summary>
    public string Bgm { get; init; } = "auto";
    public bool Sfx { get; init; } = true;
    public bool Tsukkomi { get; init; }
}

public sealed record Job
{
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required List<string> PresetIds { get; init; }
    public JobStatus Status { get; init; } = JobStatus.Queued;
    public double Progress { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string? ErrorCode { get; init; }
    public string? LlmModel { get; init; }
    public string? PromptVersion { get; init; }
    public string? Instruction { get; init; }
    public JobOptions Options { get; init; } = new();
    public List<string> ExcludeCandidateIds { get; init; } = [];
    public List<string> CandidateIds { get; init; } = [];
    public Dictionary<string, double> StageSeconds { get; init; } = [];
    public CostEstimate? Cost { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public enum QaStatus { Pass, Warn, Fail }

public sealed record QaResult(string CheckId, QaStatus Status, string Measured, string Threshold, string Message)
{
    public bool Hard => CheckId.StartsWith("H-", StringComparison.Ordinal);
}

public sealed record Render
{
    public required string Id { get; init; }
    public required string EditplanId { get; init; }
    public required string Kind { get; init; }   // preview | final
    public required string Path { get; init; }
    public string FfmpegVersion { get; init; } = "";
    public List<string> Commands { get; init; } = [];
    public double Duration { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<QaResult> Qa { get; init; } = [];
    public Dictionary<string, string> Outputs { get; init; } = [];
}
