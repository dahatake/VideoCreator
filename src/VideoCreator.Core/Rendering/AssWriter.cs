using System.Globalization;
using System.Text;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

/// <summary>ASS (libass) writer — styles follow R§3.3 / C-4; text escaped (NFR-SEC-06).</summary>
public static partial class AssWriter
{
    public const string FontName = "M PLUS 1p ExtraBold";   // OFL, bundled in tools/fonts (Q-6)
    public static string FontFor(string language) => language switch
    {
        "zh-CN" => "Noto Sans CJK SC",
        "ko-KR" => "Noto Sans CJK KR",
        _ => FontName,
    };
    public const int HeadlineMarginV = 300, EmphLift = 115, MarginL = 60, CounterMarginV = 520, TsukkomiMarginV = 930, SfxY = 820;

    /// <summary>Vertical telop positions and the C-4 safe zone on one canvas (all canvas px; margins from the bottom except Headline/Counter from the top).</summary>
    public sealed record Geometry(int CaptionMarginV, int HeadlineMarginV, int CounterMarginV, int TsukkomiMarginV, int SfxY, (int Top, int Bottom, int Left, int Right) Safe, int MarginL, int MarginR);

    /// <summary>
    /// Portrait canvases keep the 1080×1920 values tuned around phone-app UI (the preset's caption.margin_v, safe zone 250/580).
    /// Landscape canvases have no app UI over a 16:9 player: the same distances are kept from the 5 % (90 %) title-safe frame on every side instead,
    /// so a preset's extra caption margin (margin_v − 580) carries over and the layout scales with the canvas height.
    /// </summary>
    public static Geometry Geo(Preset p, int canvasWidth, int canvasHeight)
    {
        var portraitSafe = Qa.QaEngine.SafeZone;
        int mv = p.Telop.Caption.MarginV;
        if (canvasWidth <= canvasHeight) return new(mv, HeadlineMarginV, CounterMarginV, TsukkomiMarginV, SfxY, portraitSafe, MarginL, p.Telop.Caption.MarginR);
        double k = canvasHeight / 1080.0;
        int safe = (int)Math.Round(canvasHeight * 0.05), side = (int)Math.Round(canvasWidth * 0.05);   // 90 % title-safe (SMPTE ST 2046-1)
        int caption = safe + (int)Math.Round((mv - portraitSafe.Bottom) * k);
        int headline = safe + (int)Math.Round((HeadlineMarginV - portraitSafe.Top) * k);
        return new(caption, headline, headline + (int)Math.Round((CounterMarginV - HeadlineMarginV) * k),
            caption + (int)Math.Round(310 * k), (int)Math.Round(canvasHeight * 0.4), (safe, safe, side, side), side, side);
    }

    public static Geometry Geo(Preset p, Canvas c) => Geo(p, c.Width, c.Height);
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
    public static List<string> CaptionLines(string text, Preset p, int canvasWidth = 1080, string language = "ja-JP") =>
        Planning.CaptionText.WrapLines(text, CaptionLineChars(p, canvasWidth), p.Telop.Caption.MaxLines, language);

    /// <summary>Font size that keeps the telop inside the horizontal safe zone (C-4): shrink instead of overflowing. <paramref name="scaleX"/> is the style's ScaleX (%).</summary>
    public static int FitFontSize(string text, int fontPx, int outline, int canvasWidth, int marginR, int scaleX = 100, string language = "ja-JP", int marginL = MarginL)
    {
        double available = canvasWidth - marginL - marginR - 2 * outline;
        double width = Planning.JapaneseText.DisplayWidth(text);
        if (language != "ja-JP")
            foreach (char ch in text)
                if (ch is >= '!' and <= '~')
                    width += "MWmw@%#&".Contains(ch) ? 0.65 : 0.15;
        double est = width * fontPx * GlyphRatio * scaleX / 100.0;
        return est <= available ? fontPx : Math.Max(24, (int)Math.Floor(fontPx * available / est));
    }

