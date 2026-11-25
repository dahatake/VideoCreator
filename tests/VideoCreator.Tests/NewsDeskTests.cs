using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>
/// The news pattern's split screen for several anchors and the web-searched ticker: layout invariants, speaker framing, the source check that keeps
/// only pages the search tool returned, the approval gate (H-08), and pixels measured in a rendered file.
/// </summary>
public class NewsDeskTests : IAsyncLifetime
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-news-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    static Presets.Preset P13 => Fixtures.Presets["P13"];

    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    static SourceAsset Cam(string id, AssetRole role, double dur = 12, int w = 1280, int h = 720, string path = "x.mp4") => new()
    {
        Id = id, ProjectId = "p", Path = path, Sha256 = "x", Role = role, SyncMethod = "audio",
        Media = new MediaInfo { Duration = dur, Width = w, Height = h, Fps = 30, AudioChannels = 1 },
    };

    static Transcript TwoSpeakers(double switchAt = 6, double end = 12) => new()
    {
        Provider = "test",
        Sentences = [new(0, 0.2, switchAt - 0.2, "最初のニュースです", 1, 0, 0), new(1, switchAt + 0.1, end - 0.2, "次のニュースです", 2, 1, 1)],
        Words = [new("w1", 0.2, switchAt - 0.2, "最初のニュースです", 1), new("w2", switchAt + 0.1, end - 0.2, "次のニュースです", 2)],
    };

    static void AssertLayout(List<PipSpec> panels, Canvas canvas)
    {
        var (top, bottom) = PipPlanner.PanelArea(P13, canvas);
        foreach (var x in panels)
        {
            Assert.True(x.X >= 0 && x.X + x.Width <= canvas.Width && x.Y >= top && x.Y + x.Height <= bottom, $"{x.X},{x.Y} {x.Width}x{x.Height} outside {top}-{bottom}");
            double aspect = x.Width / (double)x.Height;
            Assert.InRange(aspect, 9 / 16.0 - 0.01, 16 / 9.0 + 0.01);
            Assert.Equal(aspect, x.CropW / (double)x.CropH, 1);
        }
        for (int i = 0; i < panels.Count; i++)
            for (int j = i + 1; j < panels.Count; j++)
            {
                var (a, b) = (panels[i], panels[j]);
                Assert.True(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y, $"panels {i} and {j} overlap");
            }
    }

    [Theory] // landscape: one row for 2–3 anchors, 2×2 for 4; portrait: stacked. Boxes never overlap and stay above the captions and ticker
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(2, false)]
    public void Split_panels_follow_the_anchor_count_and_canvas(int n, bool landscape)
    {
        var canvas = landscape ? Canvas.Landscape() : new Canvas();
        var anchors = Enumerable.Range(0, n).Select(i => Cam($"a{i}", i == 0 ? AssetRole.Main : AssetRole.CamA)).ToList();
        var panels = PipPlanner.SplitPanels(anchors, anchors[1], anchors[0], 1, 3, null, P13, canvas);
        Assert.Equal(n, panels.Count);
        AssertLayout(panels, canvas);
        Assert.Equal(["a1"], panels.Where(x => x.Border > 0).Select(x => x.AssetId));
        var rows = panels.Select(x => x.Y).Distinct().Count();
        var cols = panels.Select(x => x.X).Distinct().Count();
        (int r, int c) expected = n == 4 ? (2, 2) : landscape ? (1, n) : (n, 1);
        Assert.Equal(expected, (rows, cols));
        var ticker = AssWriter.TickerBand(P13, canvas);
        if (ticker is { } band) Assert.All(panels, x => Assert.True(x.Y + x.Height <= band.Top));
    }

    [Fact] // the box is framed on the median face of the whole recording, so it does not jump at cuts
    public void Split_panel_crop_is_centred_on_the_recordings_median_face()
    {
        var canvas = Canvas.Landscape();
        var anchors = new List<SourceAsset> { Cam("a", AssetRole.Main), Cam("b", AssetRole.CamA) };
        var faces = new Dictionary<string, List<FaceSample>>
        {
            ["a"] = Enumerable.Range(0, 9).Select(i => new FaceSample(i, [new(0.75, 0.3, 0.1, 0.2, 1)])).Append(new FaceSample(9, [new(0.05, 0.3, 0.1, 0.2, 1)])).ToList(),
        };
        var early = PipPlanner.SplitPanels(anchors, null, anchors[0], 0, 2, faces, P13, canvas)[0];
        var late = PipPlanner.SplitPanels(anchors, null, anchors[0], 9, 11, faces, P13, canvas)[0];
        Assert.Equal((early.CropX, early.CropY), (late.CropX, late.CropY));
        Assert.InRange(early.CropX + early.CropW / 2.0, 0.8 * 1280 - early.CropW / 2.0 - 2, 0.8 * 1280 + 2);   // shifted towards the face at x = 0.8
        Assert.True(early.CropX > 0);
    }

    [Fact] // two anchors and no material: the whole edit is a split screen whose yellow frame follows the speaker; a screen scene puts the inset on the speaker's camera
    public void Two_anchors_without_material_are_split_and_the_frame_follows_the_speaker()
    {
        var canvas = Canvas.Landscape();
        var main = Cam("m", AssetRole.Main);
        var b = Cam("b", AssetRole.CamA);
        var speech = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 12 } };
        var notes = new List<string>();
        var timeline = PipPlanner.Plan(speech, TwoSpeakers(), main, main, [], P13, canvas, notes, null, [main, b]);
        Assert.Equal(12, timeline.Sum(c => c.Length), 6);
        Assert.All(timeline, c => Assert.Equal(2, c.Panels!.Count));
        Assert.Contains(timeline, c => Math.Abs(c.MasterOut - 6.1) < 1e-6);   // the cut sits where speaker 2 starts
        foreach (var c in timeline)
            Assert.Equal(c.MasterIn < 6 - 1e-6 ? "m" : "b", Assert.Single(c.Panels!, x => x.Border > 0).AssetId);
        Assert.Contains(notes, n => n.Contains("分割画面（左右 2 分割）"));

        var screen = Cam("s", AssetRole.Screen) with { PipScenes = [new(7.5, 11.5)] };
        var faces = new Dictionary<string, List<FaceSample>>
        {
            ["m"] = Enumerable.Range(0, 24).Select(i => new FaceSample(i * 0.5, [new(0.7, 0.15, 0.15, 0.25, 1)])).ToList(),
            ["b"] = Enumerable.Range(0, 24).Select(i => new FaceSample(i * 0.5, [new(0.7, 0.15, 0.15, 0.25, 1)])).ToList(),
        };
        var withScreen = PipPlanner.Plan(speech, TwoSpeakers(), main, main, [screen], P13, canvas, [], null, [main, b], faces);
        var shown = withScreen.Where(c => c.Pip != null || c.AssetId == "s").ToList();
        Assert.NotEmpty(shown);
        Assert.All(shown, c => { Assert.Null(c.Panels); Assert.True(c.MasterIn >= 6 - 1e-6); });
        Assert.All(shown.Where(c => c.Pip != null), c => Assert.Equal("b", c.AssetId));
        Assert.Contains(withScreen, c => c.Panels != null);
    }

    [Fact] // an anchor that stopped recording halfway is not shown after it ends (no black box): the remaining anchor fills the frame
    public void Anchors_are_split_only_where_their_recordings_cover_the_time()
    {
        var main = Cam("m", AssetRole.Main);
        var b = Cam("b", AssetRole.CamA, dur: 5);
        var speech = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 12 } };
        var timeline = PipPlanner.Plan(speech, TwoSpeakers(), main, main, [], P13, Canvas.Landscape(), [], null, [main, b]);
        Assert.All(timeline.Where(c => c.MasterOut > 5 + 1e-6), c => { Assert.Null(c.Panels); Assert.Equal("m", c.AssetId); });
        Assert.Contains(timeline, c => c.Panels?.Count == 2);
    }

    static NewsTicker Ticker(bool approved = false) => new()
    {
        Provider = "azure-openai", RetrievedAt = new DateTimeOffset(2025, 11, 27, 9, 0, 0, TimeSpan.FromHours(9)),
        Items =
        [
            new() { Id = "tk1", Text = "新モデルを一般提供", SourceUrl = "https://www.example.com/news/1", SourceTitle = "Example", Published = "2025-11-26", Approved = approved },
            new() { Id = "tk2", Text = "国内リージョンに拡大", SourceUrl = "https://news.example.org/a", Approved = approved, RetrievedAt = new DateTimeOffset(2025, 11, 27, 0, 0, 0, TimeSpan.Zero) },
        ],
    };

    static EditPlan Plan(double length = 20, NewsTicker? ticker = null) => new()
    {
        EditplanId = "ep", ProjectId = "p", Preset = P13.Ref, Canvas = Canvas.Landscape(),
        Timeline = [new() { ClipId = "c1", AssetId = "m", SrcIn = 0, SrcOut = length }],
        Audio = new AudioSpec { Master = "m", SfxEnabled = false }, Ticker = ticker,
    };

    [Fact] // the ticker reserves its strip: captions move up by its height, items rotate every 8 s with their source and date
    public void Ticker_reserves_a_band_and_rotates_items_with_their_source()
    {
        var canvas = Canvas.Landscape();
        var with = AssWriter.Geo(P13, canvas);
        var without = AssWriter.Geo(P13 with { Telop = P13.Telop with { Types = [.. P13.Telop.Types.Where(t => t != "ticker")] } }, canvas);
        Assert.Equal(AssWriter.TickerHeightFor(canvas.Height), with.TickerHeight);
        Assert.Equal(without.CaptionMarginV + with.TickerHeight + AssWriter.TickerGap, with.CaptionMarginV);
        var captionBottom = canvas.Height - with.CaptionMarginV;
        Assert.True(captionBottom <= AssWriter.TickerBand(P13, canvas)!.Value.Top);

        var ass = AssWriter.Write(Plan(20, Ticker()), P13);
        Assert.Contains("}速報", ass);
        Assert.Contains("新モデルを一般提供", ass);
        Assert.Contains("出典 example.com 2025-11-26", ass);
        Assert.Contains("出典 news.example.org 2025-11-27 取得", ass);
        var items = ass.Split('\n').Where(l => l.Contains(@"\an4\pos(")).ToList();
        Assert.Equal(3, items.Count);   // 0–8 tk1, 8–16 tk2, 16–20 tk1
        Assert.Contains("0:00:08.00", items[1]);
        Assert.DoesNotContain("Ticker", AssWriter.Write(Plan(20), P13).Split("[Events]")[1]);
        var off = PlanEditor.Apply(Plan(20, Ticker()), [new PlanOp("set_ticker_enabled", "tk1", Enabled: false), new PlanOp("set_ticker_enabled", "tk2", Enabled: false)]);
        Assert.DoesNotContain(@"\an4\pos(", AssWriter.Write(off, P13));
    }

    [Fact] // AI-found items block export until a person approves (or turns off) each; editing the text keeps the source and counts as approval
    public void Ticker_items_need_approval_before_export()
    {
        var tr = TwoSpeakers();
        QaStatus H08(EditPlan p) => QaEngine.PlanChecks(new QaContext(p, P13, tr, ["m"], [])).Single(q => q.CheckId == "H-08").Status;
        var plan = Plan(12, Ticker());
        Assert.Equal(QaStatus.Fail, H08(plan));
        Assert.Contains(QaEngine.PlanChecks(new QaContext(plan, P13, tr, ["m"], [])).Single(q => q.CheckId == "H-08").Items!, i => i.Target == "tk1" && i.Kind == "ticker");
        var one = PlanEditor.Apply(plan, [new PlanOp("approve_ticker", "tk1")]);
        Assert.Equal(QaStatus.Fail, H08(one));
        Assert.Equal(QaStatus.Pass, H08(PlanEditor.Apply(one, [new PlanOp("set_ticker_enabled", "tk2", Enabled: false)])));
        Assert.Equal(QaStatus.Pass, H08(PlanEditor.Apply(plan, [new PlanOp("approve_all_ticker")])));
        var edited = PlanEditor.Apply(one, [new PlanOp("replace_ticker_text", "tk2", "  国内3リージョンに拡大 ")]);
        var tk2 = edited.Ticker!.Items.Single(i => i.Id == "tk2");
        Assert.Equal(("国内3リージョンに拡大", "https://news.example.org/a", true), (tk2.Text, tk2.SourceUrl, tk2.Approved));
        Assert.Equal(QaStatus.Pass, H08(edited));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, [new PlanOp("replace_ticker_text", "tk1", " ")]));
        Assert.Throws<ArgumentException>(() => PlanEditor.Apply(plan, [new PlanOp("replace_ticker_text", "tk1", new string('あ', PlanEditor.MaxTickerLength + 1))]));
        Assert.Throws<KeyNotFoundException>(() => PlanEditor.Apply(plan, [new PlanOp("approve_ticker", "tk9")]));
        Assert.Throws<KeyNotFoundException>(() => PlanEditor.Apply(Plan(12), [new PlanOp("approve_ticker", "tk1")]));
        Assert.NotEqual(EditPlanFingerprint.Of(plan), EditPlanFingerprint.Of(edited));
    }

    sealed class FakeSearch(WebSearchReply reply) : IWebSearch
    {
        public string Name => "fake";
        public string? Instructions, Prompt;
        public Task<WebSearchReply> SearchAsync(string instructions, string prompt, CancellationToken ct) { Instructions = instructions; Prompt = prompt; return Task.FromResult(reply); }
    }

    [Fact] // the model may only cite pages the search tool returned; future-dated, over-long and duplicate items are dropped; nothing usable → no ticker
    public async Task Ticker_keeps_only_items_whose_source_the_search_returned()
    {
        var now = new DateTimeOffset(2025, 11, 27, 10, 0, 0, TimeSpan.FromHours(9));
        const string json = """
        {"topics":["Azure AI"],"queries":["Azure AI 発表 2025年11月"],"items":[
         {"text":"新モデルを一般提供","source_url":"https://WWW.Example.com/news/1/#top","source_title":"","published":"2025-11-26"},
         {"text":"新モデルを一般提供","source_url":"https://example.com/news/1","source_title":"dup","published":""},
         {"text":"存在しない記事","source_url":"https://invented.example.net/x","source_title":"x","published":"2025-11-25"},
         {"text":"来年の発表","source_url":"https://example.com/future","source_title":"f","published":"2026-03-01"},
         {"text":"国内リージョンに拡大","source_url":"https://news.example.org/a","source_title":"News","published":"not a date"}
        ]}
        """;
        var sources = new List<WebSource> { new("https://example.com/news/1", "Example"), new("https://example.com/future"), new("https://news.example.org/a") };
        var search = new FakeSearch(new WebSearchReply("```json\n" + json + "\n```", sources));
        var tr = TwoSpeakers();
        var (ticker, notes) = await NewsTickerService.FetchAsync(search, tr, [new(0, 6)], "ja-JP", now, Ct);
        Assert.NotNull(ticker);
        Assert.Equal(["新モデルを一般提供", "国内リージョンに拡大"], ticker.Items.Select(i => i.Text));
        Assert.Equal("https://example.com/news/1", ticker.Items[0].SourceUrl);   // the URL as the search returned it
        Assert.Equal("Example", ticker.Items[0].SourceTitle);
        Assert.Equal("2025-11-26", ticker.Items[0].Published);
        Assert.Null(ticker.Items[1].Published);
        Assert.All(ticker.Items, i => { Assert.False(i.Approved); Assert.True(i.Enabled); Assert.Equal(now, i.RetrievedAt); });
        Assert.Equal(["Azure AI 発表 2025年11月"], ticker.Queries);
        Assert.Equal("fake", ticker.Provider);
        Assert.Contains(notes, n => n.Contains("検索結果に無い出典を挙げた 1 件"));
        Assert.Contains(notes, n => n.Contains("公開日が未来の 1 件"));
        Assert.Contains("最初のニュースです", search.Prompt);
        Assert.DoesNotContain("次のニュースです", search.Prompt);   // only speech inside the edit
        Assert.Contains("2025-11-27", search.Instructions);

        var (none, why) = await NewsTickerService.FetchAsync(new FakeSearch(new("""{"items":[{"text":"a","source_url":"https://x.example/1","source_title":"","published":""}]}""", [])), tr, [], "ja-JP", now, Ct);
        Assert.Null(none);
        Assert.Contains(why, n => n.Contains("見つからなかった"));
        var (bad, badWhy) = await NewsTickerService.FetchAsync(new FakeSearch(new("検索できませんでした", [])), tr, [], "ja-JP", now, Ct);
        Assert.Null(bad);
        Assert.Contains(badWhy, n => n.Contains("読み取れなかった"));
    }

    [Fact] // rendered pixels: two coloured anchors side by side above the navy ticker, the speaker's box framed in yellow
    public async Task Split_screen_and_ticker_render_where_the_plan_puts_them()
    {
        Task Run(params string[] args) => Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-y", .. args], Ct);
        string P(string n) => Path.Combine(_dir, n);
        await Run("-f", "lavfi", "-i", "color=c=red:s=640x360:r=30:d=4", "-f", "lavfi", "-i", "sine=f=440:r=48000:d=4", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", P("a.mp4"));
        await Run("-f", "lavfi", "-i", "color=c=0x00C000:s=640x360:r=30:d=4", "-c:v", "libx264", "-preset", "ultrafast", P("b.mp4"));
        var main = Cam("m", AssetRole.Main, 4, 640, 360, P("a.mp4"));
        var b = Cam("b", AssetRole.CamA, 4, 640, 360, P("b.mp4"));
        var canvas = Canvas.Landscape();
        var tr = new Transcript { Provider = "test", Sentences = [new(0, 0.2, 3.8, "ニュース", 2, 0, 0)], Words = [new("w1", 0.2, 3.8, "ニュース", 2)] };
        var speech = new List<Clip> { new() { ClipId = "c", AssetId = "m", SrcIn = 0, SrcOut = 4 } };
        var timeline = PipPlanner.Plan(speech, tr, main, main, [], P13, canvas, [], null, [main, b]);
        var plan = Plan(4, Ticker(approved: true)) with { Timeline = timeline };
        var output = await new Renderer(Ff).RenderAsync(plan, P13, new[] { main, b }.ToDictionary(a => a.Id), P("split.mp4"), preview: false, Ct);

        using var buffer = new MemoryStream();
        await Ff.RunFfmpegAsync(["-v", "error", "-ss", "2", "-i", output.Path, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"], Ct, stdout: buffer);
        var px = buffer.ToArray();
        Assert.Equal(canvas.Width * canvas.Height * 3, px.Length);
        (int R, int G, int B) At(double x, double y) { int i = ((int)y * canvas.Width + (int)x) * 3; return (px[i], px[i + 1], px[i + 2]); }
        var panels = timeline[0].Panels!;
        var left = panels.Single(p => p.AssetId == "m");
        var right = panels.Single(p => p.AssetId == "b");
        var l = At(left.X + left.Width / 2.0, left.Y + left.Height / 2.0);
        var r = At(right.X + right.Width / 2.0, right.Y + right.Height / 2.0);
        Assert.True(l.R > 180 && l.G < 70 && l.B < 70, $"left box {l}");
        Assert.True(r.G > 150 && r.R < 70 && r.B < 70, $"right box {r}");
        var frame = At(right.X + right.Width / 2.0, right.Y - PipPlanner.SpeakerFrame / 2.0);   // speaker 2 = camera A
        Assert.True(frame.R > 200 && frame.G > 160 && frame.B < 90, $"speaker frame {frame}");
        var noFrame = At(left.X + left.Width / 2.0, left.Y - PipPlanner.SpeakerFrame / 2.0);
        Assert.False(noFrame.R > 200 && noFrame.G > 160 && noFrame.B < 90, $"non-speaker frame {noFrame}");
        var band = AssWriter.TickerBand(P13, canvas)!.Value;
        var strip = At(canvas.Width * 0.6, band.Top + 3);
        Assert.True(strip.B > strip.R && strip.B > strip.G && strip.B > 40, $"ticker strip {strip}");
    }
}
