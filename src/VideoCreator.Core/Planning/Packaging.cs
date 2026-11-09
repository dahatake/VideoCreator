using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Selection;

namespace VideoCreator.Planning;

/// <summary>
/// Body timeline → rendered-file timeline. Telops, SFX, chapters and QA times are planned on the body (EditPlan.Timeline);
/// inserts (opening, scene cards, closing) sit at clip boundaries and push everything after them later. This is the only place that does that arithmetic.
/// </summary>
public static class InsertMap
{
    const double Eps = 1e-3;
    static int Order(Insert i) => i.Kind switch { "opening" => 0, "card" => 1, _ => 2 };

    /// <summary>Enabled inserts in playback order.</summary>
    public static List<Insert> Active(EditPlan plan) => plan.Inserts.Where(i => i.Enabled && i.Duration > 0).OrderBy(i => i.At).ThenBy(Order).ToList();

    /// <summary>Final time of body time <paramref name="t"/>. A start exactly at an insert's position plays after the insert; an end there plays before it.</summary>
    public static double ToFinal(IReadOnlyList<Insert> active, double t, bool end = false)
    {
        double off = 0;
        foreach (var i in active) if (i.At < t - Eps || (!end && i.At <= t + Eps)) off += i.Duration;
        return t + off;
    }

    public static double ToFinal(EditPlan plan, double t, bool end = false) => ToFinal(Active(plan), t, end);

    /// <summary>Final start of an insert.</summary>
    public static double StartOf(IReadOnlyList<Insert> active, Insert ins)
    {
        double off = 0;
        foreach (var i in active) { if (ReferenceEquals(i, ins) || i.Id == ins.Id) break; off += i.Duration; }
        return ins.At + off;
    }

    /// <summary>A body span on the final timeline, split around inserts that fall inside it (so nothing is drawn over a card or bumper).</summary>
    public static List<(double Start, double End)> Span(IReadOnlyList<Insert> active, double start, double end)
    {
        var cuts = active.Select(i => i.At).Where(a => a > start + Eps && a < end - Eps).Distinct().OrderBy(a => a).ToList();
        var res = new List<(double, double)>(); double a0 = start;
        foreach (var c in cuts.Append(end))
        {
            double s = ToFinal(active, a0), e = ToFinal(active, c, end: true);
            if (e - s > Eps) res.Add((s, e));
            a0 = c;
        }
        return res;
    }

    /// <summary>Final-timeline spans occupied by inserts (QA excludes them from freeze/black detection).</summary>
    public static List<(Insert Insert, double Start, double End)> InsertSpans(IReadOnlyList<Insert> active) =>
        active.Select(i => { var s = StartOf(active, i); return (i, s, s + i.Duration); }).ToList();
}

/// <summary>Registers the channel's opening / closing videos and card images: classify by file name, probe, measure loudness.</summary>
public static partial class BrandKitService
{
    public static readonly string[] VideoExt = [".mp4", ".mov", ".m4v", ".mkv", ".webm"];
    public static readonly string[] ImageExt = [".png", ".jpg", ".jpeg", ".webp"];
    public const double MinBumperSec = 0.5, MaxBumperSec = 60, MinCardSec = 2.0, MaxCardSec = 3.0;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex TokenSplit();