    /// <summary>
    /// Variety look (preset telop.look = "variety"), from measurements of Japanese variety videos (docs/IMPLEMENTATION.md §12):
    /// captions ≈ 10 % of the width per character, condensed (長体) with a dark halo so white text reads on a white set;
    /// punch telops (emphasis / tsukkomi) ≈ 15–20 % of the width per character and only one at a time; emphasis words red inside captions.
    /// Punch telops are layered (§23, measured on アメトーーク!: fill → white inner stroke → black outer stroke → ≈ 16 px soft shadow). Windows OCR at
    /// Shorts-grid size (270 px wide, 5 phrases × 3 backgrounds) read the white inner stroke worse than before (22 % vs 46 %: the bright ring makes
    /// letters look hollow), and the soft shadow better (69 %), so the emphasis keeps yellow + black and gains the soft shadow as a blurred dark
    /// glow on an underlay event; the tsukkomi box gets a white frame and the SFX a black outer stroke the same way.
    /// </summary>
    public static class Variety
    {
        public const int CaptionScaleX = 85, CaptionOutline = 10, HaloOutline = 24, HaloBlur = 10;
        public const int EmphPx = 210, EmphOutline = 14, TsukkomiPx = 180, TsukkomiPad = 20, SfxPx = 160, HeadlinePx = 56, HeadlinePad = 14;
        /// <summary>Emphasis glow (underlay outline, blurred), white inner strokes inside the black outer stroke of the SFX (SfxOutline is the whole stroke),
        /// the red tsukkomi box inside its white frame (TsukkomiPad).</summary>
        public const int EmphGlow = 24, EmphGlowBlur = 6, SfxOutline = 16, SfxInner = 9, TsukkomiInnerPad = 14, UnderBlur = 2;
        /// <summary>The punch band sits this far above the caption baseline band (caption ≤ 128 px · 1.3 line box + outlines).</summary>
        public const int PunchLift = 200;
        /// <summary>&amp;HBBGGRR: red for emphasis words inside captions; red tsukkomi box; light-cyan headline bar with navy text.</summary>
        public const string KeywordRed = "&H2A2AE8&", BoxRed = "&H001C1CE6", BarCyan = "&H00F5E6A0", BarNavy = "&H00803010";
        public const int LayerHalo = 0, LayerCaption = 1, LayerPunchUnder = 2, LayerPunch = 3, LayerTop = 4, LayerCard = 5;
    }

    /// <summary>
    /// Study look (telop.look = "study"), measured on a QuizKnock quiz video (docs/IMPLEMENTATION.md §23): the speaker's colour carries the caption
    /// (here as the stroke: a coloured fill inside a cream stroke, as in the reference, was read 0–10 % by Windows OCR at Shorts-grid size; a white fill 94 %),
    /// with a thin dark edge (underlay event); the headline is a question box — white box, red frame, near-black text.
    /// The stroke totals and the headline padding equal the standard look's, so every band and the P10 screen layout stay where they were.
    /// </summary>
    public static class Study
    {
        public const int CaptionStroke = 5, CaptionEdge = 7, HeadlinePad = 10, HeadlineFramePad = 14;
        /// <summary>&amp;HAABBGGRR. Strokes: green #007040, purple #9A0C9D, blue #0050B4 — each ≥ 6:1 against the white fill (WCAG 2.x; ≥ 5:1 as libass renders them).</summary>
        public const string White = "&H00FFFFFF", Edge = "&H00202020", Paper = "&H00FFFFFF", FrameRed = "&H001E1ED8", Ink = "&H00202020";
        public static readonly string[] Strokes = ["&H00407000", "&H009D0C9A", "&H00B45000"];
    }

    /// <summary>
    /// Corporate look (telop.look = "corporate"), measured on the Microsoft Ignite 2026 keynote (docs/IMPLEMENTATION.md §23): no heavy strokes —
    /// white text on a dark box with a thin frame (the frame takes the speaker's colour), the keyword in a cream pill with dark text,
    /// and short fades (Fluent motion "normal" 200 ms) instead of a bounce.
    /// </summary>
    public static class Corporate
    {
        public const int CaptionPad = 10, FramePad = 14, EmphPad = 14, HeadlinePad = 14, HeadlineFramePad = 16, FadeInMs = 200, FadeOutMs = 120;
        /// <summary>The keyword pill sits this far above the caption baseline (caption box 1.3 line + frame, pill padding, 8 px gap).</summary>
        public const int EmphLift = 140;
        /// <summary>&amp;HAABBGGRR: opaque box #1B1B1B (a translucent box would show the frame underlay through it), frame #A0A0A0 at 69 %, pill #FFFFE0 (measured), ink #1A1A1A;
        /// speaker frames = Microsoft logo blue, orange-red, green.</summary>
        public const string Box = "&H001B1B1B", Frame = "&H50A0A0A0", Pill = "&H00E0FFFF", Ink = "&H001A1A1A";
        public static readonly string[] SpeakerFrames = ["&H00EFA400", "&H002250F2", "&H0000BA7F"];
    }

