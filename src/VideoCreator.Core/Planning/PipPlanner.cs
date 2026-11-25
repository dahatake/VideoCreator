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
        IReadOnlyList<FaceSample>? faces = null, IReadOnlyList<SourceAsset>? anchors = null, IReadOnlyDictionary<string, List<FaceSample>>? anchorFaces = null)
    {
        anchors = (anchors ?? []).Take(MaxPanels).ToList();
        bool multi = anchors.Count >= 2;
        if (!multi && screens.All(s => s.EffectiveScenes.Count == 0))
            throw new PlanException("E-PIP-SCENES", "副画面の使用シーンがありません。画面の録画に動きがあるか、使うシーンを選んでください");
        if (tr.Sentences.Count == 0)
            throw new PlanException("E-PIP-SPEECH", "会話に合わせた副画面の表示には、発話時刻付きの書き起こしが必要です");
        foreach (var screen in screens)
        {
            double rate = 1 + screen.SyncDriftPpm * 1e-6;
            if (!double.IsFinite(rate) || rate <= 0) throw new PlanException("E-PIP-SYNC", "副画面の同期情報が不正です");
        }
        var cfg = preset.Screen ?? new PresetScreen();
        var turns = multi ? SpeakerTurns(tr, cfg.MinHoldSec) : [];
        var result = new List<Clip>();
        InsetLayout? previous = null;
        double outputOffset = 0, slideSeconds = 0, shrunkSeconds = 0, splitSeconds = 0;
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
                .Concat(anchors.Where(x => x.Id != main.Id && x.Id != presenter.Id).SelectMany(x => x.SyncJumps(clip.MasterIn, clip.MasterOut)))
                .Append(clip.MasterIn).Append(clip.MasterOut).Distinct().Order().ToList();
            // an anchor whose recording starts or ends inside the clip changes how many boxes the split screen has
            foreach (var x in multi ? anchors.Where(x => x.Id != main.Id) : [])
                foreach (var t in new[] { MasterTime(main, x, 0), MasterTime(main, x, x.Media.Duration) })
                    if (t > clip.MasterIn + 0.05 && t < clip.MasterOut - 0.05 && baseCuts.All(c => Math.Abs(c - t) >= 0.05)) baseCuts.Add(t);
            // a new speaker starts a new piece: the split screen frames them, an inset moves onto their camera
            foreach (var t in turns.Select(x => x.T0).Where(t => t > clip.MasterIn + 0.5 && t < clip.MasterOut - 0.5))
                if (baseCuts.All(c => Math.Abs(c - t) >= 0.5)) baseCuts.Add(t);
            baseCuts = baseCuts.Order().ToList();
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
                    // with several anchors the inset sits on the current speaker's camera
                    var speaking = multi ? SpeakingAnchor(anchors, turns, a, b) : null;
                    var self = speaking != null && Covers(speaking, main, a, b) ? speaking : presenter;
                    // the speaker's recording may not cover this stretch (started late, ended early): the master's own picture stands in
                    bool presenting = Covers(self, main, a, b);
                    var cam = presenting ? self : main;
                    double c0 = cam.CameraTime(a, main.Id);
                    var item = clip with
                    {
                        ClipId = $"c{result.Count + 1}", AssetId = cam.Id, SrcIn = c0, SrcOut = c0 + b - a, AudioIn = a,
                        Reframe = ReframeSpec.Default(cam.Media, canvas),
                        Zoom = 1, TransitionSec = 0, Pip = null, Panels = null,
                    };
                    if (show)
                    {
                        var screen = owner!;
                        double s0 = screen.CameraTime(a, main.Id);
                        var slide = SlideDecks.At(screen, screen.CameraTime((a + b) / 2, main.Id));
                        if (slide != null) slideSeconds += (b - a) / clip.Speed;
                        var camFaces = multi ? anchorFaces?.GetValueOrDefault(cam.Id) : faces;
                        var layout = presenting ? PlaceInset(item, cam, screen, camFaces, preset, canvas, previous, slide) : null;
                        previous = layout;
                        if (layout != null)
                        {
                            item = item with { Reframe = layout.Main, Pip = layout.Pip with { SrcIn = s0 } };
                            if (layout.Main.FgScale > 0) shrunkSeconds += item.Length;
                        }
                        else item = item with { AssetId = screen.Id, SrcIn = s0, SrcOut = s0 + b - a, Reframe = SwitchFrame(screen, preset, canvas, slide) };
                    }
                    else if (multi)
                    {
                        var shown = anchors.Where(x => Covers(x, main, a, b)).ToList();
                        if (shown.Count >= 2)
                        {
                            item = item with { Panels = SplitPanels(shown, speaking, main, a, b, anchorFaces, preset, canvas) };
                            splitSeconds += item.Length;
                        }
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
        if (seconds == 0 && !multi) throw new PlanException("E-PIP-SCENES", "選んだ副画面シーンと会話の重なる区間がありません。同期と使用区間を確認してください");
        if (multi)
            notes.Add(splitSeconds > 0
                ? $"キャスター {anchors.Count} 人の映像を、資料を出さない {splitSeconds:F1} 秒は分割画面（{anchors.Count switch { 2 => "左右 2 分割", 3 => "3 分割", _ => "2×2" }}）で並べ、話している人の枠を黄色で示しました。資料の表示中は話している人のカメラに資料を重ねます。話者の対応はプレビューで確認してください"
                : "複数のキャスター素材がありますが、同じ時刻を撮っている区間がないため分割画面にはしませんでした");
        if (seconds > 0)
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

    /// <summary>Most anchors shown side by side (2×2).</summary>
    public const int MaxPanels = 4;
    /// <summary>Outer margin and gap between split-screen boxes (canvas px); the speaker's box gets a frame of <see cref="SpeakerFrame"/> px.</summary>
    public const int PanelMargin = 24, PanelGap = 12, SpeakerFrame = 6;

    /// <summary>Does the recording cover master time [a, b]?</summary>
    static bool Covers(SourceAsset cam, SourceAsset main, double a, double b)
    {
        double p0 = cam.CameraTime(a, main.Id);
        return p0 >= -0.01 && p0 + (b - a) <= cam.Media.Duration + 0.01;
    }

    /// <summary>Speaker turns over the whole transcript (master time, contiguous); turns shorter than <paramref name="minHold"/> join a neighbour.</summary>
    public static List<(double T0, double T1, int Speaker)> SpeakerTurns(Transcript tr, double minHold)
    {
        var turns = new List<(double T0, double T1, int Speaker)>();
        foreach (var s in tr.Sentences.OrderBy(s => s.T0))
        {
            if (turns.Count > 0 && turns[^1].Speaker == s.Speaker) turns[^1] = (turns[^1].T0, s.T1, s.Speaker);
            else turns.Add((turns.Count == 0 ? s.T0 : Math.Max(turns[^1].T1, s.T0), s.T1, s.Speaker));
        }
        for (int i = 0; i + 1 < turns.Count; i++) turns[i] = (turns[i].T0, turns[i + 1].T0, turns[i].Speaker);
        for (bool again = true; again && turns.Count > 1;)
        {
            again = false;
            for (int i = 0; i < turns.Count; i++)
            {
                if (turns[i].T1 - turns[i].T0 >= minHold) continue;
                int j = i > 0 ? i - 1 : i + 1; int lo = Math.Min(i, j), hi = Math.Max(i, j);
                turns[lo] = (turns[lo].T0, turns[hi].T1, turns[j].Speaker); turns.RemoveAt(hi);
                again = true; break;
            }
        }
        return turns;
    }

    /// <summary>The anchor of the speaker who talks most in master time [a, b] (null when no anchor shows that speaker).</summary>
    static SourceAsset? SpeakingAnchor(IReadOnlyList<SourceAsset> anchors, List<(double T0, double T1, int Speaker)> turns, double a, double b)
    {
        var speaker = turns.Select(t => (t.Speaker, Overlap: Math.Min(b, t.T1) - Math.Max(a, t.T0))).Where(t => t.Overlap > 0)
            .OrderByDescending(t => t.Overlap).Select(t => (int?)t.Speaker).FirstOrDefault();
        return speaker is > 0 ? anchors.FirstOrDefault(x => AssetRoles.AnchorSpeaker(anchors, x) == speaker) : null;
    }

    /// <summary>
    /// Where the split screen may draw: below the top margin and above the captions (or the ticker, or the bottom margin), so no box is under a caption.
    /// </summary>
    public static (int Top, int Bottom) PanelArea(Preset preset, Canvas canvas)
    {
        int bottom = canvas.Height - PanelMargin;
        if (AssWriter.TickerBand(preset, canvas) is { } ticker) bottom = (int)ticker.Top - PanelGap;
        if (preset.HasTelop("caption") && AssWriter.Band(TelopType.Caption, preset, canvas) is { } band) bottom = Math.Min(bottom, (int)band.Top - PanelGap);
        return (PanelMargin, bottom & ~1);
    }

    /// <summary>
    /// One box per anchor recording that covers [a, b] (in anchor order): side by side on a landscape canvas, stacked on a portrait one, 2×2 for four.
    /// Each box is cover-cropped around the median face of its recording (no face: upper centre) and kept between 9:16 and 16:9; the speaker's box is framed.
    /// </summary>
    public static List<PipSpec> SplitPanels(IReadOnlyList<SourceAsset> shown, SourceAsset? speaking, SourceAsset main, double a, double b,
        IReadOnlyDictionary<string, List<FaceSample>>? faces, Preset preset, Canvas canvas)
    {
        int n = Math.Min(shown.Count, MaxPanels);
        var (top, bottom) = PanelArea(preset, canvas);
        int cols = n == 4 ? 2 : canvas.IsLandscape ? n : 1, rows = (n + cols - 1) / cols;
        int areaW = canvas.Width - 2 * PanelMargin, areaH = bottom - top;
        int cellW = (areaW - (cols - 1) * PanelGap) / cols, cellH = (areaH - (rows - 1) * PanelGap) / rows;
        cellW = Math.Min(cellW, (int)(cellH * 16.0 / 9)) & ~1;
        cellH = Math.Min(cellH, (int)(cellW * 16.0 / 9)) & ~1;
        int gridW = cols * cellW + (cols - 1) * PanelGap, gridH = rows * cellH + (rows - 1) * PanelGap;
        int x0 = (canvas.Width - gridW) / 2, y0 = top + (areaH - gridH) / 2;
        var res = new List<PipSpec>();
        for (int i = 0; i < n; i++)
        {
            var cam = shown[i];
            double s0 = cam.CameraTime(a, main.Id);
            int srcW = cam.Media.Width, srcH = cam.Media.Height;
            double aspect = cellW / (double)cellH;
            int cropW = (int)Math.Min(srcW, srcH * aspect) & ~1, cropH = Math.Min(srcH, (int)Math.Round(cropW / aspect)) & ~1;
            double fx = 0.5 * srcW, fy = 0.4 * srcH;
            // one framing per anchor for the whole edit (a box that re-frames at every cut reads as a jump): the median face of the recording
            var centres = (faces?.GetValueOrDefault(cam.Id) ?? []).Where(s => s.Boxes.Count > 0)
                .Select(s => s.Boxes.MaxBy(f => f.W * f.H)!).Select(f => ((f.X + f.W / 2) * srcW, (f.Y + f.H / 2) * srcH)).ToList();
            if (centres.Count > 0)
            {
                fx = centres.Select(c => c.Item1).Order().ElementAt(centres.Count / 2);
                fy = centres.Select(c => c.Item2).Order().ElementAt(centres.Count / 2);
            }
            int cx = Math.Clamp((int)Math.Round(fx - cropW / 2.0), 0, Math.Max(0, srcW - cropW)) & ~1;
            int cy = Math.Clamp((int)Math.Round(fy - cropH * 0.4), 0, Math.Max(0, srcH - cropH)) & ~1;
            int col = i % cols, row = i / cols;
            res.Add(new PipSpec
            {
                AssetId = cam.Id, SrcIn = Math.Round(s0, 3), Shape = "panel",
                X = x0 + col * (cellW + PanelGap), Y = y0 + row * (cellH + PanelGap), Width = cellW, Height = cellH,
                CropX = cx, CropY = cy, CropW = cropW, CropH = cropH,
                Border = speaking != null && cam.Id == speaking.Id ? SpeakerFrame : 0,
            });
        }
        return res;
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
