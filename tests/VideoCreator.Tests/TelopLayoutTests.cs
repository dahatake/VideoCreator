using System.Text;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

public class TelopLayoutTests
{
    static Preset P => Fixtures.Presets["P3"];
    static MediaInfo Vertical => Fixtures.Landscape(8) with { Width = 1080, Height = 1920 };
    static Telop Emphasis => new() { Id = "e", Type = TelopType.Emphasis, Text = "見せ場です", Start = 0, End = 3, Style = "Emph", Animation = "pop" };
    static Clip Clip => new() { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 4, Reframe = new() { Mode = "fit" } };
    static List<FaceSample> Samples(params FaceBox[] faces) => Enumerable.Range(0, 9).Select(i => new FaceSample(i * 0.5, [.. faces])).ToList();
    static EditPlan Plan(List<FaceSample> samples, List<Telop>? telops = null) => new()
    {
        EditplanId = "e", ProjectId = "p", Preset = P.Ref, Audio = new() { Master = "a" },
        Timeline = [Clip], Telops = telops ?? [Emphasis], TelopFaces = new() { ["a"] = new(Vertical, samples) },
    };

    [Fact]
    public void Repeated_keywords_increase_frequency_without_changing_captions()
    {
        var tr = new Transcript
        {
            Provider = "test",
            Duration = 30,
            Words = Enumerable.Range(0, 6).SelectMany(i => new[]
            {
                new Word($"a{i}", i * 5, i * 5 + 1, "大切なのは", 1),
                new Word($"b{i}", i * 5 + 1, i * 5 + 2, "安全です", 1),
                new Word($"c{i}", i * 5 + 2, i * 5 + 3, "。", 1),
            }).ToList(),
        };
        var clips = new List<Clip> { Clip with { SrcOut = 30 } };
        var p = Fixtures.Presets["P4"];
        var actual = TelopBuilder.Build(clips, tr, p, "", "", ["安全です"], []);
        var emphasis = actual.Where(t => t.Type == TelopType.Emphasis).ToList();
        // The previous builder displayed only the first match: 1 -> 6 on this identical 30 s input.
        Assert.Equal(6, emphasis.Count);
        Assert.All(emphasis, t => Assert.Equal("安全です", t.Text));
        Assert.Equal([0, 5, 10, 15, 20, 25], emphasis.Select(t => (int)t.Start - 1).ToArray());
        var captionsOnly = TelopBuilder.Build(clips, tr, p with { Telop = p.Telop with { Types = ["caption"] } }, "", "", [], []);
        Assert.Equal(Json.Serialize(captionsOnly), Json.Serialize(actual.Where(t => t.Type == TelopType.Caption).ToList()));
        var supplement = TelopBuilder.Build(clips, tr, p, "", "", [], []).Where(t => t.Type == TelopType.Emphasis).ToList();
        Assert.True(supplement.Count >= 5, $"Expected >=5 verbatim phrases per 30s, got {supplement.Count}");
        Assert.All(supplement, t => Assert.NotEmpty(t.SourceWordIds));
    }

    [Theory]
    [InlineData(TelopType.Emphasis, "Emph")]
    [InlineData(TelopType.Tsukkomi, "Tsukkomi")]
    [InlineData(TelopType.Hook, "Hook")]
    [InlineData(TelopType.Headline, "Headline")]
    [InlineData(TelopType.Sfx, "Sfx")]
    [InlineData(TelopType.Counter, "Counter")]
    public void Every_presentation_type_avoids_faces_and_keeps_captions(TelopType type, string style)
    {
        var cap = new Telop { Id = "c", Type = TelopType.Caption, Text = "話した言葉はそのまま", Start = 0, End = 3 };
        var face = new FaceBox(0.2, 0.4, 0.5, 0.25, 1);
        var plan = Plan(Samples(face), [Emphasis with { Type = type, Style = style }, cap]);
        var layout = TelopLayout.Resolve(plan, P);
        var bounds = Assert.IsType<TelopLayout.Rect>(layout["e"].Bounds);
        Assert.False(bounds.Intersects(TelopLayout.Project(face, Clip, Vertical, plan.Canvas, 1)));
        Assert.False(bounds.Intersects(layout["c"].Bounds!));
        Assert.False(layout["c"].Positioned);
        Assert.Contains(@"\pos(", AssWriter.Write(plan, P));
        Assert.Contains(cap.Text, AssWriter.Srt(plan, P));
    }