    /// <summary>(font size, outline or box padding, ScaleX) of a style — the one table used for fitting (here) and for the QA bands.</summary>
    public static (int Size, int Outline, int ScaleX) Metrics(string style, Preset p)
    {
        var c = p.Telop.Caption;
        if (p.Telop.IsShort) return ShortMetrics(style, p);
        if (p.Telop.IsVariety)
            return style switch
            {
                "Emph" => (Variety.EmphPx, Variety.EmphOutline, 100), "Hook" => (84, 18, 100), "Headline" => (Variety.HeadlinePx, Variety.HeadlinePad, 100),
                "Counter" => (96, 10, 100), "Sfx" => (Variety.SfxPx, Variety.SfxOutline, 100), "Tsukkomi" => (Variety.TsukkomiPx, Variety.TsukkomiPad, 100),
                _ => (c.FontPx, Variety.CaptionOutline, Variety.CaptionScaleX),
            };
        if (p.Telop.IsCorporate)
            return style switch
            {
                "Emph" => ((int)(c.FontPx * 1.25), Corporate.EmphPad, 100), "Hook" => (84, 18, 100), "Headline" => (60, Corporate.HeadlineFramePad, 100),
                "Counter" => (96, 10, 100), "Sfx" => (120, 10, 100), "Tsukkomi" => (64, 12, 100), _ => (c.FontPx, Corporate.FramePad, 100),
            };
        if (p.Telop.IsStudy && (style == "Caption" || style.StartsWith("CapS"))) return (c.FontPx, Study.CaptionEdge, 100);
        return style switch
        {
            "Emph" => ((int)(c.FontPx * 1.25), 9, 100), "Hook" => (84, 18, 100), "Headline" => (60, 14, 100), "Counter" => (96, 10, 100), "Sfx" => (120, 10, 100), "Tsukkomi" => (64, 12, 100),
            _ when style.StartsWith("CapS") => (c.FontPx, 9, 100), _ => (c.FontPx, 7, 100),
        };
    }

    /// <summary>Vertical band [top, bottom] (canvas px) a body telop can occupy; null for telops positioned elsewhere (top bar, SFX). Used by QA S-05.</summary>
    public static (double Top, double Bottom)? Band(TelopType type, Preset p, int canvasHeight) => Band(type, p, 0, canvasHeight);

    public static (double Top, double Bottom)? Band(TelopType type, Preset p, Canvas canvas) => Band(type, p, canvas.Width, canvas.Height);

