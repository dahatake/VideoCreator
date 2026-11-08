namespace VideoCreator.Models;

// ---- Project / assets (PRD §9.2) ----
public sealed record Project
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Language { get; init; } = "ja-JP";
    public required string Directory { get; init; }
    public List<SceneCardImage> SceneCardImages { get; init; } = [];
    /// <summary>The user's 作る / 作らない choice for video-extracted scene-card images. Null (older projects) = use them when some exist.</summary>
    public bool? MakeSceneCardImages { get; init; }
    public bool UsesSceneCardImages() => MakeSceneCardImages ?? SceneCardImages.Count > 0;
}

/// <summary>
/// A project-only card image extracted from a video frame, with provenance for rights checks: either a project source
/// (<see cref="AssetId"/>, its rights confirmation) or a registered opening / closing video (<see cref="BrandPath"/>, the brand kit's confirmation).
/// </summary>
public sealed record SceneCardImage(string Path, int Width, int Height, string? AssetId, double SourceT, string? BrandPath = null);

public enum AssetRole { Main, CamA, CamB, Wide, AudioMaster, Screen }

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
    /// <summary>Seconds such that cameraTime = masterTime + SyncOffset + SyncDriftPpm·1e-6·masterTime (audio alignment, metadata or manual).</summary>
    public double SyncOffset { get; init; }
    public double? SyncConfidence { get; init; }
    /// <summary>Clock-rate difference to the master (parts per million); 0 = none measured.</summary>
    public double SyncDriftPpm { get; init; }
    /// <summary>audio | metadata | manual | none. Null = aligned by an older version (single window) → aligned again.</summary>
    public string? SyncMethod { get; init; }
    /// <summary>Median disagreement of the measurement windows with the fit (ms) and how many windows agreed.</summary>
    public double? SyncResidualMs { get; init; }
    public string? SyncWindows { get; init; }
    public List<string> Warnings { get; init; } = [];

    /// <summary>This asset's own time for master time <paramref name="masterT"/> (identity for the master itself).</summary>
    public double CameraTime(double masterT, string masterId) => Id == masterId ? masterT : masterT + SyncOffset + SyncDriftPpm * 1e-6 * masterT;
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
    /// <summary>The user's approval, bound to the edit plan as it was when approved; any later edit makes it stale.</summary>
    public CandidateApproval? Approval { get; init; }
    /// <summary>Set when this candidate is one part of a 前編/後編 series (JobOptions.PartMinutes).</summary>
    public SeriesInfo? Series { get; init; }
    public double TotalDuration => Segments.Sum(s => s.Length);
}

/// <summary>Who approved a candidate, when, and the <see cref="EditPlanFingerprint"/> of the plan they saw.</summary>
public sealed record CandidateApproval(DateTimeOffset At, string By, string PlanHash);

public static class EditPlanFingerprint
{
    /// <summary>SHA-256 of the plan's canonical JSON: every edit (telops, cuts, BGM, inserts…) changes it.</summary>
    public static string Of(EditPlan plan) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json.Serialize(plan))));
}

// ---- Series (前編/後編): a long source edited into consecutive parts of a user-chosen length ----
/// <summary>A chapter on the part's OUTPUT timeline (seconds).</summary>
public sealed record SeriesChapter(double Start, string Title);

public sealed record SeriesInfo
{
    public int Part { get; init; }                 // 1-based
    public int Parts { get; init; }
    public required string Label { get; init; }    // 前編 / 中編 / 後編 / 第n話
    /// <summary>Labels of every part in order (structural text, used by the H-07 grounding check).</summary>
    public List<string> Labels { get; init; } = [];
    public string SeriesTitle { get; init; } = "";
    public double TargetSec { get; init; }
    /// <summary>Set when the user chose the number of parts (TargetSec is then the balanced length).</summary>
    public int? RequestedParts { get; init; }
    public double MinSec { get; init; }
    public double MaxSec { get; init; }
    /// <summary>Source range (master time) of this part's main body; bodies of consecutive parts tile the source.</summary>
    public required Interval Body { get; init; }
    public List<SeriesChapter> Chapters { get; init; } = [];
    /// <summary>Why the split before / after this part was placed where it is.</summary>
    public string Boundary { get; init; } = "";
}

