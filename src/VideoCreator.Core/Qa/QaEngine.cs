using System.Globalization;
using System.Text;
using System.Text.Json;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;

namespace VideoCreator.Qa;

public sealed record QaContext(EditPlan Plan, Preset Preset, Transcript Transcript, IReadOnlyCollection<string> RightsAssetIds, IReadOnlyList<string> Dictionary, IReadOnlyList<SourceAsset>? Assets = null);

/// <summary>PRD §13 automatic QA. Hard (H-*) failures block export.</summary>
public sealed class QaEngine(FFmpeg ff)
{
    public Faces.FaceService? Faces { get; init; }
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    public static readonly (int Top, int Bottom, int Left, int Right) SafeZone = (250, 580, 60, 160);
    public const double WordTolerance = 0.15;

    /// <summary>Checks that need no rendered file (also used for instant re-evaluation after edits, AT-09).</summary>
    public static List<QaResult> PlanChecks(QaContext ctx)
    {
        var res = new List<QaResult>();
        var p = ctx.Preset; var plan = ctx.Plan; var tr = ctx.Transcript;
        double dur = plan.Duration;
        res.Add(new("H-01", dur >= p.Target.Min - 0.05 && dur <= p.Target.Max + 0.05 && dur <= 180 ? QaStatus.Pass : QaStatus.Fail, $"{dur:F2}s", $"{p.Target.Min}–{p.Target.Max}s, ≤180s", "尺"));

        // H-06: an edge must not fall inside a word by more than the ASR timing tolerance on both sides
        var bad = new List<string>();
        for (int ci = 0; ci < plan.Timeline.Count; ci++)
        {
            var c = plan.Timeline[ci];
            if (c.Speed > 2.001) continue;   // audio muted
            var edges = new List<double>();
            // a camera switch inside continuous master audio is not an audio cut
            if (ci == 0 || Math.Abs(plan.Timeline[ci - 1].MasterOut - c.MasterIn) > 0.002 || plan.Timeline[ci - 1].Speed != c.Speed) edges.Add(c.MasterIn);
            if (ci == plan.Timeline.Count - 1 || Math.Abs(plan.Timeline[ci + 1].MasterIn - c.MasterOut) > 0.002 || plan.Timeline[ci + 1].Speed != c.Speed) edges.Add(c.MasterOut);
            foreach (var edge in edges)
                foreach (var w in tr.Words)
                    if (edge - w.T0 > WordTolerance && w.T1 - edge > WordTolerance) bad.Add($"{edge:F2}s「{w.Text}」");
        }
        res.Add(new("H-06", bad.Count == 0 ? QaStatus.Pass : p.Planner == "beat" ? QaStatus.Warn : QaStatus.Fail, bad.Count == 0 ? "0" : string.Join(", ", bad.Take(3)), p.Planner == "beat" ? "拍に合わせるため警告のみ" : "0 mid-word cuts (±150ms)", "発話途中のカット"));

        // H-07: numbers / proper tokens in generated telops must be grounded in the clip transcript (or dictionary)
        var src = string.Concat(tr.Sentences.Where(s => plan.Timeline.Any(c => s.T1 > c.MasterIn && s.T0 < c.MasterOut)).Select(s => s.Text));
        var ungrounded = plan.Telops.Where(t => t.Enabled && t.Type is TelopType.Emphasis or TelopType.Hook or TelopType.Headline or TelopType.Counter or TelopType.Tsukkomi)
            .SelectMany(t => JapaneseText.UngroundedClaims(t.Text, src, ctx.Dictionary).Select(x => $"{t.Id}:{x}")).ToList();
        res.Add(new("H-07", ungrounded.Count == 0 ? QaStatus.Pass : QaStatus.Fail, ungrounded.Count == 0 ? "0" : string.Join(", ", ungrounded), "0 ungrounded claims", "テロップの忠実性"));

        var unapproved = plan.Telops.Count(t => t.Enabled && !t.Approved);
        res.Add(new("H-08", unapproved == 0 ? QaStatus.Pass : QaStatus.Fail, unapproved.ToString(IC), "0", "未承認テロップ"));

        var missing = plan.Timeline.Select(c => c.AssetId).Distinct().Where(a => !ctx.RightsAssetIds.Contains(a)).ToList();
        res.Add(new("H-09", missing.Count == 0 ? QaStatus.Pass : QaStatus.Fail, missing.Count == 0 ? "all confirmed" : string.Join(",", missing), "all assets confirmed", "権利確認"));

        var caps = plan.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).ToList();
        var fast = caps.Where(t => JapaneseText.DisplayWidth(t.Text) / Math.Max(0.05, t.End - t.Start) > p.Telop.Caption.MaxCps + 0.01).ToList();
        res.Add(new("S-01", fast.Count == 0 ? QaStatus.Pass : QaStatus.Warn, fast.Count == 0 ? "ok" : string.Join(", ", fast.Take(3).Select(t => $"{t.Id} {JapaneseText.DisplayWidth(t.Text) / (t.End - t.Start):F1}cps")), $"≤{p.Telop.Caption.MaxCps} 文字/秒", "字幕速度"));
        var wide = plan.Telops.Where(t => t.Enabled && t.Type is TelopType.Caption or TelopType.Emphasis && JapaneseText.DisplayWidth(t.Text) > p.Telop.Caption.MaxCharsPerLine).ToList();
        res.Add(new("S-02", wide.Count == 0 ? QaStatus.Pass : QaStatus.Warn, wide.Count == 0 ? "ok" : string.Join(", ", wide.Take(3).Select(t => $"{t.Id} {JapaneseText.DisplayWidth(t.Text)}字")), $"≤{p.Telop.Caption.MaxCharsPerLine} 字/行", "1行の文字数"));

