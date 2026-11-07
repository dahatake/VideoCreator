using VideoCreator.Faces;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Selection;

namespace VideoCreator.Planning;

public sealed record PlanRequest(
    Candidate Candidate, Transcript Transcript, SignalSet Signals, Preset Preset,
    IReadOnlyList<SourceAsset> Assets, SourceAsset Master, string ProjectId, Job Job,
    IReadOnlyDictionary<string, List<FaceSample>> Faces, BgmTrack? Bgm, IHighlightSelector? Llm,
    IReadOnlyList<Interval>? OverrideSegments = null, IReadOnlyList<Interval>? Bursts = null);

/// <summary>Candidate → EditPlan. Dispatches on preset.Planner, then reframes, adds telops, BGM and SFX (PRD FR-SOL, FR-SWT, FR-SPD, FR-BGM, FR-AUD).</summary>
public static class PlanBuilder
{
    public static string DefaultBgmId(Preset p) => p.Audio.Bgm switch { "auto" => p.Planner == "beat" ? "hype" : "upbeat", var x => x };

    /// <summary>Which BGM this job uses (null = none): job option first, otherwise the preset's default.</summary>
    public static string? ResolveBgmId(Preset p, JobOptions o)
    {
        if (o.Bgm == "none") return null;
        if (o.Bgm != "auto" && !string.IsNullOrEmpty(o.Bgm)) return o.Bgm;
        var d = DefaultBgmId(p);
        return d == "none" ? (p.Planner == "beat" ? "hype" : null) : d;
    }

