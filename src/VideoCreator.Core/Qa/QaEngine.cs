using System.Globalization;
using System.Text;
using System.Text.Json;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;

namespace VideoCreator.Qa;

public sealed record QaContext(EditPlan Plan, Preset Preset, Transcript Transcript, IReadOnlyCollection<string> RightsAssetIds, IReadOnlyList<string> Dictionary, IReadOnlyList<SourceAsset>? Assets = null,
    IReadOnlyList<string>? MaskWords = null, CandidateApproval? Approval = null);

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
        var ins = InsertMap.Active(plan);
        QaItem TelopItem(Telop t, string detail) => new(t.Id, "telop", detail, Math.Round(InsertMap.ToFinal(ins, t.Start), 3), Math.Round(InsertMap.ToFinal(ins, t.End, true), 3));
        QaItem InsertItem(Insert i, string detail)
        {
            double s = i.Enabled && i.Duration > 0 ? InsertMap.StartOf(ins, i) : InsertMap.ToFinal(ins, i.At);
            return new(i.Id, "insert", detail, Math.Round(s, 3), Math.Round(s + (i.Enabled ? i.Duration : 0), 3));
        }
        var clipStart = new double[plan.Timeline.Count];
        for (int ci = 1; ci < plan.Timeline.Count; ci++) clipStart[ci] = clipStart[ci - 1] + plan.Timeline[ci - 1].Length;
        QaItem ClipItem(int ci, string detail, double? bodyAt = null)
        {
            var c = plan.Timeline[ci];
            double s = InsertMap.ToFinal(ins, bodyAt ?? clipStart[ci]), e = bodyAt != null ? s : InsertMap.ToFinal(ins, clipStart[ci] + c.Length, true);
            return new(c.ClipId, "clip", detail, Math.Round(s, 3), Math.Round(e, 3));
        }
        double dur = plan.Duration, fileDur = plan.TotalDuration;
        var (minD, maxD, cap) = DurationRange(plan, p);
        res.Add(new("H-01", DurationOk(dur, fileDur, minD, maxD, cap, 0.05) ? QaStatus.Pass : QaStatus.Fail, DurationText(dur, fileDur), RangeText(minD, maxD, cap), "尺"));

        // H-06: an edge must not fall inside a word by more than the ASR timing tolerance on both sides
        var bad = new List<string>(); var badItems = new List<QaItem>();
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
                    if (edge - w.T0 > WordTolerance && w.T1 - edge > WordTolerance)
                    {
                        bad.Add($"{edge:F2}s「{w.Text}」");
                        badItems.Add(ClipItem(ci, $"「{w.Text}」の途中で{(edge == c.MasterIn ? "始まります" : "切れます")}", clipStart[ci] + (edge - c.MasterIn) / c.Speed));
                    }
        }
        res.Add(new("H-06", bad.Count == 0 ? QaStatus.Pass : p.Planner == "beat" ? QaStatus.Warn : QaStatus.Fail, bad.Count == 0 ? "0" : string.Join(", ", bad.Take(3)), p.Planner == "beat" ? "拍に合わせるため警告のみ" : "0 mid-word cuts (±150ms)", "発話途中のカット") { Items = badItems });

        // H-07: numbers / proper tokens in generated telops must be grounded in the clip transcript (or dictionary).
        // Series parts: hook/chapter telops may name the series (whole source), and part labels (第2話 …) are structural, not claims.
        var src = plan.Series is { } se
            ? tr.FullText + string.Concat(se.Labels)
            : string.Concat(tr.Sentences.Where(s => plan.Timeline.Any(c => s.T1 > c.MasterIn && s.T0 < c.MasterOut)).Select(s => s.Text));
        var ungroundedHits = plan.Telops.Where(t => t.Enabled && t.Type is TelopType.Emphasis or TelopType.Hook or TelopType.Headline or TelopType.Counter or TelopType.Tsukkomi)
            .Select(t => (id: t.Id, claims: JapaneseText.UngroundedClaims(t.Text, src, ctx.Dictionary), item: (Func<string, QaItem>)(d => TelopItem(t, d))))
            .Concat(plan.Inserts.Where(i => i.Enabled && i.Kind == "card" && i.Text.Length > 0).Select(i => (id: i.Id, claims: JapaneseText.UngroundedClaims(i.Text, src, ctx.Dictionary), item: (Func<string, QaItem>)(d => InsertItem(i, d)))))
            .Where(x => x.claims.Count > 0).ToList();
        var ungrounded = ungroundedHits.SelectMany(x => x.claims.Select(c => $"{x.id}:{c}")).ToList();
        res.Add(new("H-07", ungrounded.Count == 0 ? QaStatus.Pass : QaStatus.Fail, ungrounded.Count == 0 ? "0" : string.Join(", ", ungrounded), "0 ungrounded claims", "テロップの未出現語（内容の忠実性は別途確認）")
            { Items = ungroundedHits.Select(x => x.item($"「{string.Join("」「", x.claims)}」が発話にありません")).ToList() });

        var evidence = plan.Telops.Where(t => t.Enabled && (TelopGrounding.Applies(t) || p.Planner == "switch" && t.Speaker != null))
            .Select(t => (t, problems: TelopGrounding.Problems(t, plan.Timeline, tr, ctx.Dictionary, plan.Series))).Where(x => x.problems.Count > 0).ToList();
        var evidenceErrors = evidence.SelectMany(x => x.problems.Select(message => $"{x.t.Id}: {message}")).ToList();
        res.Add(new("H-11", evidenceErrors.Count == 0 ? QaStatus.Pass : QaStatus.Fail,
            evidenceErrors.Count == 0 ? "0" : string.Join(", ", evidenceErrors), "区間内の根拠発話・数字/単位/対象・話者が一致", "テロップと発話の対応")
            { Items = evidence.Select(x => TelopItem(x.t, string.Join(" / ", x.problems))).ToList() });
        var lostNegation = plan.Telops.Where(t => t.Enabled && TelopGrounding.NegationDropped(t, plan.Timeline, tr)).ToList();
        res.Add(new("S-09", lostNegation.Count == 0 ? QaStatus.Pass : QaStatus.Warn,
            lostNegation.Count == 0 ? "0" : string.Join(", ", lostNegation.Select(t => t.Id)), "根拠発話の否定を保持", "否定の脱落")
            { Items = lostNegation.Select(t => TelopItem(t, "根拠発話の否定（〜ない など）がテロップから抜けています")).ToList() });

        var unapprovedItems = plan.Telops.Where(t => t.Enabled && !t.Approved).Select(t => TelopItem(t, "未承認"))
            .Concat(plan.Inserts.Where(i => i.Enabled && i.Kind == "card" && i.Text.Length > 0 && !i.Approved).Select(i => InsertItem(i, "未承認のカード"))).ToList();
        var unapproved = unapprovedItems.Count;
        bool staleApproval = ctx.Approval != null && ctx.Approval.PlanHash != EditPlanFingerprint.Of(plan);
        res.Add(new("H-08", unapproved == 0 && !staleApproval ? QaStatus.Pass : QaStatus.Fail,
            staleApproval ? "候補の承認は編集後に失効しています" : unapproved.ToString(IC), "0・承認ハッシュが一致", "未承認テロップ・失効した承認") { Items = unapprovedItems });

        var missing = plan.Timeline.Select(c => c.AssetId).Concat(plan.Timeline.Where(c => c.Pip != null).Select(c => c.Pip!.AssetId)).Distinct().Where(a => !ctx.RightsAssetIds.Contains(a))
            .Concat(plan.Inserts.Where(i => i.Enabled && i.ImageAssetId != null && !ctx.RightsAssetIds.Contains(i.ImageAssetId)).Select(i => i.ImageAssetId!))
            .Concat(plan.Inserts.Where(i => i.Enabled && i.Path != null && !i.RightsConfirmed).Select(i => Path.GetFileName(i.Path!))).ToList();
        res.Add(new("H-09", missing.Count == 0 ? QaStatus.Pass : QaStatus.Fail, missing.Count == 0 ? "all confirmed" : string.Join(",", missing), "all assets confirmed", "権利確認")
            { Items = missing.Select(m => new QaItem(m, "asset", "権利確認が済んでいません")).ToList() });

        // H-10 伏せ字: no hidden word may survive in any telop / card (including hand edits), and every spoken occurrence inside the edit must be bleeped.
        // The spans are recomputed here from the original transcript, independently of the planner.
        var mask = Planning.Masking.Clean(ctx.MaskWords);
        if (mask.Count > 0)
        {
            var leakItems = plan.Telops.Where(t => t.Enabled && Planning.Masking.Contains(t.Text, mask)).Select(t => TelopItem(t, "伏せる語が文字に残っています"))
                .Concat(plan.Inserts.Where(i => i.Enabled && Planning.Masking.Contains(i.Text, mask)).Select(i => InsertItem(i, "伏せる語が文字に残っています"))).ToList();
            var spoken = Planning.Masking.ToOutput(plan.Timeline, Planning.Masking.Apply(tr, mask).Spans);
            var unbleepedSpans = spoken.Where(s => !plan.Audio.Bleeps.Any(b => b.T0 <= s.T0 + 0.01 && b.T1 >= s.T1 - 0.01)).ToList();
            var maskBad = leakItems.Select(x => x.Target).Concat(unbleepedSpans.Select(s => $"{s.T0:F2}s")).ToList();
            res.Add(new("H-10", maskBad.Count == 0 ? QaStatus.Pass : QaStatus.Fail, maskBad.Count == 0 ? $"{mask.Count} 語・{spoken.Count} か所" : string.Join(", ", maskBad.Take(5)), "伏せた語が文字にも音声にも残らない", "伏せ字")
            {
                Items = [.. leakItems, .. unbleepedSpans.Select(s => new QaItem($"{s.T0:F2}s", "audio", "伏せる語の音声がピー音で消されていません", Math.Round(InsertMap.ToFinal(ins, s.T0), 3), Math.Round(InsertMap.ToFinal(ins, s.T1, true), 3)))],
            });
        }

        var caps = plan.Telops.Where(t => t.Enabled && t.Type == TelopType.Caption).ToList();
        var cc = new CaptionChecks(p, plan.Canvas.Width, tr);
        var fast = caps.Select(t => (t, why: cc.Speed(t))).Where(x => x.why != null).ToList();
        res.Add(new("S-01", fast.Count == 0 ? QaStatus.Pass : QaStatus.Warn, fast.Count == 0 ? "ok" : string.Join(", ", fast.Take(3).Select(x => $"{x.t.Id} {JapaneseText.DisplayWidth(x.t.Text) / (x.t.End - x.t.Start):F1}cps")), $"≤{p.Telop.Caption.MaxCps} 文字/秒", "字幕速度")
            { Items = fast.Select(x => TelopItem(x.t, x.why!)).ToList() });
        int lineChars = cc.LineChars;
        var wide = plan.Telops.Where(t => t.Enabled && (t.Type == TelopType.Caption ? cc.Width(t) != null
            : t.Type == TelopType.Emphasis && JapaneseText.DisplayWidth(t.Text) > p.Telop.Caption.MaxCharsPerLine)).ToList();
        res.Add(new("S-02", wide.Count == 0 ? QaStatus.Pass : QaStatus.Warn, wide.Count == 0 ? "ok" : string.Join(", ", wide.Take(3).Select(t => $"{t.Id} {JapaneseText.DisplayWidth(t.Text)}字")), $"≤{lineChars} 字/行" + (p.Telop.Caption.MaxLines > 1 ? $"・{p.Telop.Caption.MaxLines}行まで" : ""), "1行の文字数")
            { Items = wide.Select(t => TelopItem(t, $"{JapaneseText.DisplayWidth(t.Text)} 字")).ToList() });

        // S-08 禁則: every displayed caption line (incl. hand edits) obeys JIS X 4051 line-start/line-end rules, and a caption never starts with a particle/auxiliary of the previous caption
        var kinsoku = caps.Select(t => (t, why: cc.Kinsoku(t, caps))).Where(x => x.why != null).ToList();
        res.Add(new("S-08", kinsoku.Count == 0 ? QaStatus.Pass : QaStatus.Warn, kinsoku.Count == 0 ? "ok" : string.Join(", ", kinsoku.Take(3).Select(x => $"{x.t.Id} {x.why}")), "行頭・行末の禁則を守る", "字幕の禁則")
            { Items = kinsoku.Select(x => TelopItem(x.t, x.why!)).ToList() });

        var mapped = TelopBuilder.MapWords(plan.Timeline, tr);
        bool hookOk = mapped.Any(m => m.Start <= p.Hook.MaxSec) || plan.Timeline.FirstOrDefault()?.Role == "teaser" || tr.Words.Count == 0;
        // an opening video before the first words delays the hook (a Shorts viewer decides in the first seconds); series parts are long-form
        var lead = InsertMap.Active(plan).FirstOrDefault(i => i.Kind == "opening" && i.At < 0.01);
        if (lead != null && plan.Series == null)
            res.Add(new("S-03", QaStatus.Warn, $"オープニング {lead.Duration:F1}s が先頭", $"発話 ≤{p.Hook.MaxSec}s", "冒頭フック（オープニングの前に見どころを先出しすると離脱を防げます）") { Items = [InsertItem(lead, "オープニングが先頭にあります")] });
        else
            res.Add(new("S-03", hookOk ? QaStatus.Pass : QaStatus.Warn, hookOk ? "speech/teaser in opening" : "no speech in opening", $"発話 ≤{p.Hook.MaxSec}s", "冒頭フック")
                { Items = hookOk || plan.Timeline.Count == 0 ? [] : [ClipItem(0, $"最初の {p.Hook.MaxSec} 秒に発話がありません")] });

        var shortShots = plan.Timeline.Select((c, ci) => (c, ci)).Where(x => x.c.Role == "body" && x.c.Length < p.Cut.MinShotSec - 0.01).ToList();
        res.Add(new("S-04", shortShots.Count == 0 ? QaStatus.Pass : QaStatus.Warn, shortShots.Count == 0 ? "ok" : string.Join(",", shortShots.Select(x => $"{x.c.ClipId} {x.c.Length:F2}s")), $"≥{p.Cut.MinShotSec}s", "最短ショット")
            { Items = shortShots.Select(x => ClipItem(x.ci, $"{x.c.Length:F2} 秒のショット")).ToList() });

        var firstBody = plan.Timeline.FirstOrDefault(c => c.Role == "body") ?? plan.Timeline.FirstOrDefault();
        var firstSentence = firstBody == null ? null : tr.Sentences.FirstOrDefault(s => s.T1 > firstBody.MasterIn + 0.05);
        bool demo = firstSentence != null && JapaneseText.StartsWithDemonstrative(firstSentence.Text);
        res.Add(new("S-07", demo ? QaStatus.Warn : QaStatus.Pass, demo ? firstSentence!.Text : "ok", "冒頭が指示語でない", "冒頭の指示語")
            { Items = demo ? [ClipItem(plan.Timeline.IndexOf(firstBody!), $"「{firstSentence!.Text}」")] : [] });

        // S-13: camera alignment is distinct from S-09's utterance negation check.
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
            res.Add(new("S-13", ratio >= 0.9 ? QaStatus.Pass : QaStatus.Warn, $"{ratio:P0} ({match}/{total})", "≥ 90% で話者とカメラが一致", "話者とカメラの一致"));
        }
        if (plan.Series is { } series)
        {
            // S-14: YouTube chapters; S-11: TikTok accepts uploads up to 60 minutes.
            var yt = Planning.SeriesPlanner.YouTubeChapters(FinalChapters(plan), fileDur);
            res.Add(new("S-14", yt != null ? QaStatus.Pass : QaStatus.Warn, yt != null ? $"{yt.Count} チャプター" : "条件を満たさない", "00:00 から・3つ以上・各10秒以上", "YouTube のチャプター"));
            res.Add(new("S-11", fileDur <= 3600 ? QaStatus.Pass : QaStatus.Warn, $"{fileDur / 60:F1}分", "≤ 60分（TikTok のアップロード上限）", "投稿先の長さの上限"));
        }
        // S-12: bumpers / card images are scaled to the canvas; upscaling softens them (e.g. a 720x1280 opening in a 1080x1920 output)
        var soft = plan.Inserts.Where(i => i.Enabled && i.Path != null && i.Width > 0)
            .Select(i => (i, s: BrandKitService.Upscale(i.Width, i.Height, plan.Canvas))).Where(x => x.s > 1.05 && x.i.Kind != "card").ToList();
        if (plan.Inserts.Any(i => i.Enabled && i.Path != null && i.Kind != "card"))
        {
            var enhanced = plan.Inserts.Where(i => i.Enabled && i.Kind != "card" && i.Enhanced != null && soft.All(x => x.i.Id != i.Id)).Select(i => $"{Path.GetFileName(i.OriginalPath)} {i.OriginalWidth}x{i.OriginalHeight} → {i.Width}x{i.Height} 高画質化済み").ToList();
            res.Add(new("S-12", soft.Count == 0 ? QaStatus.Pass : QaStatus.Warn,
                soft.Count == 0 ? (enhanced.Count > 0 ? string.Join(", ", enhanced) : "等倍以下") : string.Join(", ", soft.Select(x => $"{Path.GetFileName(x.i.Path)} {x.i.Width}x{x.i.Height} を {x.s:0.##} 倍に拡大")),
                $"{plan.Canvas.Width}x{plan.Canvas.Height} 以上の素材（小さい素材は登録時に高画質化）", "オープニング・クロージングの解像度")
                { Items = soft.Select(x => InsertItem(x.i, $"{x.s:0.##} 倍に拡大されます")).ToList() });
        }
        var hash = EditPlanFingerprint.Of(plan);
        return res.Select(q => QaAdvisor.Attach(q, plan) with { Stage = "plan", PlanHash = hash }).ToList();
    }

    /// <summary>Series chapters on the rendered file's timeline (shifted past the opening and cards).</summary>
    public static List<SeriesChapter> FinalChapters(EditPlan plan)
    {
        var ins = InsertMap.Active(plan);
        return plan.Series?.Chapters.Select(c => c with { Start = Math.Round(InsertMap.ToFinal(ins, c.Start), 3) }).ToList() ?? [];
    }

    static bool DurationOk(double body, double total, double min, double max, double cap, double tol) => body >= min - tol && body <= max + tol && total <= cap + tol;
    static string DurationText(double body, double total, string suffix = "") => Math.Abs(total - body) < 0.01 ? $"{body:F2}s{suffix}" : $"本編 {body:F2}s / 全体 {total:F2}s{suffix}";

    /// <summary>Shorts: the preset's range and the 180 s cap (C-1). Series parts: the part's own range around the requested length.</summary>
    public static (double Min, double Max, double Cap) DurationRange(EditPlan plan, Preset p) =>
        plan.Series is { } s ? (s.MinSec, s.MaxSec, double.PositiveInfinity) : (p.Target.Min, p.Target.Max, 180);

    static string RangeText(double min, double max, double cap) =>
        double.IsInfinity(cap) ? $"{min / 60:F1}–{max / 60:F1}分（{min:F0}–{max:F0}s）" : $"本編 {min}–{max}s, 全体 ≤{cap}s";

    /// <summary>Full QA on a rendered file. Preview renders skip loudness and black/freeze detection.</summary>
    public async Task<List<QaResult>> RunAsync(QaContext ctx, string rendered, bool preview, string workDir, CancellationToken ct)
    {
        var res = PlanChecks(ctx);
        // A half-size preview cannot certify the final file's platform constraints.
        var pr = await ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=codec_type,width,height:format=duration", "-of", "json", rendered], ct);
        using (var doc = JsonDocument.Parse(pr.StdOut))
        {
            var dur = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, IC);
            var v = doc.RootElement.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "video");
            int w = v.GetProperty("width").GetInt32(), h = v.GetProperty("height").GetInt32();
            var p = ctx.Preset;
            var (minD, maxD, cap) = DurationRange(ctx.Plan, p);
            // the preset range is for the edited body; the platform cap is for the whole file. The file must also match the planned length.
            double inserts = ctx.Plan.TotalDuration - ctx.Plan.Duration, body = dur - inserts;
            bool match = Math.Abs(dur - ctx.Plan.TotalDuration) <= Math.Max(0.25, 0.002 * ctx.Plan.TotalDuration);
            if (preview)
            {
                if (w != ctx.Plan.Canvas.Width / 2 || h != ctx.Plan.Canvas.Height / 2 || !match)
                    throw new FFmpegException("プレビューの解像度または尺が編集計画と一致しません");
                foreach (var (id, threshold, message) in new[]
                {
                    ("H-02", $"{ctx.Plan.Canvas.Width}x{ctx.Plan.Canvas.Height}", "解像度・比率 9:16"),
                    ("H-03", "−14 ±1 LUFS", "積分ラウドネス"),
                    ("H-04", "≤ −1.0 dBTP", "True Peak"),
                })
                    res.Add(new(id, QaStatus.NotRun, "未実施（書き出し時に検査）", threshold, message) { Stage = "final" });
            }
            else
            {
                res.Add(new("H-01", DurationOk(body, dur, minD, maxD, cap, 0.1) && match ? QaStatus.Pass : QaStatus.Fail, DurationText(body, dur, " (ffprobe)") + (match ? "" : $"・計画 {ctx.Plan.TotalDuration:F2}s と不一致"), RangeText(minD, maxD, cap), "尺") { Stage = "final" });
                res.Add(new("H-02", w == ctx.Plan.Canvas.Width && h == ctx.Plan.Canvas.Height ? QaStatus.Pass : QaStatus.Fail, $"{w}x{h}", $"{ctx.Plan.Canvas.Width}x{ctx.Plan.Canvas.Height}", "解像度・比率 9:16") { Stage = "final" });
            }
        }
        if (!preview)
        {
            var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", rendered, "-vn", "-af", "loudnorm=I=-14:TP=-1.5:print_format=summary", "-f", "null", "-"], ct);
            double I = Num(r.StdErr, @"Input Integrated:\s*(-?[\d.]+)"), TP = Num(r.StdErr, @"Input True Peak:\s*([-+]?[\d.]+)");
            res.Add(new("H-03", Math.Abs(I - ctx.Plan.Audio.TargetLufs) <= 1 ? QaStatus.Pass : QaStatus.Fail, $"{I:F1} LUFS", "−14 ±1 LUFS", "積分ラウドネス") { Stage = "final" });
            res.Add(new("H-04", TP <= -1.0 ? QaStatus.Pass : QaStatus.Fail, $"{TP:F1} dBTP", "≤ −1.0 dBTP", "True Peak") { Stage = "final" });
        }
        var safe = await SafeZoneAsync(ctx, workDir, ct);
        res.Add(safe with { Stage = "plan" });
        if (!preview) res.Add(safe with { Stage = "final" });
        if (!preview && Faces is { Available: true }) res.Add(await FaceOverlapAsync(ctx, rendered, ct));
        else if (LayoutOmissions(ctx) is { } omissions) res.Add(omissions);
        if (!preview) res.Add(await BlackFreezeAsync(rendered, ctx.Preset.Id == "P9", ct, InsertMap.InsertSpans(InsertMap.Active(ctx.Plan)).Select(x => (x.Start, x.End)).ToList()));
        var hash = EditPlanFingerprint.Of(ctx.Plan);
        return res.Select(q => QaAdvisor.Attach(q) with { Stage = q.Stage ?? (preview ? "preview" : "final"), PlanHash = hash })
            .OrderBy(r => r.Hard ? 0 : 1).ThenBy(r => r.CheckId, StringComparer.Ordinal).ToList();
    }

    static double Num(string s, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, pattern);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out var d) ? d : double.NaN;
    }

    /// <summary>
    /// S-15: speech over music, measured on the rendered stems (Renderer.MixReport, FFmpeg ebur128). Pass when the loudness difference while speaking is at
    /// least the research floor (10 LU, Torcoli et al. JAES 2019) and within 1.5 LU of the plan's target.
    /// </summary>
    public static QaResult SpeechOverMusic(Rendering.BgmMixReport m)
    {
        const string name = "声と BGM の音量差（話している間）";
        string threshold = $"≥ {m.TargetLd:0.#} LU（下限 {Planning.BgmPlanner.ResearchFloorLd:0} LU）";
        if (m.SpeechBlocks == 0) return new("S-15", QaStatus.Pass, "発話なし", threshold, name);
        bool ok = m.Ld >= Planning.BgmPlanner.ResearchFloorLd && m.Ld >= m.TargetLd - 1.5;
        string ld = m.Ld >= 99 ? "BGM なし" : $"{m.Ld:0.0} LU";
        string gap = m.GapLu is double g ? $"・声のない所の BGM は声より {-g:0.0} LU 小さい（設定 {-m.GapTargetLu:0.#}）" : "";
        return new("S-15", ok ? QaStatus.Pass : QaStatus.Warn, $"{ld}（下位10% {Math.Min(m.LdP10, 99):0.0} LU・{m.SpeechBlocks * 0.1:0} 秒分）{gap}", threshold, name);
    }

    /// <summary>Report face-protection omissions even in previews and when the detector is unavailable.</summary>
    public static QaResult? LayoutOmissions(QaContext ctx)
    {
        var layout = TelopLayout.Resolve(ctx.Plan, ctx.Preset);
        var ins = InsertMap.Active(ctx.Plan);
        var items = ctx.Plan.Telops.Where(t => t.Enabled && layout[t.Id].Reason != null)
            .Select(t => new QaItem(t.Id, "telop", layout[t.Id].Reason!,
                Math.Round(InsertMap.ToFinal(ins, t.Start), 3), Math.Round(InsertMap.ToFinal(ins, t.End, true), 3))).ToList();
        return items.Count == 0 ? null : new("S-05", QaStatus.Warn, $"顔保護のため演出テロップ {items.Count} 件を非表示",
            "顔にテロップが重ならない", "顔とテロップの重なり") { Items = items };
    }

    /// <summary>S-05: independently detect faces near the start, middle and end of sampled rendered telops, including top bars and SFX.</summary>
    public async Task<QaResult> FaceOverlapAsync(QaContext ctx, string rendered, CancellationToken ct)
    {
        int H = ctx.Plan.Canvas.Height, W = ctx.Plan.Canvas.Width;
        var layout = TelopLayout.Resolve(ctx.Plan, ctx.Preset);
        var cand = ctx.Plan.Telops.Where(t => t.Enabled && layout[t.Id].Bounds != null).OrderBy(t => t.Start).ToList();
        var omissions = LayoutOmissions(ctx);
        if (cand.Count == 0) return omissions ?? new("S-05", QaStatus.Pass, "no telops", "顔にテロップが重ならない", "顔とテロップの重なり");
        int step = Math.Max(1, (int)Math.Ceiling(cand.Count / 12.0)), checkedN = 0;
        var hits = new List<string>(); var items = omissions?.Items?.ToList() ?? [];
        var ins = InsertMap.Active(ctx.Plan);
        for (int i = 0; i < cand.Count; i += step)
        {
            var t = cand[i]; var bounds = layout[t.Id].Bounds!;
            foreach (double bodyTime in new[] { t.Start + Math.Min(0.25, (t.End - t.Start) / 4), (t.Start + t.End) / 2, t.End - Math.Min(0.1, (t.End - t.Start) / 4) }.Distinct())
            {
                var mid = InsertMap.ToFinal(ins, bodyTime);
                var faces = await Faces!.DetectAtAsync(rendered, mid, ct); checkedN++;
                foreach (var f in faces)
                {
                    if (bounds.Intersects(new TelopLayout.Rect(f.X * W, f.Y * H, (f.X + f.W) * W, (f.Y + f.H) * H)))
                    {
                        hits.Add($"{t.Id}@{mid:F1}s");
                        if (items.All(x => x.Target != t.Id)) items.Add(new(t.Id, "telop", "顔にテロップが重なっています", Math.Round(InsertMap.ToFinal(ins, t.Start), 3), Math.Round(InsertMap.ToFinal(ins, t.End, true), 3)));
                    }
                }
            }
        }
        var measured = hits.Count == 0 ? $"{checkedN} 箇所で重なりなし" : string.Join(", ", hits.Take(3));
        if (omissions != null) measured += "・" + omissions.Measured;
        return new("S-05", items.Count == 0 ? QaStatus.Pass : QaStatus.Warn, measured, "顔にテロップが重ならない", "顔とテロップの重なり") { Items = items };
    }

            /// <summary>Long series parts have hundreds of captions; the measurement keeps the widest ones (most likely to overflow) plus an even spread.</summary>
    public const int MaxSafeZoneSamples = 160;

    public static List<Telop> SafeZoneSample(List<Telop> telops)
    {
        if (telops.Count <= MaxSafeZoneSamples) return telops;
        var widest = telops.OrderByDescending(t => JapaneseText.DisplayWidth(t.Text)).ThenBy(t => t.Start).Take(MaxSafeZoneSamples / 2);
        int step = (int)Math.Ceiling(telops.Count / (MaxSafeZoneSamples / 2.0));
        var spread = telops.OrderBy(t => t.Start).Where((_, i) => i % step == 0);
        return widest.Concat(spread).Concat(telops.Where(t => t.Type != TelopType.Caption).Take(MaxSafeZoneSamples / 4)).Distinct().OrderBy(t => t.Start).ToList();
    }

    /// <summary>H-05 measured: render the telop layer alone (half resolution, 10 fps) and compute glyph bounding boxes from raw pixels. Scene-card text is measured at its place on the final timeline.</summary>
    public async Task<QaResult> SafeZoneAsync(QaContext ctx, string workDir, CancellationToken ct)
    {
        var layout = TelopLayout.Resolve(ctx.Plan, ctx.Preset);
        var telops = SafeZoneSample(ctx.Plan.Telops.Where(t => t.Enabled && t.End - t.Start > 0.15).ToList());
        var ins = InsertMap.Active(ctx.Plan);
        var cards = InsertMap.InsertSpans(ins).Where(x => x.Insert.Kind == "card" && x.Insert.Text.Length > 0).ToList();
        if (telops.Count == 0 && cards.Count == 0) return new("H-05", QaStatus.Pass, "no telops", "safe zone", "テロップの安全領域");
        Directory.CreateDirectory(workDir);
        var violations = new List<string>(); var items = new List<QaItem>();
        var groups = new List<(string Name, bool Top, EditPlan Plan, List<double> Times, double Dur, List<(string Id, string Kind, double Start, double End)> Owners)>();
        foreach (var g in new[] { telops.Where(t => t.Type is not (TelopType.Hook or TelopType.Headline)).ToList(), telops.Where(t => t.Type is TelopType.Hook or TelopType.Headline).ToList() })
        {
            if (g.Count == 0) continue;
            bool top = g[0].Type is TelopType.Hook or TelopType.Headline;
            groups.Add((top ? "qa_top" : "qa_body", top, ctx.Plan with { Telops = g, Inserts = [] }, g.Select(t => t.Start + Math.Min(0.6, (t.End - t.Start) / 2)).ToList(), ctx.Plan.Duration,
                g.Select(t => (t.Id, "telop", InsertMap.ToFinal(ins, t.Start), InsertMap.ToFinal(ins, t.End, true))).ToList()));
        }
        if (cards.Count > 0)
            groups.Add(("qa_cards", false, ctx.Plan with { Telops = [] },             cards.Select(x => x.Start + Math.Min(0.6, (x.End - x.Start) / 2)).ToList(), ctx.Plan.TotalDuration,
                            cards.Select(x => (x.Insert.Id, "insert", x.Start, x.End)).ToList()));
                    foreach (var (name, top, plan, times, total, owners) in groups)
        {
            var ass = Path.Combine(workDir, name + ".ass");
            await File.WriteAllTextAsync(ass, AssWriter.Write(plan, ctx.Preset, layout), new UTF8Encoding(false), ct);
            const int W = 540, H = 960; const double fps = 10;
            var frames = times.Select(t => (int)Math.Round(t * fps)).Distinct().OrderBy(x => x).ToList();
            var select = string.Join("+", frames.Select(f => $"eq(n,{f})"));
            using var ms = new MemoryStream();
            var dur = (total + 1).ToString("0.##", IC);
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
                if (ok) continue;
                violations.Add($"t≈{frames.ElementAtOrDefault(k) / fps:F1}s bbox x{x1}-{x2} y{y1}-{y2}");
                for (int o = 0; o < times.Count; o++)
                    if ((int)Math.Round(times[o] * fps) == frames.ElementAtOrDefault(k) && items.All(x => x.Target != owners[o].Id))
                        items.Add(new(owners[o].Id, owners[o].Kind, $"文字が安全領域からはみ出しています（x{x1}–{x2}, y{y1}–{y2}）", Math.Round(owners[o].Start, 3), Math.Round(owners[o].End, 3)));
            }
        }
        return new("H-05", violations.Count == 0 ? QaStatus.Pass : QaStatus.Fail, violations.Count == 0 ? "all telops inside (measured)" : string.Join("; ", violations.Take(3)),
            $"x {SafeZone.Left}–{1080 - SafeZone.Right}, y {SafeZone.Top}–{1920 - SafeZone.Bottom}", "テロップの安全領域（実測）") { Items = items };
    }

    /// <summary>S-06. Inserts are excluded: a scene card is a deliberate 1 s still, and bumpers may hold a title or fade to a solid colour.</summary>
    public async Task<QaResult> BlackFreezeAsync(string file, bool allowFreeze, CancellationToken ct, IReadOnlyList<(double Start, double End)>? exclude = null)
    {
        var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", file, "-an", "-vf", "scale=270:-2,blackdetect=d=0.5:pix_th=0.1,freezedetect=n=0.003:d=0.5", "-f", "null", "-"], ct);
        bool Inside(System.Text.RegularExpressions.Match m) => exclude != null && double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out var t) && exclude.Any(x => t >= x.Start - 0.1 && t <= x.End + 0.1);
        int black = System.Text.RegularExpressions.Regex.Matches(r.StdErr, @"black_start:\s*([\d.]+)").Count(m => !Inside(m));
        int freeze = allowFreeze ? 0 : System.Text.RegularExpressions.Regex.Matches(r.StdErr, @"freeze_start:\s*([\d.]+)").Count(m => !Inside(m));
        return new("S-06", black + freeze == 0 ? QaStatus.Pass : QaStatus.Warn, $"black={black} freeze={freeze}" + (exclude is { Count: > 0 } ? "（オープニング・カード・クロージングを除く）" : ""), "0 (≥0.5s)", "黒画面・フリーズ");
    }
}
