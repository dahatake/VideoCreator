using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Planning;

/// <summary>Keep the user's main camera behind a whole-screen rectangular inset. All decisions use master time; eligibility uses screen time.</summary>
public static class PipPlanner
{
    /// <summary>Master time at which the screen recording shows its own time <paramref name="t"/> (inverse of CameraTime).</summary>
    public static double MasterTime(SourceAsset main, SourceAsset screen, double t)
    {
        double m = (t - screen.SyncOffset) / (1 + screen.SyncDriftPpm * 1e-6);
        for (int i = 0; i < 4; i++) m -= screen.CameraTime(m, main.Id) - t;   // exact for a line; follows a measured delay map
        return m;
    }

    /// <summary>The selected scenes of every screen in master time, clipped to the master recording.</summary>
    public static List<Interval> SceneWindows(SourceAsset main, IEnumerable<SourceAsset> screens) =>
        CandidateValidator.MergeOverlaps(screens.SelectMany(screen => screen.EffectiveScenes
            .Select(s => new Interval(Math.Max(0, MasterTime(main, screen, Math.Max(0, s.T0))), Math.Min(main.Media.Duration, MasterTime(main, screen, Math.Min(screen.Media.Duration, s.T1)))))
            .Where(w => w.T1 > w.T0)).ToList(), 0);

    /// <summary>The selected screen scenes in master time, clipped to the main recording.</summary>
    public static List<Interval> SceneWindows(SourceAsset main, SourceAsset screen) => SceneWindows(main, [screen]);

    /// <summary>
    /// A PiP candidate is only useful where the conversation overlaps a selected scene: rank candidates by that overlap and drop
    /// those without a showable stretch. When the selector found none, build one around the scenes on sentence boundaries,
    /// opening on the presenter (HookPresenterSec) before the first scene.
    /// </summary>
    public static (List<Candidate> Candidates, bool Built) PreferScenes(IReadOnlyList<Candidate> cands, IReadOnlyList<Interval> windows, Transcript tr, SignalSet sig, Preset p, double duration)
    {
        var cfg = p.Screen ?? new PresetScreen();
        double Overlap(Candidate c) => c.Segments.Sum(s => windows.Sum(w => Math.Max(0, Math.Min(s.T1, w.T1) - Math.Max(s.T0, w.T0))));
        var kept = cands.Where(c => Overlap(c) >= cfg.MinHoldSec).OrderByDescending(Overlap).ThenBy(c => c.Rank).Select((c, i) => c with { Rank = i + 1 }).ToList();
        if (kept.Count > 0 || windows.Count == 0 || tr.Sentences.Count == 0) return (kept, false);
        double t0 = windows[0].T0 - cfg.HookPresenterSec - cfg.LeadSec, t1 = Math.Max(windows[^1].T1 + cfg.TailSec, t0 + p.Target.Min);
        if (t1 > duration) { t0 -= t1 - duration; t1 = duration; }
        t0 = Math.Max(0, t0);
        // whole sentences only: start at the sentence that is running at t0, end with the last one that starts before t1
        var first = tr.Sentences.FirstOrDefault(s => s.T1 > t0) ?? tr.Sentences[0];
        var last = tr.Sentences.LastOrDefault(s => s.T0 < t1) ?? tr.Sentences[^1];
        var iv = new Interval(Math.Max(0, Math.Min(first.T0, t0)), Math.Min(duration, Math.Max(last.T1, Math.Min(t1, duration))));
        while (iv.Length > p.Target.Max && tr.Sentences.FirstOrDefault(s => s.T0 > iv.T0 + 0.05 && s.T0 < windows[0].T0 - cfg.HookPresenterSec) is { } later)
            iv = iv with { T0 = later.T0 };
        var raw = new RawCandidate([iv], "", "", [], "副画面の選択シーンと重なる会話から作りました", 5, 5, 5, 5);
        return ([CandidateValidator.Normalize(raw, tr, sig, duration, "pip_01", "pip") with { Rank = 1 }], true);
    }
    public static List<Clip> Plan(IReadOnlyList<Clip> speech, Transcript tr, SourceAsset main, SourceAsset screen, Preset preset, Canvas canvas, List<string> notes,
        IReadOnlyList<FaceSample>? faces = null) => Plan(speech, tr, main, main, [screen], preset, canvas, notes, faces);

