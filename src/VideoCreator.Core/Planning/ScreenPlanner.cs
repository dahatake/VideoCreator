using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Planning;

/// <summary>The screen recording, the presenter's camera (null = none), the screen's activity and detail maps (screen time).</summary>
public sealed record ScreenContext(string ScreenAssetId, string? PresenterAssetId, IReadOnlyList<ActivitySample> Activity, IReadOnlyList<DetailSample>? Details = null);

/// <summary>Evidence for telling a screen recording from a camera. FaceRatio &lt; 0 = not measured (no face model).</summary>
public sealed record SourceProfile(string AssetId, double StaticRatio, double FaceRatio);

public static class ScreenRoles
{
    /// <summary>A camera is practically never still for 30% of half-second steps; a screen recording usually is for more than half.</summary>
    public const double MinStaticRatio = 0.3;

    /// <summary>Screen = the asset set to 画面 by the user, else the stillest one (≥ <see cref="MinStaticRatio"/>), faces counting against it.
    /// Presenter = the remaining asset that shows a face most often (the master when faces are unknown).</summary>
    public static (string? Screen, string? Presenter, string Reason) Classify(IReadOnlyList<SourceAsset> assets, IReadOnlyDictionary<string, SourceProfile> profiles)
    {
        string Desc(SourceAsset a) => profiles.TryGetValue(a.Id, out var p)
            ? $"{System.IO.Path.GetFileName(a.Path)}: 静止 {p.StaticRatio:P0}・顔 {(p.FaceRatio < 0 ? "未解析" : p.FaceRatio.ToString("P0"))}" : $"{System.IO.Path.GetFileName(a.Path)}: 未解析";
        double Score(SourceAsset a) => profiles[a.Id].StaticRatio - 0.5 * Math.Max(0, profiles[a.Id].FaceRatio);
        var screen = assets.FirstOrDefault(a => a.Role == AssetRole.Screen)
            ?? assets.Where(a => profiles.TryGetValue(a.Id, out var p) && p.StaticRatio >= MinStaticRatio).OrderByDescending(Score).FirstOrDefault();
        var evidence = string.Join("、", assets.Select(Desc));
        if (screen == null) return (null, null, $"画面収録らしい素材（動きの少ない映像）が見つかりませんでした（{evidence}）");
        var presenter = assets.Where(a => a.Id != screen.Id)
            .OrderByDescending(a => profiles.TryGetValue(a.Id, out var p) ? p.FaceRatio : -1).ThenBy(a => a.Role == AssetRole.Main ? 0 : 1).FirstOrDefault();
        var how = screen.Role == AssetRole.Screen ? "役割の指定" : "動きの少なさと顔の有無";
        return (screen.Id, presenter?.Id, $"画面: {System.IO.Path.GetFileName(screen.Path)}、話し手: {(presenter != null ? System.IO.Path.GetFileName(presenter.Path) : "なし")}（{how}で判定。{evidence}）");
    }
}

/// <summary>Where the screen and the PiP go on the canvas: the screen band ends above the captions, the PiP sits in a corner of the band.</summary>
public sealed record ScreenLayout(int W, int H, int BoxY, int BoxH, int D, int Border, int Margin)
{
    public int Ring => D + 2 * Border;
    public double BoxAspect => (double)W / BoxH;

    /// <summary>
    /// The band sits between the headline (top, whole video) and the emphasis/caption telops (bottom), so no telop ever covers screen content;
    /// without a headline it starts near the top. The PiP is at most 42% of the band's height so it never hides most of it.
    /// </summary>
    public static ScreenLayout For(Canvas c, Preset p)
    {
        var cap = p.Telop.Caption; var geo = Rendering.AssWriter.Geo(p, c);
        int top = p.HasTelop("headline") ? geo.HeadlineMarginV + 60 + 2 * 14 + 24 : (int)Math.Round(c.Height * 0.0833);
        int textTop = p.HasTelop("emphasis")
            ? c.Height - (geo.CaptionMarginV + Rendering.AssWriter.EmphLift) - (int)(cap.FontPx * 1.25) - 9
            : c.Height - geo.CaptionMarginV - cap.FontPx * cap.MaxLines - 7;
        int boxY = top & ~1;
        int boxH = Math.Max((int)Math.Round(c.Width * 9.0 / 16), textTop - 16 - boxY) & ~1;
        // a 16:9 band is the whole landscape canvas: the screen fills it and the telops sit over its lower edge
        if (c.IsLandscape) { boxY = 0; boxH = c.Height & ~1; }
        int d = (int)Math.Min((p.Screen ?? new PresetScreen()).PipDiameter, boxH * 0.42) & ~1;
        return new ScreenLayout(c.Width, c.Height, boxY, boxH, d, Math.Max(4, d / 45) & ~1, Math.Max(16, c.Width / 45));
    }

