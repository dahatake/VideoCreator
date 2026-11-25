using VideoCreator.Faces;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Rendering;

/// <summary>Stable placement over the entire telop span, using source faces projected through the actual edit.</summary>
public static class TelopLayout
{
    public sealed record Rect(double Left, double Top, double Right, double Bottom)
    {
        public bool Intersects(Rect other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

    /// <param name="Hidden">Body-time spans in which the telop is not drawn (shorts title bar: the cuts where a face is under its fixed place).</param>
    public sealed record Placement(Rect? Bounds, int FontSize, bool Positioned, string? Reason = null, IReadOnlyList<(double Start, double End)>? Hidden = null);
    public const double FacePadding = 32, PopScale = 1.12;

    public static Dictionary<string, Placement> Resolve(EditPlan plan, Preset p)
    {
        var result = new Dictionary<string, Placement>();
        var occupied = new List<(Telop Telop, Rect Bounds)>();
        int w = plan.Canvas.Width, h = plan.Canvas.Height;
        var geo = AssWriter.Geo(p, plan.Canvas);
        var captionBand = AssWriter.Band(TelopType.Caption, p, plan.Canvas)!.Value;
        var captionBounds = new Rect(0, captionBand.Top - 32, w, captionBand.Bottom + 32);
        // the shorts title bar never moves, so it is placed before the telops that move around it
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Type == TelopType.Caption ? 0 : p.Telop.IsShort && t.Type == TelopType.Headline ? 1 : 2).ThenBy(t => t.Start).ThenBy(t => t.Type))
        {
            var (size, outline, scaleX) = AssWriter.Metrics(t.Style, p);
            int fit = AssWriter.FitFontSize(t.Text, size, outline, w, geo.MarginR, scaleX, plan.Language, geo.MarginL);
            var band = AssWriter.Band(t.Type, p, plan.Canvas);
            double height = fit * 1.5 + 2 * outline + 12;
            double top = band?.Top ?? (t.Type == TelopType.Sfx ? geo.SfxY - height / 2 : geo.HeadlineMarginV);
            var original = new Rect(geo.MarginL, top, w - geo.MarginR, band?.Bottom ?? top + height);
            if (t.Type == TelopType.Caption && p.Telop.Caption.AvoidFaces && plan.TelopFaces != null)
            {
                result[t.Id] = CaptionSlot(plan, p, t, original, fit);
                if (result[t.Id].Positioned && result[t.Id].Bounds is { } moved) occupied.Add((t, moved));
                continue;
            }
            if (t.Type == TelopType.Caption || plan.TelopFaces == null)
            {
                result[t.Id] = new(original, fit, false);
                continue;
            }
            if (p.Telop.IsShort && t.Type == TelopType.Headline)
            {
                var (barTop, barBottom) = AssWriter.Short.BarRows(geo, fit);
                var bar = result[t.Id] = TitleBar(plan, t, original with { Top = barTop, Bottom = barBottom }, fit);
                // later telops must not be pushed up under the bar where it is drawn
                if (bar.Bounds is { } barRect)
                    foreach (var (vs, ve) in Visible(bar, t.Start, t.End)) occupied.Add((t with { Start = vs, End = ve }, barRect));
                continue;
            }
            var faces = Obstacles(plan, t, out bool known);
            if (!known)
            {
                result[t.Id] = new(null, fit, true, "顔の解析情報が不足しているため表示しません");
                continue;
            }
            // Reserve animation overshoot on all sides instead of shrinking the face-protection margin.
            // The shorts look tries smaller sizes before giving up: a big face in a selfie leaves no room for a 200 px punch telop or the title bar.
            var blocked = faces.Concat(occupied.Where(x => x.Telop.Start < t.End && x.Telop.End > t.Start).Select(x => x.Bounds)).ToList();
            if (plan.Telops.Any(c => c.Enabled && c.Type == TelopType.Caption && c.Start < t.End && c.End > t.Start)) blocked.Add(captionBounds);
            var screenOffset = 0.0;
            foreach (var clip in plan.Timeline)
            {
                if (screenOffset < t.End && screenOffset + clip.Length > t.Start && clip.Reframe.Mode == "screen")
                    blocked.Add(new(0, clip.Reframe.BoxY, w, clip.Reframe.BoxY + clip.Reframe.BoxH));
                screenOffset += clip.Length;
            }
            Rect? bounds = null;
            foreach (double scale in p.Telop.IsShort ? ShrinkSteps : [1.0])
            {
                int px = (int)Math.Round(size * scale);
                if (scale < 1 && px < MinShrunkPx) break;
                fit = AssWriter.FitFontSize(t.Text, px, outline + 6, w, geo.MarginR, (int)Math.Ceiling(scaleX * PopScale), plan.Language, geo.MarginL);
                height = fit * 1.5 * PopScale + 2 * outline + 16;
                double min = geo.Safe.Top, max = h - geo.Safe.Bottom - height;
                var candidates = new[] { Math.Clamp(top, min, Math.Max(min, max)), min, max }
                    .Concat(blocked.SelectMany(b => new[] { b.Bottom + 8, b.Top - height - 8 }))
                    .Where(y => y >= min && y <= max).Distinct().OrderBy(y => Math.Abs(y - top));
                double hgt = height;
                bounds = candidates.Select(y => new Rect(geo.MarginL, y, w - geo.MarginR, y + hgt))
                    .FirstOrDefault(b => blocked.All(o => !b.Intersects(o)));
                if (bounds != null) break;
            }
            result[t.Id] = new(bounds, fit, true, bounds == null ? "顔・字幕と重ならない表示場所がないため表示しません" : null);
            if (bounds != null) occupied.Add((t, bounds));
        }
        return result;
    }