    public static async Task<(EditPlan Plan, List<string> Notes)> BuildAsync(PlanRequest r, CancellationToken ct)
    {
        var p = r.Preset; var tr = r.Transcript; var sig = r.Signals; var notes = new List<string>(); var master = r.Master;
        var segs = r.OverrideSegments ?? r.Candidate.Segments;
        var bursts = r.Bursts ?? [];
        if (p.Loop.Enabled && p.Planner == "speech" && r.Llm != null && r.OverrideSegments == null && tr.Sentences.Count > 0)
        {
            try { if (await LoopEnding.ChooseAsync(r.Llm, tr, segs, sig, p, master.Media, master.Id, ct) is { } lp) { segs = lp.Segments; notes.Add(lp.Note); } }
            catch (Exception ex) when (ex is not OperationCanceledException) { notes.Add("ループの終わりの判定に失敗しました: " + ex.Message); }
        }
        List<Clip> clips;
        switch (p.Planner)
        {
            case "speed":
                clips = SpeedPlanner.Plan(segs[0], tr, p, master.Id, master.Media, notes);
                break;
            case "beat":
            {
                var bgm = r.Bgm ?? throw new PlanException("E-SOL-INFEASIBLE", "ビート同期には BGM が必要です");
                if (bgm.Bpm <= 0) throw new PlanException("E-SOL-INFEASIBLE", "BGM のテンポを検出できませんでした");
                clips = BeatPlanner.Plan(segs, sig, tr, p, master.Id, master.Media, bgm.Bpm, notes);
                if (clips.Sum(c => c.Length) < p.Target.Min - 0.5)
                {
                    clips = BeatPlanner.Plan([new Interval(0, master.Media.Duration)], sig, tr, p, master.Id, master.Media, bgm.Bpm, notes);
                    notes.Add("選んだ区間だけでは尺が足りないため、動画全体から拍に合わせて集めました");
                }
                break;
            }
            default:
            {
                var res = Solver.Plan(segs, tr, sig, p, master.Media, master.Id, r.Candidate.EmphasisWords);
                notes.AddRange(res.Notes);
                clips = res.Clips;
                if (p.Planner == "switch")
                {
                    var cams = r.Assets.Where(a => SwitchPlanner.SpeakerOf(a) > 0 || a.Role == AssetRole.Wide).ToList();
                    if (cams.Count >= 2) clips = SwitchPlanner.Plan(clips, tr, r.Assets, master, p, bursts, notes);
                    else notes.Add("カメラ素材が 2 つ以上ないため、切り替えは行いませんでした");
                }
                break;
            }
        }
        if (clips.Count == 0) throw new PlanException("E-SOL-INFEASIBLE", "条件を満たす区間が見つかりませんでした");

        clips = ApplyFades(clips, p, r.Assets, notes);
        clips = ApplyFaces(clips, r, notes);

        double total = clips.Sum(c => c.Length);
        var telops = TelopBuilder.Build(clips, tr, p, r.Candidate.HookText, r.Candidate.Headline, r.Candidate.EmphasisWords, notes);
        if (r.Llm != null)
        {
            try { var (sh, ch) = await CaptionShortener.ApplyAsync(r.Llm, telops, p.Telop.Caption.MaxCps, ct); telops = sh; if (ch > 0) notes.Add($"表示が間に合わない字幕 {ch} 件を、文字を削って短くしました（言い換えなし）"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { notes.Add("字幕の短縮に失敗しました: " + ex.Message); }
        }
        int n = telops.Count + 100;
        var mapped = TelopBuilder.MapWords(clips, tr);
        if (p.HasTelop("counter")) telops.AddRange(TelopExtras.Counters(mapped, telops, total, ref n));
        if (p.HasTelop("sfx")) telops.AddRange(TelopExtras.Onomatopoeia(clips, bursts, telops, total, ref n));
        if (p.HasTelop("tsukkomi") && r.Job.Options.Tsukkomi)
        {
            if (r.Llm == null) notes.Add("ツッコミテロップには AI が必要です（現在は無効）");
            else
            {
                try
                {
                    var caps = telops.Where(t => t.Type == TelopType.Caption).ToList();
                    var src = CandidateValidator.TextOf(tr, clips.Select(c => new Interval(c.MasterIn, c.MasterOut)));
                    var items = await TsukkomiService.GenerateAsync(r.Llm, caps, src, ct);
                    foreach (var (id, text) in items)
                    {
                        var cap = caps.First(c => c.Id == id);
                        if (telops.Any(t => t.Type == TelopType.Tsukkomi && Math.Abs(t.Start - cap.End) < 1.5)) continue;
                        telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Tsukkomi, Start = cap.End, End = Math.Round(Math.Min(total, cap.End + 1.4), 3), Text = text, Style = "Tsukkomi", Animation = "pop", Approved = false });
                    }
                    notes.Add(items.Count > 0 ? $"ツッコミテロップ {items.Count} 件を提案しました（承認するまで書き出せません）" : "ツッコミテロップは提案できませんでした");
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { notes.Add("ツッコミテロップの生成に失敗しました: " + ex.Message); }
            }
        }
        telops = telops.OrderBy(t => t.Start).ThenBy(t => t.Type).ToList();

        string? bgmId = r.Bgm?.Id;
        var (sfx, mutes) = r.Job.Options.Sfx ? SfxPlanner.Plan(clips, telops, p.Audio.Sfx, total) : ([], []);
        var audio = new AudioSpec
        {
            Master = master.Id, TargetLufs = p.Audio.Loudnorm.I, TruePeak = p.Audio.Loudnorm.TP,
            Bgm = bgmId, BgmGainDb = p.Audio.BgmDb, Duck = p.Audio.Duck, DialogueGainDb = p.Audio.DialogueGainDb,
            BgmPhase = p.Planner == "beat" ? r.Bgm?.Phase ?? 0 : 0,
            SfxEnabled = r.Job.Options.Sfx, Sfx = sfx, BgmMutes = bgmId == null ? [] : mutes,
        };
        var plan = new EditPlan
        {
            EditplanId = $"ep_{r.Candidate.Id}", ProjectId = r.ProjectId, Preset = p.Ref, Timeline = clips, Telops = telops, Audio = audio,
            Provenance = new Provenance { JobId = r.Job.Id, CandidateId = r.Candidate.Id, LlmModel = r.Job.LlmModel ?? r.Llm?.Model, PromptVersion = SelectionPrompt.Version },
        };
        return (plan, notes);
    }

    /// <summary>FR-SWT-05: a short cross-fade only where the story jumps (source gap > 3 s between consecutive body clips of one camera).</summary>
    public static List<Clip> ApplyFades(List<Clip> clips, Preset p, IReadOnlyList<SourceAsset> assets, List<string> notes)
    {
        double d = p.Cut.SceneFadeSec; if (d <= 0) return clips;
        var dur = assets.ToDictionary(a => a.Id, a => a.Media.Duration);
        int n = 0; var res = new List<Clip>();
        for (int i = 0; i < clips.Count; i++)
        {
            var c = clips[i];
            if (i > 0)
            {
                var prev = clips[i - 1];
                bool jump = prev.AssetId == c.AssetId && Math.Abs(c.MasterIn - prev.MasterOut) > 3.0 && prev.Role == "body" && c.Role == "body";
                bool room = prev.Length >= 2 * d + 0.3 && c.Length >= 2 * d + 0.3 && dur.TryGetValue(prev.AssetId, out var D) && prev.SrcOut + d * prev.Speed <= D;
                if (jump && room) { c = c with { TransitionSec = d }; n++; }
            }
            res.Add(c);
        }
        if (n > 0) notes.Add($"場面が飛ぶ {n} か所に {d:0.0#} 秒のクロスフェードを入れました");
        return res;
    }

    static List<Clip> ApplyFaces(List<Clip> clips, PlanRequest r, List<string> notes)
    {
        if (r.Preset.Reframe != "auto" || r.Faces.Count == 0) return clips;
        var byId = r.Assets.ToDictionary(a => a.Id);
        int followed = 0, eligible = 0; var reasons = new HashSet<string>();
        var res = new List<Clip>();
        foreach (var c in clips)
        {
            if (!byId.TryGetValue(c.AssetId, out var a) || a.Media.Aspect < 0.9 || !r.Faces.TryGetValue(a.Id, out var samples)) { res.Add(c); continue; }
            eligible++;
            var spec = FaceTrack.Plan(samples, a.Media, c.SrcIn, c.SrcOut, c.Speed, out var why);
            if (spec != null) { followed++; res.Add(c with { Reframe = spec }); }
            else { if (why.Length > 0) reasons.Add(why); res.Add(c); }
        }
        if (eligible > 0) notes.Add($"顔追従の縦型化: {followed}/{eligible} カット" + (reasons.Count > 0 ? $"（それ以外は背景ぼかし: {string.Join("、", reasons)}）" : ""));
        return res;
    }
}

public sealed class PlanException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