    static (double Top, double Bottom)? Band(TelopType type, Preset p, int canvasWidth, int canvasHeight)
    {
        var c = p.Telop.Caption; int H = canvasHeight;
        var g = Geo(p, canvasWidth, canvasHeight); int mv = g.CaptionMarginV;
        if (p.Telop.IsShort) return ShortBand(type, p, g, H);
        if (p.Telop.IsCorporate)
        {
            // boxes (BorderStyle 3) are visible, so the band includes the padding and frame; the keyword pill sits above the caption box
            int fp = Corporate.FramePad, ep = Corporate.EmphPad; double ePx = (int)(c.FontPx * 1.25);
            return type switch
            {
                TelopType.Caption => (H - mv - c.FontPx * 1.3 * c.MaxLines - fp, H - mv + fp),
                TelopType.Emphasis => (H - mv - Corporate.EmphLift - ePx * 1.3 - ep, H - mv - Corporate.EmphLift + ep),
                TelopType.Tsukkomi => (H - g.TsukkomiMarginV - 90, H - g.TsukkomiMarginV),
                TelopType.Counter => (g.CounterMarginV, g.CounterMarginV + 120),
                _ => null,
            };
        }
        if (!p.Telop.IsVariety)
            return type switch
            {
                TelopType.Caption => (H - mv - c.FontPx * 1.3 * c.MaxLines, H - mv),
                TelopType.Emphasis => (H - mv - EmphLift - c.FontPx * 1.6, H - mv - EmphLift),
                TelopType.Tsukkomi => (H - g.TsukkomiMarginV - 90, H - g.TsukkomiMarginV),
                TelopType.Counter => (g.CounterMarginV, g.CounterMarginV + 120),
                _ => null,
            };
        double punchBottom = H - mv - Variety.PunchLift;
        return type switch
        {
            TelopType.Caption => (H - mv - c.FontPx * 1.3 * c.MaxLines - Variety.CaptionOutline, H - mv + Variety.CaptionOutline),
            TelopType.Emphasis => (punchBottom - Variety.EmphPx * 1.3 - Variety.EmphOutline, punchBottom + Variety.EmphOutline),
            TelopType.Tsukkomi => (punchBottom - Variety.TsukkomiPx * 1.3 - Variety.TsukkomiPad, punchBottom + Variety.TsukkomiPad),
            TelopType.Counter => (g.CounterMarginV, g.CounterMarginV + 120),
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

    public static string Write(EditPlan plan, Preset p, IReadOnlyDictionary<string, TelopLayout.Placement>? layout = null)
    {
        layout ??= TelopLayout.Resolve(plan, p);
        var ass = p.Telop.IsShort ? WriteShort(plan, p, layout) : p.Telop.IsVariety ? WriteVariety(plan, p, layout) : p.Telop.IsStudy || p.Telop.IsCorporate ? WriteLayered(plan, p, layout) : WriteStandard(plan, p, layout);
        var font = FontFor(plan.Language);
        return string.Join("\n", ass.Split('\n').Select(line => line.StartsWith("Style:", StringComparison.Ordinal)
            ? line.Replace("," + FontName + ",", "," + font + ",", StringComparison.Ordinal) : line));
    }

    static string WriteVariety(EditPlan plan, Preset p, IReadOnlyDictionary<string, TelopLayout.Placement> layout)
    {
        var c = p.Telop.Caption; int sx = Variety.CaptionScaleX; var g = Geo(p, plan.Canvas);
        var sb = new StringBuilder();
        Header(sb, plan);
        // captions: white, condensed, black outline + a separate halo event (transparent fill, wide blurred dark border) underneath
        sb.AppendLine($"Style: Caption,{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,&H00000000,&H00000000,0,0,0,0,{sx},100,0,0,1,{Variety.CaptionOutline},0,2,{g.MarginL},{g.MarginR},{g.CaptionMarginV},1");
        sb.AppendLine($"Style: Halo,{FontName},{c.FontPx},&HFF000000,&H0000FFFF,&H30000000,&H00000000,0,0,0,0,{sx},100,0,0,1,{Variety.HaloOutline},0,2,{g.MarginL},{g.MarginR},{g.CaptionMarginV},1");
        string[] outlines = ["&H00D47800", "&H003C3CD4", "&H0050A030"];
        for (int i = 0; i < outlines.Length; i++)
            sb.AppendLine($"Style: CapS{i + 1},{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,{outlines[i]},&H00000000,0,0,0,0,{sx},100,0,0,1,{Variety.CaptionOutline},0,2,{g.MarginL},{g.MarginR},{g.CaptionMarginV},1");
        int punchV = g.CaptionMarginV + Variety.PunchLift;
        // punch telops: main event = the telop; underlay event on its own layer (else libass would push it aside) = glow / black outer stroke / white frame
        sb.AppendLine($"Style: Emph,{FontName},{Variety.EmphPx},&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,{Variety.EmphOutline},4,2,{g.MarginL},{g.MarginR},{punchV},1");
        sb.AppendLine($"Style: EmphUnder,{FontName},{Variety.EmphPx},&HFF000000,&H0000FFFF,&H60000000,&H00000000,0,0,0,0,100,100,0,0,1,{Variety.EmphGlow},0,2,{g.MarginL},{g.MarginR},{punchV},1");
        sb.AppendLine($"Style: Tsukkomi,{FontName},{Variety.TsukkomiPx},&H00FFFFFF,&H0000FFFF,{Variety.BoxRed},{Variety.BoxRed},0,0,0,0,100,100,0,0,3,{Variety.TsukkomiInnerPad},0,2,{g.MarginL},{g.MarginR},{punchV},1");
        sb.AppendLine($"Style: TsukkomiUnder,{FontName},{Variety.TsukkomiPx},&HFF000000,&H0000FFFF,&H00FFFFFF,&H00FFFFFF,0,0,0,0,100,100,0,0,3,{Variety.TsukkomiPad},0,2,{g.MarginL},{g.MarginR},{punchV},1");
        sb.AppendLine($"Style: Hook,{FontName},84,&H00FFFFFF,&H0000FFFF,&H00D47800,&H00D47800,0,0,0,0,100,100,0,0,3,18,0,8,{g.MarginL},{g.MarginR},{g.HeadlineMarginV},1");
        sb.AppendLine($"Style: Headline,{FontName},{Variety.HeadlinePx},{Variety.BarNavy},&H0000FFFF,{Variety.BarCyan},{Variety.BarCyan},0,0,0,0,100,100,0,0,3,{Variety.HeadlinePad},0,8,{g.MarginL},{g.MarginR},{g.HeadlineMarginV},1");
        sb.AppendLine($"Style: Counter,{FontName},96,&H0000E0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,10,3,8,{g.MarginL},{g.MarginR},{g.CounterMarginV},1");
        sb.AppendLine($"Style: Sfx,{FontName},{Variety.SfxPx},&H002020E0,&H0000FFFF,&H00FFFFFF,&H00000000,0,0,0,0,100,100,0,0,1,{Variety.SfxInner},0,5,{g.MarginL},{g.MarginR},0,1");
        sb.AppendLine($"Style: SfxUnder,{FontName},{Variety.SfxPx},&HFF000000,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,{Variety.SfxOutline},3,5,{g.MarginL},{g.MarginR},0,1");
        CardStyle(sb, g);
        EventsHeader(sb);
        var ins = Planning.InsertMap.Active(plan);
        var keywords = plan.Telops.Where(t => t.Enabled && t.Type == TelopType.Emphasis).Select(t => t.Text.Trim()).Distinct().ToList();
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            var placement = layout[t.Id];
            if (placement.Bounds == null) continue;
            bool caption = t.Type == TelopType.Caption;
            var layer = t.Type is TelopType.Hook or TelopType.Headline ? Variety.LayerTop : caption ? Variety.LayerCaption : Variety.LayerPunch;
            var style = t.Style;
            if (caption && p.Telop.SpeakerColors && t.Speaker is > 0) style = $"CapS{(t.Speaker.Value - 1) % 3 + 1}";
            var (size, outline, scaleX) = Metrics(style, p);
            var lines = caption ? CaptionLines(t.Text, p, plan.Canvas.Width, plan.Language) : [t.Text];
            var fit = lines.Min(l => FitFontSize(l, size, outline, plan.Canvas.Width, g.MarginR, scaleX, plan.Language, g.MarginL));
            if (placement.Positioned && t.Type != TelopType.Caption) fit = placement.FontSize;   // a moved caption keeps its per-line size
            var fx = Fx(t with { Animation = null }, plan, fit < size ? $@"\fs{fit}" : "", placement, g.SfxY);
            var text = caption ? string.Join(@"\N", lines.Select(l => Colorize(l, keywords))) : Escape(t.Text);
            string? under = style switch { "Emph" => "EmphUnder", "Sfx" => "SfxUnder", "Tsukkomi" => "TsukkomiUnder", _ => null };
            foreach (var (s, e) in ins.Count == 0 ? [(t.Start, t.End)] : Planning.InsertMap.Span(ins, t.Start, t.End))
            {
                if (caption) sb.AppendLine($@"Dialogue: {Variety.LayerHalo},{Time(s)},{Time(e)},Halo,,0,0,0,,{{\blur{Variety.HaloBlur}}}{fx}{string.Join(@"\N", lines.Select(Escape))}");
                if (under != null)
                    sb.AppendLine($@"Dialogue: {Variety.LayerPunchUnder},{Time(s)},{Time(e)},{under},,0,0,0,,{under switch { "EmphUnder" => $@"{{\blur{Variety.EmphGlowBlur}}}", "SfxUnder" => $@"{{\blur{Variety.UnderBlur}}}", _ => "" }}{fx}{text}");
                sb.AppendLine($"Dialogue: {layer},{Time(s)},{Time(e)},{style},,0,0,0,,{fx}{text}");
            }
        }
        CardEvents(sb, plan, g, Variety.LayerCard);
        return sb.ToString();
    }

    /// <summary>
    /// Study and corporate looks (§23): the standard layout and sizes, restyled; strokes and frames that need two colours are an underlay event
    /// (transparent text, wider outline or box) on its own layer directly below the main event.
    /// </summary>
    static string WriteLayered(EditPlan plan, Preset p, IReadOnlyDictionary<string, TelopLayout.Placement> layout)
    {
        bool corp = p.Telop.IsCorporate;
        var c = p.Telop.Caption; var g = Geo(p, plan.Canvas);
        var sb = new StringBuilder();
        Header(sb, plan);
        void S(string name, int size, string primary, string outlineColour, string back, int border, int outline, int shadow, int align, int marginV) =>
            sb.AppendLine($"Style: {name},{FontName},{size},{primary},&H0000FFFF,{outlineColour},{back},0,0,0,0,100,100,0,0,{border},{outline},{shadow},{align},{g.MarginL},{g.MarginR},{marginV},1");
        int emphPx = (int)(c.FontPx * 1.25);
        if (corp)
        {
            S("Caption", c.FontPx, "&H00FFFFFF", Corporate.Box, Corporate.Box, 3, Corporate.CaptionPad, 0, 2, g.CaptionMarginV);
            S("CapFrame", c.FontPx, "&HFF000000", Corporate.Frame, Corporate.Frame, 3, Corporate.FramePad, 0, 2, g.CaptionMarginV);
            for (int i = 0; i < Corporate.SpeakerFrames.Length; i++)
            {
                S($"CapS{i + 1}", c.FontPx, "&H00FFFFFF", Corporate.Box, Corporate.Box, 3, Corporate.CaptionPad, 0, 2, g.CaptionMarginV);
                S($"CapFrameS{i + 1}", c.FontPx, "&HFF000000", Corporate.SpeakerFrames[i], Corporate.SpeakerFrames[i], 3, Corporate.FramePad, 0, 2, g.CaptionMarginV);
            }
            S("Emph", emphPx, Corporate.Ink, Corporate.Pill, Corporate.Pill, 3, Corporate.EmphPad, 0, 2, g.CaptionMarginV + Corporate.EmphLift);
            S("Hook", 84, Corporate.Ink, Corporate.Pill, Corporate.Pill, 3, 18, 0, 8, g.HeadlineMarginV);
            S("Headline", 60, "&H00FFFFFF", Corporate.Box, Corporate.Box, 3, Corporate.HeadlinePad, 0, 8, g.HeadlineMarginV);
            S("HeadlineFrame", 60, "&HFF000000", Corporate.Frame, Corporate.Frame, 3, Corporate.HeadlineFramePad, 0, 8, g.HeadlineMarginV);
        }
        else
        {
            S("Caption", c.FontPx, Study.White, Study.Strokes[0], "&H00000000", 1, Study.CaptionStroke, 0, 2, g.CaptionMarginV);
            S("CapEdge", c.FontPx, "&HFF000000", Study.Edge, "&H80000000", 1, Study.CaptionEdge, 2, 2, g.CaptionMarginV);
            for (int i = 0; i < Study.Strokes.Length; i++) S($"CapS{i + 1}", c.FontPx, Study.White, Study.Strokes[i], "&H00000000", 1, Study.CaptionStroke, 0, 2, g.CaptionMarginV);
            S("Emph", emphPx, "&H0000F0FF", "&H00000000", "&H80000000", 1, 9, 3, 2, g.CaptionMarginV + EmphLift);
            S("Hook", 84, "&H00FFFFFF", "&H00D47800", "&H00D47800", 3, 18, 0, 8, g.HeadlineMarginV);
            S("Headline", 60, Study.Ink, Study.Paper, Study.Paper, 3, Study.HeadlinePad, 0, 8, g.HeadlineMarginV);
            S("HeadlineFrame", 60, "&HFF000000", Study.FrameRed, Study.FrameRed, 3, Study.HeadlineFramePad, 0, 8, g.HeadlineMarginV);
        }
        S("Counter", 96, "&H0000E0FF", "&H00000000", "&H80000000", 1, 10, 3, 8, g.CounterMarginV);
        S("Sfx", 120, "&H002020E0", "&H00FFFFFF", "&H80000000", 1, 10, 3, 5, 0);
        S("Tsukkomi", 64, "&H00202020", "&H00FFFFFF", "&H00FFFFFF", 3, 12, 0, 2, g.TsukkomiMarginV);
        CardStyle(sb, g);
        EventsHeader(sb);
        const int CaptionUnder = 0, CaptionLayer = 1, PunchLayer = 2, TopUnder = 3, TopLayer = 4, CardLayer = 5;
        var ins = Planning.InsertMap.Active(plan);
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            var placement = layout[t.Id];
            if (placement.Bounds == null) continue;
            bool caption = t.Type == TelopType.Caption, top = t.Type is TelopType.Hook or TelopType.Headline;
            var style = t.Style;
            if (caption && p.Telop.SpeakerColors && t.Speaker is > 0) style = $"CapS{(t.Speaker.Value - 1) % 3 + 1}";
            var (size, outline, _) = Metrics(style, p);
            var lines = caption ? CaptionLines(t.Text, p, plan.Canvas.Width, plan.Language) : [t.Text];
            var fit = lines.Min(l => FitFontSize(l, size, outline, plan.Canvas.Width, g.MarginR, language: plan.Language, marginL: g.MarginL));
            if (placement.Positioned && t.Type != TelopType.Caption) fit = placement.FontSize;   // a moved caption keeps its per-line size
            var fx = Fx(t with { Animation = null }, plan, fit < size ? $@"\fs{fit}" : "", placement, g.SfxY);
            if (corp && t.Animation == "pop")
                fx = fx.Length > 0 ? fx.Insert(1, $@"\fad({Corporate.FadeInMs},{Corporate.FadeOutMs})") : $@"{{\fad({Corporate.FadeInMs},{Corporate.FadeOutMs})}}";
            string? under = style switch
            {
                "Caption" => corp ? "CapFrame" : "CapEdge",
                _ when style.StartsWith("CapS", StringComparison.Ordinal) => corp ? "CapFrame" + style[3..] : "CapEdge",
                "Headline" => "HeadlineFrame",
                _ => null,
            };
            int layer = caption ? CaptionLayer : top ? TopLayer : PunchLayer;
            var text = string.Join(@"\N", lines.Select(Escape));
            foreach (var (phaseStart, phaseEnd, phaseText) in StudyStages(t, p, text))
            foreach (var (s, e) in ins.Count == 0 ? [(phaseStart, phaseEnd)] : Planning.InsertMap.Span(ins, phaseStart, phaseEnd))
            {
                if (under != null) sb.AppendLine($"Dialogue: {(top ? TopUnder : CaptionUnder)},{Time(s)},{Time(e)},{under},,0,0,0,,{fx}{phaseText}");
                sb.AppendLine($"Dialogue: {layer},{Time(s)},{Time(e)},{style},,0,0,0,,{fx}{phaseText}");
            }
        }
        CardEvents(sb, plan, g, CardLayer);
        return sb.ToString();
    }

