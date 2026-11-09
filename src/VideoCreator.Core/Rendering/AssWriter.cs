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

    /// <summary>
    /// Full-width characters that fit on one caption line: the preset's max_chars_per_line, lowered automatically when the preset's font,
    /// condensing and outline would push a line of that length outside the horizontal safe zone (C-4).
    /// </summary>
    public static int CaptionLineChars(Preset p, int canvasWidth = 1080)
    {
        var (size, outline, scaleX) = Metrics("Caption", p);
        if (p.Telop.SpeakerColors) outline = Math.Max(outline, Metrics("CapS1", p).Outline);
        double available = canvasWidth - MarginL - p.Telop.Caption.MarginR - 2 * outline;
        int fit = (int)Math.Floor(available / (size * GlyphRatio * scaleX / 100.0));
        return Math.Clamp(fit, 1, p.Telop.Caption.MaxCharsPerLine);
    }

    /// <summary>Displayed lines of a caption: one line, or up to caption.max_lines broken where kinsoku allows.</summary>
    public static List<string> CaptionLines(string text, Preset p, int canvasWidth = 1080) =>
        Planning.JapaneseText.WrapLines(text, CaptionLineChars(p, canvasWidth), p.Telop.Caption.MaxLines);

    /// <summary>Font size that keeps the telop inside the horizontal safe zone (C-4): shrink instead of overflowing. <paramref name="scaleX"/> is the style's ScaleX (%).</summary>
    public static int FitFontSize(string text, int fontPx, int outline, int canvasWidth, int marginR, int scaleX = 100)
    {
        double available = canvasWidth - MarginL - marginR - 2 * outline;
        double est = Planning.JapaneseText.DisplayWidth(text) * fontPx * GlyphRatio * scaleX / 100.0;
        return est <= available ? fontPx : Math.Max(24, (int)Math.Floor(fontPx * available / est));
    }

    /// <summary>
    /// Variety look (preset telop.look = "variety"), from measurements of Japanese variety videos (docs/IMPLEMENTATION.md §12):
    /// captions ≈ 10 % of the width per character, condensed (長体) with a dark halo so white text reads on a white set;
    /// punch telops (emphasis / tsukkomi) ≈ 15–20 % of the width per character and only one at a time; emphasis words red inside captions.
    /// </summary>
    public static class Variety
    {
        public const int CaptionScaleX = 85, CaptionOutline = 10, HaloOutline = 24, HaloBlur = 10;
        public const int EmphPx = 210, EmphOutline = 14, TsukkomiPx = 180, TsukkomiPad = 20, SfxPx = 160, HeadlinePx = 56, HeadlinePad = 14;
        /// <summary>The punch band sits this far above the caption baseline band (caption ≤ 128 px · 1.3 line box + outlines).</summary>
        public const int PunchLift = 200;
        /// <summary>&amp;HBBGGRR: red for emphasis words inside captions; red tsukkomi box; light-cyan headline bar with navy text.</summary>
        public const string KeywordRed = "&H2A2AE8&", BoxRed = "&H001C1CE6", BarCyan = "&H00F5E6A0", BarNavy = "&H00803010";
        public const int LayerHalo = 0, LayerCaption = 1, LayerPunch = 2, LayerTop = 3, LayerCard = 4;
    }

    /// <summary>(font size, outline or box padding, ScaleX) of a style — the one table used for fitting (here) and for the QA bands.</summary>
    public static (int Size, int Outline, int ScaleX) Metrics(string style, Preset p)
    {
        var c = p.Telop.Caption;
        if (p.Telop.IsVariety)
            return style switch
            {
                "Emph" => (Variety.EmphPx, Variety.EmphOutline, 100), "Hook" => (84, 18, 100), "Headline" => (Variety.HeadlinePx, Variety.HeadlinePad, 100),
                "Counter" => (96, 10, 100), "Sfx" => (Variety.SfxPx, 10, 100), "Tsukkomi" => (Variety.TsukkomiPx, Variety.TsukkomiPad, 100),
                _ => (c.FontPx, Variety.CaptionOutline, Variety.CaptionScaleX),
            };
        return style switch
        {
            "Emph" => ((int)(c.FontPx * 1.25), 9, 100), "Hook" => (84, 18, 100), "Headline" => (60, 14, 100), "Counter" => (96, 10, 100), "Sfx" => (120, 10, 100), "Tsukkomi" => (64, 12, 100),
            _ when style.StartsWith("CapS") => (c.FontPx, 9, 100), _ => (c.FontPx, 7, 100),
        };
    }

    /// <summary>Vertical band [top, bottom] (canvas px) a body telop can occupy; null for telops positioned elsewhere (top bar, SFX). Used by QA S-05.</summary>
    public static (double Top, double Bottom)? Band(TelopType type, Preset p, int canvasHeight)
    {
        var c = p.Telop.Caption; int H = canvasHeight;
        if (!p.Telop.IsVariety)
            return type switch
            {
                TelopType.Caption => (H - c.MarginV - c.FontPx * 1.3 * c.MaxLines, H - c.MarginV),
                TelopType.Emphasis => (H - c.MarginV - EmphLift - c.FontPx * 1.6, H - c.MarginV - EmphLift),
                TelopType.Tsukkomi => (H - TsukkomiMarginV - 90, H - TsukkomiMarginV),
                TelopType.Counter => (CounterMarginV, CounterMarginV + 120),
                _ => null,
            };
        double punchBottom = H - c.MarginV - Variety.PunchLift;
        return type switch
        {
            TelopType.Caption => (H - c.MarginV - c.FontPx * 1.3 * c.MaxLines - Variety.CaptionOutline, H - c.MarginV + Variety.CaptionOutline),
            TelopType.Emphasis => (punchBottom - Variety.EmphPx * 1.3 - Variety.EmphOutline, punchBottom + Variety.EmphOutline),
            TelopType.Tsukkomi => (punchBottom - Variety.TsukkomiPx * 1.3 - Variety.TsukkomiPad, punchBottom + Variety.TsukkomiPad),
            TelopType.Counter => (CounterMarginV, CounterMarginV + 120),
            _ => null,
        };
    }

    /// <summary>Emphasis words (≥ 2 chars, from enabled emphasis telops — grounded verbatim in the transcript) painted red inside a caption. Returns escaped ASS text.</summary>
    public static string Colorize(string text, IReadOnlyList<string> words)
    {
        var marks = new bool[text.Length];
        foreach (var w in words.Where(w => w.Length >= 2).OrderByDescending(w => w.Length))
            for (int i = text.IndexOf(w, StringComparison.Ordinal); i >= 0; i = text.IndexOf(w, i + w.Length, StringComparison.Ordinal))
                if (!marks.AsSpan(i, w.Length).Contains(true)) Array.Fill(marks, true, i, w.Length);
        if (!marks.Contains(true)) return Escape(text);
        var sb = new StringBuilder(); bool red = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (marks[i] != red) { sb.Append(marks[i] ? $@"{{\c{Variety.KeywordRed}}}" : @"{\c&HFFFFFF&}"); red = marks[i]; }
            sb.Append(Escape(text[i].ToString()));
        }
        return sb.ToString();
    }

    public static string Write(EditPlan plan, Preset p) => p.Telop.IsVariety ? WriteVariety(plan, p) : WriteStandard(plan, p);

    static string WriteVariety(EditPlan plan, Preset p)
    {
        var c = p.Telop.Caption; int sx = Variety.CaptionScaleX;
        var sb = new StringBuilder();
        Header(sb, plan);
        // captions: white, condensed, black outline + a separate halo event (transparent fill, wide blurred dark border) underneath
        sb.AppendLine($"Style: Caption,{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,&H00000000,&H00000000,0,0,0,0,{sx},100,0,0,1,{Variety.CaptionOutline},0,2,60,{c.MarginR},{c.MarginV},1");
        sb.AppendLine($"Style: Halo,{FontName},{c.FontPx},&HFF000000,&H0000FFFF,&H30000000,&H00000000,0,0,0,0,{sx},100,0,0,1,{Variety.HaloOutline},0,2,60,{c.MarginR},{c.MarginV},1");
        string[] outlines = ["&H00D47800", "&H003C3CD4", "&H0050A030"];
        for (int i = 0; i < outlines.Length; i++)
            sb.AppendLine($"Style: CapS{i + 1},{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,{outlines[i]},&H00000000,0,0,0,0,{sx},100,0,0,1,{Variety.CaptionOutline},0,2,60,{c.MarginR},{c.MarginV},1");
        int punchV = c.MarginV + Variety.PunchLift;
        sb.AppendLine($"Style: Emph,{FontName},{Variety.EmphPx},&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,{Variety.EmphOutline},4,2,60,{c.MarginR},{punchV},1");
        sb.AppendLine($"Style: Tsukkomi,{FontName},{Variety.TsukkomiPx},&H00FFFFFF,&H0000FFFF,{Variety.BoxRed},{Variety.BoxRed},0,0,0,0,100,100,0,0,3,{Variety.TsukkomiPad},0,2,60,{c.MarginR},{punchV},1");
        sb.AppendLine($"Style: Hook,{FontName},84,&H00FFFFFF,&H0000FFFF,&H00D47800,&H00D47800,0,0,0,0,100,100,0,0,3,18,0,8,60,{c.MarginR},{HeadlineMarginV},1");
        sb.AppendLine($"Style: Headline,{FontName},{Variety.HeadlinePx},{Variety.BarNavy},&H0000FFFF,{Variety.BarCyan},{Variety.BarCyan},0,0,0,0,100,100,0,0,3,{Variety.HeadlinePad},0,8,60,{c.MarginR},{HeadlineMarginV},1");
        sb.AppendLine($"Style: Counter,{FontName},96,&H0000E0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,10,3,8,60,{c.MarginR},{CounterMarginV},1");
        sb.AppendLine($"Style: Sfx,{FontName},{Variety.SfxPx},&H002020E0,&H0000FFFF,&H00FFFFFF,&H80000000,0,0,0,0,100,100,0,0,1,10,3,5,60,{c.MarginR},0,1");
        CardStyle(sb, c);
        EventsHeader(sb);
        var ins = Planning.InsertMap.Active(plan);
        var keywords = plan.Telops.Where(t => t.Enabled && t.Type == TelopType.Emphasis).Select(t => t.Text.Trim()).Distinct().ToList();
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            bool caption = t.Type == TelopType.Caption;
            var layer = t.Type is TelopType.Hook or TelopType.Headline ? Variety.LayerTop : caption ? Variety.LayerCaption : Variety.LayerPunch;
            var style = t.Style;
            if (caption && p.Telop.SpeakerColors && t.Speaker is > 0) style = $"CapS{(t.Speaker.Value - 1) % 3 + 1}";
            var (size, outline, scaleX) = Metrics(style, p);
            var lines = caption ? CaptionLines(t.Text, p, plan.Canvas.Width) : [t.Text];
            var fit = lines.Min(l => FitFontSize(l, size, outline, plan.Canvas.Width, c.MarginR, scaleX));
            var fx = Fx(t, plan, fit < size ? $@"\fs{fit}" : "");
            var text = caption ? string.Join(@"\N", lines.Select(l => Colorize(l, keywords))) : Escape(t.Text);
            foreach (var (s, e) in ins.Count == 0 ? [(t.Start, t.End)] : Planning.InsertMap.Span(ins, t.Start, t.End))
            {
                if (caption) sb.AppendLine($@"Dialogue: {Variety.LayerHalo},{Time(s)},{Time(e)},Halo,,0,0,0,,{{\blur{Variety.HaloBlur}}}{fx}{string.Join(@"\N", lines.Select(Escape))}");
                sb.AppendLine($"Dialogue: {layer},{Time(s)},{Time(e)},{style},,0,0,0,,{fx}{text}");
            }
        }
        CardEvents(sb, plan, c, Variety.LayerCard);
        return sb.ToString();
    }

    static string Fx(Telop t, EditPlan plan, string fs)
    {
        var pos = t.Style == "Sfx" ? $@"\an5\pos({plan.Canvas.Width / 2},{SfxY})\frz-8" : "";
        return t.Animation == "pop" ? $@"{{{pos}\fad(60,0){fs}\fscx70\fscy70\t(0,110,\fscx112\fscy112)\t(110,220,\fscx100\fscy100)}}" : (fs.Length > 0 || pos.Length > 0) ? $"{{{pos}{fs}}}" : "";
    }

    static void Header(StringBuilder sb, EditPlan plan)
    {
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {plan.Canvas.Width}");
        sb.AppendLine($"PlayResY: {plan.Canvas.Height}");
        sb.AppendLine("WrapStyle: 2");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
    }

    static void CardStyle(StringBuilder sb, CaptionStyle c) =>
        // scene-change card (tsukkomi): big yellow text, thick black outline, drop shadow; positioned explicitly
        sb.AppendLine($"Style: Card,{FontName},{CardFontPx},&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,{CardOutline},6,5,60,{c.MarginR},0,1");

    static void EventsHeader(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
    }

    static void CardEvents(StringBuilder sb, EditPlan plan, CaptionStyle c, int layer)
    {
        foreach (var (card, s, e) in Planning.InsertMap.InsertSpans(Planning.InsertMap.Active(plan)).Where(x => x.Insert.Kind == "card" && x.Insert.Text.Length > 0))
        {
            var fit = CardFontSize(card, plan.Canvas, c);
            int x = card.Bgm != null ? plan.Canvas.Width / 2 : (MarginL + plan.Canvas.Width - c.MarginR) / 2;
            int y = (int)(plan.Canvas.Height * (card.Path != null && card.Bgm == null ? 0.62 : 0.42));
            var fs = fit < CardFontPx ? $@"\fs{fit}" : "";
            sb.AppendLine($@"Dialogue: {layer},{Time(s)},{Time(e)},Card,,0,0,0,,{{\an5\pos({x},{y})\frz4{fs}\fscx60\fscy60\t(0,120,\fscx115\fscy115)\t(120,220,\fscx100\fscy100)}}{Escape(card.Text)}");
        }
    }

    static string WriteStandard(EditPlan plan, Preset p)
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
        // scene-change card (tsukkomi): big yellow text, thick black outline, drop shadow; positioned explicitly
        sb.AppendLine($"Style: Card,{FontName},{CardFontPx},&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,{CardOutline},6,5,60,{c.MarginR},0,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        var ins = Planning.InsertMap.Active(plan);
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            var layer = t.Type is TelopType.Hook or TelopType.Headline ? 2 : t.Type is TelopType.Emphasis or TelopType.Counter or TelopType.Sfx or TelopType.Tsukkomi ? 1 : 0;
            var style = t.Style;
            if (t.Type == TelopType.Caption && p.Telop.SpeakerColors && t.Speaker is > 0) style = $"CapS{(t.Speaker.Value - 1) % 3 + 1}";
            var (size, outline, _) = Metrics(style, p);
            var lines = t.Type == TelopType.Caption ? CaptionLines(t.Text, p, plan.Canvas.Width) : [t.Text];
            var fit = lines.Min(l => FitFontSize(l, size, outline, plan.Canvas.Width, c.MarginR));
            var fs = fit < size ? $@"\fs{fit}" : "";
            var pos = style == "Sfx" ? $@"\an5\pos({plan.Canvas.Width / 2},{SfxY})\frz-8" : "";
            var fx = t.Animation == "pop" ? $@"{{{pos}\fad(60,0){fs}\fscx70\fscy70\t(0,110,\fscx112\fscy112)\t(110,220,\fscx100\fscy100)}}" : (fs.Length > 0 || pos.Length > 0) ? $"{{{pos}{fs}}}" : "";
            // body time → file time; a telop running across a card or bumper is split around it
            foreach (var (s, e) in ins.Count == 0 ? [(t.Start, t.End)] : Planning.InsertMap.Span(ins, t.Start, t.End))
                sb.AppendLine($"Dialogue: {layer},{Time(s)},{Time(e)},{style},,0,0,0,,{fx}{string.Join(@"\N", lines.Select(Escape))}");
        }
        foreach (var (card, s, e) in Planning.InsertMap.InsertSpans(ins).Where(x => x.Insert.Kind == "card" && x.Insert.Text.Length > 0))
        {
            var fit = CardFontSize(card, plan.Canvas, c);
            int x = card.Bgm != null ? plan.Canvas.Width / 2 : (MarginL + plan.Canvas.Width - c.MarginR) / 2;
            int y = (int)(plan.Canvas.Height * (card.Path != null && card.Bgm == null ? 0.62 : 0.42));
            var fs = fit < CardFontPx ? $@"\fs{fit}" : "";
            sb.AppendLine($@"Dialogue: 3,{Time(s)},{Time(e)},Card,,0,0,0,,{{\an5\pos({x},{y})\frz4{fs}\fscx60\fscy60\t(0,120,\fscx115\fscy115)\t(120,220,\fscx100\fscy100)}}{Escape(card.Text)}");
        }
        return sb.ToString();
    }

    public const int CardFontPx = 120, CardOutline = 12;

    static int CardFontSize(Insert card, Canvas canvas, CaptionStyle c)
    {
        if (card.Bgm == null) return FitFontSize(card.Text, CardFontPx, CardOutline, canvas.Width, c.MarginR);
        // the card image fills the screen, so the text may use the whole safe width
        double width = canvas.Width - 2 * Math.Max(MarginL, c.MarginR);
        return FitFontSize(card.Text, CardFontPx, CardOutline, (int)width + MarginL + c.MarginR, c.MarginR);
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

    /// <summary>SRT sidecar of the captions; with a preset, multi-line captions keep the same kinsoku-safe line breaks as the video.</summary>
    public static string Srt(EditPlan plan, Preset? p = null)
    {
        var sb = new StringBuilder(); int i = 0;
        var ins = Planning.InsertMap.Active(plan);
        foreach (var t in plan.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).OrderBy(t => t.Start))
            foreach (var (s, e) in ins.Count == 0 ? [(t.Start, t.End)] : Planning.InsertMap.Span(ins, t.Start, t.End))
            {
                sb.AppendLine((++i).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine($"{SrtTime(s)} --> {SrtTime(e)}");
                foreach (var line in p == null ? [t.Text] : CaptionLines(t.Text, p, plan.Canvas.Width)) sb.AppendLine(line);
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
