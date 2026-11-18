using VideoCreator.Asr;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>Output orientation follows the main video: landscape in → 1920×1080 out, with the 16:9 opening/closing (PRD FR-RFM-06).</summary>
public class LandscapeTests
{
    static Transcript Tr => Fixtures.Talk;
    static Preset P3 => Fixtures.Presets["P3"];
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static BrandKit Kit => new()
    {
        RightsConfirmed = true,
        Files = [new() { Path = "op916.mp4", Kind = "opening", Width = 1080, Height = 1920, Duration = 3, HasAudio = true, Lufs = -14 },
                 new() { Path = "op169.mp4", Kind = "opening", Width = 1920, Height = 1080, Duration = 3, HasAudio = true, Lufs = -14 },
                 new() { Path = "ed916.mp4", Kind = "closing", Width = 1080, Height = 1920, Duration = 3, HasAudio = true, Lufs = -14 },
                 new() { Path = "ed169.mp4", Kind = "closing", Width = 1920, Height = 1080, Duration = 3, HasAudio = true, Lufs = -14 }],
    };

    [Theory]
    [InlineData(1920, 1080, "auto", 1920, 1080)]
    [InlineData(1280, 720, "auto", 1920, 1080)]
    [InlineData(1080, 1920, "auto", 1080, 1920)]
    [InlineData(1000, 1000, "auto", 1080, 1920)]      // square is not landscape: the shorts default stays
    [InlineData(1024, 1000, "auto", 1080, 1920)]      // within 5% of square
    [InlineData(1920, 1080, "portrait", 1080, 1920)]  // forced 9:16 for Shorts
    [InlineData(1080, 1920, "landscape", 1920, 1080)]
    public void Canvas_follows_the_input_shape_unless_forced(int w, int h, string orientation, int cw, int ch)
    {
        var c = Canvas.For(w, h, orientation);
        Assert.Equal((cw, ch), (c.Width, c.Height));
    }

    static Task<(EditPlan Plan, List<string> Notes)> Build(MediaInfo media, string orientation)
    {
        var assets = new List<SourceAsset> { new() { Id = "a1", ProjectId = "p", Path = "x.mp4", Sha256 = "x", Role = AssetRole.Main, Media = media } };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P3"], Options = new JobOptions { Opening = true, Closing = true, Orientation = orientation } };
        var cand = new Candidate { Id = "c1", Segments = [new Interval(8.8, 40)], HookText = "1万円で帰れない", Headline = "100円だけで1万円" };
        return PlanBuilder.BuildAsync(new PlanRequest(cand, Tr, Fixtures.Signals(Tr), P3, assets, assets[0], "p", job, new Dictionary<string, List<FaceSample>>(), null, null, Kit: Kit), Ct);
    }

    [Fact] // the whole 16:9 frame is shown (no 9:16 crop, no blurred bands), the 16:9 bumpers are used and every telop stays inside the 5% title-safe area
    public async Task Landscape_master_gets_a_landscape_plan_with_landscape_bumpers()
    {
        var (plan, _) = await Build(Fixtures.Landscape(), "auto");
        Assert.Equal((1920, 1080), (plan.Canvas.Width, plan.Canvas.Height));
        Assert.All(plan.Timeline, c => Assert.Equal("fit", c.Reframe.Mode));
        Assert.Equal("op169.mp4", plan.Inserts.Single(i => i.Kind == "opening").Path);
        Assert.Equal("ed169.mp4", plan.Inserts.Single(i => i.Kind == "closing").Path);

        var geo = AssWriter.Geo(P3, plan.Canvas);
        Assert.Equal((54, 54, 96, 96), geo.Safe);
        Assert.Equal((96, 96), (geo.MarginL, geo.MarginR));
        var placed = TelopLayout.Resolve(plan, P3).Values.Where(x => x.Bounds != null).Select(x => x.Bounds!).ToList();
        Assert.NotEmpty(placed);
        Assert.All(placed, b => Assert.True(b.Left >= geo.Safe.Left && b.Right <= 1920 - geo.Safe.Right && b.Top >= geo.Safe.Top && b.Bottom <= 1080 - geo.Safe.Bottom, $"{b} is outside the title-safe area"));
        Assert.Contains("PlayResX: 1920", AssWriter.Write(plan, P3));
    }

    [Fact] // choosing "portrait" keeps the shorts behaviour for a landscape source: 9:16 canvas, 9:16 bumpers
    public async Task Portrait_can_be_forced_for_a_landscape_master()
    {
        var (plan, _) = await Build(Fixtures.Landscape(), "portrait");
        Assert.Equal((1080, 1920), (plan.Canvas.Width, plan.Canvas.Height));
        Assert.Equal("op916.mp4", plan.Inserts.Single(i => i.Kind == "opening").Path);
        Assert.DoesNotContain(plan.Timeline, c => c.Reframe.Mode == "fit");
    }

    [Fact] // portrait geometry is unchanged by the landscape work
    public void Portrait_geometry_keeps_the_tuned_values()
    {
        var geo = AssWriter.Geo(P3, new Canvas());
        Assert.Equal(QaEngine.SafeZone, geo.Safe);
        Assert.Equal(P3.Telop.Caption.MarginV, geo.CaptionMarginV);
        Assert.Equal((AssWriter.MarginL, P3.Telop.Caption.MarginR), (geo.MarginL, geo.MarginR));
    }
}
