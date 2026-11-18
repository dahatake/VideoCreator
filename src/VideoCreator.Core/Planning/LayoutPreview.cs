using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Rendering;

namespace VideoCreator.Planning;

/// <summary>
/// Where each video goes on the canvas for a preset and the project's current roles, as a short looping storyboard for the UI
/// (which video fills the frame, which is the inset, when it switches). The rectangles come from the planners' and the renderer's own code
/// (<see cref="ScreenLayout"/>, <see cref="PipPlanner.PlaceInset"/>, <see cref="PipPlanner.SwitchFrame"/>, <see cref="TelopLayout.Project"/>),
/// the timing from the preset (hook, minimum hold, wide shot), and the asset choice follows the pipeline's rules, so the sketch shows what
/// a run would do. Content-dependent parts (where a face is, when the screen changes) use the cached analysis or a typical value.
/// </summary>
public static class LayoutPreview
{
    /// <summary>A picture on the canvas. <see cref="AssetId"/> null = no such video yet (illustration). Backdrop = the blurred copy behind a smaller picture.</summary>
    public sealed record Box(string Slot, string? AssetId, string Shape, int X, int Y, int W, int H, bool Backdrop = false);
    /// <summary>One state of the layout. Kind: single | hook | pip | talk | fallback | screen | static | wide | cam.</summary>
    public sealed record Beat(string Kind, double Sec, List<Box> Boxes, int Speaker = 0);
    /// <summary>FaceBasis: measured (cached face detections) | typical (assumed) | "" (faces do not affect this layout).</summary>
    public sealed record Result(string PresetId, string Planner, int Width, int Height, Box? Captions, List<Beat> Beats, List<string> Issues, string FaceBasis);

    static readonly MediaInfo Camera = new() { Width = 1080, Height = 1920, Duration = 60, AudioChannels = 1 };
    static readonly MediaInfo Desktop = new() { Width = 1920, Height = 1080, Duration = 60 };
    /// <summary>Normalised face of a typical vertical talking-head shot: centred, in the upper third.</summary>
    public static readonly FaceBox TypicalFace = new(0.36, 0.2, 0.28, 0.16, 1);

    public static Result For(Preset p, IReadOnlyList<SourceAsset> assets, IReadOnlyList<FaceSample>? mainFaces = null, Canvas? canvas = null)
    {
        var c = canvas ?? new Canvas();
        var issues = new List<string>();
        var main = assets.FirstOrDefault(a => a.Role == AssetRole.Main) ?? assets.FirstOrDefault() ?? Placeholder("main", Camera);
        if (assets.Count > 1 && assets.All(a => a.Role != AssetRole.Main)) issues.Add("implicit_main");
        Box? captions = p.HasTelop("caption") && AssWriter.Band(TelopType.Caption, p, c) is { } band
            ? new Box("captions", null, "rect", 0, (int)band.Top, c.Width, (int)Math.Ceiling(band.Bottom - band.Top)) : null;
        var beats = new List<Beat>(); string basis = "";
        switch (p.Planner)
        {
            case "pip": basis = Pip(p, assets, main, mainFaces, c, beats, issues); break;
            case "screen": Screen(p, assets, main, c, beats, issues); break;
            case "switch": Switch(p, assets, main, c, beats, issues); break;
            default:
                beats.Add(new Beat("single", 3, Picture(main, "main", CameraFrame(main), c)));
                if (assets.Count > 1) issues.Add("only_main");
                break;
        }
        return new Result(p.Id, p.Planner, c.Width, c.Height, captions, beats, issues, basis);
    }