// ---- EditPlan (PRD §9.3) ----
public sealed record TrackPoint(double T, double X);

public sealed record ReframeSpec
{
    public string Mode { get; init; } = "blur_bg";   // blur_bg | fit | face | screen
    public int FgOffsetY { get; init; } = -120;
    /// <summary>face / screen mode: crop window (source pixels), left edge over clip time (seconds after the clip start).</summary>
    public List<TrackPoint>? Track { get; init; }
    public int CropW { get; init; }
    public int CropH { get; init; }
    public int CropY { get; init; }
    /// <summary>screen mode: crop top edge over clip time (null = <see cref="CropY"/>). CropW = 0 shows the whole screen fitted to the box.</summary>
    public List<TrackPoint>? TrackY { get; init; }
    /// <summary>screen mode: the band of the canvas (full-size pixels) the screen is shown in; the rest is its blurred, dimmed copy.</summary>
    public int BoxY { get; init; }
    public int BoxH { get; init; }
}

/// <summary>Picture-in-picture: a circular inset of another (synchronised) asset, e.g. the presenter's camera over a screen recording.</summary>
public sealed record PipSpec
{
    public required string AssetId { get; init; }
    /// <summary>Start in the PiP asset's own time; it runs as long as the clip, at the clip's speed.</summary>
    public double SrcIn { get; init; }
    /// <summary>Square crop in the PiP asset (source pixels), usually around the face.</summary>
    public int CropX { get; init; }
    public int CropY { get; init; }
    public int CropS { get; init; }
    /// <summary>Position (top-left of the ring) and inner diameter on the full-size canvas; <see cref="Border"/> = white ring width.</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int D { get; init; }
    public int Border { get; init; } = 8;
    /// <summary>tl | tr | bl | br — chosen to cover the least on-screen activity.</summary>
    public string Corner { get; init; } = "br";
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
    /// <summary>Optional inset of a second asset (screen layout: the presenter over the screen recording).</summary>
    public PipSpec? Pip { get; init; }
    public double Zoom { get; init; } = 1.0;
    public string TransitionIn { get; init; } = "cut";
    public string Role { get; init; } = "body";      // body | teaser | reaction | wide | recap | next (series)
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
    public SeriesInfo? Series { get; init; }
    /// <summary>Opening / closing videos and scene-change cards placed between timeline clips. Telops, SFX and chapters stay on the BODY timeline; see Planning.InsertMap.</summary>
    public List<Insert> Inserts { get; init; } = [];
    /// <summary>Length of the edited body (the timeline) — what the preset's duration range applies to.</summary>
    public double Duration => Timeline.Sum(c => c.Length);
    /// <summary>Length of the rendered file: body + enabled inserts.</summary>
    public double TotalDuration => Duration + Inserts.Where(i => i.Enabled).Sum(i => i.Duration);
}