    public static readonly string[] Corners = ["bl", "br", "tr", "tl"];   // preference on ties: bottom-left (clear of a phone UI's right rail)

    public (int X, int Y) Corner(string k) => k switch
    {
        "tl" => (Margin, BoxY + Margin),
        "tr" => (W - Margin - Ring, BoxY + Margin),
        "bl" => (Margin, BoxY + BoxH - Margin - Ring),
        _ => (W - Margin - Ring, BoxY + BoxH - Margin - Ring),
    };
}

/// <summary>
/// P10: screen recording + presenter. Master audio time is the spine (as in <see cref="SwitchPlanner"/>). On a 0.25 s grid the screen is wanted while it
/// changes (from <see cref="PresetScreen.LeadSec"/> before to <see cref="PresetScreen.TailSec"/> after) or while the sentence points at it ("ここ", "クリック"…);
/// a still, unmentioned screen for <see cref="PresetScreen.PresenterAfterStaticSec"/> hands the frame to the presenter. Runs shorter than the minimum hold are
/// absorbed and switches move onto the existing jump cuts nearby, so the layout never flickers.
/// </summary>
public static class ScreenPlanner
{
    public const double Step = 0.25;
    /// <summary>Switches within this distance of a jump cut move onto it (one cut instead of two).</summary>
    public const double SnapSec = 1.0;
    static readonly string[] Deictic = ["ここ", "こちら", "この画面", "画面", "クリック", "押し", "押す", "選択", "入力", "表示", "開い", "開き", "見て", "ボタン", "メニュー", "タブ",
        "ウィンドウ", "スライド", "右上", "左上", "右下", "左下", "コード", "設定", "ドラッグ", "スクロール"];

    public static bool PointsAtScreen(string text) => Deictic.Any(text.Contains);

    sealed record Cell(int Clip, double M0, double M1, double Out0, double Out1, bool Want, bool Active, bool ScreenIn, bool PresIn)
    {
        public bool Screen { get; set; }
        public bool Forced { get; set; }
    }