    static string Pip(Preset p, IReadOnlyList<SourceAsset> assets, SourceAsset main, IReadOnlyList<FaceSample>? faces, Canvas c, List<Beat> beats, List<string> issues)
    {
        var cfg = p.Screen ?? new PresetScreen();
        // the pipeline's rule: exactly one asset other than the main one, with the role "screen"
        var screens = assets.Where(a => a.Id != main.Id && a.Role == AssetRole.Screen).ToList();
        if (assets.Count > 1 && screens.Count == 0) issues.Add("need_screen");
        if (screens.Count > 1) issues.Add("many_screens");
        if (screens.Count == 1 && screens[0].PipScenes.Count == 0) issues.Add("no_scenes");
        var screen = screens.FirstOrDefault() ?? Placeholder("screen", Desktop);
        var slide = screen.Deck?.Slides.FirstOrDefault();
        var windows = screens.Count == 1 && screen.PipScenes.Count > 0 ? PipPlanner.SceneWindows(main, screen) : [];
        var face = RepresentativeFace(faces, windows);
        string basis = face != null ? "measured" : "typical";
        face ??= TypicalFace;
        var clip = new Clip { ClipId = "preview", AssetId = main.Id, SrcIn = 0, SrcOut = 4, Reframe = CameraFrame(main) };
        var samples = Enumerable.Range(0, 9).Select(i => new FaceSample(i * 0.5, [face])).ToList();
        var inset = PipPlanner.PlaceInset(clip, main, screen, samples, p, c, null, slide);
        var whole = Picture(main, "main", CameraFrame(main), c);
        double hold = Math.Max(cfg.MinHoldSec, 2.5);
        beats.Add(new Beat("hook", Math.Max(1, cfg.HookPresenterSec), whole));
        if (inset != null)
            beats.Add(new Beat("pip", hold, [.. Picture(main, "main", inset.Main, c),
                new Box("screen", Id(screen), "rect", inset.Pip.X, inset.Pip.Y, inset.Pip.Width, inset.Pip.Height)]));
        else issues.Add("no_place");
        beats.Add(new Beat("talk", Math.Max(cfg.MinHoldSec, 2), whole));
        var media = slide is { Width: > 0, Height: > 0 } ? screen.Media with { Width = slide.Width, Height = slide.Height } : screen.Media;
        beats.Add(new Beat("fallback", Math.Max(cfg.MinHoldSec, 2), Picture(screen with { Media = media }, "screen", PipPlanner.SwitchFrame(screen, p, c, slide), c)));
        return basis;
    }

    static void Screen(Preset p, IReadOnlyList<SourceAsset> assets, SourceAsset main, Canvas c, List<Beat> beats, List<string> issues)
    {
        var cfg = p.Screen ?? new PresetScreen();
        // ScreenPlanner.Detect: the asset set to 画面, else the stillest recording (only known after analysis)
        var screen = assets.FirstOrDefault(a => a.Role == AssetRole.Screen);
        if (screen == null && assets.Count > 1) { issues.Add("auto_screen"); screen = assets.First(a => a.Id != main.Id); }
        screen ??= Placeholder("screen", Desktop);
        var presenter = (main.Id != screen.Id ? main : assets.FirstOrDefault(a => a.Id != screen.Id && a.Role != AssetRole.AudioMaster)) ?? Placeholder("presenter", Camera);
        var lay = ScreenLayout.For(c, p);
        var (x, y) = lay.Corner("bl");
        double hold = Math.Max(cfg.MinHoldSec, 2.5);
        beats.Add(new Beat("hook", Math.Max(1, cfg.HookPresenterSec), Picture(presenter, "presenter", CameraFrame(presenter), c)));
        beats.Add(new Beat("screen", hold, [.. Picture(screen, "screen", new ReframeSpec { Mode = "screen", BoxY = lay.BoxY, BoxH = lay.BoxH }, c),
            new Box("presenter", Id(presenter), "circle", x, y, lay.Ring, lay.Ring)]));
        beats.Add(new Beat("static", Math.Max(cfg.MinHoldSec, 2), Picture(presenter, "presenter", CameraFrame(presenter), c)));
    }

    static void Switch(Preset p, IReadOnlyList<SourceAsset> assets, SourceAsset main, Canvas c, List<Beat> beats, List<string> issues)
    {
        var sw = p.Switching ?? new PresetSwitching();
        var ordered = assets.OrderBy(a => a.Id == main.Id ? 0 : 1).ToList();
        var wide = ordered.FirstOrDefault(a => a.Role == AssetRole.Wide);
        // PipelineRunner.AutoAssignSpeakersAsync: with no speaker set, the cameras' microphones decide (shown here in pipeline order)
        var mics = ordered.Where(a => a.Role != AssetRole.Wide && a.Media.AudioChannels > 0).ToList();
        bool auto = mics.Count >= 2 && mics.All(a => a.Speaker == null);
        var cams = new SortedDictionary<int, SourceAsset>();
        if (auto) { issues.Add("auto_speaker"); for (int i = 0; i < mics.Count; i++) cams.TryAdd(i + 1, mics[i]); }
        else foreach (var a in ordered.Where(a => SwitchPlanner.SpeakerOf(a) > 0)) cams.TryAdd(SwitchPlanner.SpeakerOf(a), a);
        if (assets.Count < 2) { cams.TryAdd(1, main); cams.TryAdd(2, Placeholder("cam_b", Camera)); }
        else if (cams.Count < 2) issues.Add("one_camera");
        double hold = Math.Max(sw.MinHoldSec, 2.5);
        if (wide != null && sw.WideOpenSec > 0) beats.Add(new Beat("wide", sw.WideOpenSec, Picture(wide, "wide", CameraFrame(wide), c)));
        foreach (var (speaker, cam) in cams.Take(4))
            beats.Add(new Beat("cam", hold, Picture(cam, Slot(cam), CameraFrame(cam), c), speaker));
        if (beats.Count == 0) beats.Add(new Beat("single", 3, Picture(main, "main", CameraFrame(main), c)));
    }

