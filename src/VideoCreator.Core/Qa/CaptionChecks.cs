using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;

namespace VideoCreator.Qa;

/// <summary>The per-caption findings of S-01 (speed), S-02 (line width) and S-08 (kinsoku), shared by QaEngine.PlanChecks and QaRepair
/// so a repair candidate can be judged on the captions it touches without re-running every check.</summary>
public sealed class CaptionChecks
{
    readonly Preset p;
    readonly int canvasWidth;
    readonly Transcript tr;
    readonly Dictionary<string, int> wordIndex = [];

    public CaptionChecks(Preset preset, int canvasWidth, Transcript transcript)
    {
        p = preset; this.canvasWidth = canvasWidth; tr = transcript;
        for (int i = 0; i < tr.Words.Count; i++) wordIndex.TryAdd(tr.Words[i].Id, i);
    }

    public int LineChars => AssWriter.CaptionLineChars(p, canvasWidth);

    public static double Cps(Telop t) => JapaneseText.DisplayWidth(t.Text) / Math.Max(0.05, t.End - t.Start);

    /// <summary>S-01: shown faster than the preset's characters per second.</summary>
    public string? Speed(Telop t) => Cps(t) > p.Telop.Caption.MaxCps + 0.01
        ? $"毎秒 {Cps(t):F1} 字（{t.End - t.Start:F1} 秒に {JapaneseText.DisplayWidth(t.Text)} 字）" : null;

    /// <summary>S-02 for a caption: a wrapped line wider than the safe zone allows.</summary>
    public string? Width(Telop t) => AssWriter.CaptionLines(t.Text, p, canvasWidth, tr.Language).Any(l => JapaneseText.DisplayWidth(l) > LineChars)
        ? $"{JapaneseText.DisplayWidth(t.Text)} 字" : null;

    static string Bare(string s) => new(JapaneseText.NormalizeTelop(s).Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>S-08: a line breaks a JIS X 4051 rule, or the caption starts with a particle/auxiliary left over from the previous caption.</summary>
    public string? Kinsoku(Telop t, IEnumerable<Telop> caps) =>
        JapaneseText.KinsokuViolations(AssWriter.CaptionLines(t.Text, p, canvasWidth, tr.Language)).FirstOrDefault() ?? (tr.Language == "ja-JP" ? DetachedHead(t, caps) : null);

    string? DetachedHead(Telop t, IEnumerable<Telop> caps)
    {
        if (t.SourceWordIds.Count == 0 || t.Start <= 0.01 || !wordIndex.TryGetValue(t.SourceWordIds[0], out var i0)) return null;
        var w0 = tr.Words[i0].Text;
        if (JapaneseText.IsPunct(w0) || JapaneseText.CanStartLine(w0)) return null;
        // judged on what is shown: a head the filler cleaning / a hand edit removed is not on screen
        if (Bare(w0) is not { Length: > 0 } b0 || !Bare(t.Text).StartsWith(b0, StringComparison.Ordinal)) return null;
        // 「。で、えっと」: a conjunction opening a new sentence, not a particle left over from the previous caption
        if (JapaneseText.CanStartSentence(w0) && i0 > 0 && JapaneseText.EndsSentence(tr.Words[i0 - 1].Text)) return null;
        return caps.Any(o => o.End >= t.Start - 0.05 && o.Start < t.Start && o.Speaker == t.Speaker) ? $"先頭が「{w0}」" : null;
    }

    /// <summary>The finding of <paramref name="check"/> (S-01/S-02/S-08) on caption <paramref name="t"/>, or null.</summary>
    public string? Of(string check, Telop t, IEnumerable<Telop> caps) => check switch
    {
        "S-01" => Speed(t),
        "S-02" => Width(t),
        "S-08" => Kinsoku(t, caps),
        _ => null,
    };
}