    [Fact]
    public void Swept_faces_multiple_faces_and_no_space_fail_closed()
    {
        var samples = Samples();
        for (int i = 0; i < samples.Count; i++)
            samples[i] = samples[i] with { Boxes = [new(0.2, i % 2 == 0 ? 0.1 : 0.7, 0.4, 0.18, 1)] };
        var moving = Plan(samples);
        var swept = TelopLayout.Obstacles(moving, Emphasis, out var known);
        Assert.True(known);
        Assert.Single(swept);
        Assert.True(swept[0].Top < 200 && swept[0].Bottom > 1600);
        Assert.Null(TelopLayout.Resolve(moving, P)["e"].Bounds);
        var group = Plan(Samples(new(0, 0.1, 0.5, 0.4, 1), new(0.5, 0.5, 0.5, 0.4, 1)));
        Assert.Null(TelopLayout.Resolve(group, P)["e"].Bounds);
        Assert.DoesNotContain("Dialogue:", AssWriter.Write(group, P));
    }

    [Fact]
    public void Missing_or_incomplete_analysis_is_not_treated_as_no_faces()
    {
        foreach (var samples in new[] { new List<FaceSample>(), new List<FaceSample> { new(0, []), new(3, []) } })
        {
            var placement = TelopLayout.Resolve(Plan(samples), P)["e"];
            Assert.Null(placement.Bounds);
            Assert.Contains("解析情報", placement.Reason);
        }
        Assert.NotNull(TelopLayout.Resolve(Plan(Samples()), P)["e"].Bounds);
        var unknown = Plan(Samples()) with { TelopFaces = [] };
        Assert.Null(TelopLayout.Resolve(unknown, P)["e"].Bounds);
    }

    [Fact]
    public void Placement_is_recomputed_after_text_time_and_type_edits_and_survives_json()
    {
        var plan = Plan(Samples(new FaceBox(0.2, 0.05, 0.5, 0.05, 1)), [Emphasis, Emphasis with { Id = "s", Type = TelopType.Counter, Style = "Counter" }]);
        plan = Json.Deserialize<EditPlan>(Json.Serialize(plan));
        var layout = TelopLayout.Resolve(plan, P);
        Assert.False(Assert.IsType<TelopLayout.Rect>(layout["e"].Bounds).Intersects(Assert.IsType<TelopLayout.Rect>(layout["s"].Bounds)));
        var edited = plan with { Telops = [Emphasis with { Text = "これはとても長いテロップです" }] };
        Assert.True(TelopLayout.Resolve(edited, P)["e"].FontSize < layout["e"].FontSize);
        var disabled = plan with { Telops = [Emphasis with { Enabled = false }] };
        Assert.Empty(TelopLayout.Resolve(disabled, P));
    }

    [Fact]
    public void Projection_accounts_for_fit_blur_crop_track_zoom_and_pip()
    {
        var face = new FaceBox(0.4, 0.3, 0.1, 0.2, 1);
        var media = Fixtures.Landscape();
        var canvas = new Canvas();
        var fit = TelopLayout.Project(face, Clip, media, canvas, 1);
        Assert.Equal(432 - 32, fit.Left);
        Assert.Equal((1920 - 607.5) / 2 + 0.3 * 607.5 - 32, fit.Top, 3);
        var blur = TelopLayout.Project(face, Clip with { Reframe = new() }, media, canvas, 1);
        Assert.Equal(fit.Top - 120, blur.Top, 3);
        var crop = Clip with { Reframe = new() { Mode = "face", CropW = 600, CropH = 1080, Track = [new(0, 400), new(3, 700)] } };
        var left = TelopLayout.Project(face, crop, media, canvas, 0);
        var right = TelopLayout.Project(face, crop, media, canvas, 3);
        Assert.Equal(540, left.Left - right.Left, 3);
        var zoom = TelopLayout.Project(face, crop with { Zoom = 1.2 }, media, canvas, 0);
        Assert.Equal((left.Right - left.Left - 64) * 1.2 + 64, zoom.Right - zoom.Left, 3);
        var pip = new PipSpec { AssetId = "b", SrcIn = 10, CropS = 300, X = 600, Y = 700, D = 360 };
        var plan = Plan(Samples()) with { Timeline = [Clip with { Pip = pip }] };
        Assert.Contains(TelopLayout.Obstacles(plan, Emphasis, out _), b => b.Left == 568 && b.Top == 668);
    }

