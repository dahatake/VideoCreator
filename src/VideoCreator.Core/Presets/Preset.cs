using System.Text.Json;
using VideoCreator.Models;

namespace VideoCreator.Presets;

public sealed record CaptionStyle
{
    public int FontPx { get; init; } = 78;
    public int MaxCharsPerLine { get; init; } = 14;
    public int MaxLines { get; init; } = 1;
    public double MaxCps { get; init; } = 6;
    public int MarginV { get; init; } = 620;
    public int MarginR { get; init; } = 160;
}

public sealed record PresetTarget
{
    public string Aspect { get; init; } = "9:16";
    public double[] DurationSec { get; init; } = [25, 35];
    public double Min => DurationSec[0];
    public double Max => DurationSec[1];
}

public sealed record PresetHook
{
    public double MaxSec { get; init; } = 2.0;
    public List<string> Prefer { get; init; } = [];
}

public sealed record PresetCut
{
    public double? RemoveSilenceOverSec { get; init; } = 0.7;
    public double MinShotSec { get; init; } = 1.0;
    public double[] TargetAslSec { get; init; } = [1.5, 3.0];
    public List<string> SnapTo { get; init; } = ["sentence_end", "shot_boundary"];
    public double PunchInScale { get; init; } = 1.15;
    public string ColdOpen { get; init; } = "none";   // none | teaser
    /// <summary>P8: compress low-activity stretches. MaxFactor caps the speed, SpeechFactor is used while someone talks.</summary>
    public SpeedUp? SpeedUp { get; init; }
    /// <summary>Cross-fade length (seconds) at scene changes (source jumps > 3 s); 0 = hard cuts only.</summary>
    public double SceneFadeSec { get; init; }
}

public sealed record SpeedUp(double MaxFactor = 16, double SpeechFactor = 1.0, double TailSec = 4.0, double TeaserSec = 1.5);

/// <summary>P7: multi-camera switching rules (PRD FR-SWT-02..05).</summary>
public sealed record PresetSwitching(double MinHoldSec = 2.0, double ReactionSec = 1.2, double WideEverySec = 45, double WideOpenSec = 2.0);

public sealed record PresetTelop
{
    public List<string> Types { get; init; } = ["caption", "emphasis", "headline"];
    public CaptionStyle Caption { get; init; } = new();
    /// <summary>Fixed caption colour per speaker (FR-TEL-10), for conversations.</summary>
    public bool SpeakerColors { get; init; }
}

public sealed record PresetLoudnorm(double I = -14, double TP = -1.5);

public sealed record PresetAudio
{
    public PresetLoudnorm Loudnorm { get; init; } = new();
    public double BgmDuckDb { get; init; } = -15;
    public string Sfx { get; init; } = "none";          // none | light | heavy
    /// <summary>none | auto (beat-synced patterns pick a library track) | library track id.</summary>
    public string Bgm { get; init; } = "none";
    public double BgmDb { get; init; } = -16;
    public bool Duck { get; init; } = true;
    public double DialogueGainDb { get; init; }
}

public sealed record PresetLoop(bool Enabled = false, bool Required = false);

public sealed record PresetCard(string Opening, string Tempo, string Telop, string Sound);

public sealed record PresetExample(string Title, string Url);

public sealed record Preset
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Name { get; init; }
    public string Genre { get; init; } = "";
    public bool Available { get; init; } = true;
    public string? UnavailableReason { get; init; }
    public required PresetTarget Target { get; init; }
    public required PresetHook Hook { get; init; }
    public required PresetCut Cut { get; init; }
    public required PresetTelop Telop { get; init; }
    public required PresetAudio Audio { get; init; }
    public required PresetLoop Loop { get; init; }
    public PresetCard? Card { get; init; }
    public List<PresetExample> Examples { get; init; } = [];
    public List<string> IncompatibleWith { get; init; } = [];
    public string SelectionRules { get; init; } = "";
    /// <summary>speech | switch | speed | beat — which planner turns a candidate into an EditPlan.</summary>
    public string Planner { get; init; } = "speech";
    /// <summary>auto: follow a single face when there is one (else blurred background); blur: always blurred background.</summary>
    public string Reframe { get; init; } = "auto";
    public PresetSwitching? Switching { get; init; }

    public bool HasTelop(string type) => Telop.Types.Contains(type);
    public PresetRef Ref => new(Id, Version);
}