    /// <summary>Shorts look: sizes tried in turn when a telop has no free place at full size; never below <see cref="MinShrunkPx"/>.</summary>
    static readonly double[] ShrinkSteps = [1.0, 0.8, 0.65, 0.5];
    public const int MinShrunkPx = 48;

    /// <summary>
    /// Shorts title bar: it stays at its fixed place at the top (an editor never moves it), and is left out only in the cuts where a face —
    /// or unknown faces — would be under it. Hidden everywhere = not shown, with the reason.
    /// </summary>
    static Placement TitleBar(EditPlan plan, Telop t, Rect bar, int fit)
    {
        var hidden = new List<(double Start, double End)>();
        double off = 0;
        foreach (var c in plan.Timeline)
        {
            double s = Math.Max(t.Start, off), e = Math.Min(t.End, off + c.Length);
            off += c.Length;
            if (e - s <= 1e-6) continue;
            var faces = Obstacles(plan, t with { Start = s, End = e }, out bool known);
            if (!known || faces.Any(f => f.Intersects(bar)))
            {
                if (hidden.Count > 0 && Math.Abs(hidden[^1].End - s) < 1e-6) hidden[^1] = (hidden[^1].Start, e);
                else hidden.Add((s, e));
            }
        }
        double shown = t.End - t.Start - hidden.Sum(h => h.End - h.Start);
        if (shown < 0.5) return new(null, fit, false, "顔と重ならずに上部のタイトル帯を出せるカットがないため表示しません");
        return new(bar, fit, false, Hidden: hidden.Count > 0 ? hidden : null);
    }

    /// <summary>The spans of [start, end) in which a telop with this placement is drawn.</summary>
    public static List<(double Start, double End)> Visible(Placement placement, double start, double end)
    {
        var res = new List<(double, double)>();
        double at = start;
        foreach (var (hs, he) in (placement.Hidden ?? []).OrderBy(h => h.Start))
        {
            if (hs > at + 1e-6) res.Add((at, Math.Min(hs, end)));
            at = Math.Max(at, he);
        }
        if (end > at + 1e-6) res.Add((at, end));
        return res;
    }