    /// <summary>opening | closing | card | null (unknown) from the file name.</summary>
    public static string? Classify(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ImageExt.Contains(ext)) return "card";
        if (!VideoExt.Contains(ext)) return null;
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var tokens = TokenSplit().Split(name).Where(t => t.Length > 0).ToHashSet();
        bool Has(params string[] keys) => keys.Any(k => tokens.Contains(k) || (k.Length > 3 && name.Contains(k, StringComparison.Ordinal)));
        bool open = Has("opening", "intro", "op", "オープニング"), close = Has("closing", "ending", "outro", "ed", "クロージング", "エンディング");
        return open == close ? null : open ? "opening" : "closing";
    }

    /// <summary>Files (or top-level files of folders) → brand kit. Videos whose role cannot be told from the name are reported, not guessed.</summary>
    public static async Task<(BrandKit Kit, List<string> Problems)> ScanAsync(FFmpeg ff, IEnumerable<string> sources, bool rightsConfirmed, double cardSec, CancellationToken ct)
    {
        var problems = new List<string>(); var files = new List<BrandFile>(); var srcs = new List<string>();
        var paths = new List<string>();
        foreach (var raw in sources.Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0))
        {
            if (!Path.IsPathFullyQualified(raw)) { problems.Add($"フルパスで指定してください: {raw}"); continue; }
            var full = Path.GetFullPath(raw); srcs.Add(full);
            if (Directory.Exists(full)) paths.AddRange(Directory.GetFiles(full).Where(f => Classify(f) != null || VideoExt.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(full)) paths.Add(full);
            else problems.Add($"見つかりません: {full}");
        }
        var probe = new MediaProbe(ff);
        foreach (var f in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var kind = Classify(f);
            if (kind == null) { problems.Add($"オープニングかクロージングか判別できません（ファイル名に Opening / Closing を含めてください）: {Path.GetFileName(f)}"); continue; }
            MediaInfo m;
            try { m = await probe.ProbeAsync(f, ct); }
            catch (IngestException ex) { problems.Add($"{Path.GetFileName(f)}: {ex.Message}"); continue; }
            if (kind != "card" && m.Duration is < MinBumperSec or > MaxBumperSec) { problems.Add($"{Path.GetFileName(f)}: {MinBumperSec}〜{MaxBumperSec} 秒の動画にしてください（{m.Duration:F1} 秒）"); continue; }
            double lufs = double.NaN;
            if (kind != "card" && m.AudioChannels > 0)
            {
                var r = await ff.RunFfmpegAsync(["-hide_banner", "-i", f, "-vn", "-af", "ebur128", "-f", "null", "-"], ct);
                var mm = Regex.Matches(r.StdErr, @"I:\s+(-?[\d.]+) LUFS");
                if (mm.Count > 0 && double.TryParse(mm[^1].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > -70) lufs = v;
            }
            files.Add(new BrandFile { Path = f, Kind = kind, Width = m.Width, Height = m.Height, Duration = kind == "card" ? 0 : Math.Round(m.Duration, 3), HasAudio = m.AudioChannels > 0, Lufs = lufs });
        }
        var kit = new BrandKit { Sources = srcs, Files = files, RightsConfirmed = rightsConfirmed, CardSec = Math.Clamp(cardSec, MinCardSec, MaxCardSec) };
        return (kit, problems);
    }

    /// <summary>The variant whose shape is closest to the canvas (e.g. Opening-9x16 for a 1080x1920 output), then the sharpest.</summary>
    public static BrandFile? Pick(IEnumerable<BrandFile> files, string kind, Canvas canvas)
    {
        double target = (double)canvas.Width / canvas.Height;
        return files.Where(f => f.Kind == kind && f.Aspect > 0).OrderBy(f => Math.Abs(Math.Log(f.Aspect / target))).ThenByDescending(f => f.Height).FirstOrDefault();
    }

    /// <summary>Scale factor the renderer applies to fit the file into the canvas (> 1 = upscaled, softer).</summary>
    public static double Upscale(int w, int h, Canvas canvas) => w <= 0 || h <= 0 ? 1 : Math.Min((double)canvas.Width / w, (double)canvas.Height / h);
}

/// <summary>
/// Registration-time upscale of opening / closing videos smaller than the canvas (e.g. 720x1280 for a 1080x1920 output). Per file, the method is
/// first checked on the file itself: its middle (up to 4 s) is downscaled to 2/3, brought back with the render-time lanczos and with
/// <see cref="SuperResolution"/>, and both are compared with the original. The upscaled copy is only used when it is at least as faithful
/// (VMAF-NEG not lower, XPSNR not more than 0.1 dB lower); otherwise the file is used as registered and the render scales it with lanczos.
/// Results are cached by file content (name, size, time) next to a JSON record, so re-registering the same files is instant.
/// </summary>
public static class BrandKitEnhancer
{
    public const double MinFactor = 1.05, SampleSec = 4;