    [Fact]
    public void Speed_source_offsets_cuts_and_crossfades_are_checked_for_the_entire_span()
    {
        var first = Clip with { SrcIn = 10, SrcOut = 14, Speed = 2 };
        var second = Clip with { ClipId = "b", AssetId = "b", SrcIn = 20, SrcOut = 22, TransitionSec = 0.2 };
        var plan = Plan(Samples()) with
        {
            Timeline = [first, second],
            Telops = [Emphasis with { Start = 1.8, End = 2.3 }],
            TelopFaces = new()
            {
                ["a"] = new(Vertical, Enumerable.Range(0, 11).Select(i => new FaceSample(10 + i * 0.5, [new(0, 0.1, 0.5, 0.2, 1)])).ToList()),
                ["b"] = new(Vertical, Enumerable.Range(0, 5).Select(i => new FaceSample(20 + i * 0.5, [new(0.5, 0.7, 0.5, 0.2, 1)])).ToList()),
            },
        };
        var obstacles = TelopLayout.Obstacles(plan, plan.Telops[0], out var known);
        Assert.True(known);
        Assert.Equal(2, obstacles.Count);
        var afterCut = plan.Telops[0] with { Start = 2.05, End = 2.15 };
        Assert.Equal(2, TelopLayout.Obstacles(plan, afterCut, out _).Count);
    }

    [Theory]
    [InlineData("P3")]
    [InlineData("P4")]
    public async Task Libass_pixels_and_rendered_face_detection_confirm_no_overlap(string presetId)
    {
        var ct = TestContext.Current.CancellationToken;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("FFmpeg is required");
        var dir = Path.Combine(Path.GetTempPath(), "vc-telop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var video = Path.Combine(dir, "face.mp4");
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-loop", "1", "-i", Path.Combine(Fixtures.Dir, "faces", "face1.jpg"),
                "-t", "4", "-vf", "scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2,format=yuv420p",
                "-r", "10", "-c:v", "libx264", "-preset", "ultrafast", video], ct);
            var detector = new FaceService(ff, Path.Combine(Fixtures.RepoRoot, "tools", "models", "ultraface.onnx"));
            var samples = await detector.AnalyzeAsync(video, [new(0, 3.5)], Path.Combine(dir, "faces.json"), ct);
            Assert.Contains(samples, s => s.Boxes.Count > 0);
            var p = Fixtures.Presets[presetId];
            var plan = Plan(samples) with { Preset = p.Ref, Telops = [Emphasis with { Text = "顔を隠さない" }] };
            Assert.NotNull(TelopLayout.Resolve(plan, p)["e"].Bounds);
            var ass = Path.Combine(dir, "telop.ass");
            await File.WriteAllTextAsync(ass, AssWriter.Write(plan, p), new UTF8Encoding(false), ct);
            var output = Path.Combine(dir, "output.mp4");
            string filter = $"subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(ff.FontsDir)}";
            await ff.RunFfmpegCheckedAsync(["-v", "error", "-y", "-i", video, "-vf", filter, "-c:v", "libx264", "-preset", "ultrafast", output], ct);
            foreach (double time in new[] { 0.1, 0.2, 1.5, 2.8 })
            {
                var inputFaces = await detector.DetectAtAsync(video, time, ct);
                var outputFaces = await detector.DetectAtAsync(output, time, ct);
                Assert.NotEmpty(inputFaces);
                Assert.Equal(inputFaces.Count, outputFaces.Count);
                using var ms = new MemoryStream();
                var frame = await ff.RunFfmpegAsync(["-v", "error", "-f", "lavfi", "-i", "color=black:s=1080x1920:r=10:d=4",
                    "-vf", filter + $",select='eq(n,{(int)Math.Round(time * 10)})'", "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", "-"], ct, stdout: ms);
                Assert.Equal(0, frame.ExitCode);
                var pixels = ms.ToArray();
                Assert.Equal(1080 * 1920, pixels.Length);
                Assert.Contains(pixels, pixel => pixel > 48);
                foreach (var face in inputFaces.Concat(outputFaces))
                {
                    int x0 = Math.Clamp((int)(face.X * 1080), 0, 1079), x1 = Math.Clamp((int)Math.Ceiling((face.X + face.W) * 1080), 0, 1080);
                    int y0 = Math.Clamp((int)(face.Y * 1920), 0, 1919), y1 = Math.Clamp((int)Math.Ceiling((face.Y + face.H) * 1920), 0, 1920);
                    for (int y = y0; y < y1; y++)
                        Assert.DoesNotContain(pixels.AsSpan(y * 1080 + x0, x1 - x0).ToArray(), pixel => pixel > 48);
                }
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
