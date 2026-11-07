using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VideoCreator.Asr;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Qa;
using VideoCreator.Rendering;
using VideoCreator.Selection;
using VideoCreator.Signals;
using VideoCreator.Storage;

namespace VideoCreator.Pipeline;

public sealed record JobUpdate(string JobId, JobStatus Stage, double Progress, string Message);

/// <summary>Retail list prices (JPY, Japan East) from prices.azure.com, retrieved 2026-11-07.</summary>
public sealed record Pricing(double AsrJpyPerHour = 56.96, double LlmInJpyPer1M = 118.67, double LlmOutJpyPer1M = 712.03)
{
    public const string Basis = "Azure Retail Prices API (JPY, japaneast, 2026-11-07): Fast Transcription ¥56.96/時間, gpt-5.4-mini 入力 ¥118.67/1M・出力 ¥712.03/1M トークン";
    public static int EstimateTokens(int chars) => (int)(chars * 1.1) + 1500;
}

public sealed class PipelineServices
{
    public required FFmpeg Ff { get; init; }
    public required Store Store { get; init; }
    public required IReadOnlyDictionary<string, Preset> Presets { get; init; }
    public required string DataDir { get; init; }
    public ITranscriber? Asr { get; init; }
    public IHighlightSelector? Llm { get; init; }
    public Pricing Pricing { get; init; } = new();
    public List<string> Dictionary { get; init; } = [];
    public int SelectionCharBudget { get; init; } = SelectionService.DefaultCharBudget;
    public Action<string> Log { get; init; } = _ => { };
    public AudioLibrary? Audio { get; init; }
    public FaceService? Faces { get; init; }
    public MediaProbe Probe => new(Ff);
    public SignalExtractor Signals => new(Ff);
    public Renderer Renderer => new(Ff) { BgmResolver = id => Audio?.Find(id)?.Path ?? Store.Get<BgmTrack>(Kinds.Bgm, id)?.Path, SfxResolver = k => Audio?.SfxPath(k) };
    public QaEngine Qa => new(Ff) { Faces = Faces };
}

public sealed class PipelineException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PipelineRunner(PipelineServices s)
{
    string CacheDir(SourceAsset a) => Path.Combine(s.DataDir, "cache", a.Sha256[..16]);

    public CostEstimate EstimateCost(SourceAsset main, string scope = "balanced")
    {
        double minutes = main.Media.AudioChannels > 0 && s.Asr != null ? main.Media.Duration / 60 : 0;
        int chars = SelectionService.EstimateSent(main.Media.Duration, scope, s.SelectionCharBudget).chars;
        int inTok = s.Llm == null ? 0 : Pricing.EstimateTokens(chars), outTok = s.Llm == null ? 0 : 2500;
        double jpy = minutes / 60 * s.Pricing.AsrJpyPerHour + inTok / 1e6 * s.Pricing.LlmInJpyPer1M + outTok / 1e6 * s.Pricing.LlmOutJpyPer1M;
        return new CostEstimate(Math.Round(minutes, 1), inTok, outTok, Math.Round(jpy, 1), Pricing.Basis);
    }

    /// <summary>Per-scope estimate for the "長尺の絞り込み" choice (FR-SEL-10).</summary>
    public IReadOnlyList<object> EstimateScopes(SourceAsset main) => new[] { "economy", "balanced", "full" }.Select(sc =>
    {
        var (chars, share) = SelectionService.EstimateSent(main.Media.Duration, sc, s.SelectionCharBudget);
        var c = EstimateCost(main, sc);
        return (object)new { scope = sc, sent_chars = chars, share = Math.Round(share, 2), cost = c };
    }).ToList();