    /// <summary>
    /// A caption stays on its band unless a face (or an inset) is there over its span; then it moves to the nearest free height inside the
    /// safe zone, else (when allowed) anywhere on the canvas. Unknown faces or no free height keep the band (QA S-05 reports an overlap).
    /// </summary>
    static Placement CaptionSlot(EditPlan plan, Preset p, Telop t, Rect band, int fit)
    {
        var faces = Obstacles(plan, t, out bool known);
        if (!known || !faces.Any(f => f.Intersects(band))) return new(band, fit, false);
        int w = plan.Canvas.Width, h = plan.Canvas.Height;
        double height = band.Bottom - band.Top;
        Rect? Search(double min, double max) => new[] { Math.Clamp(band.Top, min, Math.Max(min, max)), min, max }
            .Concat(faces.SelectMany(b => new[] { b.Bottom + 8, b.Top - height - 8 }))
            .Where(y => y >= min && y <= max).Distinct().OrderBy(y => Math.Abs(y - band.Top))
            .Select(y => new Rect(band.Left, y, band.Right, y + height))
            .FirstOrDefault(r => faces.All(o => !r.Intersects(o)));
        var safe = AssWriter.Geo(p, plan.Canvas).Safe;
        var slot = Search(safe.Top, h - safe.Bottom - height)
                   ?? (p.Telop.Caption.AllowOutsideSafe ? Search(24, h - 24 - height) : null);
        return slot == null ? new(band, fit, false) : new(slot, fit, true);
    }

    public static List<Rect> Obstacles(EditPlan plan, Telop telop, out bool known)
    {
        var result = new List<Rect>(); known = true;
        double off = 0;
        for (int i = 0; i < plan.Timeline.Count; i++)
        {
            var c = plan.Timeline[i];
            double fade = i + 1 < plan.Timeline.Count ? plan.Timeline[i + 1].TransitionSec : 0;
            double end = off + c.Length + fade;
            if (off < telop.End && end > telop.Start)
            {
                if (c.Panels is { Count: > 0 } panels)
                {
                    // split screen: the background is a blurred copy, so only the faces inside the boxes count; a box without face data is protected whole
                    double lo = Math.Max(0, telop.Start - off), hi = Math.Min(c.Length + fade, telop.End - off);
                    foreach (var pn in panels)
                    {
                        var box = new Rect(pn.X - pn.Border, pn.Y - pn.Border, pn.X + pn.Width + pn.Border, pn.Y + pn.Height + pn.Border);
                        double s0 = pn.SrcIn + lo * c.Speed, s1 = pn.SrcIn + hi * c.Speed;
                        var samples = plan.TelopFaces != null && plan.TelopFaces.TryGetValue(pn.AssetId, out var ps)
                            ? ps.Samples.Where(s => s.T >= s0 - 0.5 && s.T <= s1 + 0.5).OrderBy(s => s.T).ToList() : null;
                        if (samples == null || samples.Count == 0 || samples[0].T > s0 + 0.5 || samples[^1].T < s1 - 0.5
                            || samples.Zip(samples.Skip(1)).Any(pair => pair.Second.T - pair.First.T > 0.75) || pn.CropW <= 0 || pn.CropH <= 0)
                        {
                            result.Add(box);
                            continue;
                        }
                        var media = plan.TelopFaces![pn.AssetId].Media;
                        double kx = pn.Width / (double)pn.CropW, ky = pn.Height / (double)pn.CropH;
                        var faces = samples.SelectMany(s => s.Boxes).Select(f => new Rect(
                            Math.Clamp(pn.X + (f.X * media.Width - pn.CropX) * kx - FacePadding, box.Left, box.Right),
                            Math.Clamp(pn.Y + (f.Y * media.Height - pn.CropY) * ky - FacePadding, box.Top, box.Bottom),
                            Math.Clamp(pn.X + ((f.X + f.W) * media.Width - pn.CropX) * kx + FacePadding, box.Left, box.Right),
                            Math.Clamp(pn.Y + ((f.Y + f.H) * media.Height - pn.CropY) * ky + FacePadding, box.Top, box.Bottom)))
                            .Where(r => r.Right > r.Left && r.Bottom > r.Top).ToList();
                        if (faces.Count > 0) result.Add(new(faces.Min(b => b.Left), faces.Min(b => b.Top), faces.Max(b => b.Right), faces.Max(b => b.Bottom)));
                    }
                    off += c.Length;
                    continue;
                }
                if (plan.TelopFaces == null || !plan.TelopFaces.TryGetValue(c.AssetId, out var source))
                    known = false;
                else
                {
                    int first = result.Count;
                    double lo = Math.Max(0, telop.Start - off), hi = Math.Min(c.Length + fade, telop.End - off);
                    double s0 = c.SrcIn + lo * c.Speed, s1 = c.SrcIn + hi * c.Speed;
                    var samples = source.Samples.Where(s => s.T >= s0 - 0.5 && s.T <= s1 + 0.5).OrderBy(s => s.T).ToList();
                    if (samples.Count == 0 || samples[0].T > s0 + 0.5 || samples[^1].T < s1 - 0.5
                        || samples.Zip(samples.Skip(1)).Any(pair => pair.Second.T - pair.First.T > 0.75)) known = false;
                    foreach (var sample in samples)
                    {
                        double a = Math.Clamp((sample.T - c.SrcIn - 0.5) / c.Speed, lo, hi);
                        double b = Math.Clamp((sample.T - c.SrcIn + 0.5) / c.Speed, lo, hi);
                        var times = new[] { a, b }.Concat((c.Reframe.Track ?? []).Concat(c.Reframe.TrackY ?? []).Where(k => k.T > a && k.T < b).Select(k => k.T));
                        foreach (var face in sample.Boxes)
                        foreach (double time in times)
                            result.Add(Project(face, c, source.Media, plan.Canvas, time));
                    }
                    // The detector samples at 2 fps: reserve the swept envelope between samples, not just isolated boxes.
                    if (result.Count > first)
                    {
                        var swept = result.Skip(first).ToList();
                        result.RemoveRange(first, result.Count - first);
                        result.Add(new(swept.Min(b => b.Left), swept.Min(b => b.Top), swept.Max(b => b.Right), swept.Max(b => b.Bottom)));
                    }
                }
                // Protect the entire inset, including faces between samples and the surrounding ring.
                if (c.Pip is { } pip) result.Add(new(pip.X - FacePadding, pip.Y - FacePadding,
                    pip.X + (pip.Shape is "rectangle" or "panel" ? pip.Width : pip.D + 2 * pip.Border) + FacePadding,
                    pip.Y + (pip.Shape is "rectangle" or "panel" ? pip.Height : pip.D + 2 * pip.Border) + FacePadding));
            }
            off += c.Length;
        }
        return result;
    }