/// <summary>Something played between timeline clips: an opening / closing video (bumper) or a ~1 s scene-change card.</summary>
public sealed record Insert
{
    public required string Id { get; init; }
    public required string Kind { get; init; }       // opening | closing | card
    /// <summary>Body-timeline time (a clip boundary) where the insert plays; body content from this time on starts after it.</summary>
    public double At { get; init; }
    public double Duration { get; init; }
    /// <summary>Bumper video, or the card's image (null = text card over the frozen last frame of the previous scene).</summary>
    public string? Path { get; init; }
    /// <summary>The registered file when <see cref="Path"/> is its upscaled copy (also the fallback if the cached copy has been deleted).</summary>
    public string? OriginalPath { get; init; }
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
    /// <summary>How <see cref="Path"/> was made from <see cref="OriginalPath"/> (null = used as registered).</summary>
    public string? Enhanced { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool HasAudio { get; init; }
    /// <summary>Gain that brings the bumper's own audio to the plan's loudness target (from the loudness measured at registration).
    /// At render time it is re-based on the body's measured loudness, so the bumper ends up exactly as loud as the speech after the whole-file normalisation.</summary>
    public double GainDb { get; init; }
    public bool RightsConfirmed { get; init; }
    /// <summary>Source asset of an extracted card image; checked again when exporting.</summary>
    public string? ImageAssetId { get; init; }
    /// <summary>Card text (tsukkomi). Empty for bumpers and for image cards without text.</summary>
    public string Text { get; init; } = "";
    public string TextSource { get; init; } = "";    // ai | fixed | user
    public bool Approved { get; init; } = true;
    public bool Enabled { get; init; } = true;
    /// <summary>Card: master time of the cut it covers (used to keep user edits when the plan is rebuilt).</summary>
    public double? SourceT { get; init; }
    public string? Sfx { get; init; }
}

// ---- Brand kit: the channel's opening / closing videos and card images (registered once, used by every project) ----
public sealed record BrandFile
{
    public required string Path { get; init; }
    public required string Kind { get; init; }       // opening | closing | card
    public int Width { get; init; }
    public int Height { get; init; }
    public double Duration { get; init; }
    public bool HasAudio { get; init; }
    /// <summary>Integrated loudness of the file's audio (EBU R128, measured at registration). NaN when silent / no audio.</summary>
    public double Lufs { get; init; } = double.NaN;
    public double Aspect => Height == 0 ? 0 : (double)Width / Height;
    /// <summary>Set when a smaller-than-canvas video was upscaled at registration and the per-file quality check accepted it; renders use this file.</summary>
    public BrandEnhancement? Enhancement { get; init; }
    public string? ImageAssetId { get; init; }
}

/// <summary>
/// Result of the registration-time upscale. <see cref="Gain"/> is measured on THIS file (its own downscaled copy upscaled back and compared with it):
/// the enhanced file is only used when it is at least as faithful as the render-time lanczos.
/// </summary>
public sealed record BrandEnhancement
{
    public string? Path { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public required string Method { get; init; }
    public string Device { get; init; } = "";
    public double Seconds { get; init; }
    public QualityGain? Gain { get; init; }
    public bool Adopted => Path != null;
    public string Note { get; init; } = "";
}

/// <summary>Enhanced minus lanczos on the same test (FFmpeg libvmaf NEG model, XPSNR luma, SSIM).</summary>
public sealed record QualityGain(double Vmaf, double Xpsnr, double Ssim);

public sealed record BrandKit
{
    public List<string> Sources { get; init; } = [];
    public List<BrandFile> Files { get; init; } = [];
    public bool RightsConfirmed { get; init; }
    public string ConfirmedBy { get; init; } = Environment.UserName;
    public DateTimeOffset ConfirmedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Scene-change card length (seconds).</summary>
    public double CardSec { get; init; } = 1.0;
    /// <summary>Upscale opening / closing videos smaller than the output once, at registration (kept only where it measurably helps).</summary>
    public bool Upscale { get; init; } = true;
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
    /// <summary>Series mode: edit the whole source into consecutive parts (前編/後編…) of about this many minutes each. Null = short candidates.</summary>
    public double? PartMinutes { get; init; }
    /// <summary>Series mode by count: exactly this many parts of balanced length. Mutually exclusive with <see cref="PartMinutes"/>.</summary>
    public int? Parts { get; init; }
    public bool IsSeries => PartMinutes != null || Parts != null;
    /// <summary>Play the registered opening video (after the cold open when there is one).</summary>
    public bool Opening { get; init; }
    /// <summary>Play the registered closing video at the end.</summary>
    public bool Closing { get; init; }
    /// <summary>~1 s tsukkomi cards at scene changes.</summary>
    public bool SceneCards { get; init; }
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