    BgmTrack? FindBgm(string id) => s.Audio?.Find(id) ?? s.Store.Get<BgmTrack>(Kinds.Bgm, id);

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double Bpm, double Phase)> TempoCache = new();

    public async Task<BgmTrack> AnalyzedBgmAsync(BgmTrack t, CancellationToken ct)
    {
        var key = t.Path + "|" + File.GetLastWriteTimeUtc(t.Path).Ticks;
        if (!TempoCache.TryGetValue(key, out var tp)) { tp = await TempoEstimator.AnalyzeFileAsync(s.Ff, t.Path, ct); TempoCache[key] = tp; }
        return t with { Bpm = t.Source == "library" && t.Bpm > 0 ? t.Bpm : tp.Bpm, Phase = tp.Phase };
    }

    public async Task<Transcript> TranscriptAsync(SourceAsset a, string audio, CancellationToken ct)
    {
        if (a.Media.AudioChannels == 0 || s.Asr == null) return new Transcript { Provider = "none", Duration = a.Media.Duration };
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s.Asr.Name + "|" + string.Join("|", s.Dictionary))))[..12];
        var file = Path.Combine(CacheDir(a), $"transcript_{key}.json");
        if (File.Exists(file)) return Json.Deserialize<Transcript>(await File.ReadAllTextAsync(file, ct));
        var tr = await s.Asr.TranscribeAsync(audio, a.Media.Duration, s.Dictionary, 4, ct);
        await File.WriteAllTextAsync(file, Json.Serialize(tr), ct);
        return tr;
    }

    public async Task<SignalSet> SignalsAsync(SourceAsset a, string proxy, string? audio, CancellationToken ct)
    {
        var file = Path.Combine(CacheDir(a), "signals.json");
        if (File.Exists(file)) return Json.Deserialize<SignalSet>(await File.ReadAllTextAsync(file, ct));
        var sig = await s.Signals.ExtractAsync(proxy, audio, 0.5, ct);
        await File.WriteAllTextAsync(file, Json.Serialize(sig), ct);
        return sig;
    }

    /// <summary>Generate candidates + previews for a job (FR-SEL, FR-SOL, FR-TEL, FR-RND-02).</summary>
    public async Task<Job> GenerateAsync(Job job, IProgress<JobUpdate> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew(); var stageTimes = new Dictionary<string, double>();
        void Stage(JobStatus st, double pct, string msg) { progress.Report(new JobUpdate(job.Id, st, pct, msg)); s.Log($"[{job.Id}] {st} {pct:P0} {msg}"); }
        void Mark(string name) { stageTimes[name] = Math.Round(sw.Elapsed.TotalSeconds, 2); sw.Restart(); }

        var project = s.Store.Get<Project>(Kinds.Project, job.ProjectId) ?? throw new PipelineException("E-NOT-FOUND", "プロジェクトがありません");
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, project.Id);
        var main = assets.FirstOrDefault(a => a.Role == AssetRole.Main) ?? assets.FirstOrDefault() ?? throw new PipelineException("E-ING-UNSUPPORTED", "素材が登録されていません");
        var rights = s.Store.List<RightsConfirmation>(Kinds.Rights, project.Id).Select(r => r.AssetId).ToHashSet();
        if (assets.Any(a => !rights.Contains(a.Id))) throw new PipelineException("E-RIGHTS-REQUIRED", "素材の権利確認が完了していません");
        var preset = s.Presets[job.PresetIds[0]];
        if (!preset.Available) throw new PipelineException("E-PRESET-UNAVAILABLE", $"{preset.Name} は未対応です: {preset.UnavailableReason}");
        var warnings = new List<string>();
        var used = new List<SourceAsset> { main };
        if (preset.Planner == "switch")
        {
            if (assets.Count < 2) throw new PipelineException("E-SWT-ASSETS", "複数カメラの切り替えには、2 つ以上の素材（カメラ）を追加してください");
            used = assets.OrderBy(a => a.Id == main.Id ? 0 : 1).ToList();
        }
        else if (assets.Count > 1) warnings.Add("このパターンは基準の素材（最初に選んだ動画）だけを使います");
        if (preset.HasTelop("caption") && main.Media.AudioChannels > 0 && s.Asr == null)
            throw new PipelineException("E-ASR-PROVIDER", "字幕には書き起こしが必要ですが、書き起こしサービス（Azure AI Speech）が設定されていません。設定を確認してください。");

        Stage(JobStatus.Ingesting, 0.05, "解析用のプロキシを作成しています");
        var (proxy, audio) = await s.Probe.MakeProxyAsync(main.Path, main.Media, CacheDir(main), ct);
        // multi-camera: align every other camera to the master audio (GCC-PHAT, FR-SWT-01)
        for (int i = 0; i < used.Count; i++)
        {
            var a = used[i];
            if (a.Id == main.Id) continue;
            if (a.Media.AudioChannels == 0) { warnings.Add($"{Path.GetFileName(a.Path)} は音声がないため同期できません。時間のずれは 0 秒として扱います"); continue; }
            if (a.SyncConfidence == null)
            {
                Stage(JobStatus.Ingesting, 0.08, $"カメラの時間を音声で合わせています（{Path.GetFileName(a.Path)}）");
                var sync = await AudioSync.EstimateAsync(s.Ff, main.Path, a.Path, ct);
                a = a with { SyncOffset = Math.Round(sync.Delay, 3), SyncConfidence = Math.Round(sync.Confidence, 1) };
                s.Store.Put(Kinds.Asset, a.Id, a.ProjectId, a); used[i] = a;
            }
            if (a.SyncConfidence < SyncResult.MinConfidence)
            {
                warnings.Add($"{Path.GetFileName(a.Path)} の音声が基準映像と一致しません（信頼度 {a.SyncConfidence}）。このカメラは使いません");
                used.RemoveAt(i--);
            }
        }
        if (preset.Planner == "switch" && used.Count < 2) throw new PipelineException("E-SWT-SYNC", "カメラの音声を同期できませんでした。同じ場面を撮影した素材を選んでください");
        Mark("ingest");

        Stage(JobStatus.Transcribing, 0.15, s.Asr == null ? "書き起こしは無効です" : "書き起こしています（Azure AI Speech）");
        Transcript tr;
        try { tr = await TranscriptAsync(main, audio, ct); }
        catch (AsrException ex) { throw new PipelineException("E-ASR-PROVIDER", "書き起こしに失敗しました: " + ex.Message); }
        if (tr.Words.Count == 0 && main.Media.AudioChannels > 0 && s.Asr != null) warnings.Add("発話が検出されませんでした");
        Mark("transcribe");

        Stage(JobStatus.Analyzing, 0.35, "ショット・音量・無音を解析しています");
        var sig = await SignalsAsync(main, proxy, main.Media.AudioChannels > 0 ? audio : null, ct);
        var laughs = await LaughsAsync(main, tr, sig, audio, ct);
        if (preset.Planner == "switch") await AutoAssignSpeakersAsync(used, main, tr, warnings, ct);
        Mark("signals");

        BgmTrack? bgm = null;
        var bgmId = PlanBuilder.ResolveBgmId(preset, job.Options);
        if (bgmId != null)
        {
            if (s.Audio != null) await s.Audio.EnsureAsync(ct);
            var t0 = FindBgm(bgmId) ?? throw new PipelineException("E-BGM-NOTFOUND", $"BGM「{bgmId}」が見つかりません");
            if (t0.Source == "user" && !t0.RightsConfirmed) throw new PipelineException("E-RIGHTS-REQUIRED", "BGM の権利確認が完了していません");
            bgm = preset.Planner == "beat" || t0.Bpm <= 0 ? await AnalyzedBgmAsync(t0, ct) : t0;
        }

        Stage(JobStatus.Selecting, 0.45, preset.Planner == "speed" ? "早送りに向く区間を探しています" : s.Llm == null ? "音声の盛り上がりから選んでいます" : $"AI がハイライトを選んでいます（{s.Llm.Model}）");
        var exclude = job.ExcludeCandidateIds.Select(id => s.Store.Get<Candidate>(Kinds.Candidate, id)).Where(c => c != null).SelectMany(c => c!.Segments).ToList();
        SelectionOutcome outcome;
        if (preset.Planner == "speed")
        {
            var spans = SpeedPlanner.Candidates(tr, sig, main.Media, preset).Where(c => exclude.Count == 0 || exclude.All(e => Math.Min(c.Segments[0].T1, e.T1) - Math.Max(c.Segments[0].T0, e.T0) < 0.5 * c.Segments[0].Length)).ToList();
            outcome = new SelectionOutcome(spans, "none", 0, 0, true, [], 0, 0);
        }
        else
        {
            var selector = new SelectionService(s.Llm, s.Log, s.SelectionCharBudget);
            outcome = await selector.SelectAsync(tr, sig, preset, main.Media.Duration, job.Instruction, exclude, ct, job.Options.Scope);
        }
        warnings.AddRange(outcome.Warnings);
        Mark("select");

        // faces are analysed only inside the ranges that will be used (2 fps from the proxy), then cached
        var faces = new Dictionary<string, List<FaceSample>>();
        if (preset.Reframe == "auto" && s.Faces is { Available: true } fs)
        {
            Stage(JobStatus.Analyzing, 0.55, "顔の位置を解析しています");
            foreach (var a in used.Where(a => a.Media.Aspect >= 0.9 && a.Media.Width > 0))
            {
                var ranges = outcome.Candidates.SelectMany(c => c.Segments).Select(x => new Interval(Math.Max(0, x.T0 + (a.Id == main.Id ? 0 : a.SyncOffset)), x.T1 + (a.Id == main.Id ? 0 : a.SyncOffset))).ToList();
                var apx = a.Id == main.Id ? proxy : (await s.Probe.MakeProxyAsync(a.Path, a.Media, CacheDir(a), ct)).Item1;
                faces[a.Id] = await fs.AnalyzeAsync(apx, ranges, Path.Combine(CacheDir(a), "faces.json"), ct);
            }
            Mark("faces");
        }

        Stage(JobStatus.Planning, 0.6, "編集計画を作成しています");
        var candidates = new List<Candidate>();
        foreach (var c0 in outcome.Candidates)
        {
            var cid = $"{job.Id}_{c0.Id}";
            (EditPlan plan, List<string> notes) built;
            try { built = await BuildPlanAsync(c0 with { Id = cid }, tr, sig, preset, main, used, project.Id, job, faces, bgm, null, ct, laughs); }
            catch (PlanException ex) { warnings.Add($"{c0.Id}: {ex.Message}"); continue; }
            var (plan, notes) = built;
            s.Store.Put(Kinds.EditPlan, plan.EditplanId, project.Id, plan);
            var cand = c0 with { Id = cid, EditPlanId = plan.EditplanId, Notes = [.. c0.Notes, .. notes] };
            candidates.Add(cand);
            s.Store.Put(Kinds.Candidate, cid, job.Id, cand);
        }
        if (candidates.Count == 0) throw new PipelineException("E-SOL-INFEASIBLE", "条件を満たす区間が見つかりませんでした。別のパターンを試してください。");
        Mark("plan");

        for (int i = 0; i < candidates.Count; i++)
        {
            Stage(JobStatus.Previewing, 0.65 + 0.3 * i / candidates.Count, $"プレビューを作成しています（{i + 1}/{candidates.Count}）");
            var plan = s.Store.Get<EditPlan>(Kinds.EditPlan, candidates[i].EditPlanId!)!;
            var render = await RenderAsync(plan, preview: true, ct);
            candidates[i] = candidates[i] with { PreviewRenderId = render.Id };
            s.Store.Put(Kinds.Candidate, candidates[i].Id, job.Id, candidates[i]);
        }
        Mark("preview");

        var cost = new CostEstimate(Math.Round(tr.Duration / 60, 1), outcome.InputTokens, outcome.OutputTokens,
            Math.Round(tr.Duration / 3600 * s.Pricing.AsrJpyPerHour + outcome.InputTokens / 1e6 * s.Pricing.LlmInJpyPer1M + outcome.OutputTokens / 1e6 * s.Pricing.LlmOutJpyPer1M, 2), Pricing.Basis + "（実績）");
        if (outcome.FullTextChars > 0 && outcome.SentChars > 0)
            s.Log($"[{job.Id}] LLM input chars {outcome.SentChars}/{outcome.FullTextChars} ({100.0 * outcome.SentChars / outcome.FullTextChars:F0}%)");
        return job with
        {
            Status = JobStatus.Ready, Progress = 1, Message = $"{candidates.Count} 件の候補ができました", CandidateIds = candidates.Select(c => c.Id).ToList(),
            LlmModel = outcome.Model, PromptVersion = SelectionPrompt.Version, StageSeconds = stageTimes, Cost = cost, Warnings = warnings,
        };
    }

    public async Task<(EditPlan plan, List<string> notes)> BuildPlanAsync(Candidate c, Transcript tr, SignalSet sig, Preset p, SourceAsset main, IReadOnlyList<SourceAsset> assets, string projectId, Job job,
        IReadOnlyDictionary<string, List<FaceSample>> faces, BgmTrack? bgm, IReadOnlyList<Interval>? overrideSegments, CancellationToken ct, IReadOnlyList<Interval>? bursts = null)
        => await PlanBuilder.BuildAsync(new PlanRequest(c, tr, sig, p, assets, main, projectId, job, faces, bgm, s.Llm, overrideSegments, bursts), ct);

    /// <summary>FR-SWT-01: cameras without a user-set speaker get one from their own microphone, when the evidence is clear.</summary>
    async Task AutoAssignSpeakersAsync(List<SourceAsset> used, SourceAsset main, Transcript tr, List<string> warnings, CancellationToken ct)
    {
        var cams = used.Where(a => a.Role != AssetRole.Wide && a.Media.AudioChannels > 0).ToList();
        if (cams.Count < 2 || cams.Any(a => a.Speaker != null)) return;
        var audios = new List<float[]>();
        foreach (var a in cams)
        {
            double off = a.Id == main.Id ? 0 : a.SyncOffset;
            audios.Add(await AudioSync.LoadAsync(s.Ff, a.Path, 8000, 600, ct, Math.Max(0, off), 0));
        }
        var res = SpeakerAssign.Assign(audios, 8000, tr);
        if (res == null) { warnings.Add("カメラと話者を自動では判定できませんでした（マイクが共通の可能性）。素材の「話者」欄で指定できます。いまは基準=話者1、カメラB=話者2として扱います"); return; }
        for (int i = 0; i < cams.Count; i++)
        {
            var u = cams[i] with { Speaker = res[i] };
            s.Store.Put(Kinds.Asset, u.Id, u.ProjectId, u);
            used[used.FindIndex(x => x.Id == u.Id)] = u;
        }
        warnings.Add("各カメラのマイク音量から話者を自動で割り当てました: " + string.Join("、", cams.Select((a, i) => $"{Path.GetFileName(a.Path)}=話者{res[i]}")));
    }

    /// <summary>FR-SIG-04: laughter intervals (loud non-speech bursts that pulse like laughter), cached next to the signals.</summary>
    public async Task<List<Interval>> LaughsAsync(SourceAsset a, Transcript tr, SignalSet sig, string audio, CancellationToken ct)
    {
        var file = Path.Combine(CacheDir(a), "laughs_" + Path.GetFileNameWithoutExtension(Directory.GetFiles(CacheDir(a), "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "none") + ".json");
        if (File.Exists(file)) return Json.Deserialize<List<Interval>>(await File.ReadAllTextAsync(file, ct));
        var res = a.Media.AudioChannels > 0 && File.Exists(audio) ? await LaughDetector.DetectAsync(s.Ff, audio, NonSpeech.Bursts(sig, tr), ct) : [];
        await File.WriteAllTextAsync(file, Json.Serialize(res), ct);
        return res;
    }

    public List<Interval> LoadLaughs(SourceAsset a)
    {
        var dir = CacheDir(a);
        var f = Directory.Exists(dir) ? Directory.GetFiles(dir, "laughs_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        return f != null ? Json.Deserialize<List<Interval>>(File.ReadAllText(f)) : [];
    }

    /// <summary>Re-plan an existing plan's candidate after the user edited the segments (PATCH /editplans).</summary>
    public async Task<(EditPlan plan, List<string> notes)> ReplanAsync(Candidate c, EditPlan old, Job job, IReadOnlyList<Interval> segments, CancellationToken ct)
    {
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, old.ProjectId);
        var main = assets.First(a => a.Id == old.Audio.Master);
        var p = s.Presets[old.Preset.Id];
        var used = p.Planner == "switch" ? assets.Where(a => a.Id == main.Id || old.Timeline.Any(t => t.AssetId == a.Id)).ToList() : [main];
        var faces = new Dictionary<string, List<FaceSample>>();
        foreach (var a in used) { var f = FaceService.Load(Path.Combine(CacheDir(a), "faces.json")); if (f.Count > 0) faces[a.Id] = f; }
        BgmTrack? bgm = old.Audio.Bgm is { } id && FindBgm(id) is { } t ? (p.Planner == "beat" ? await AnalyzedBgmAsync(t, ct) : t) : null;
        return await BuildPlanAsync(c, LoadTranscript(main), LoadSignals(main), p, main, used, old.ProjectId, job with { Options = job.Options with { Bgm = old.Audio.Bgm ?? "none" } }, faces, bgm, segments, ct, LoadLaughs(main));
    }

    public QaContext QaContextFor(EditPlan plan)
    {
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, plan.ProjectId);
        var main = assets.First(a => a.Id == plan.Audio.Master);
        var tr = LoadTranscript(main);
        var rights = s.Store.List<RightsConfirmation>(Kinds.Rights, plan.ProjectId).Select(r => r.AssetId).ToHashSet();
        return new QaContext(plan, s.Presets[plan.Preset.Id], tr, rights, s.Dictionary, assets);
    }

    public Transcript LoadTranscript(SourceAsset a)
    {
        var dir = CacheDir(a);
        var f = Directory.Exists(dir) ? Directory.GetFiles(dir, "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        return f != null ? Json.Deserialize<Transcript>(File.ReadAllText(f)) : new Transcript { Provider = "none", Duration = a.Media.Duration };
    }

    /// <summary>FR-ASR-06: correct misrecognised words. Timing is kept; sentence texts are rebuilt; the cached transcript is overwritten so later runs use the fix.</summary>
    public Transcript EditTranscript(SourceAsset a, IEnumerable<(string WordId, string Text)> edits)
    {
        var dir = CacheDir(a);
        var f = Directory.Exists(dir) ? Directory.GetFiles(dir, "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (f == null) throw new KeyNotFoundException("書き起こしがありません");
        var tr = Json.Deserialize<Transcript>(File.ReadAllText(f));
        var map = edits.ToDictionary(e => e.WordId, e => e.Text.Replace("\r", "").Replace("\n", " ").Trim());
        foreach (var (id, text) in map) { if (text.Length == 0 || text.Length > 30) throw new ArgumentException("単語は1〜30文字にしてください"); if (tr.Words.All(w => w.Id != id)) throw new KeyNotFoundException($"単語 {id} がありません"); }
        var words = tr.Words.Select(w => map.TryGetValue(w.Id, out var t) ? w with { Text = t } : w).ToList();
        var sents = tr.Sentences.Select(s => s with { Text = string.Concat(words.Skip(s.FirstWord).Take(s.LastWord - s.FirstWord + 1).Select(w => w.Text)) }).ToList();
        var res = tr with { Words = words, Sentences = sents };
        File.WriteAllText(f, Json.Serialize(res));
        return res;
    }

    public SignalSet LoadSignals(SourceAsset a)
    {
        var f = Path.Combine(CacheDir(a), "signals.json");
        return File.Exists(f) ? Json.Deserialize<SignalSet>(File.ReadAllText(f)) : new SignalSet();
    }

    /// <summary>Render (preview or final) + QA. Final renders are blocked by hard QA failures (E-QA-HARDFAIL).</summary>
    public async Task<Render> RenderAsync(EditPlan plan, bool preview, CancellationToken ct, string? exportBaseName = null)
    {
        var project = s.Store.Get<Project>(Kinds.Project, plan.ProjectId)!;
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, plan.ProjectId).ToDictionary(a => a.Id);
        var preset = s.Presets[plan.Preset.Id];
        var ctx = QaContextFor(plan);
        var renderId = $"r_{Guid.NewGuid():N}"[..14];
        if (!preview)
        {
            var pre = QaEngine.PlanChecks(ctx).Where(q => q.Hard && q.Status == QaStatus.Fail).ToList();
            if (pre.Count > 0) throw new QaBlockedException(pre);
        }
        var dir = preview ? Path.Combine(project.Directory, "previews") : Path.Combine(project.Directory, "exports");
        var file = Path.Combine(dir, preview ? $"{renderId}.mp4" : exportBaseName + ".mp4");
        if (!preview) EnsureInside(project.Directory, file);
        var outp = await s.Renderer.RenderAsync(plan, preset, assets, file, preview, ct);
        var qa = await s.Qa.RunAsync(ctx, file, preview, Path.Combine(s.DataDir, "qa", renderId), ct);
        var outputs = new Dictionary<string, string> { ["mp4"] = file };
        if (!preview)
        {
            var hard = qa.Where(q => q.Hard && q.Status == QaStatus.Fail).ToList();
            if (hard.Count > 0)
            {
                var rejected = Path.Combine(project.Directory, "rejected", Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(rejected)!);
                File.Move(file, rejected, true);
                throw new QaBlockedException(hard);
            }
            var srt = Path.ChangeExtension(file, ".srt"); await File.WriteAllTextAsync(srt, AssWriter.Srt(plan), new UTF8Encoding(false), ct); outputs["srt"] = srt;
            var epj = Path.ChangeExtension(file, ".editplan.json"); await File.WriteAllTextAsync(epj, Json.Serialize(plan, true), ct); outputs["editplan"] = epj;
            var thumbs = await s.Renderer.ThumbnailsAsync(file, plan.Duration, Path.ChangeExtension(file, null), ct);
            for (int i = 0; i < thumbs.Count; i++) outputs[$"thumb{i + 1}"] = thumbs[i];
            var ass = Path.ChangeExtension(file, ".ass"); if (File.Exists(ass)) outputs["ass"] = ass;
        }
        var render = new Render
        {
            Id = renderId, EditplanId = plan.EditplanId, Kind = preview ? "preview" : "final", Path = file, Duration = plan.Duration,
            FfmpegVersion = await s.Ff.VersionAsync(), Commands = outp.Commands, Qa = qa, Outputs = outputs,
        };
        s.Store.Put(Kinds.Render, render.Id, plan.EditplanId, render);
        return render;
    }

    /// <summary>NFR-SEC-05: refuse to write outside the project directory.</summary>
    public static void EnsureInside(string root, string path)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(path);
        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new PipelineException("E-PATH", "プロジェクトフォルダの外には書き込めません");
    }

    public static string ExportBaseName(Project project, EditPlan plan, int rank)
    {
        var safe = new string(project.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '.' ? '_' : ch).ToArray()).Trim();
        if (safe.Length == 0) safe = "video";
        return $"{safe}_{plan.Preset.Id}_{rank}_{DateTime.Now:yyyyMMdd-HHmm}";
    }

    /// <summary>FR-EXP-03: posting metadata suggestions (LLM when available, otherwise a template).</summary>
    public async Task<string> MetadataAsync(Candidate c, EditPlan plan, CancellationToken ct)
    {
        var main = s.Store.List<SourceAsset>(Kinds.Asset, plan.ProjectId).First(a => a.Id == plan.Audio.Master);
        var text = CandidateValidator.TextOf(LoadTranscript(main), plan.Timeline.Select(x => new Interval(x.MasterIn, x.MasterOut)));
        string titles = "", desc = "", tags = "";
        if (s.Llm != null && text.Length > 0)
        {
            try
            {
                const string schema = """{"type":"object","additionalProperties":false,"required":["titles","description","hashtags"],"properties":{"titles":{"type":"array","items":{"type":"string"}},"description":{"type":"string"},"hashtags":{"type":"array","items":{"type":"string"}}}}""";
                var json = await s.Llm.CompleteJsonAsync("あなたはショート動画の投稿担当です。書き起こしの内容だけに基づき、誇張や事実にない内容を書かずに、タイトル案3件（各40字以内）、説明文（200字以内）、ハッシュタグ5件以内を作ってください。", text, "post_metadata", schema, ct);
                using var d = JsonDocument.Parse(json);
                titles = string.Join("\n", d.RootElement.GetProperty("titles").EnumerateArray().Select((t, i) => $"{i + 1}. {t.GetString()}"));
                desc = d.RootElement.GetProperty("description").GetString() ?? "";
                tags = string.Join(" ", d.RootElement.GetProperty("hashtags").EnumerateArray().Select(t => { var x = t.GetString() ?? ""; return x.StartsWith('#') ? x : "#" + x; }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { s.Log("metadata generation failed: " + ex.Message); }
        }
        if (titles.Length == 0) titles = $"1. {(c.Headline.Length > 0 ? c.Headline : c.HookText)}";
        return $"""
# 投稿用メタデータ案（AI による提案。投稿前に内容を確認してください）

## タイトル候補
{titles}

## 説明文
{desc}

元動画: （ここに元動画の URL を記入）

## ハッシュタグ
{tags} #Shorts

## AI 利用の開示（C-6 / FR-EXP-04）
この動画は既存の映像を切り出して字幕を付けたもので、AI による写実的な映像・音声の生成や改変は含みません。
YouTube の「AI use」は通常「いいえ」で問題ありません（YouTube ヘルプ「Disclosing use of GenAI content」）。
""";
    }
}

public sealed class QaBlockedException(List<QaResult> failures) : Exception("自動QAのハード制約に違反したため書き出しを中止しました: " + string.Join(", ", failures.Select(f => $"{f.CheckId} {f.Message} ({f.Measured})")))
{
    public List<QaResult> Failures { get; } = failures;
}