    public static Rect Project(FaceBox face, Clip clip, MediaInfo media, Canvas canvas, double time)
    {
        var r = clip.Reframe; double w = canvas.Width, h = canvas.Height;
        double x = face.X * media.Width, y = face.Y * media.Height, fw = face.W * media.Width, fh = face.H * media.Height;
        double sx, sy, dx, dy;
        bool crop = (r.Mode is "face" or "screen") && r.CropW > 0 && r.Track is { Count: > 0 };
        if (crop)
        {
            x -= FaceTrack.XAt(r.Track!, time); y -= r.TrackY is { Count: > 0 } ty ? FaceTrack.XAt(ty, time) : r.CropY;
            sx = w / r.CropW; sy = (r.Mode == "screen" ? r.BoxH : h) / r.CropH;
            dx = 0; dy = r.Mode == "screen" ? r.BoxY : 0;
        }
        else if (r.Mode == "blur_bg" && r.FgScale is > 0 and < 0.999)
        {
            sx = sy = ((int)(w * r.FgScale) & ~1) / (double)media.Width;
            dx = (w - media.Width * sx) / 2;
            dy = Math.Max(0, r.FgOffsetY);
        }
        else
        {
            sx = sy = Math.Min(w / media.Width, (r.Mode == "screen" ? r.BoxH : h) / media.Height);
            dx = (w - media.Width * sx) / 2;
            dy = r.Mode == "screen" ? r.BoxY + (r.BoxH - media.Height * sy) / 2 : (h - media.Height * sy) / 2 + (r.Mode == "fit" ? 0 : r.FgOffsetY);
        }
        x = x * sx + dx; y = y * sy + dy; fw *= sx; fh *= sy;
        if (clip.Zoom > 1.001 && r.Mode != "screen")
        {
            double cx = crop ? w / 2 : dx + media.Width * sx / 2, cy = crop ? h / 2 : dy + media.Height * sy / 2;
            x = cx + (x - cx) * clip.Zoom; y = cy + (y - cy) * clip.Zoom; fw *= clip.Zoom; fh *= clip.Zoom;
        }
        return new(x - FacePadding, y - FacePadding, x + fw + FacePadding, y + fh + FacePadding);
    }
}