        var mapped = TelopBuilder.MapWords(plan.Timeline, tr);
        bool hookOk = mapped.Any(m => m.Start <= p.Hook.MaxSec) || plan.Timeline.FirstOrDefault()?.Role == "teaser" || tr.Words.Count == 0;
        res.Add(new("S-03", hookOk ? QaStatus.Pass : QaStatus.Warn, hookOk ? "speech/teaser in opening" : "no speech in opening", $"発話 ≤{p.Hook.MaxSec}s", "冒頭フック"));

        var shortShots = plan.Timeline.Where(c => c.Role == "body" && c.Length < p.Cut.MinShotSec - 0.01).ToList();
        res.Add(new("S-04", shortShots.Count == 0 ? QaStatus.Pass : QaStatus.Warn, shortShots.Count == 0 ? "ok" : string.Join(",", shortShots.Select(c => $"{c.ClipId} {c.Length:F2}s")), $"≥{p.Cut.MinShotSec}s", "最短ショット"));

        var firstBody = plan.Timeline.FirstOrDefault(c => c.Role == "body") ?? plan.Timeline.FirstOrDefault();
        var firstSentence = firstBody == null ? null : tr.Sentences.FirstOrDefault(s => s.T1 > firstBody.MasterIn + 0.05);
        bool demo = firstSentence != null && JapaneseText.StartsWithDemonstrative(firstSentence.Text);
        res.Add(new("S-07", demo ? QaStatus.Warn : QaStatus.Pass, demo ? firstSentence!.Text : "ok", "冒頭が指示語でない", "冒頭の指示語"));