public sealed class PresetValidationException(string message) : Exception(message);

public static class PresetStore
{
    public static IReadOnlyList<Preset> LoadDirectory(string dir)
    {
        var list = new List<Preset>();
        foreach (var f in Directory.GetFiles(dir, "P*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var p = JsonSerializer.Deserialize<Preset>(File.ReadAllText(f), Json.Options) ?? throw new PresetValidationException($"{f}: empty");
            Validate(p, Path.GetFileName(f));
            list.Add(p);
        }
        return list;
    }

    /// <summary>Enforces the PRD §20.1 schema ranges in code.</summary>
    public static void Validate(Preset p, string source = "")
    {
        void Req(bool ok, string msg) { if (!ok) throw new PresetValidationException($"{source} {p.Id}: {msg}"); }
        Req(System.Text.RegularExpressions.Regex.IsMatch(p.Id, "^P[0-9]+$"), "id must match ^P[0-9]+$");
        Req(System.Text.RegularExpressions.Regex.IsMatch(p.Version, @"^\d+\.\d+\.\d+$"), "version must be semver");
        Req(p.Target.Aspect is "9:16" or "1:1" or "16:9", "aspect");
        Req(p.Target.DurationSec.Length == 2 && p.Target.Min >= 5 && p.Target.Max <= 180 && p.Target.Min < p.Target.Max, "duration_sec must be [min,max] within 5..180 (C-1)");
        Req(p.Hook.MaxSec is >= 0.5 and <= 5, "hook.max_sec 0.5..5");
        Req(p.Cut.RemoveSilenceOverSec is null || p.Cut.RemoveSilenceOverSec >= 0.2, "remove_silence_over_sec >= 0.2");
        Req(p.Cut.MinShotSec >= 0.3, "min_shot_sec >= 0.3");
        Req(p.Cut.PunchInScale is >= 1.0 and <= 1.3, "punch_in_scale 1.0..1.3");
        var c = p.Telop.Caption;
        Req(c.FontPx is >= 48 and <= 120, "caption.font_px 48..120");
        Req(c.MaxCharsPerLine is >= 8 and <= 16, "caption.max_chars_per_line 8..16");
        Req(c.MaxLines is 1 or 2, "caption.max_lines 1..2");
        Req(c.MaxCps is >= 3 and <= 10, "caption.max_cps 3..10");
        Req(c.MarginV >= 580, "caption.margin_v >= 580 (C-4 safe zone)");
        Req(c.MarginR >= 160, "caption.margin_r >= 160 (C-4 safe zone)");
        Req(p.Planner is "speech" or "switch" or "speed" or "beat", "planner must be speech|switch|speed|beat");
        Req(p.Reframe is "auto" or "blur", "reframe must be auto|blur");
        Req(p.Audio.Sfx is "none" or "light" or "heavy", "audio.sfx must be none|light|heavy");
        Req(p.Audio.BgmDb is >= -40 and <= -6, "audio.bgm_db -40..-6");
        Req(p.Planner != "switch" || p.Switching is { MinHoldSec: >= 1.0 and <= 4.0 }, "switch planner needs switching.min_hold_sec 1..4 (FR-SWT-02)");
        Req(p.Cut.SpeedUp is null || p.Cut.SpeedUp.MaxFactor is >= 1 and <= 64, "speed_up.max_factor 1..64");
        Req(p.Audio.Loudnorm.I == -14, "loudnorm.I must be -14 (C-5)");
        Req(p.Audio.Loudnorm.TP <= -1.0, "loudnorm.TP <= -1.0 (C-5)");
        var known = new HashSet<string> { "caption", "emphasis", "tsukkomi", "headline", "sfx", "counter" };
        Req(p.Telop.Types.All(known.Contains), "unknown telop type");
    }
}
