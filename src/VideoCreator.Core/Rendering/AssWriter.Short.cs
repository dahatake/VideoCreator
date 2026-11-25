using System.Text;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

public static partial class AssWriter
{
    /// <summary>
    /// Shorts look (telop.look = "short", P12), from 20 vertical shorts measured with spikes/25-shorts-style.cs (docs/IMPLEMENTATION.md §24):
    /// a title bar shown from the first frame to the last, a big title (the hook) over the opening seconds, captions ≈ 1.5× the standard size
    /// with one fill colour per speaker, and one big punch telop at a time. Sizes are OCR glyph heights of the references ÷ 0.62 (calibrated on our own render).
    /// Captions: light fill + black stroke, a wider black edge on an underlay event and a blurred halo; no red keywords inside them
    /// (red keywords and dark fills inside white strokes both read worse to OCR at feed size — §24).
    /// </summary>
    public static class Short
    {
        /// <summary>CaptionOutline is the whole edge (the underlay) used for fitting and bands; CaptionStroke is the main event's own stroke.</summary>
        public const int CaptionScaleX = 90, CaptionStroke = 6, CaptionOutline = 12, HaloOutline = 24, HaloBlur = 10;
        /// <summary>Big title (hook): fill + white inner stroke, black outer stroke + shadow on an underlay event.</summary>
        public const int TitlePx = 180, TitleInner = 7, TitleOutline = 16;
        public const int EmphPx = 200, EmphInner = 6, EmphOutline = 14, TsukkomiPx = 170, TsukkomiInnerPad = 14, TsukkomiPad = 20, SfxPx = 160, SfxInner = 9, SfxOutline = 16;
        /// <summary>Title bar: text on a red box (pad) inside a white frame (frame pad = the whole padding used for fitting and bands).</summary>
        public const int HeadlinePx = 150, HeadlinePad = 14, HeadlineFramePad = 22;
        /// <summary>The punch band sits this far above the caption baseline band (caption ≤ 128 px · 1.3 + outlines); the title sits this far below the bar's top.</summary>
        public const int PunchLift = 200, TitleDrop = 230, UnderBlur = 2;
        /// <summary>Title bar text top: the bar's frame starts just inside the top safe edge (editors put it at the very top; 4 px against rounding).</summary>
        public static int BarMarginV(Geometry g) => g.Safe.Top + HeadlineFramePad + 4;
        /// <summary>Canvas rows the drawn bar (frame included) covers at font size <paramref name="fit"/>: line box ≈ 1.3 × size.</summary>
        public static (double Top, double Bottom) BarRows(Geometry g, int fit) => (BarMarginV(g) - HeadlineFramePad, BarMarginV(g) + fit * 1.3 + HeadlineFramePad);
        /// <summary>&amp;HAABBGGRR. Speaker fills yellow #FFE14D, cyan #4DD8FF, light green #8CF26A (each ≥ 7:1 against the black stroke); emphasis red #E82A2A
        /// (5.3:1 against its white stroke); title yellow #FFE600; bar red #E8343A.</summary>
        public static readonly string[] SpeakerFills = ["&H004DE1FF", "&H00FFD84D", "&H006AF28C"];
        public const string White = "&H00FFFFFF", Black = "&H00000000", KeywordRed = "&H002A2AE8", TitleYellow = "&H0000E6FF", BarRed = "&H003A34E8";
        public const int LayerHalo = 0, LayerCaptionEdge = 1, LayerCaption = 2, LayerPunchUnder = 3, LayerPunch = 4, LayerTopUnder = 5, LayerTop = 6, LayerCard = 7;
    }

    static (int Size, int Outline, int ScaleX) ShortMetrics(string style, Preset p) => style switch
    {
        "Emph" => (Short.EmphPx, Short.EmphOutline, 100),
        "Hook" => (Short.TitlePx, Short.TitleOutline, 100),
        // +12: the frame box sits on the margins when the title fills the width, and digits/Latin are wider than the shared estimate (measured 5 px over)
        "Headline" => (Short.HeadlinePx, Short.HeadlineFramePad + 12, 100),
        "Counter" => (96, 10, 100),
        "Sfx" => (Short.SfxPx, Short.SfxOutline, 100),
        "Tsukkomi" => (Short.TsukkomiPx, Short.TsukkomiPad, 100),
        _ => (p.Telop.Caption.FontPx, Short.CaptionOutline, Short.CaptionScaleX),
    };