        // S-09: in multi-camera plans the camera on screen should be the speaker's (FR-SWT-02)
        if (p.Planner == "switch" && ctx.Assets != null)
        {
            var spk = ctx.Assets.Where(a => Planning.SwitchPlanner.SpeakerOf(a) > 0).ToDictionary(a => a.Id, Planning.SwitchPlanner.SpeakerOf);
            int total = 0, match = 0;
            foreach (var c in plan.Timeline.Where(c => c.Role == "body" && spk.ContainsKey(c.AssetId)))
            {
                var by = tr.Sentences.Where(s => s.T1 > c.MasterIn && s.T0 < c.MasterOut).GroupBy(s => s.Speaker)
                    .Select(g => (speaker: g.Key, len: g.Sum(s => Math.Min(c.MasterOut, s.T1) - Math.Max(c.MasterIn, s.T0)))).OrderByDescending(x => x.len).FirstOrDefault();
                if (by.len <= 0) continue;
                total++; if (by.speaker == spk[c.AssetId]) match++;
            }
            double ratio = total == 0 ? 1 : (double)match / total;
            res.Add(new("S-09", ratio >= 0.9 ? QaStatus.Pass : QaStatus.Warn, $"{ratio:P0} ({match}/{total})", "≥ 90% で話者とカメラが一致", "話者とカメラの一致"));
        }
        return res;
    }

    /// <summary>Full QA on a rendered file. Preview renders skip loudness and black/freeze detection.</summary>
    public async Task<List<QaResult>> RunAsync(QaContext ctx, string rendered, bool preview, string workDir, CancellationToken ct)
    {
        var res = PlanChecks(ctx);
        // H-01/H-02 measured on the file (third-party measurement: ffprobe)
        var pr = await ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=codec_type,width,height:format=duration", "-of", "json", rendered], ct);
        using (var doc = JsonDocument.Parse(pr.StdOut))
        {
            var dur = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, IC);
            var v = doc.RootElement.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "video");
            int w = v.GetProperty("width").GetInt32(), h = v.GetProperty("height").GetInt32();
            var p = ctx.Preset;
            res.RemoveAll(r => r.CheckId == "H-01");
            res.Insert(0, new("H-01", dur >= p.Target.Min - 0.1 && dur <= p.Target.Max + 0.1 && dur <= 180 ? QaStatus.Pass : QaStatus.Fail, $"{dur:F2}s (ffprobe)", $"{p.Target.Min}–{p.Target.Max}s", "尺"));
            int ew = preview ? ctx.Plan.Canvas.Width / 2 : ctx.Plan.Canvas.Width, eh = preview ? ctx.Plan.Canvas.Height / 2 : ctx.Plan.Canvas.Height;
            res.Insert(1, new("H-02", w == ew && h == eh ? QaStatus.Pass : QaStatus.Fail, $"{w}x{h}", $"{ew}x{eh}", "解像度・比率 9:16"));
        }
        if (!preview)
        {
            var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", rendered, "-vn", "-af", "loudnorm=I=-14:TP=-1.5:print_format=summary", "-f", "null", "-"], ct);
            double I = Num(r.StdErr, @"Input Integrated:\s*(-?[\d.]+)"), TP = Num(r.StdErr, @"Input True Peak:\s*([-+]?[\d.]+)");
            res.Insert(2, new("H-03", Math.Abs(I - ctx.Plan.Audio.TargetLufs) <= 1 ? QaStatus.Pass : QaStatus.Fail, $"{I:F1} LUFS", "−14 ±1 LUFS", "積分ラウドネス"));
            res.Insert(3, new("H-04", TP <= -1.0 ? QaStatus.Pass : QaStatus.Fail, $"{TP:F1} dBTP", "≤ −1.0 dBTP", "True Peak"));
        }
        res.Add(await SafeZoneAsync(ctx, workDir, ct));
        if (!preview && Faces is { Available: true }) res.Add(await FaceOverlapAsync(ctx, rendered, ct));
        if (!preview) res.Add(await BlackFreezeAsync(rendered, ctx.Preset.Id == "P9", ct));
        return res.OrderBy(r => r.Hard ? 0 : 1).ThenBy(r => r.CheckId, StringComparer.Ordinal).ToList();
    }

    static double Num(string s, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, pattern);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out var d) ? d : double.NaN;
    }

    /// <summary>S-05: detect faces in the rendered frames (independent of the planner's own face data) and check that no caption-type telop covers a face.</summary>
    public async Task<QaResult> FaceOverlapAsync(QaContext ctx, string rendered, CancellationToken ct)
    {
        var c = ctx.Preset.Telop.Caption; int H = ctx.Plan.Canvas.Height;
        (double top, double bottom)? Band(Telop t) => t.Type switch
        {
            TelopType.Caption => (H - c.MarginV - c.FontPx * 1.3, H - c.MarginV),
            TelopType.Emphasis => (H - c.MarginV - AssWriter.EmphLift - c.FontPx * 1.6, H - c.MarginV - AssWriter.EmphLift),
            TelopType.Tsukkomi => (H - AssWriter.TsukkomiMarginV - 90, H - AssWriter.TsukkomiMarginV),
            TelopType.Counter => (AssWriter.CounterMarginV, AssWriter.CounterMarginV + 120),
            _ => null,
        };
        var cand = ctx.Plan.Telops.Where(t => t.Enabled && Band(t) != null).OrderBy(t => t.Start).ToList();
        if (cand.Count == 0) return new("S-05", QaStatus.Pass, "no telops", "顔にテロップが重ならない", "顔とテロップの重なり");
        int step = Math.Max(1, cand.Count / 12), checkedN = 0; var hits = new List<string>();
        for (int i = 0; i < cand.Count; i += step)
        {
            var t = cand[i]; var mid = (t.Start + t.End) / 2; var (top, bottom) = Band(t)!.Value;
            var faces = await Faces!.DetectAtAsync(rendered, mid, ct); checkedN++;
            foreach (var f in faces)
            {
                double fy0 = f.Y * H, fy1 = (f.Y + f.H) * H, ov = Math.Min(bottom, fy1) - Math.Max(top, fy0);
                if (ov > 0.3 * (fy1 - fy0)) hits.Add($"{t.Id}@{mid:F1}s");
            }
        }
        return new("S-05", hits.Count == 0 ? QaStatus.Pass : QaStatus.Warn, hits.Count == 0 ? $"{checkedN} 箇所で重なりなし" : string.Join(", ", hits.Take(3)), "顔にテロップが重ならない", "顔とテロップの重なり");
    }

    /// <summary>H-05 measured: render the telop layer alone (half resolution, 10 fps) and compute glyph bounding boxes from raw pixels.</summary>
    public async Task<QaResult> SafeZoneAsync(QaContext ctx, string workDir, CancellationToken ct)
    {
        var telops = ctx.Plan.Telops.Where(t => t.Enabled && t.End - t.Start > 0.15).ToList();
        if (telops.Count == 0) return new("H-05", QaStatus.Pass, "no telops", "safe zone", "テロップの安全領域");
        Directory.CreateDirectory(workDir);
        var violations = new List<string>();
        foreach (var group in new[] { telops.Where(t => t.Type is not (TelopType.Hook or TelopType.Headline)).ToList(), telops.Where(t => t.Type is TelopType.Hook or TelopType.Headline).ToList() })
        {
            if (group.Count == 0) continue;
            bool top = group[0].Type is TelopType.Hook or TelopType.Headline;
            var plan = ctx.Plan with { Telops = group };
            var ass = Path.Combine(workDir, top ? "qa_top.ass" : "qa_body.ass");
            await File.WriteAllTextAsync(ass, AssWriter.Write(plan, ctx.Preset), new UTF8Encoding(false), ct);
            const int W = 540, H = 960; const double fps = 10;
            var frames = group.Select(t => (int)Math.Round((t.Start + Math.Min(0.6, (t.End - t.Start) / 2)) * fps)).Distinct().OrderBy(x => x).ToList();
            var select = string.Join("+", frames.Select(f => $"eq(n,{f})"));
            using var ms = new MemoryStream();
            var dur = (ctx.Plan.Duration + 1).ToString("0.##", IC);
            await ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"color=black:s={W}x{H}:r={fps.ToString(IC)}:d={dur}",
                "-vf", $"subtitles={FFmpeg.FilterPath(ass)}:fontsdir={FFmpeg.FilterPath(ff.FontsDir)},select='{select}'", "-fps_mode", "passthrough", "-pix_fmt", "gray", "-f", "rawvideo", "-"], ct, stdout: ms);
            var buf = ms.GetBuffer(); int n = (int)(ms.Length / (W * H));
            for (int k = 0; k < n; k++)
            {
                int x1 = W, x2 = -1, y1 = H, y2 = -1;
                for (int y = 0; y < H; y++)
                {
                    int row = k * W * H + y * W;
                    for (int x = 0; x < W; x++) if (buf[row + x] > 48) { if (x < x1) x1 = x; if (x > x2) x2 = x; if (y < y1) y1 = y; if (y > y2) y2 = y; }
                }
                if (x2 < 0) continue;
                (x1, x2, y1, y2) = (x1 * 2, x2 * 2 + 1, y1 * 2, y2 * 2 + 1);   // back to 1080x1920 coordinates
                var canvasH = ctx.Plan.Canvas.Height; var canvasW = ctx.Plan.Canvas.Width;
                bool ok = top ? y1 >= SafeZone.Top && x1 >= SafeZone.Left && x2 <= canvasW - SafeZone.Right
                              : y1 >= SafeZone.Top && y2 <= canvasH - SafeZone.Bottom && x1 >= SafeZone.Left && x2 <= canvasW - SafeZone.Right;
                if (!ok) violations.Add($"t≈{frames.ElementAtOrDefault(k) / fps:F1}s bbox x{x1}-{x2} y{y1}-{y2}");
            }
        }
        return new("H-05", violations.Count == 0 ? QaStatus.Pass : QaStatus.Fail, violations.Count == 0 ? "all telops inside (measured)" : string.Join("; ", violations.Take(3)),
            $"x {SafeZone.Left}–{1080 - SafeZone.Right}, y {SafeZone.Top}–{1920 - SafeZone.Bottom}", "テロップの安全領域（実測）");
    }

    public async Task<QaResult> BlackFreezeAsync(string file, bool allowFreeze, CancellationToken ct)
    {
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", file, "-an", "-vf", "scale=270:-2,blackdetect=d=0.5:pix_th=0.1,freezedetect=n=0.003:d=0.5", "-f", "null", "-"], ct);
        int black = System.Text.RegularExpressions.Regex.Matches(r.StdErr, "black_start").Count;
        int freeze = allowFreeze ? 0 : System.Text.RegularExpressions.Regex.Matches(r.StdErr, "freeze_start").Count;
        return new("S-06", black + freeze == 0 ? QaStatus.Pass : QaStatus.Warn, $"black={black} freeze={freeze}", "0 (≥0.5s)", "黒画面・フリーズ");
    }
}