    static string Slot(SourceAsset a) => a.Id.StartsWith('~') ? a.Id[1..] : a.Role switch
    {
        AssetRole.Main => "main", AssetRole.CamA => "cam_a", AssetRole.CamB => "cam_b", AssetRole.Wide => "wide", AssetRole.Screen => "screen", _ => "audio_master",
    };

    /// <summary>The camera framing every multi-source planner uses: a vertical source fits the canvas, a horizontal one sits over its blurred copy.</summary>
    static ReframeSpec CameraFrame(SourceAsset a) => new() { Mode = a.Media.Aspect < 0.9 ? "fit" : "blur_bg" };

    /// <summary>The source's picture on the canvas as the renderer places it (the whole frame projected like a face box), plus the blurred backdrop when it does not fill the canvas.</summary>
    static List<Box> Picture(SourceAsset a, string slot, ReframeSpec r, Canvas c)
    {
        var clip = new Clip { ClipId = "preview", AssetId = a.Id, SrcIn = 0, SrcOut = 1, Reframe = r };
        var f = TelopLayout.Project(new FaceBox(0, 0, 1, 1, 1), clip, a.Media, c, 0);
        double pad = TelopLayout.FacePadding;
        double top = r.Mode == "screen" ? r.BoxY : 0, bottom = r.Mode == "screen" ? r.BoxY + r.BoxH : c.Height;
        int x0 = (int)Math.Round(Math.Max(0, f.Left + pad)), y0 = (int)Math.Round(Math.Max(top, f.Top + pad));
        int x1 = (int)Math.Round(Math.Min(c.Width, f.Right - pad)), y1 = (int)Math.Round(Math.Min(bottom, f.Bottom - pad));
        var boxes = new List<Box>();
        bool fills = x0 <= 1 && y0 <= 1 && x1 >= c.Width - 1 && y1 >= c.Height - 1;
        if (!fills && r.Mode != "fit") boxes.Add(new Box(slot, Id(a), "rect", 0, 0, c.Width, c.Height, Backdrop: true));
        boxes.Add(new Box(slot, Id(a), "rect", x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0)));
        return boxes;
    }

    /// <summary>
    /// The face to plan the inset around: per sample the largest detection, then the median of each edge, preferring samples inside the
    /// selected scenes (master time). Null when nothing has been detected yet.
    /// </summary>
    public static FaceBox? RepresentativeFace(IReadOnlyList<FaceSample>? samples, IReadOnlyList<Interval> windows)
    {
        if (samples == null) return null;
        var largest = samples.Where(s => s.Boxes.Count > 0).Select(s => (s.T, Box: s.Boxes.MaxBy(b => b.W * b.H)!)).ToList();
        var inScenes = largest.Where(x => windows.Any(w => x.T >= w.T0 && x.T <= w.T1)).ToList();
        var use = inScenes.Count > 0 ? inScenes : largest;
        if (use.Count == 0) return null;
        static double Median(IEnumerable<double> v) { var s = v.Order().ToList(); return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2; }
        return new FaceBox(Median(use.Select(x => x.Box.X)), Median(use.Select(x => x.Box.Y)), Median(use.Select(x => x.Box.W)), Median(use.Select(x => x.Box.H)), 1);
    }

    static SourceAsset Placeholder(string slot, MediaInfo media) => new() { Id = "~" + slot, ProjectId = "", Path = "", Sha256 = "", Media = media, Role = slot == "screen" ? AssetRole.Screen : AssetRole.Main };
    static string? Id(SourceAsset a) => a.Id.StartsWith('~') ? null : a.Id;
}
