using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>Study (P6/P10) and corporate (P7/P11) telop looks — docs/IMPLEMENTATION.md §23. Checked on what libass actually draws.</summary>
public class TelopLookTests : IAsyncLifetime
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-look-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    const int W = 1080, H = 1920;

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static EditPlan Plan(Preset p, List<Telop> telops) => new()
    {
        EditplanId = "e", ProjectId = "p", Preset = p.Ref, Telops = telops, Audio = new AudioSpec { Master = "a1" },
        Timeline = [new Clip { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 4 }],
    };

    static Telop T(string id, TelopType type, string text, string style, string? anim = null, int? speaker = null) =>
        new() { Id = id, Type = type, Start = 0, End = 3, Text = text, Style = style, Animation = anim, Speaker = speaker };

    /// <summary>RGB frame at 1.0 s of the plan's telops drawn by libass over a mid-grey canvas (so dark boxes, dark edges and white text all differ from it).</summary>
    async Task<byte[]> RenderAsync(EditPlan plan, Preset p, string name, int frame = 10)
    {
        var ass = Path.Combine(_dir, name + ".ass");
        await File.WriteAllTextAsync(ass, AssWriter.Write(plan, p), new System.Text.UTF8Encoding(false), Ct);
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=0x808080:s={W}x{H}:r=10:d=2",
            "-vf", $"format=rgb24,subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(Ff.FontsDir)},select='eq(n,{frame})'", "-fps_mode", "passthrough", "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: ms);
        var buf = ms.ToArray(); Assert.Equal(W * H * 3, buf.Length);
        return buf;
    }

    static (int X1, int X2, int Y1, int Y2) Bbox(byte[] rgb)
    {
        int x1 = W, x2 = -1, y1 = H, y2 = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 3;
                if (Math.Abs(rgb[i] - 128) + Math.Abs(rgb[i + 1] - 128) + Math.Abs(rgb[i + 2] - 128) > 24) { x1 = Math.Min(x1, x); x2 = Math.Max(x2, x); y1 = Math.Min(y1, y); y2 = Math.Max(y2, y); }
            }
        return (x1, x2, y1, y2);
    }

    [Theory] // every element inside the safe zone; the caption and the emphasis drawn together never touch; corporate boxes stay inside their bands
    [InlineData("P6")]
    [InlineData("P7")]
    public async Task Layered_looks_draw_inside_the_safe_zone_without_collisions(string id)
    {
        var p = Fixtures.Presets[id];
        var (top, bottom, left, right) = QaEngine.SafeZone;
        var caption = await RenderAsync(Plan(p, [T("c", TelopType.Caption, "ネタを書いてる芸人です", "Caption", speaker: 2)]), p, id + "_cap");
        var emph = await RenderAsync(Plan(p, [T("e", TelopType.Emphasis, "それは300円", "Emph", "pop")]), p, id + "_emph");
        var head = await RenderAsync(Plan(p, [T("h", TelopType.Headline, "1時間で1万円使い切れるか", "Headline")]), p, id + "_head");
        var (c, e, h) = (Bbox(caption), Bbox(emph), Bbox(head));
        foreach (var (name, b) in new[] { ("caption", c), ("emph", e), ("headline", h) })
        {
            Assert.True(b.X2 > 0, $"{name}: nothing drawn");
            Assert.True(b.X1 >= left && b.X2 <= W - right && b.Y1 >= top && b.Y2 <= H - bottom, $"{id} {name}: drawn x{b.X1}-{b.X2} y{b.Y1}-{b.Y2}");
        }
        Assert.True(e.Y2 < c.Y1, $"{id}: emphasis y{e.Y1}-{e.Y2} touches the caption y{c.Y1}-{c.Y2}");
        if (p.Telop.IsCorporate)
            foreach (var (type, b) in new[] { (TelopType.Caption, c), (TelopType.Emphasis, e) })
            {
                var band = AssWriter.Band(type, p, H)!.Value;
                Assert.True(b.Y1 >= band.Top && b.Y2 <= band.Bottom, $"{type}: drawn y{b.Y1}-{b.Y2}, band {band.Top:F0}-{band.Bottom:F0}");
            }
    }

    [Fact] // the speaker's colour is only on the frame: the box under the text stays the same dark grey for every speaker (a translucent box showed the frame through)
    public async Task Corporate_caption_frame_carries_the_speaker_colour_and_the_box_stays_dark()
    {
        var p = Fixtures.Presets["P7"];
        foreach (var (speaker, frame) in new[] { (1, (R: 0x00, G: 0xA4, B: 0xEF)), (2, (R: 0xF2, G: 0x50, B: 0x22)) })
        {
            var rgb = await RenderAsync(Plan(p, [T("c", TelopType.Caption, "ネタを書いてる芸人です", "Caption", speaker: speaker)]), p, $"frame{speaker}");
            var (x1, _, y1, y2) = Bbox(rgb);
            int Px(int x, int y, int ch) => rgb[(y * W + x) * 3 + ch];
            // libass treats ASS colours as TV-range (no "YCbCr Matrix" header): v → 16 + v·219/255
            static int Tv(int v) => 16 + v * 219 / 255;
            int my = (y1 + y2) / 2, fx = x1 + 2, bx = x1 + AssWriter.Corporate.FramePad - AssWriter.Corporate.CaptionPad + 3;
            Assert.True(Math.Abs(Px(fx, my, 0) - Tv(frame.R)) + Math.Abs(Px(fx, my, 1) - Tv(frame.G)) + Math.Abs(Px(fx, my, 2) - Tv(frame.B)) < 12,
                $"speaker {speaker}: frame pixel {Px(fx, my, 0)},{Px(fx, my, 1)},{Px(fx, my, 2)}");
            for (int ch = 0; ch < 3; ch++) Assert.InRange(Px(bx, my, ch), Tv(0x1B) - 3, Tv(0x1B) + 3);
        }
    }

    [Fact]
    public void Corporate_fades_instead_of_bouncing_and_standard_presets_are_untouched()
    {
        var p7 = Fixtures.Presets["P7"];
        var ass = AssWriter.Write(Plan(p7, [T("e", TelopType.Emphasis, "Copilot", "Emph", "pop")]), p7);
        Assert.Contains($@"\fad({AssWriter.Corporate.FadeInMs},{AssWriter.Corporate.FadeOutMs})", ass);
        Assert.DoesNotContain(@"\fscx70", ass);
        var p6 = Fixtures.Presets["P6"];
        var studyEmphasis = AssWriter.Write(Plan(p6, [T("e", TelopType.Emphasis, "要点", "Emph", "pop")]), p6);
        Assert.DoesNotContain(@"\fscx70", studyEmphasis);
        Assert.DoesNotContain(@"\fad(60,0)", studyEmphasis);
        var standard = AssWriter.Write(Plan(Fixtures.Presets["P4"], [T("e", TelopType.Emphasis, "要点", "Emph", "pop")]), Fixtures.Presets["P4"]);
        Assert.Contains(@"\fscx70", standard);
        // looks that need two colours draw an underlay on a lower layer (libass would push a same-layer event aside)
        var study = AssWriter.Write(Plan(p6, [T("c", TelopType.Caption, "字幕", "Caption"), T("h", TelopType.Headline, "見出し", "Headline")]), p6);
        Assert.Contains("Dialogue: 0,0:00:00.00,0:00:03.00,CapEdge,", study);
        Assert.Contains("Dialogue: 1,0:00:00.00,0:00:03.00,Caption,", study);
        Assert.Contains("Dialogue: 3,0:00:00.00,0:00:03.00,HeadlineFrame,", study);
        Assert.Contains("Dialogue: 4,0:00:00.00,0:00:03.00,Headline,", study);
        Assert.Throws<PresetValidationException>(() => PresetStore.Validate(p6 with { Telop = p6.Telop with { Look = "loud" } }));
    }

    [Theory]
    [InlineData("P3")]
    [InlineData("P6")]
    public async Task Variety_and_study_pop_is_visible_from_its_first_frame(string id)
    {
        var p = Fixtures.Presets[id];
        var plan = Plan(p, [T("e", TelopType.Emphasis, "安全です", "Emph", "pop")]);
        var first = await RenderAsync(plan, p, id + "_first", 0);
        var next = await RenderAsync(plan, p, id + "_next", 1);
        Assert.True(Bbox(first).X2 > 0, $"{id}: no telop on the first frame");
        Assert.Equal(first, next);
    }

    [Fact]
    public async Task Study_reveals_a_selected_phrase_in_two_steps_after_800_ms()
    {
        var p = Fixtures.Presets["P6"];
        var plan = Plan(p, [T("e", TelopType.Emphasis, "安全です", "Emph", "two-stage")]);
        var ass = AssWriter.Write(plan, p);
        Assert.Contains(@"Dialogue: 2,0:00:00.00,0:00:00.80,Emph,,0,0,0,,安全{\alpha&HFF&}です", ass);
        Assert.Contains("Dialogue: 2,0:00:00.80,0:00:03.00,Emph,", ass);

        var first = await RenderAsync(plan, p, "staged_first", 0);
        var beforeReveal = await RenderAsync(plan, p, "staged_before", 7);
        var afterReveal = await RenderAsync(plan, p, "staged_after", 8);
        Assert.Equal(first, beforeReveal);
        Assert.True(Bbox(afterReveal).X2 > Bbox(first).X2, "the second stage must add the remaining phrase");
    }

    [Fact]
    public void Study_caption_wrapping_is_preserved()
    {
        var p = Fixtures.Presets["P6"] with
        {
            Telop = Fixtures.Presets["P6"].Telop with
            {
                Caption = Fixtures.Presets["P6"].Telop.Caption with { MaxLines = 2 },
            },
        };
        var plan = Plan(p, [T("c", TelopType.Caption, "これは長い字幕で二つの行に改行されることを確認する文章です", "Caption")]);
        var ass = AssWriter.Write(plan, p);
        Assert.Contains(@"\N", ass.Split('\n').Single(line => line.Contains(",Caption,,0,0,0,,", StringComparison.Ordinal)));
    }
}
