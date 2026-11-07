using System.Globalization;
using System.Text;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

/// <summary>ASS (libass) writer — styles follow R§3.3 / C-4; text escaped (NFR-SEC-06).</summary>
public static class AssWriter
{
    public const string FontName = "M PLUS 1p ExtraBold";   // OFL, bundled in tools/fonts (Q-6)
    public const int HeadlineMarginV = 300, EmphLift = 115, MarginL = 60, CounterMarginV = 520, TsukkomiMarginV = 930, SfxY = 820;
    /// <summary>Glyph advance / font size for M PLUS 1p ExtraBold. Measured 0.70 (spikes/03: 15 chars @72px = 771px); 0.75 keeps a margin.</summary>
    public const double GlyphRatio = 0.75;

    /// <summary>Font size that keeps the telop inside the horizontal safe zone (C-4): shrink instead of overflowing.</summary>
    public static int FitFontSize(string text, int fontPx, int outline, int canvasWidth, int marginR)
    {
        double available = canvasWidth - MarginL - marginR - 2 * outline;
        double est = Planning.JapaneseText.DisplayWidth(text) * fontPx * GlyphRatio;
        return est <= available ? fontPx : Math.Max(24, (int)Math.Floor(fontPx * available / est));
    }

    public static string Write(EditPlan plan, Preset p)
    {
        var c = p.Telop.Caption;
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {plan.Canvas.Width}");
        sb.AppendLine($"PlayResY: {plan.Canvas.Height}");
        sb.AppendLine("WrapStyle: 2");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        // colours are &HAABBGGRR. White=speech, yellow=emphasis (FR-TEL-10). Captions: black outline (readable on any background).
        sb.AppendLine($"Style: Caption,{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,7,2,2,60,{c.MarginR},{c.MarginV},1");
        sb.AppendLine($"Style: Emph,{FontName},{(int)(c.FontPx * 1.25)},&H0000F0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,9,3,2,60,{c.MarginR},{c.MarginV + EmphLift},1");
        sb.AppendLine($"Style: Hook,{FontName},84,&H00FFFFFF,&H0000FFFF,&H00D47800,&H00D47800,0,0,0,0,100,100,0,0,3,18,0,8,60,{c.MarginR},{HeadlineMarginV},1");
        sb.AppendLine($"Style: Headline,{FontName},60,&H00FFFFFF,&H0000FFFF,&H00202020,&HB0202020,0,0,0,0,100,100,0,0,3,14,0,8,60,{c.MarginR},{HeadlineMarginV},1");
        sb.AppendLine($"Style: Counter,{FontName},96,&H0000E0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,10,3,8,60,{c.MarginR},{CounterMarginV},1");
        sb.AppendLine($"Style: Sfx,{FontName},120,&H002020E0,&H0000FFFF,&H00FFFFFF,&H80000000,0,0,0,0,100,100,0,0,1,10,3,5,60,{c.MarginR},0,1");
        sb.AppendLine($"Style: Tsukkomi,{FontName},64,&H00202020,&H0000FFFF,&H00FFFFFF,&H00FFFFFF,0,0,0,0,100,100,0,0,3,12,0,2,60,{c.MarginR},{TsukkomiMarginV},1");
        // per-speaker caption colour: white text, speaker-coloured outline (FR-TEL-10)
        string[] outlines = ["&H00D47800", "&H003C3CD4", "&H0050A030"];
        for (int i = 0; i < outlines.Length; i++)
            sb.AppendLine($"Style: CapS{i + 1},{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,{outlines[i]},&H80000000,0,0,0,0,100,100,0,0,1,9,2,2,60,{c.MarginR},{c.MarginV},1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            var layer = t.Type is TelopType.Hook or TelopType.Headline ? 2 : t.Type is TelopType.Emphasis or TelopType.Counter or TelopType.Sfx or TelopType.Tsukkomi ? 1 : 0;
            var style = t.Style;
            if (t.Type == TelopType.Caption && p.Telop.SpeakerColors && t.Speaker is > 0) style = $"CapS{(t.Speaker.Value - 1) % 3 + 1}";
            var (size, outline) = style switch
            {
                "Emph" => ((int)(c.FontPx * 1.25), 9), "Hook" => (84, 18), "Headline" => (60, 14), "Counter" => (96, 10), "Sfx" => (120, 10), "Tsukkomi" => (64, 12),
                _ when style.StartsWith("CapS") => (c.FontPx, 9), _ => (c.FontPx, 7),
            };
            var fit = FitFontSize(t.Text, size, outline, plan.Canvas.Width, c.MarginR);
            var fs = fit < size ? $@"\fs{fit}" : "";
            var pos = style == "Sfx" ? $@"\an5\pos({plan.Canvas.Width / 2},{SfxY})\frz-8" : "";
            var fx = t.Animation == "pop" ? $@"{{{pos}\fad(60,0){fs}\fscx70\fscy70\t(0,110,\fscx112\fscy112)\t(110,220,\fscx100\fscy100)}}" : (fs.Length > 0 || pos.Length > 0) ? $"{{{pos}{fs}}}" : "";
            sb.AppendLine($"Dialogue: {layer},{Time(t.Start)},{Time(t.End)},{style},,0,0,0,,{fx}{Escape(t.Text)}");
        }
        return sb.ToString();
    }

    public static string Time(double s)
    {
        if (s < 0) s = 0;
        var cs = (long)Math.Round(s * 100, MidpointRounding.AwayFromZero);
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", cs / 360000, cs / 6000 % 60, cs / 100 % 60, cs % 100);
    }

    /// <summary>Neutralise ASS override syntax and line breaks so user/LLM text cannot inject tags.</summary>
    public static string Escape(string text) =>
        text.Replace("\\", "＼").Replace("{", "｛").Replace("}", "｝").Replace("\r", "").Replace("\n", " ");

    public static string Srt(EditPlan plan)
    {
        var sb = new StringBuilder(); int i = 0;
        foreach (var t in plan.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).OrderBy(t => t.Start))
        {
            sb.AppendLine((++i).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine($"{SrtTime(t.Start)} --> {SrtTime(t.End)}");
            sb.AppendLine(t.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string SrtTime(double s)
    {
        var ms = (long)Math.Round(Math.Max(0, s) * 1000);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}", ms / 3600000, ms / 60000 % 60, ms / 1000 % 60, ms % 1000);
    }
}