    const double StudyRevealDelay = 0.8;

    static List<(double Start, double End, string Text)> StudyStages(Telop telop, Preset preset, string text)
    {
        int split = preset.Telop.IsStudy && telop.Type == TelopType.Emphasis && telop.Animation == "two-stage"
            ? StudyRevealBoundary(telop.Text) : -1;
        if (split < 0 || telop.End - telop.Start < 2 * StudyRevealDelay)
            return [(telop.Start, telop.End, text)];

        // Keep the full phrase's width during stage one so the visible prefix remains in its final position.
        return [
            (telop.Start, telop.Start + StudyRevealDelay,
                Escape(telop.Text[..split]) + @"{\alpha&HFF&}" + Escape(telop.Text[split..])),
            (telop.Start + StudyRevealDelay, telop.End, Escape(telop.Text)),
        ];
    }

    static int StudyRevealBoundary(string text)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (boundaries.Length < 2) return -1;

        double width = Planning.JapaneseText.DisplayWidth(text), target = width / 2;
        int split = -1; double distance = double.MaxValue;
        for (int i = 1; i < boundaries.Length; i++)
        {
            double left = Planning.JapaneseText.DisplayWidth(text[..boundaries[i]]);
            if (left < 2 || width - left < 2) continue;
            double candidate = Math.Abs(left - target);
            if (candidate < distance) { split = boundaries[i]; distance = candidate; }
        }
        return split;
    }

    static string Fx(Telop t, EditPlan plan, string fs, TelopLayout.Placement placement, int sfxY)
    {
        var pos = placement.Positioned && placement.Bounds is { } b
            ? FormattableString.Invariant($@"\an5\pos({(b.Left + b.Right) / 2:0.###},{(b.Top + b.Bottom) / 2:0.###})")
            : t.Style == "Sfx" ? $@"\an5\pos({plan.Canvas.Width / 2},{sfxY})\frz-8" : "";
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

    static void CardStyle(StringBuilder sb, Geometry g) =>
        // scene-change card (tsukkomi): big yellow text, thick black outline, drop shadow; positioned explicitly
        sb.AppendLine($"Style: Card,{FontName},{CardFontPx},&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,{CardOutline},6,5,{g.MarginL},{g.MarginR},0,1");

    static void EventsHeader(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
    }

    static void CardEvents(StringBuilder sb, EditPlan plan, Geometry g, int layer)
    {
        foreach (var (card, s, e) in Planning.InsertMap.InsertSpans(Planning.InsertMap.Active(plan)).Where(x => x.Insert.Kind == "card" && x.Insert.Text.Length > 0))
        {
            var fit = CardFontSize(card, plan.Canvas, g, plan.Language);
            int x = card.Bgm != null ? plan.Canvas.Width / 2 : (g.MarginL + plan.Canvas.Width - g.MarginR) / 2;
            int y = (int)(plan.Canvas.Height * (card.Path != null && card.Bgm == null ? 0.62 : 0.42));
            var fs = fit < CardFontPx ? $@"\fs{fit}" : "";
            sb.AppendLine($@"Dialogue: {layer},{Time(s)},{Time(e)},Card,,0,0,0,,{{\an5\pos({x},{y})\frz4{fs}\fscx60\fscy60\t(0,120,\fscx115\fscy115)\t(120,220,\fscx100\fscy100)}}{Escape(card.Text)}");
        }
    }

    static string WriteStandard(EditPlan plan, Preset p, IReadOnlyDictionary<string, TelopLayout.Placement> layout)
    {
        var c = p.Telop.Caption; var g = Geo(p, plan.Canvas);
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
        sb.AppendLine($"Style: Caption,{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,7,2,2,{g.MarginL},{g.MarginR},{g.CaptionMarginV},1");
        sb.AppendLine($"Style: Emph,{FontName},{(int)(c.FontPx * 1.25)},&H0000F0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,9,3,2,{g.MarginL},{g.MarginR},{g.CaptionMarginV + EmphLift},1");
        sb.AppendLine($"Style: Hook,{FontName},84,&H00FFFFFF,&H0000FFFF,&H00D47800,&H00D47800,0,0,0,0,100,100,0,0,3,18,0,8,{g.MarginL},{g.MarginR},{g.HeadlineMarginV},1");
        sb.AppendLine($"Style: Headline,{FontName},60,&H00FFFFFF,&H0000FFFF,&H00202020,&HB0202020,0,0,0,0,100,100,0,0,3,14,0,8,{g.MarginL},{g.MarginR},{g.HeadlineMarginV},1");
        sb.AppendLine($"Style: Counter,{FontName},96,&H0000E0FF,&H0000FFFF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,10,3,8,{g.MarginL},{g.MarginR},{g.CounterMarginV},1");
        sb.AppendLine($"Style: Sfx,{FontName},120,&H002020E0,&H0000FFFF,&H00FFFFFF,&H80000000,0,0,0,0,100,100,0,0,1,10,3,5,{g.MarginL},{g.MarginR},0,1");
        sb.AppendLine($"Style: Tsukkomi,{FontName},64,&H00202020,&H0000FFFF,&H00FFFFFF,&H00FFFFFF,0,0,0,0,100,100,0,0,3,12,0,2,{g.MarginL},{g.MarginR},{g.TsukkomiMarginV},1");
        // per-speaker caption colour: white text, speaker-coloured outline (FR-TEL-10)
        string[] outlines = ["&H00D47800", "&H003C3CD4", "&H0050A030"];
        for (int i = 0; i < outlines.Length; i++)
            sb.AppendLine($"Style: CapS{i + 1},{FontName},{c.FontPx},&H00FFFFFF,&H0000FFFF,{outlines[i]},&H80000000,0,0,0,0,100,100,0,0,1,9,2,2,{g.MarginL},{g.MarginR},{g.CaptionMarginV},1");
        // scene-change card (tsukkomi): big yellow text, thick black outline, drop shadow; positioned explicitly
        sb.AppendLine($"Style: Card,{FontName},{CardFontPx},&H0000E6FF,&H0000FFFF,&H00000000,&H90000000,0,0,0,0,100,100,0,0,1,{CardOutline},6,5,{g.MarginL},{g.MarginR},0,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        var ins = Planning.InsertMap.Active(plan);
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            var placement = layout[t.Id];
            if (placement.Bounds == null) continue;
            var layer = t.Type is TelopType.Hook or TelopType.Headline ? 2 : t.Type is TelopType.Emphasis or TelopType.Counter or TelopType.Sfx or TelopType.Tsukkomi ? 1 : 0;
            var style = t.Style;
            if (t.Type == TelopType.Caption && p.Telop.SpeakerColors && t.Speaker is > 0) style = $"CapS{(t.Speaker.Value - 1) % 3 + 1}";
            var (size, outline, _) = Metrics(style, p);
            var lines = t.Type == TelopType.Caption ? CaptionLines(t.Text, p, plan.Canvas.Width, plan.Language) : [t.Text];
            var fit = lines.Min(l => FitFontSize(l, size, outline, plan.Canvas.Width, g.MarginR, language: plan.Language, marginL: g.MarginL));
            if (placement.Positioned && t.Type != TelopType.Caption) fit = placement.FontSize;   // a moved caption keeps its per-line size
            var fx = Fx(t, plan, fit < size ? $@"\fs{fit}" : "", placement, g.SfxY);
            // body time → file time; a telop running across a card or bumper is split around it
            foreach (var (s, e) in ins.Count == 0 ? [(t.Start, t.End)] : Planning.InsertMap.Span(ins, t.Start, t.End))
                sb.AppendLine($"Dialogue: {layer},{Time(s)},{Time(e)},{style},,0,0,0,,{fx}{string.Join(@"\N", lines.Select(Escape))}");
        }
        foreach (var (card, s, e) in Planning.InsertMap.InsertSpans(ins).Where(x => x.Insert.Kind == "card" && x.Insert.Text.Length > 0))
        {
            var fit = CardFontSize(card, plan.Canvas, g, plan.Language);
            int x = card.Bgm != null ? plan.Canvas.Width / 2 : (g.MarginL + plan.Canvas.Width - g.MarginR) / 2;
            int y = (int)(plan.Canvas.Height * (card.Path != null && card.Bgm == null ? 0.62 : 0.42));
            var fs = fit < CardFontPx ? $@"\fs{fit}" : "";
            sb.AppendLine($@"Dialogue: 3,{Time(s)},{Time(e)},Card,,0,0,0,,{{\an5\pos({x},{y})\frz4{fs}\fscx60\fscy60\t(0,120,\fscx115\fscy115)\t(120,220,\fscx100\fscy100)}}{Escape(card.Text)}");
        }
        return sb.ToString();
    }

    public const int CardFontPx = 120, CardOutline = 12;

    static int CardFontSize(Insert card, Canvas canvas, Geometry g, string language)
    {
        if (card.Bgm == null) return FitFontSize(card.Text, CardFontPx, CardOutline, canvas.Width, g.MarginR, language: language, marginL: g.MarginL);
        // the card image fills the screen, so the text may use the whole safe width
        double width = canvas.Width - 2 * Math.Max(g.MarginL, g.MarginR);
        return FitFontSize(card.Text, CardFontPx, CardOutline, (int)width + g.MarginL + g.MarginR, g.MarginR, language: language, marginL: g.MarginL);
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
                foreach (var line in p == null ? [t.Text] : CaptionLines(t.Text, p, plan.Canvas.Width, plan.Language)) sb.AppendLine(line);
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