    public static bool Needs(BrandFile f, Canvas canvas) => f.Kind != "card" && BrandKitService.Upscale(f.Width, f.Height, canvas) > MinFactor;

    static int Even(double x) => Math.Max(2, (int)Math.Round(x / 2) * 2);

    public static async Task<BrandFile> EnhanceAsync(FFmpeg ff, SuperResolution sr, BrandFile f, Canvas canvas, string cacheDir, IProgress<(string Stage, double Progress)>? progress, CancellationToken ct)
    {
        if (!Needs(f, canvas)) return f;
        double factor = BrandKitService.Upscale(f.Width, f.Height, canvas);
        int ow = Even(f.Width * factor), oh = Even(f.Height * factor);
        var fi = new FileInfo(f.Path);
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{SuperResolution.Method}|{SuperResolution.DetailSigma}|{SuperResolution.DetailGain}")))[..16];
        Directory.CreateDirectory(cacheDir);
        var dest = Path.Combine(cacheDir, $"{Path.GetFileNameWithoutExtension(f.Path)}_{key}_{ow}x{oh}.mp4");
        var record = dest + ".json";
        if (File.Exists(record) && Json.Deserialize<BrandEnhancement>(await File.ReadAllTextAsync(record, ct)) is { } cached && (cached.Path == null || File.Exists(cached.Path)))
            return f with { Enhancement = cached };

        var m = await new MediaProbe(ff).ProbeAsync(f.Path, ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var work = Path.Combine(cacheDir, "work_" + key); Directory.CreateDirectory(work);
        try
        {
            // 1) does it help on THIS file?
            progress?.Report(("確認", 0));
            double len = Math.Min(SampleSec, m.Duration), start = Math.Max(0, m.Duration / 2 - len / 2);
            string I(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
            int lw = Even(m.Width * 2.0 / 3), lh = Even(m.Height * 2.0 / 3);
            string reference = Path.Combine(work, "ref.mp4"), lo = Path.Combine(work, "lo.mp4"), lz = Path.Combine(work, "lanczos.mp4"), en = Path.Combine(work, "enhanced.mp4");
            await ff.RunFfmpegCheckedAsync(["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-ss", I(start), "-t", I(len), "-i", f.Path, "-an", "-vf", "format=yuv420p", "-c:v", "libx264", "-crf", "4", "-preset", "veryfast", reference], ct);
            await ff.RunFfmpegCheckedAsync(["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", reference, "-vf", $"scale={lw}:{lh}:flags=area,format=yuv420p", "-c:v", "libx264", "-crf", "4", "-preset", "veryfast", lo], ct);
            await ff.RunFfmpegCheckedAsync(["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", lo, "-vf", $"scale={m.Width}:{m.Height}:flags=lanczos,format=yuv420p", "-c:v", "libx264", "-crf", "10", "-preset", "slow", lz], ct);
            await sr.UpscaleVideoAsync(ff, lo, lw, lh, m.Fps, m.Width, m.Height, en, new Progress<double>(p => progress?.Report(("確認", 0.2 * p))), ct);
            var a = await VideoQuality.ScoreAsync(ff, lz, reference, ct);
            var b = await VideoQuality.ScoreAsync(ff, en, reference, ct);
            var gain = new QualityGain(Math.Round(b.Vmaf - a.Vmaf, 2), Math.Round(b.Xpsnr - a.Xpsnr, 2), Math.Round(b.Ssim - a.Ssim, 5));
            bool better = !(gain.Vmaf < 0) && !(gain.Xpsnr < -0.1);   // NaN (metric unavailable) does not block
            BrandEnhancement result;
            if (!better)
                result = new BrandEnhancement { Method = SuperResolution.Method, Device = sr.Device, Gain = gain, Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1),
                    Note = "この動画では高画質化しても元に忠実にならなかったため、元の動画を使います（書き出し時に lanczos で拡大）" };
            else
            {
                // 2) the real job
                await sr.UpscaleVideoAsync(ff, f.Path, m.Width, m.Height, m.Fps, ow, oh, dest, new Progress<double>(p => progress?.Report(("高画質化", 0.2 + 0.8 * p))), ct);
                result = new BrandEnhancement { Path = dest, Width = ow, Height = oh, Method = SuperResolution.Method, Device = sr.Device, Gain = gain, Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1),
                    Note = $"{m.Width}×{m.Height} を {ow}×{oh} に高画質化しました" };
            }
            await File.WriteAllTextAsync(record, Json.Serialize(result), ct);
            progress?.Report(("完了", 1));
            return f with { Enhancement = result };
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
/// <summary>Places the opening (after the cold open), scene-change cards and the closing on clip boundaries of a planned timeline.</summary>
public static class InsertPlanner
{
    /// <summary>A topic jump in master time larger than this between consecutive body clips is a scene change (same rule as the FR-SWT-05 cross-fade).</summary>
    public const double JumpSec = 3.0;
    public const double CardMinGapSec = 8, CardEdgeSec = 3, SnapSec = 4;
    public const int MaxShortCards = 3;
    /// <summary>Neutral connectives used when no AI is available: no claims, no humour aimed at people.</summary>
    public static readonly string[] FixedPhrases = ["さて、次！", "ところで…", "話は変わって", "続いては…"];
    public const string CardSfx = "hit";

    public sealed record Boundary(int ClipIndex, double At, double SourceT, string Reason);

    static bool IsBody(Clip c) => c.Role is "body" or "reaction" or "wide";

    /// <summary>Clip boundaries where the scene changes: series → chapter starts (snapped to the nearest cut); shorts → source jumps.</summary>
    public static List<Boundary> SceneChanges(IReadOnlyList<Clip> clips, SeriesInfo? series)
    {
        var starts = new double[clips.Count + 1];
        for (int i = 0; i < clips.Count; i++) starts[i + 1] = starts[i] + clips[i].Length;
        var cuts = Enumerable.Range(1, Math.Max(0, clips.Count - 1)).Where(i => IsBody(clips[i - 1]) && IsBody(clips[i])).ToList();
        var found = new List<Boundary>();
        if (series != null)
        {
            int firstBody = clips.ToList().FindIndex(IsBody);
            if (firstBody < 0) return [];
            double bodyStart = starts[firstBody];
            foreach (var ch in series.Chapters.Where(c => c.Start > bodyStart + 1).OrderBy(c => c.Start))
            {
                var best = cuts.Where(i => Math.Abs(starts[i] - ch.Start) <= SnapSec).OrderBy(i => Math.Abs(starts[i] - ch.Start)).Select(i => (int?)i).FirstOrDefault();
                if (best is int b && found.All(f => f.ClipIndex != b)) found.Add(new Boundary(b, starts[b], clips[b - 1].MasterOut, $"章「{ch.Title}」の頭"));
            }
        }
        else
        {
            foreach (var i in cuts)
                if (Math.Abs(clips[i].MasterIn - clips[i - 1].MasterOut) > JumpSec)
                    found.Add(new Boundary(i, starts[i], clips[i - 1].MasterOut, $"元動画で {Math.Abs(clips[i].MasterIn - clips[i - 1].MasterOut):F0} 秒先へ飛ぶ所"));
        }
        double total = starts[^1];
        var kept = new List<Boundary>();
        foreach (var b in found.OrderBy(b => b.At))
        {
            if (b.At < CardEdgeSec || total - b.At < CardEdgeSec) continue;
            if (kept.Count > 0 && b.At - kept[^1].At < CardMinGapSec) continue;
            kept.Add(b);
        }
        return series == null ? kept.Take(MaxShortCards).ToList() : kept;
    }

    /// <summary>
    /// Series: a chapter start with no cut nearby (continuous talk has few silences to jump-cut) gets one — the body clip is split there,
    /// so the card lands on the topic change. The halves are contiguous in source time, so nothing else moves.
    /// </summary>
    public static List<Clip> SplitAtChapters(List<Clip> clips, SeriesInfo series)
    {
        var res = clips.ToList();
        foreach (var ch in series.Chapters.OrderBy(c => c.Start))
        {
            double off = 0; int idx = -1;
            for (int i = 0; i < res.Count; i++) { if (ch.Start > off + 0.5 && ch.Start < off + res[i].Length - 0.5) { idx = i; break; } off += res[i].Length; }
            if (idx < 0 || !IsBody(res[idx])) continue;
            double near = 0; var nearest = double.MaxValue;
            for (int i = 0; i < res.Count; i++) { nearest = Math.Min(nearest, Math.Abs(near - ch.Start)); near += res[i].Length; }
            if (nearest <= SnapSec) continue;
            SplitAt(res, ch.Start);
        }
        return res;
    }

    /// <summary>Split at body time without removing source frames or shifting camera/audio/PiP tracks.</summary>
    public static void SplitAt(List<Clip> clips, double at)
    {
        double off = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            var c = clips[i]; double rel = at - off;
            if (rel > 0.001 && rel < c.Length - 0.001)
            {
                double src = c.SrcIn + rel * c.Speed;
                List<TrackPoint>? Shift(List<TrackPoint>? tr)
                {
                    if (tr is not { Count: > 0 }) return tr;
                    double x = tr.Count == 1 || rel <= tr[0].T ? tr[0].X : rel >= tr[^1].T ? tr[^1].X
                        : tr.Zip(tr.Skip(1)).Where(p => rel >= p.First.T && rel <= p.Second.T).Select(p => p.First.X + (p.Second.X - p.First.X) * (rel - p.First.T) / (p.Second.T - p.First.T)).First();
                    return [new TrackPoint(0, x), .. tr.Where(p => p.T > rel).Select(p => p with { T = p.T - rel })];
                }
                clips[i] = c with { SrcOut = src };
                clips.Insert(i + 1, c with
                {
                    ClipId = c.ClipId + "s", SrcIn = src, TransitionSec = 0,
                    AudioIn = c.AudioIn is double a ? a + rel * c.Speed : null,
                    Reframe = c.Reframe with { Track = Shift(c.Reframe.Track), TrackY = Shift(c.Reframe.TrackY) },
                    Pip = c.Pip is { } pip ? pip with { SrcIn = pip.SrcIn + rel * c.Speed } : null,
                });
                return;
            }
            off += c.Length;
        }
    }

    public static double CardDuration(BrandKit? kit, Canvas canvas) =>
        Math.Round(Math.Clamp(kit?.CardSec ?? 2.5, BrandKitService.MinCardSec, BrandKitService.MaxCardSec) * canvas.Fps) / canvas.Fps;

    public static void AddTsukkomi(List<Clip> clips, List<Insert> inserts, IEnumerable<(double At, string Text)> suggestions, BrandKit? kit, Canvas canvas, List<string> notes)
    {
        var image = kit is { RightsConfirmed: true } ? BrandKitService.Pick(kit.Files, "card", canvas) : null;
        foreach (var (at, text) in suggestions.OrderBy(x => x.At))
        {
            if (at <= 0 || at >= clips.Sum(c => c.Length) - 0.001) { notes.Add($"ツッコミ「{text}」は本編の端にあるため挿入しません"); continue; }
            if (inserts.Any(i => i.Kind == "card" && Math.Abs(i.At - at) < 1.5)) { notes.Add($"ツッコミ「{text}」は近接するカードがあるため挿入しません"); continue; }
            SplitAt(clips, at);
            double off = 0, sourceT = 0;
            for (int k = 0; k < clips.Count; k++)
            {
                var c = clips[k];
                off += c.Length;
                if (Math.Abs(off - at) < 0.001)
                {
                    sourceT = c.MasterOut;
                    if (k + 1 < clips.Count) clips[k + 1] = clips[k + 1] with { TransitionSec = 0 };
                    break;
                }
            }
            inserts.Add(new Insert
            {
                Id = $"tsukkomi{inserts.Count + 1}", Kind = "card", At = at, Duration = CardDuration(kit, canvas),
                Text = text, TextSource = "ai", Approved = false, SourceT = sourceT, Sfx = CardSfx,
                Path = image?.Path, Width = image?.Width ?? 0, Height = image?.Height ?? 0,
                RightsConfirmed = image != null, ImageAssetId = image?.ImageAssetId,
            });
        }
        int count = inserts.Count(i => i.Id.StartsWith("tsukkomi", StringComparison.Ordinal));
        if (count > 0) notes.Add($"ツッコミを {count} 枚の停止カードとして提案しました（承認するまで書き出せません）");
    }

    public static (List<Clip> Clips, List<Insert> Inserts) Plan(List<Clip> clips, JobOptions o, BrandKit? kit, Canvas canvas, double targetLufs, SeriesInfo? series, List<string> notes)
    {
        var inserts = new List<Insert>();
        if (clips.Count == 0) return (clips, inserts);
        if (o.SceneCards && series != null) clips = SplitAtChapters(clips, series);
        double total = clips.Sum(c => c.Length);
        Insert? Bumper(string kind, double at)
        {
            var f = kit is { RightsConfirmed: true } ? BrandKitService.Pick(kit.Files, kind, canvas) : null;
            if (f == null) { notes.Add(kind == "opening" ? "オープニング動画が登録されていないため入れませんでした" : "クロージング動画が登録されていないため入れませんでした"); return null; }
            double gain = double.IsNaN(f.Lufs) ? 0 : Math.Round(Math.Clamp(targetLufs - f.Lufs, -20, 12), 2);
            var e = f.Enhancement is { Adopted: true } x && File.Exists(x.Path) ? x : null;
            return new Insert
            {
                Id = kind, Kind = kind, At = Math.Round(at, 3), Duration = Math.Round(Math.Floor(f.Duration * canvas.Fps) / canvas.Fps, 3),
                Path = e?.Path ?? f.Path, Width = e?.Width ?? f.Width, Height = e?.Height ?? f.Height, OriginalPath = e != null ? f.Path : null, OriginalWidth = f.Width, OriginalHeight = f.Height, Enhanced = e?.Method,
                HasAudio = f.HasAudio, GainDb = gain, RightsConfirmed = true,
            };
        }
        if (o.Opening)
        {
            // cold open (teaser) first, then the title — the variety-show order; with no teaser the opening is the first thing on screen
            double at = clips.TakeWhile(c => c.Role == "teaser").Sum(c => c.Length);
            if (Bumper("opening", at) is { } op)
            {
                inserts.Add(op);
                notes.Add(at > 0 ? $"先出し（{at:F1} 秒）のあとにオープニング（{op.Duration:F1} 秒）を入れました" : $"冒頭にオープニング（{op.Duration:F1} 秒）を入れました");
            }
        }
        if (o.SceneCards)
        {
            var bounds = SceneChanges(clips, series);
            var images = kit is { RightsConfirmed: true } ? kit.Files.Where(f => f.Kind == "card").ToList() : [];
            double sec = CardDuration(kit, canvas);
            int k = 0, faded = 0;
            foreach (var b in bounds)
            {
                var img = images.Count > 0 ? images[k % images.Count] : null;
                inserts.Add(new Insert
                {
                    Id = $"card{k + 1}", Kind = "card", At = Math.Round(b.At, 3), Duration = sec, Path = img?.Path, Width = img?.Width ?? 0, Height = img?.Height ?? 0, RightsConfirmed = img != null,
                    ImageAssetId = img?.ImageAssetId,
                    Text = FixedPhrases[k % FixedPhrases.Length], TextSource = "fixed", SourceT = Math.Round(b.SourceT, 3), Sfx = CardSfx,
                });
                if (clips[b.ClipIndex].TransitionSec > 0) { clips[b.ClipIndex] = clips[b.ClipIndex] with { TransitionSec = 0 }; faded++; }
                k++;
            }
            notes.Add(bounds.Count > 0 ? $"場面転換カード（{sec:0.0#} 秒）を {bounds.Count} か所に入れました: {string.Join("、", bounds.Select(b => b.Reason))}" : "場面が大きく変わる所がないため、場面転換カードは入れませんでした");
            if (faded > 0) notes.Add($"カードを入れた {faded} か所はクロスフェードの代わりにカードで場面を変えます");
        }
        if (o.Closing && Bumper("closing", total) is { } cl) { inserts.Add(cl); notes.Add($"最後にクロージング（{cl.Duration:F1} 秒）を入れました"); }
        return (clips, inserts);
    }

    public const string CardSchema = """{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["card_id","text"],"properties":{"card_id":{"type":"string"},"text":{"type":"string"}}}}}}""";
    const string CardSystem = """
あなたは日本のバラエティ系 YouTube の編集者です。映像を一時停止するように2〜3秒挟む「ツッコミ」カードの文字を作ります。
各カードについて、直前の場面の字幕を読み、その場面への短いツッコミ（例:「いや長い！」「結局どっち？」「話それすぎ」）を1つ書きます。
規則:
- 14字以内の口語。2〜3秒で読める短さにする
- card_id は与えられた ID をそのまま使う
- 字幕に出てこない数字・固有名詞・商品名・人名を新しく書かない
- 人や集団を傷つける、侮辱する、差別的な内容は書かない。ツッコミの対象は「発言の内容」や「状況」にする
- 良いツッコミが思いつかないカードは返さなくてよい
""";

    /// <summary>AI tsukkomi text for text cards. Filtered in code like tsukkomi telops and created unapproved (H-08). Returns how many cards got AI text.</summary>
    public static async Task<int> AiTextAsync(IHighlightSelector llm, List<Insert> inserts, IReadOnlyList<Telop> telops, string sourceText, CancellationToken ct)
    {
        var cards = inserts.Where(i => i.Kind == "card" && i.TextSource != "ai").ToList();
        if (cards.Count == 0) return 0;
        var caps = telops.Where(t => t.Type == TelopType.Caption).OrderBy(t => t.Start).ToList();
        var blocks = new List<string>();
        foreach (var c in cards)
        {
            var before = caps.Where(t => t.Start < c.At - 0.05).TakeLast(4).Select(t => t.Text).ToList();
            var after = caps.Where(t => t.Start >= c.At - 0.05).Take(2).Select(t => t.Text).ToList();
            if (before.Count == 0) continue;
            blocks.Add($"{c.Id}\n  直前の場面: {string.Join(" / ", before)}\n  次の場面: {string.Join(" / ", after)}");
        }
        if (blocks.Count == 0) return 0;
        var json = await llm.CompleteJsonAsync(CardSystem, string.Join("\n", blocks), "scene_cards", CardSchema, ct);
        using var doc = JsonDocument.Parse(json);
        int n = 0;
        foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = it.GetProperty("card_id").GetString() ?? ""; var text = JapaneseText.NormalizeTelop((it.GetProperty("text").GetString() ?? "").Trim());
            int idx = inserts.FindIndex(i => i.Id == id && i.Kind == "card" && i.Path == null);
            if (idx < 0 || inserts[idx].TextSource == "ai" || TsukkomiService.ValidateText(text, sourceText) != null) continue;
            inserts[idx] = inserts[idx] with { Text = text, TextSource = "ai", Approved = false };
            n++;
        }
        return n;
    }
}