    public static List<Clip> Plan(IReadOnlyList<Clip> speech, Transcript tr, IReadOnlyList<SourceAsset> assets, SourceAsset master, ScreenContext ctx,
        IReadOnlyDictionary<string, List<FaceSample>> faces, Preset p, Canvas canvas, List<string> notes)
    {
        var cfg = p.Screen ?? new PresetScreen();
        var byId = assets.ToDictionary(a => a.Id);
        if (!byId.TryGetValue(ctx.ScreenAssetId, out var screen)) { notes.Add("画面収録の素材が見つからないため、基準映像だけで編集しました"); return speech.ToList(); }
        var presenter = ctx.PresenterAssetId != null && byId.TryGetValue(ctx.PresenterAssetId, out var pr) ? pr : null;
        var act = ctx.Activity;

        // 1. grid cells on the output timeline
        var cells = new List<Cell>(); double outT = 0;
        var sentences = tr.Sentences.OrderBy(s => s.T0).ToList();
        for (int ci = 0; ci < speech.Count; ci++)
        {
            var sc = speech[ci];
            for (double m = sc.MasterIn; m < sc.MasterOut - 1e-6; m += Step)
            {
                double b = Math.Min(sc.MasterOut, m + Step), mid = (m + b) / 2;
                double s0 = screen.CameraTime(m, master.Id), s1 = screen.CameraTime(b, master.Id);
                bool screenIn = s0 >= -0.01 && s1 <= screen.Media.Duration + 0.01;
                bool presIn = presenter != null && presenter.CameraTime(m, master.Id) >= -0.01 && presenter.CameraTime(b, master.Id) <= presenter.Media.Duration + 0.01;
                bool active = screenIn && ScreenActivity.AnyActive(act, s0 - cfg.TailSec, s1 + cfg.LeadSec);
                bool deictic = sentences.FirstOrDefault(s => s.T0 <= mid && s.T1 >= mid) is { } st && PointsAtScreen(st.Text);
                double len = (b - m) / sc.Speed;
                cells.Add(new Cell(ci, m, b, outT, outT + len, active || deictic, active, screenIn, presIn));
                outT += len;
            }
        }
        if (cells.Count == 0) return speech.ToList();

        // 2. states: screen by default; a long enough unwanted stretch (and the hook) go to the presenter
        foreach (var c in cells) c.Screen = true;
        for (int i = 0; i < cells.Count;)
        {
            if (cells[i].Want) { i++; continue; }
            int j = i; while (j < cells.Count && !cells[j].Want) j++;
            if (cells[j - 1].Out1 - cells[i].Out0 >= cfg.PresenterAfterStaticSec) for (int k = i; k < j; k++) cells[k].Screen = false;
            i = j;
        }
        foreach (var c in cells.Where(c => c.Out0 < cfg.HookPresenterSec && !c.Active)) c.Screen = false;
        foreach (var c in cells)
        {
            if (!c.ScreenIn && c.PresIn) { c.Screen = false; c.Forced = true; }
            else if (!c.PresIn) { c.Screen = c.ScreenIn; c.Forced = true; }
        }

        // 3. runs on the output timeline; absorb short ones, then move switches onto nearby jump cuts
        var runs = Runs(cells);
        for (bool again = true; again && runs.Count > 1;)
        {
            again = false;
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].T1 - runs[i].T0 >= cfg.MinHoldSec || runs[i].Forced) continue;
                int j = i > 0 ? i - 1 : i + 1; int lo = Math.Min(i, j), hi = Math.Max(i, j);
                runs[lo] = (runs[lo].T0, runs[hi].T1, runs[j].Screen, runs[lo].Forced || runs[hi].Forced); runs.RemoveAt(hi);
                for (int k = lo; k > 0 && runs[k].Screen == runs[k - 1].Screen; k--) { runs[k - 1] = (runs[k - 1].T0, runs[k].T1, runs[k].Screen, runs[k - 1].Forced || runs[k].Forced); runs.RemoveAt(k); }
                again = true; break;
            }
        }
        var cuts = new List<double>(); double acc = 0;
        foreach (var sc in speech) { acc += sc.Length; cuts.Add(acc); }
        for (int i = 1; i < runs.Count; i++)
        {
            double t = runs[i].T0;
            // moving a switch so the screen stays longer (or comes earlier) is harmless, so it may move up to the minimum hold to avoid a short fragment
            bool Ok(double x) => (Math.Abs(x - t) <= SnapSec || Math.Abs(x - t) <= cfg.MinHoldSec && (runs[i - 1].Screen ? x > t : x < t))
                && x - runs[i - 1].T0 >= cfg.MinHoldSec * 0.75 && runs[i].T1 - x >= cfg.MinHoldSec * 0.75;
            var near = cuts.Where(Ok).OrderBy(x => Math.Abs(x - t)).FirstOrDefault(-1);
            if (near >= 0 && !runs[i].Forced && !runs[i - 1].Forced) { runs[i - 1] = runs[i - 1] with { T1 = near }; runs[i] = runs[i] with { T0 = near }; }
        }
        foreach (var c in cells)
        {
            double mid = (c.Out0 + c.Out1) / 2;
            var r = runs.First(x => mid <= x.T1 + 1e-9);
            c.Screen = c.Forced ? c.Screen : r.Screen;
            if (c.Screen && !c.ScreenIn) c.Screen = false;
            if (!c.Screen && !c.PresIn && c.ScreenIn) c.Screen = true;
        }

        // 4. clips: split each speech clip where the state changes
        var lay = ScreenLayout.For(canvas, p);
        var res = new List<(Clip Clip, bool Screen)>();
        foreach (var g in cells.GroupBy(c => c.Clip))
        {
            var sc = speech[g.Key];
            var list = g.ToList();
            for (int i = 0; i < list.Count;)
            {
                int j = i; while (j < list.Count && list[j].Screen == list[i].Screen) j++;
                double m0 = list[i].M0, m1 = list[j - 1].M1;
                bool scr = list[i].Screen;
                var asset = scr ? screen : presenter != null && list[i].PresIn ? presenter : master;
                double s0 = asset.CameraTime(m0, master.Id);
                res.Add((sc with
                {
                    AssetId = asset.Id, SrcIn = Math.Round(s0, 3), SrcOut = Math.Round(s0 + (m1 - m0), 3), AudioIn = Math.Round(m0, 3),
                    Reframe = scr ? new ReframeSpec { Mode = "screen", BoxY = lay.BoxY, BoxH = lay.BoxH } : ReframeSpec.Default(asset.Media, canvas),
                    Pip = null,
                }, scr && presenter != null && list[i].PresIn));
                i = j;
            }
        }

        // 5. framing per run of consecutive screen clips: one zoom level and one PiP corner per run, the pan follows the activity
        var clips = res.Select(x => x.Clip).ToList();
        int framedRuns = 0, fitRuns = 0, groupShown = 0, groupMissed = 0;
        for (int i = 0; i < clips.Count;)
        {
            if (clips[i].Reframe.Mode != "screen") { i++; continue; }
            int j = i; while (j < clips.Count && clips[j].Reframe.Mode == "screen") j++;
            bool fit = Frame(clips, i, j, screen, act, lay, cfg);
            if (fit) fitRuns++; else framedRuns++;
            var withPip = Enumerable.Range(i, j - i).Where(k => res[k].Screen).ToList();
            if (presenter != null && withPip.Count > 0)
            {
                PlacePip(clips, withPip, screen, presenter, master, act, ctx.Details ?? [], faces.GetValueOrDefault(presenter.Id), lay, out var crowd);
                if (crowd.Shown >= 2) groupShown = Math.Max(groupShown, crowd.Shown);
                else if (crowd.Seen >= 2) groupMissed = Math.Max(groupMissed, crowd.Seen);
            }
            i = j;
        }
        if (groupShown >= 2) notes.Add($"話し手のワイプに {groupShown} 人をまとめて表示しました");
        if (groupMissed >= 2) notes.Add($"話し手のカメラに {groupMissed} 人映っていますが、離れていてワイプに収まらないため、最も大きく映る 1 人を表示しました");
        // contiguous pieces of one presenter stretch (split only by the grid) join again
        var merged = new List<Clip>();
        foreach (var c in clips)
        {
            if (merged.Count > 0 && merged[^1].AssetId == c.AssetId && merged[^1].Reframe.Mode != "screen" && c.Reframe.Mode != "screen" && merged[^1].Role == c.Role
                && Math.Abs(merged[^1].MasterOut - c.MasterIn) < 0.002 && Math.Abs(merged[^1].SrcOut - c.SrcIn) < 0.002)
                merged[^1] = merged[^1] with { SrcOut = c.SrcOut };
            else merged.Add(c);
        }
        double scrSec = merged.Where(c => c.Reframe.Mode == "screen").Sum(c => c.Length), all = merged.Sum(c => c.Length);
        notes.Add($"画面 {scrSec:F1}秒（話し手のワイプ付き）／話し手 {all - scrSec:F1}秒。画面は操作している所へ自動でズーム（{framedRuns} 区間）" + (fitRuns > 0 ? $"、画面全体の変化は全体表示（{fitRuns} 区間）" : ""));
        return merged.Select((c, k) => c with { ClipId = $"c{k + 1}" }).ToList();
    }

    static List<(double T0, double T1, bool Screen, bool Forced)> Runs(List<Cell> cells)
    {
        var runs = new List<(double T0, double T1, bool Screen, bool Forced)>();
        foreach (var c in cells)
        {
            if (runs.Count > 0 && runs[^1].Screen == c.Screen) runs[^1] = (runs[^1].T0, c.Out1, c.Screen, runs[^1].Forced || c.Forced);
            else runs.Add((c.Out0, c.Out1, c.Screen, c.Forced));
        }
        return runs;
    }

    /// <summary>
    /// Zoom and pan for screen clips [i, j). The crop has the box's shape; its size covers the typical (75th percentile) changing region with a margin,
    /// within [width / MaxZoom, the largest crop that fits]. When the changes usually span more than that (slide changes, window switches) the whole
    /// screen is shown instead. Returns true for the whole-screen fit.
    /// </summary>
    static bool Frame(List<Clip> clips, int i, int j, SourceAsset screen, IReadOnlyList<ActivitySample> act, ScreenLayout lay, PresetScreen cfg)
    {
        int sw = screen.Media.Width, sh = screen.Media.Height;
        double maxCw = Math.Min(sw, sh * lay.BoxAspect), minCw = Math.Min(maxCw, Math.Max(lay.W / cfg.MaxZoom, 0.3 * sw));
        var samples = Enumerable.Range(i, j - i).SelectMany(k => ScreenActivity.Between(act, clips[k].SrcIn - cfg.TailSec, clips[k].SrcOut + cfg.LeadSec)).Where(s => s.Active).ToList();
        var lastBefore = ScreenActivity.LastActive(act, clips[i].SrcIn, 30);
        if (samples.Count == 0 && lastBefore != null) samples.Add(lastBefore);
        bool fit = samples.Count == 0;
        int cw = 0, ch = 0;
        if (!fit)
        {
            var need = samples.Select(s => Math.Max((s.X1 - s.X0) * sw * 1.3, (s.Y1 - s.Y0) * sh * 1.3 * lay.BoxAspect)).OrderBy(x => x).ToList();
            double p75 = need[(int)Math.Floor(0.75 * (need.Count - 1))];
            fit = p75 > maxCw * 1.2 && sw > maxCw * 1.05;
            cw = (int)Math.Round(Math.Clamp(p75, minCw, maxCw)) & ~1;
            ch = Math.Min(sh, (int)Math.Round(cw / lay.BoxAspect)) & ~1;
        }
        if (fit)
        {
            for (int k = i; k < j; k++) clips[k] = clips[k] with { Reframe = clips[k].Reframe with { CropW = 0, CropH = 0, Track = null, TrackY = null } };
            return true;
        }
        (double X, double Y) cur = lastBefore != null ? (lastBefore.Cx * sw, lastBefore.Cy * sh) : samples.Count > 0 ? (samples[0].Cx * sw, samples[0].Cy * sh) : (sw / 2.0, sh / 2.0);
        for (int k = i; k < j; k++)
        {
            var c = clips[k];
            var (tx, ty, end) = Pan(act, c.SrcIn, c.SrcOut, c.Speed, sw, sh, cw, ch, cur, cfg.LeadSec);
            cur = end;
            clips[k] = c with { Reframe = c.Reframe with { CropW = cw, CropH = ch, Track = tx, TrackY = ty, CropY = (int)Math.Round(ty[0].X) } };
        }
        return false;
    }

    public const double MaxPanPxPerSec = 900, SmoothSec = 1.0;

    /// <summary>Crop top-left over clip time: centre on the latest changing region (looking <paramref name="lead"/> ahead), smoothed and speed-limited, clamped inside the screen.</summary>
    static (List<TrackPoint> X, List<TrackPoint> Y, (double X, double Y) End) Pan(IReadOnlyList<ActivitySample> act, double s0, double s1, double speed, int sw, int sh, int cw, int ch, (double X, double Y) start, double lead)
    {
        double fps = ScreenActivity.Fps;
        int n = Math.Max(2, (int)Math.Ceiling((s1 - s0) * fps) + 1);
        var cx = new double[n]; var cy = new double[n]; var cur = start;
        for (int i = 0; i < n; i++)
        {
            double t = Math.Min(s1, s0 + i / fps);
            var near = ScreenActivity.Between(act, t - 0.25, t + lead).Where(s => s.Active).ToList();
            if (near.Count > 0) cur = (near.Average(s => s.Cx) * sw, near.Average(s => s.Cy) * sh);
            cx[i] = cur.X; cy[i] = cur.Y;
        }
        int half = Math.Max(1, (int)Math.Round(SmoothSec * fps / 2));
        double[] Smooth(double[] v, double lo, double hi)
        {
            var o = new double[n];
            for (int i = 0; i < n; i++) { int a = Math.Max(0, i - half), b = Math.Min(n - 1, i + half); o[i] = v[a..(b + 1)].Average(); }
            double step = MaxPanPxPerSec / fps;
            for (int i = 1; i < n; i++) o[i] = o[i - 1] + Math.Clamp(o[i] - o[i - 1], -step, step);
            for (int i = 0; i < n; i++) o[i] = Math.Clamp(o[i], lo, hi);
            return o;
        }
        var x = Smooth(cx.Select(v => v - cw / 2.0).ToArray(), 0, sw - cw);
        var y = Smooth(cy.Select(v => v - ch / 2.0).ToArray(), 0, sh - ch);
        List<TrackPoint> Keys(double[] v)
        {
            var k = Enumerable.Range(0, n).Select(i => (t: Math.Min(s1 - s0, i / fps) / speed, x: v[i])).ToList();
            if (k.Max(p => p.x) - k.Min(p => p.x) < 12) return [new TrackPoint(0, Math.Round(k.Average(p => p.x), 1))];
            return FaceTrack.Simplify(k, FaceTrack.SimplifyTol).Select(p => new TrackPoint(Math.Round(p.t, 3), Math.Round(p.x, 1))).ToList();
        }
        return (Keys(x), Keys(y), (x[^1] + cw / 2.0, y[^1] + ch / 2.0));
    }

    /// <summary>
    /// One PiP for a run: the corner whose square, mapped back onto the screen through the zoom/pan, covers the least of (a) the localised changes and
    /// (b) the detailed parts of the picture (text, UI) — so it never hides where the user works nor a slide's title. The presenter crop is
    /// <see cref="PresenterCrop"/> (everyone who keeps appearing on the camera, or the upper centre when no face was measured).
    /// </summary>
    static void PlacePip(List<Clip> clips, List<int> idx, SourceAsset screen, SourceAsset presenter, SourceAsset master, IReadOnlyList<ActivitySample> act, IReadOnlyList<DetailSample> details, List<FaceSample>? faces, ScreenLayout lay,
        out PresenterFraming crowd)
    {
        int sw = screen.Media.Width, sh = screen.Media.Height;
        var cost = ScreenLayout.Corners.ToDictionary(k => k, _ => 0.0);
        int n = 0;
        foreach (var k in idx)
        {
            var c = clips[k]; var r = c.Reframe;
            foreach (var s in ScreenActivity.Between(act, c.SrcIn, c.SrcOut))
            {
                double t = (s.T - c.SrcIn) / c.Speed, scale, ox, oy;
                if (r.CropW > 0) { scale = (double)lay.W / r.CropW; ox = -FaceTrack.XAt(r.Track!, t) * scale; oy = lay.BoxY - FaceTrack.XAt(r.TrackY!, t) * scale; }
                else { scale = (double)lay.W / sw; ox = 0; oy = lay.BoxY + (lay.BoxH - sh * scale) / 2; }
                n++;
                foreach (var corner in ScreenLayout.Corners)
                {
                    var (px, py) = lay.Corner(corner);
                    // the PiP square in normalised screen coordinates at this moment
                    double nx0 = (px - ox) / scale / sw, nx1 = (px + lay.Ring - ox) / scale / sw, ny0 = (py - oy) / scale / sh, ny1 = (py + lay.Ring - oy) / scale / sh;
                    double overlap = 0;
                    if (s.Active && !s.Global)
                    {
                        double ix = Math.Max(0, Math.Min(s.X1, nx1) - Math.Max(s.X0, nx0)), iy = Math.Max(0, Math.Min(s.Y1, ny1) - Math.Max(s.Y0, ny0));
                        overlap = ix * iy / Math.Max(1e-9, (nx1 - nx0) * (ny1 - ny0));
                    }
                    cost[corner] += 2 * overlap + ScreenActivity.DetailIn(details, s.T, Math.Max(0, nx0), Math.Max(0, ny0), Math.Min(1, nx1), Math.Min(1, ny1));
                }
            }
        }
        var best = ScreenLayout.Corners.OrderBy(k => Math.Round(cost[k] / Math.Max(1, n), 3)).First();
        var (x, y) = lay.Corner(best);

        var samples = new List<FaceSample>();
        if (faces != null)
            foreach (var k in idx)
            {
                double p0 = presenter.CameraTime(clips[k].MasterIn, master.Id), p1 = p0 + (clips[k].SrcOut - clips[k].SrcIn);
                samples.AddRange(faces.Where(f => f.T >= p0 - 0.5 && f.T <= p1 + 0.5));
            }
        var crop = PresenterCrop(samples, presenter.Media.Width, presenter.Media.Height);
        int S = crop.S, cx0 = crop.X, cy0 = crop.Y;
        crowd = crop;
        foreach (var k in idx)
        {
            var c = clips[k];
            clips[k] = c with { Pip = new PipSpec { AssetId = presenter.Id, SrcIn = Math.Round(presenter.CameraTime(c.MasterIn, master.Id), 3), CropX = cx0 & ~1, CropY = cy0 & ~1, CropS = S, X = x, Y = y, D = lay.D, Border = lay.Border, Corner = best } };
        }
    }

    /// <summary>The PiP's square crop in the presenter's source pixels; <see cref="Shown"/> &lt; <see cref="Seen"/> when the people on camera could not all fit.</summary>
    public sealed record PresenterFraming(int X, int Y, int S, int Shown, int Seen);

    /// <summary>A person counts when seen in this share of the sampled moments that have a face (passers-by and stray detections do not)…</summary>
    public const double MinPersonPresence = 0.3;
    /// <summary>…and when their face is at least this share of the largest face's height at that moment (people far in the background do not).</summary>
    public const double MinPersonFaceRatio = 0.5;
    /// <summary>The smallest face, as a share of the crop, that still reads in the circle (≈ 43 px of a 360 px PiP); a smaller group falls back to one person.</summary>
    public const double MinGroupFaceShare = 0.12;
    /// <summary>Heads must stay this far inside the circle (the white ring covers its edge).</summary>
    const double CircleSafe = 0.97;

    /// <summary>
    /// One person (or none measured): a head-and-shoulders square around the median largest face, as before. Several people who keep appearing:
    /// the smallest square whose inscribed circle — the PiP is round — holds every face (its oval with a little margin, over the spread of that
    /// person's positions), never tighter than one person's framing. When they are too far apart for a square of the frame's short side, or their
    /// faces would be smaller than <see cref="MinGroupFaceShare"/>, the most prominent person is framed alone and the caller says so.
    /// </summary>
    public static PresenterFraming PresenterCrop(IReadOnlyList<FaceSample> samples, int pw, int ph)
    {
        int side = Math.Min(pw, ph);
        var seen = samples.Where(s => s.Boxes.Count > 0).ToList();
        if (seen.Count == 0)
        {
            int s0 = (int)Math.Round(side * 0.8) & ~1;
            return new((pw - s0) / 2, (int)Math.Round(Math.Min(ph - s0, ph * 0.05)), s0, 0, 0);
        }
        var people = People(seen);
        if (people.Count <= 1) return Single(seen.Select(f => f.Boxes.MaxBy(b => b.W * b.H)!).ToList(), pw, ph, people.Count);

        // each face's oval with a little margin (forehead and hair may be clipped, the face may not); a face cut by the frame only needs its visible part
        var heads = people.Select(p => (
            L: Math.Max(0, Q(p.Select(b => (b.X - 0.1 * b.W) * pw), 0.15)), R: Math.Min(pw, Q(p.Select(b => (b.X + 1.1 * b.W) * pw), 0.85)),
            T: Math.Max(0, Q(p.Select(b => (b.Y - 0.2 * b.H) * ph), 0.15)), B: Math.Min(ph, Q(p.Select(b => (b.Y + 1.1 * b.H) * ph), 0.85)),
            H: Q(p.Select(b => b.H * ph), 0.5))).ToList();
        double cx = (heads.Min(h => h.L) + heads.Max(h => h.R)) / 2, cy = (heads.Min(h => h.T) + heads.Max(h => h.B)) / 2;
        double smallest = heads.Min(h => h.H);
        for (int s = (int)Math.Ceiling(Math.Min(side, Math.Max(2.6 * heads.Max(h => h.H), 0.35 * side))) & ~1; s <= side; s += 2)
        {
            if (smallest / s < MinGroupFaceShare) break;
            int x0 = (int)Math.Round(Math.Clamp(cx - s / 2.0, 0, pw - s)), y0 = (int)Math.Round(Math.Clamp(cy - s / 2.0, 0, ph - s));
            double ox = x0 + s / 2.0, oy = y0 + s / 2.0, r = CircleSafe * s / 2;
            bool inside = heads.All(h => Enumerable.Range(0, 16).Select(k => k * Math.PI / 8).All(a =>
                Math.Pow((h.L + h.R) / 2 + (h.R - h.L) / 2 * Math.Cos(a) - ox, 2) + Math.Pow((h.T + h.B) / 2 + (h.B - h.T) / 2 * Math.Sin(a) - oy, 2) <= r * r));
            if (inside) return new(x0, y0, s, people.Count, people.Count);
        }
        var lead = people.MaxBy(p => p.Count * Q(p.Select(b => b.W * b.H), 0.5))!;
        return Single(lead, pw, ph, people.Count);
    }

    static PresenterFraming Single(IReadOnlyList<FaceBox> boxes, int pw, int ph, int seen)
    {
        double fx = Q(boxes.Select(b => b.Cx), 0.5) * pw, fy = Q(boxes.Select(b => b.Cy), 0.5) * ph, fh = Q(boxes.Select(b => b.H), 0.5) * ph;
        int s = (int)Math.Round(Math.Clamp(2.6 * fh, 0.35 * Math.Min(pw, ph), Math.Min(pw, ph))) & ~1;
        return new((int)Math.Round(Math.Clamp(fx - s / 2.0, 0, pw - s)), (int)Math.Round(Math.Clamp(fy - 0.45 * s, 0, ph - s)), s, 1, Math.Max(1, seen));
    }

    /// <summary>Upper median for q = 0.5 (as the single-person framing always used); nearest rank otherwise.</summary>
    static double Q(IEnumerable<double> v, double q)
    {
        var o = v.Order().ToList();
        return o[q == 0.5 ? o.Count / 2 : (int)Math.Round(q * (o.Count - 1))];
    }

    /// <summary>Faces linked over time by position into people; only those who keep appearing, large enough, are returned (left to right).</summary>
    static List<List<FaceBox>> People(List<FaceSample> seen)
    {
        var tracks = new List<(List<FaceBox> Boxes, double[] At)>();   // At = smoothed cx, cy, w, h
        foreach (var s in seen)
        {
            double hmax = s.Boxes.Max(b => b.H);
            var taken = new HashSet<int>();
            foreach (var b in s.Boxes.Where(b => b.H >= MinPersonFaceRatio * hmax).OrderByDescending(b => b.W * b.H))
            {
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < tracks.Count; i++)
                {
                    var a = tracks[i].At;
                    double dx = Math.Abs(b.Cx - a[0]) / Math.Max(a[2], b.W), dy = Math.Abs(b.Cy - a[1]) / Math.Max(a[3], b.H);
                    if (taken.Contains(i) || dx > 0.75 || dy > 0.75 || dx + dy >= bd) continue;
                    best = i; bd = dx + dy;
                }
                if (best < 0) { tracks.Add(([b], [b.Cx, b.Cy, b.W, b.H])); taken.Add(tracks.Count - 1); continue; }
                tracks[best].Boxes.Add(b); taken.Add(best);
                var t = tracks[best].At; t[0] += 0.3 * (b.Cx - t[0]); t[1] += 0.3 * (b.Cy - t[1]); t[2] += 0.3 * (b.W - t[2]); t[3] += 0.3 * (b.H - t[3]);
            }
        }
        return tracks.Where(t => t.Boxes.Count >= MinPersonPresence * seen.Count).OrderBy(t => t.At[0]).Select(t => t.Boxes).ToList();
    }
}