    /// <summary>
    /// The master (audio source, time spine) may differ from the recording that shows the speaker (<paramref name="presenter"/>); any number of screens
    /// may be shown, one at a time. Eligibility of each screen comes from its scenes (selected by the user, else chosen from its activity).
    /// </summary>
    public static List<Clip> Plan(IReadOnlyList<Clip> speech, Transcript tr, SourceAsset main, SourceAsset presenter, IReadOnlyList<SourceAsset> screens, Preset preset, Canvas canvas, List<string> notes,
        IReadOnlyList<FaceSample>? faces = null)
    {
        if (screens.All(s => s.EffectiveScenes.Count == 0))
            throw new PlanException("E-PIP-SCENES", "副画面の使用シーンがありません。画面の録画に動きがあるか、使うシーンを選んでください");
        if (tr.Sentences.Count == 0)
            throw new PlanException("E-PIP-SPEECH", "会話に合わせた副画面の表示には、発話時刻付きの書き起こしが必要です");
        foreach (var screen in screens)
        {
            double rate = 1 + screen.SyncDriftPpm * 1e-6;
            if (!double.IsFinite(rate) || rate <= 0) throw new PlanException("E-PIP-SYNC", "副画面の同期情報が不正です");
        }
        var cfg = preset.Screen ?? new PresetScreen();
        var result = new List<Clip>();
        InsetLayout? previous = null;
        double outputOffset = 0, slideSeconds = 0, shrunkSeconds = 0;
        foreach (var clip in speech)
        {
            var raw = new List<(Interval W, int S)>();
            for (int si = 0; si < screens.Count; si++)
            foreach (var scene in screens[si].EffectiveScenes)
            foreach (var sentence in tr.Sentences)
            {
                double lo = Math.Max(Math.Max(clip.MasterIn, MasterTime(main, screens[si], Math.Max(0, scene.T0))), sentence.T0 - cfg.LeadSec);
                double hi = Math.Min(Math.Min(clip.MasterOut, MasterTime(main, screens[si], Math.Min(screens[si].Media.Duration, scene.T1))), sentence.T1 + cfg.TailSec);
                // Always introduce the speaker before an inset, including when a candidate starts mid-recording.
                lo = Math.Max(lo, clip.MasterIn + Math.Max(0, cfg.HookPresenterSec - outputOffset) * clip.Speed);
                if (hi > lo) raw.Add((new(lo, hi), si));
            }
            var merged = Resolve(raw).Where(w => w.W.Length / clip.Speed >= cfg.MinHoldSec).ToList();
            var baseCuts = merged.SelectMany(w => new[] { w.W.T0, w.W.T1 }.Concat(screens[w.S].SyncJumps(w.W.T0, w.W.T1)))
                .Concat(presenter.Id == main.Id ? [] : presenter.SyncJumps(clip.MasterIn, clip.MasterOut))
                .Append(clip.MasterIn).Append(clip.MasterOut).Distinct().Order().ToList();
            // a slide change inside a shown stretch starts a new piece (unless it would leave a sliver)
            var slideCuts = new List<double>();
            foreach (var screen in screens.Where(s => s.Deck != null))
                foreach (var t in screen.SlideSpans.SelectMany(s => new[] { s.T0, s.T1 }).Select(t => MasterTime(main, screen, t)).Order())
                    if (merged.Any(w => screens[w.S] == screen && t > w.W.T0 && t < w.W.T1) && baseCuts.Concat(slideCuts).All(c => Math.Abs(c - t) >= 0.5)) slideCuts.Add(t);
            var cuts = baseCuts.Concat(slideCuts).Order().ToList();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double lo = cuts[i], hi = cuts[i + 1];
                var owner = merged.Where(w => lo >= w.W.T0 - 1e-9 && hi <= w.W.T1 + 1e-9).Select(w => screens[w.S]).FirstOrDefault();
                bool show = owner != null;
                int pieces = show ? Math.Max(1, (int)Math.Ceiling((hi - lo) / clip.Speed / 6)) : 1;
                for (int k = 0; k < pieces; k++)
                {
                    double a = lo + (hi - lo) * k / pieces, b = lo + (hi - lo) * (k + 1) / pieces;
                    // the speaker's recording may not cover this stretch (started late, ended early): the master's own picture stands in
                    double p0 = presenter.CameraTime(a, main.Id);
                    bool presenting = p0 >= -0.01 && p0 + (b - a) <= presenter.Media.Duration + 0.01;
                    var cam = presenting ? presenter : main;
                    double c0 = cam.CameraTime(a, main.Id);
                    var item = clip with
                    {
                        ClipId = $"c{result.Count + 1}", AssetId = cam.Id, SrcIn = c0, SrcOut = c0 + b - a, AudioIn = a,
                        Reframe = ReframeSpec.Default(cam.Media, canvas),
                        Zoom = 1, TransitionSec = 0, Pip = null,
                    };
                    if (show)
                    {
                        var screen = owner!;
                        double s0 = screen.CameraTime(a, main.Id);
                        var slide = SlideDecks.At(screen, screen.CameraTime((a + b) / 2, main.Id));
                        if (slide != null) slideSeconds += (b - a) / clip.Speed;
                        var layout = presenting ? PlaceInset(item, presenter, screen, faces, preset, canvas, previous, slide) : null;
                        previous = layout;
                        if (layout != null)
                        {
                            item = item with { Reframe = layout.Main, Pip = layout.Pip with { SrcIn = s0 } };
                            if (layout.Main.FgScale > 0) shrunkSeconds += item.Length;
                        }
                        else item = item with { AssetId = screen.Id, SrcIn = s0, SrcOut = s0 + b - a, Reframe = SwitchFrame(screen, preset, canvas, slide) };
                    }
                    result.Add(item);
                }
            }
            outputOffset += clip.Length;
        }
        var screenIds = screens.Select(s => s.Id).ToHashSet();
        double pipSeconds = result.Where(c => c.Pip != null).Sum(c => c.Length);
        double switchedSeconds = result.Where(c => c.Pip == null && screenIds.Contains(c.AssetId)).Sum(c => c.Length);
        double seconds = pipSeconds + switchedSeconds;
        if (seconds == 0) throw new PlanException("E-PIP-SCENES", "選んだ副画面シーンと会話の重なる区間がありません。同期と使用区間を確認してください");
        notes.Add($"顔と字幕を避ける位置・サイズで資料 PiP {pipSeconds:F1} 秒。安全な配置がない・顔位置が不明な区間は資料へ全画面切り替え {switchedSeconds:F1} 秒。選択シーンと発話が重なる範囲だけ使用します。意味の対応と文字の読みやすさはプレビューで確認してください");
        if (screens.Any(s => s.Deck != null))
            notes.Add(slideSeconds > 0
                ? $"資料のうち {slideSeconds:F1} 秒は、録画の代わりに PowerPoint から書き出した高解像度スライドを表示します（録画とスライドの一致を画素比較で確認した区間のみ）"
                : "登録したスライドと一致する録画区間がないため、資料は録画のまま表示します");
        if (shrunkSeconds > 0)
            notes.Add($"顔が字幕や資料と重ならないよう、資料表示中の {shrunkSeconds:F1} 秒は主映像を {cfg.MainMinScale:P0} まで縮小して上に寄せています。" +
                      "撮影時に顔を画面の上半分に収め、下側に余白を取ると、主映像を縮小せずに大きな資料を重ねられます");
        return result;
    }

    /// <summary>
    /// Windows of every screen as one non-overlapping timeline: a screen already showing keeps the frame while its window lasts; otherwise the earlier
    /// screen (in the order given) wins. Adjacent windows of the same screen join.
    /// </summary>
    public static List<(Interval W, int S)> Resolve(IReadOnlyList<(Interval W, int S)> windows)
    {
        var cuts = windows.SelectMany(w => new[] { w.W.T0, w.W.T1 }).Distinct().Order().ToList();
        var result = new List<(Interval W, int S)>();
        int current = -1;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            double a = cuts[i], b = cuts[i + 1], mid = (a + b) / 2;
            var covering = windows.Where(w => mid > w.W.T0 && mid < w.W.T1).Select(w => w.S).Distinct().Order().ToList();
            if (covering.Count == 0) { current = -1; continue; }
            int s = covering.Contains(current) ? current : covering[0];
            current = s;
            if (result.Count > 0 && result[^1].S == s && result[^1].W.T1 >= a - 1e-6) result[^1] = (result[^1].W with { T1 = b }, s);
            else result.Add((new Interval(a, b), s));
        }
        return result;
    }

    /// <summary>
    /// Screen-only frame when no inset fits: the content (or the slide image) as large as fits above the captions, at its own aspect ratio and centred
    /// in that area. A cropped content box is shown at exactly its aspect (the screen layout stretches a crop to the box); content taller than the
    /// area is widened with its surroundings rather than squeezed.
    /// </summary>
    public static ReframeSpec SwitchFrame(SourceAsset screen, Preset preset, Canvas canvas, SlideImage? slide = null)
    {
        const int top = 24;
        int bottom = preset.HasTelop("caption") && AssWriter.Band(TelopType.Caption, preset, canvas) is { } band ? (int)band.Top - 24 : canvas.Height - 24;
        int avail = (bottom - top) & ~1;
        if (slide != null) return new ReframeSpec { Mode = "screen", BoxY = top, BoxH = avail, Image = slide.Path };
        if (screen.PipContent is not { W: > 0, H: > 0 } box) return new ReframeSpec { Mode = "screen", BoxY = top, BoxH = avail };
        int h = Math.Min(avail, (int)Math.Round(canvas.Width * box.H / (double)box.W) & ~1);
        int ch = box.H, cy = box.Y;
        int cw = (int)Math.Round(ch * canvas.Width / (double)h) & ~1;
        if (cw > screen.Media.Width)
        {
            // narrower frame than needed: keep the full width and trim the content height symmetrically
            cw = screen.Media.Width & ~1;
            ch = (int)Math.Round(cw * h / (double)canvas.Width) & ~1;
            cy = box.Y + (box.H - ch) / 2;
        }
        // a wider crop than the box takes in its surroundings (centred, kept inside the frame); a narrower one is centred on the box
        int cx = Math.Clamp(box.X + (box.W - cw) / 2, 0, Math.Max(0, screen.Media.Width - cw));
        return new ReframeSpec
        {
            Mode = "screen", BoxY = (top + (avail - h) / 2) & ~1, BoxH = h,
            CropW = cw, CropH = ch, CropY = cy, Track = [new TrackPoint(0, cx)],
        };
    }

    /// <summary>The inset and the main picture under it.</summary>
    public sealed record InsetLayout(PipSpec Pip, ReframeSpec Main);

    /// <summary>
    /// One stable layout covers the swept face bounds of the entire run. Joint search: a portrait main may shrink (top-anchored, down to
    /// <see cref="PresetScreen.MainMinScale"/>) so the face clears the caption band and leaves room for a larger inset; each step down costs a little,
    /// so a full-size main wins unless shrinking buys a clearly larger inset. Missing detections fail closed (null = screen-only switch).
    /// </summary>
    public static InsetLayout? PlaceInset(Clip clip, SourceAsset main, SourceAsset screen, IReadOnlyList<FaceSample>? faces, Preset preset, Canvas canvas,
        InsetLayout? previous = null, SlideImage? slide = null)
    {
        var samples = faces?.Where(s => s.T >= clip.SrcIn - 0.5 && s.T <= clip.SrcOut + 0.5).OrderBy(s => s.T).ToList();
        if (samples == null || samples.Count == 0 || samples[0].T > clip.SrcIn + 0.5 || samples[^1].T < clip.SrcOut - 0.5
            || samples.Any(s => s.Boxes.Count == 0) || samples.Zip(samples.Skip(1)).Any(p => p.Second.T - p.First.T > 0.75))
            return null;
        var cfg = preset.Screen ?? new PresetScreen();
        TelopLayout.Rect? captions = preset.HasTelop("caption") && AssWriter.Band(TelopType.Caption, preset, canvas) is { } band
            ? new(0, band.Top - 24, canvas.Width, band.Bottom + 24) : null;
        const int margin = 24;
        var content = slide == null ? screen.PipContent : null;
        double aspect = slide is { Width: > 0, Height: > 0 } ? slide.Width / (double)slide.Height
            : content is { W: > 0, H: > 0 } ? content.W / (double)content.H : screen.Media.Aspect;
        var mains = new List<(double Scale, ReframeSpec Reframe)> { (1, clip.Reframe) };
        if (main.Media.Aspect < 0.9 && !canvas.IsLandscape)
            foreach (double s in new[] { 0.85, cfg.MainMinScale }.Where(s => s >= cfg.MainMinScale && s is > 0 and < 0.999).Distinct().OrderDescending())
                mains.Add((s, new ReframeSpec { Mode = "blur_bg", FgScale = s, FgOffsetY = 0 }));
        InsetLayout? best = null; double bestValue = double.NegativeInfinity;
        foreach (var (mainScale, reframe) in mains)
        {
            var c = clip with { Reframe = reframe };
            var projected = samples.SelectMany(s => s.Boxes.Select(f => TelopLayout.Project(f, c, main.Media, canvas, (s.T - c.SrcIn) / c.Speed))).ToList();
            var face = new TelopLayout.Rect(projected.Min(f => f.Left), projected.Min(f => f.Top), projected.Max(f => f.Right), projected.Max(f => f.Bottom));
            var obstacles = new List<TelopLayout.Rect> { face };
            if (captions != null) obstacles.Add(captions);
            // Below 72% of canvas width a whole desktop/slide is too small: prefer a readable full-screen switch.
            // widths are relative to the short side, so a landscape canvas keeps the same inset size (~1000 px) instead of covering the frame
            foreach (double scale in new[] { 0.94, 0.88, 0.80, 0.72 }.Select(x => x * Math.Min(canvas.Width, canvas.Height) / canvas.Width))
            {
                double value = scale - 0.1 * (1 - mainScale) - (captions != null && face.Intersects(captions) ? 0.03 : 0);
                int w = (int)(canvas.Width * scale) & ~1, h = (int)(w / aspect) & ~1;
                if (previous != null && Math.Abs(previous.Main.FgScale - reframe.FgScale) < 1e-9 && previous.Pip.Width == w) value += 0.02;
                if (value <= bestValue) continue;
                var xs = new[] { previous?.Pip.X ?? margin, margin, canvas.Width - margin - w }
                    .Concat(obstacles.SelectMany(o => new[] { (int)Math.Ceiling(o.Right + margin), (int)Math.Floor(o.Left - margin - w) }));
                var ys = new[] { previous?.Pip.Y ?? canvas.Height - margin - h, canvas.Height - margin - h, margin }
                    .Concat(obstacles.SelectMany(o => new[] { (int)Math.Ceiling(o.Bottom + margin), (int)Math.Floor(o.Top - margin - h) }));
                PipSpec? found = null;
                foreach (int y in ys.Select(y => y & ~1).Distinct())
                {
                    foreach (int x in xs.Select(x => x & ~1).Distinct())
                    {
                        if (x < margin || y < margin || x + w > canvas.Width - margin || y + h > canvas.Height - margin) continue;
                        var box = new TelopLayout.Rect(x, y, x + w, y + h);
                        if (obstacles.Any(o => o.Intersects(box))) continue;
                        found = new PipSpec
                        {
                            AssetId = screen.Id, Shape = "rectangle", X = x, Y = y, Width = w, Height = h, Border = 0, Image = slide?.Path,
                            CropX = content?.X ?? 0, CropY = content?.Y ?? 0, CropW = content?.W ?? 0, CropH = content?.H ?? 0,
                        };
                        break;
                    }
                    if (found != null) break;
                }
                if (found == null) continue;
                best = new InsetLayout(found, reframe); bestValue = value;
            }
        }
        return best;
    }
}