    static (double Top, double Bottom)? ShortBand(TelopType type, Preset p, Geometry g, int H)
    {
        var c = p.Telop.Caption; int mv = g.CaptionMarginV;
        double punchBottom = H - mv - Short.PunchLift, titleTop = g.HeadlineMarginV + Short.TitleDrop;
        return type switch
        {
            TelopType.Caption => (H - mv - c.FontPx * 1.3 * c.MaxLines - Short.CaptionOutline, H - mv + Short.CaptionOutline),
            TelopType.Emphasis => (punchBottom - Short.EmphPx * 1.3 - Short.EmphOutline, punchBottom + Short.EmphOutline),
            TelopType.Tsukkomi => (punchBottom - Short.TsukkomiPx * 1.3 - Short.TsukkomiPad, punchBottom + Short.TsukkomiPad),
            TelopType.Counter => (g.CounterMarginV, g.CounterMarginV + 120),
            TelopType.Hook => (titleTop, titleTop + Short.TitlePx * 1.3 + 2 * Short.TitleOutline),
            _ => null,
        };
    }

    static string WriteShort(EditPlan plan, Preset p, IReadOnlyDictionary<string, TelopLayout.Placement> layout)
    {
        var c = p.Telop.Caption; int sx = Short.CaptionScaleX; var g = Geo(p, plan.Canvas);
        var sb = new StringBuilder();
        Header(sb, plan);
        void S(string name, int size, string primary, string outline, string back, int scaleX, int border, int width, int shadow, int align, int marginV) =>
            sb.AppendLine($"Style: {name},{FontName},{size},{primary},&H0000FFFF,{outline},{back},0,0,0,0,{scaleX},100,0,0,{border},{width},{shadow},{align},{g.MarginL},{g.MarginR},{marginV},1");
        // captions: light fill (one per speaker) + black stroke; an underlay event draws a wider black edge (so a keyword's white stroke has a dark rim),
        // a blurred halo goes under everything
        S("Caption", c.FontPx, Short.SpeakerFills[0], Short.Black, Short.Black, sx, 1, Short.CaptionStroke, 0, 2, g.CaptionMarginV);
        S("CapEdge", c.FontPx, "&HFF000000", Short.Black, Short.Black, sx, 1, Short.CaptionOutline, 0, 2, g.CaptionMarginV);
        S("Halo", c.FontPx, "&HFF000000", "&H30000000", Short.Black, sx, 1, Short.HaloOutline, 0, 2, g.CaptionMarginV);
        for (int i = 0; i < Short.SpeakerFills.Length; i++)
            S($"CapS{i + 1}", c.FontPx, Short.SpeakerFills[i], Short.Black, Short.Black, sx, 1, Short.CaptionStroke, 0, 2, g.CaptionMarginV);
        int punchV = g.CaptionMarginV + Short.PunchLift;
        // punch telops and the title: main event = fill + white inner stroke; underlay event on its own layer = black outer stroke + shadow
        S("Emph", Short.EmphPx, Short.KeywordRed, Short.White, Short.Black, 100, 1, Short.EmphInner, 0, 2, punchV);
        S("EmphUnder", Short.EmphPx, "&HFF000000", Short.Black, "&H90000000", 100, 1, Short.EmphOutline, 4, 2, punchV);
        S("Hook", Short.TitlePx, Short.TitleYellow, Short.White, Short.Black, 100, 1, Short.TitleInner, 0, 8, g.HeadlineMarginV + Short.TitleDrop);
        S("HookUnder", Short.TitlePx, "&HFF000000", Short.Black, "&H90000000", 100, 1, Short.TitleOutline, 5, 8, g.HeadlineMarginV + Short.TitleDrop);
        S("Tsukkomi", Short.TsukkomiPx, Short.White, Variety.BoxRed, Variety.BoxRed, 100, 3, Short.TsukkomiInnerPad, 0, 2, punchV);
        S("TsukkomiUnder", Short.TsukkomiPx, "&HFF000000", Short.White, Short.White, 100, 3, Short.TsukkomiPad, 0, 2, punchV);
        // title bar: white text on a red box inside a white frame (the frame is a wider box on the layer below)
        S("Headline", Short.HeadlinePx, Short.White, Short.BarRed, Short.BarRed, 100, 3, Short.HeadlinePad, 0, 8, Short.BarMarginV(g));
        S("HeadlineFrame", Short.HeadlinePx, "&HFF000000", Short.White, Short.White, 100, 3, Short.HeadlineFramePad, 0, 8, Short.BarMarginV(g));
        S("Counter", 96, "&H0000E0FF", Short.Black, "&H80000000", 100, 1, 10, 3, 8, g.CounterMarginV);
        S("Sfx", Short.SfxPx, "&H002020E0", Short.White, Short.Black, 100, 1, Short.SfxInner, 0, 5, 0);
        S("SfxUnder", Short.SfxPx, "&HFF000000", Short.Black, "&H80000000", 100, 1, Short.SfxOutline, 3, 5, 0);
        CardStyle(sb, g, plan, p);
        EventsHeader(sb);
        var ins = Planning.InsertMap.Active(plan);
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Start))
        {
            var placement = layout[t.Id];
            if (placement.Bounds == null) continue;
            bool caption = t.Type == TelopType.Caption, top = t.Type is TelopType.Hook or TelopType.Headline;
            var style = t.Style;
            int speaker = caption && p.Telop.SpeakerColors && t.Speaker is > 0 ? (t.Speaker.Value - 1) % Short.SpeakerFills.Length : -1;
            if (speaker >= 0) style = $"CapS{speaker + 1}";
            var (size, outline, scaleX) = ShortMetrics(style, p);
            var lines = caption ? CaptionLines(t.Text, p, plan.Canvas.Width, plan.Language) : [t.Text];
            var fit = lines.Min(l => FitFontSize(l, size, outline, plan.Canvas.Width, g.MarginR, scaleX, plan.Language, g.MarginL));
            if (placement.Positioned && t.Type != TelopType.Caption) fit = placement.FontSize;   // a moved caption keeps its per-line size
            var fx = Fx(t, plan, fit < size ? $@"\fs{fit}" : "", placement, g.SfxY);
            // no red keywords inside captions: at feed size OCR misread them even with a white stroke (§24), and the keyword pops up as an emphasis telop anyway
            var text = caption ? string.Join(@"\N", lines.Select(Escape)) : Escape(t.Text);
            string? under = style switch
            {
                "Emph" => "EmphUnder", "Hook" => "HookUnder", "Sfx" => "SfxUnder", "Tsukkomi" => "TsukkomiUnder", "Headline" => "HeadlineFrame",
                _ when caption => "CapEdge", _ => null,
            };
            int layer = top ? Short.LayerTop : caption ? Short.LayerCaption : Short.LayerPunch;
            int underLayer = top ? Short.LayerTopUnder : caption ? Short.LayerCaptionEdge : Short.LayerPunchUnder;
            foreach (var (vs, ve) in TelopLayout.Visible(placement, t.Start, t.End))
            foreach (var (s, e) in ins.Count == 0 ? [(vs, ve)] : Planning.InsertMap.Span(ins, vs, ve))
            {
                if (caption) sb.AppendLine($@"Dialogue: {Short.LayerHalo},{Time(s)},{Time(e)},Halo,,0,0,0,,{{\blur{Short.HaloBlur}}}{fx}{string.Join(@"\N", lines.Select(Escape))}");
                if (under != null)
                {
                    var blur = under is "EmphUnder" or "HookUnder" or "SfxUnder" ? $@"{{\blur{Short.UnderBlur}}}" : "";
                    sb.AppendLine($"Dialogue: {underLayer},{Time(s)},{Time(e)},{under},,0,0,0,,{blur}{fx}{text}");
                }
                sb.AppendLine($"Dialogue: {layer},{Time(s)},{Time(e)},{style},,0,0,0,,{fx}{text}");
            }
        }
        CardEvents(sb, plan, g, Short.LayerCard, p);
        return sb.ToString();
    }
}
