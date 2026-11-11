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

    public sealed record Placement(Rect? Bounds, int FontSize, bool Positioned, string? Reason = null);
    public const double FacePadding = 32, PopScale = 1.12;

    public static Dictionary<string, Placement> Resolve(EditPlan plan, Preset p)
    {
        var result = new Dictionary<string, Placement>();
        var occupied = new List<(Telop Telop, Rect Bounds)>();
        int w = plan.Canvas.Width, h = plan.Canvas.Height;
        var captionBand = AssWriter.Band(TelopType.Caption, p, h)!.Value;
        var captionBounds = new Rect(0, captionBand.Top - 32, w, captionBand.Bottom + 32);
        foreach (var t in plan.Telops.Where(t => t.Enabled).OrderBy(t => t.Type == TelopType.Caption ? 0 : 1).ThenBy(t => t.Start).ThenBy(t => t.Type))
        {
            var (size, outline, scaleX) = AssWriter.Metrics(t.Style, p);
            int fit = AssWriter.FitFontSize(t.Text, size, outline, w, p.Telop.Caption.MarginR, scaleX, plan.Language);
            var band = AssWriter.Band(t.Type, p, h);
            double height = fit * 1.5 + 2 * outline + 12;
            double top = band?.Top ?? (t.Type == TelopType.Sfx ? AssWriter.SfxY - height / 2 : AssWriter.HeadlineMarginV);
            var original = new Rect(AssWriter.MarginL, top, w - p.Telop.Caption.MarginR, band?.Bottom ?? top + height);
            if (t.Type == TelopType.Caption || plan.TelopFaces == null)
            {
                result[t.Id] = new(original, fit, false);
                continue;
            }
            var faces = Obstacles(plan, t, out bool known);
            if (!known)
            {
                result[t.Id] = new(null, fit, true, "顔の解析情報が不足しているため表示しません");
                continue;
            }
            // Reserve animation overshoot on all sides instead of shrinking the face-protection margin.
            fit = AssWriter.FitFontSize(t.Text, size, outline + 6, w, p.Telop.Caption.MarginR, (int)Math.Ceiling(scaleX * PopScale), plan.Language);
            height = fit * 1.5 * PopScale + 2 * outline + 16;
            var blocked = faces.Concat(occupied.Where(x => x.Telop.Start < t.End && x.Telop.End > t.Start).Select(x => x.Bounds)).ToList();
            if (plan.Telops.Any(c => c.Enabled && c.Type == TelopType.Caption && c.Start < t.End && c.End > t.Start)) blocked.Add(captionBounds);
            var screenOffset = 0.0;
            foreach (var clip in plan.Timeline)
            {
                if (screenOffset < t.End && screenOffset + clip.Length > t.Start && clip.Reframe.Mode == "screen")
                    blocked.Add(new(0, clip.Reframe.BoxY, w, clip.Reframe.BoxY + clip.Reframe.BoxH));
                screenOffset += clip.Length;
            }
            double min = Qa.QaEngine.SafeZone.Top, max = h - Qa.QaEngine.SafeZone.Bottom - height;
            var candidates = new[] { Math.Clamp(top, min, Math.Max(min, max)), min, max }
                .Concat(blocked.SelectMany(b => new[] { b.Bottom + 8, b.Top - height - 8 }))
                .Where(y => y >= min && y <= max).Distinct().OrderBy(y => Math.Abs(y - top));
            var bounds = candidates.Select(y => new Rect(AssWriter.MarginL, y, w - p.Telop.Caption.MarginR, y + height))
                .FirstOrDefault(b => blocked.All(o => !b.Intersects(o)));
            result[t.Id] = new(bounds, fit, true, bounds == null ? "顔・字幕と重ならない表示場所がないため表示しません" : null);
            if (bounds != null) occupied.Add((t, bounds));
        }
        return result;
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
                if (c.Pip is { } pip) result.Add(new(pip.X - FacePadding, pip.Y - FacePadding, pip.X + pip.D + 2 * pip.Border + FacePadding, pip.Y + pip.D + 2 * pip.Border + FacePadding));
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
        else
        {
            sx = sy = r.Mode is "fit" or "screen" ? Math.Min(w / media.Width, (r.Mode == "screen" ? r.BoxH : h) / media.Height) : w / media.Width;
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
