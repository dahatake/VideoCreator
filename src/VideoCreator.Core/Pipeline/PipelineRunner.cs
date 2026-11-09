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

public sealed record JobUpdate(string JobId, JobStatus Stage, double Progress, string Message, double? StepProgress = null, string? Step = null);

/// <summary>Retail list prices (JPY, Japan East) from prices.azure.com, retrieved 2026-11-07.</summary>
public sealed record Pricing(double AsrJpyPerHour = 56.96, double LlmInJpyPer1M = 791.15, double LlmOutJpyPer1M = 4746.90)
{
    public const string Basis = "Azure Retail Prices API (JPY, japaneast, 2026-11-07): Fast Transcription ¥56.96/時間, gpt-chat-latest (2026-10-07, Global) 入力 ¥791.15/1M・出力 ¥4,746.90/1M トークン";
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
    public ITextEmbedder? Embedder { get; init; }
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
        var phrases = DictionaryFor(a.ProjectId);
        var file = TranscriptFile(a, phrases);
        if (File.Exists(file)) return Json.Deserialize<Transcript>(await File.ReadAllTextAsync(file, ct));
        var tr = await s.Asr.TranscribeAsync(audio, a.Media.Duration, phrases, 4, ct);
        await File.WriteAllTextAsync(file, Json.Serialize(tr), ct);
        return tr;
    }

    List<string> DictionaryFor(string projectId)
    {
        var project = s.Store.Get<Project>(Kinds.Project, projectId);
        return s.Dictionary.Concat(project?.ProperNouns.SelectMany(n => new[] { n.DisplayText, n.Reading }.Concat(n.Variants)) ?? [])
            .Select(text => text.Trim()).Where(text => text.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    string TranscriptFile(SourceAsset a, IReadOnlyList<string> phrases)
    {
        var provider = s.Asr?.Name ?? "none";
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(provider + "|" + string.Join("|", phrases))))[..12];
        return Path.Combine(CacheDir(a), $"transcript_{key}.json");
    }

    public async Task<SignalSet> SignalsAsync(SourceAsset a, string proxy, string? audio, CancellationToken ct, IProgress<double>? progress = null)
    {
        var file = Path.Combine(CacheDir(a), SignalsFile);
        if (File.Exists(file)) return Json.Deserialize<SignalSet>(await File.ReadAllTextAsync(file, ct));
        var sig = await s.Signals.ExtractAsync(proxy, audio, 0.5, ct, progress, a.Media.Duration);
        await File.WriteAllTextAsync(file, Json.Serialize(sig), ct);
        return sig;
    }

    /// <summary>Generate candidates + previews for a job (FR-SEL, FR-SOL, FR-TEL, FR-RND-02).</summary>
    public async Task<Job> GenerateAsync(Job job, IProgress<JobUpdate> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew(); var stageTimes = new Dictionary<string, double>();
        double overall = 0; var loggedTenth = -1;
        // overall never moves backwards; step = measured 0..1 of the current sub-task (null = not measurable → indeterminate bar)
        void Stage(JobStatus st, double pct, string msg, double? step = null, string? stepLabel = null)
        {
            overall = Math.Max(overall, pct);
            progress.Report(new JobUpdate(job.Id, st, Math.Round(overall, 4), msg, step is double v ? Math.Round(v, 4) : null, stepLabel));
            var tenth = step is double t ? (int)(t * 10) : -1;
            if (step == null || tenth != loggedTenth) s.Log($"[{job.Id}] {st} {overall:P0} {msg}{(step is double d ? $" — {stepLabel} {d:P0}" : "")}");
            loggedTenth = tenth;
        }
        IProgress<double> Sub(JobStatus st, double from, double to, string msg, string label) =>
            new SyncProgress<double>(v => Stage(st, from + (to - from) * Math.Clamp(v, 0, 1), msg, Math.Clamp(v, 0, 1), label));
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
        else if (preset.Planner == "screen") used = assets.OrderBy(a => a.Id == main.Id ? 0 : 1).ToList();
        else if (assets.Count > 1) warnings.Add("このパターンは基準の素材（最初に選んだ動画）だけを使います");
        if (!job.Options.IsSeries && main.Media.Duration < preset.Target.Min)
            throw new PipelineException("E-SOL-INFEASIBLE", $"素材が{preset.Id}の尺の下限（{preset.Target.Min:0.#}秒）より短い");
        if (preset.HasTelop("caption") && main.Media.AudioChannels > 0 && s.Asr == null)
            throw new PipelineException("E-ASR-PROVIDER", "字幕には書き起こしが必要ですが、書き起こしサービス（Azure AI Speech）が設定されていません。設定を確認してください。");

        const string proxyMsg = "解析用のプロキシを作成しています";
        Stage(JobStatus.Ingesting, 0.02, proxyMsg);
        var (proxy, audio) = await s.Probe.MakeProxyAsync(main.Path, main.Media, CacheDir(main), ct, Sub(JobStatus.Ingesting, 0.02, 0.1, proxyMsg, "360p プロキシと 16kHz 音声の作成"));
        // multi-source: align every other recording to the master audio (full-length search + drift fit, FR-SWT-01 / FR-ING-05)
        for (int i = 0; i < used.Count; i++)
        {
            var a = used[i];
            if (a.Id == main.Id) continue;
            string name = Path.GetFileName(a.Path);
            // older versions measured one 90 s window (no drift, no late-start search) → measure again; manual offsets are kept
            if (a.SyncConfidence == null || a.SyncMethod == null && a.SyncConfidence != SyncResult.Manual)
            {
                Stage(JobStatus.Ingesting, 0.1, $"素材の時間を音声で合わせています（{name}）");
                SyncFit fit;
                if (a.Media.AudioChannels > 0 && main.Media.AudioChannels > 0) fit = await AudioSync.AlignAsync(s.Ff, main.Path, main.Media.Duration, a.Path, a.Media.Duration, ct);
                else fit = new SyncFit { Method = "none" };
                if (!fit.Reliable && await AudioSync.FromMetadataAsync(s.Ff, main.Path, main.Media.Duration, a.Path, a.Media.Duration, ct) is { } meta) fit = meta;
                a = a with
                {
                    SyncOffset = fit.Method is "audio" or "metadata" ? Math.Round(fit.Offset, 4) : 0, SyncDriftPpm = fit.Method == "audio" ? fit.DriftPpm : 0,
                    SyncConfidence = Math.Round(fit.Confidence, 1), SyncMethod = fit.Reliable || fit.Method == "metadata" ? fit.Method : "none",
                    SyncResidualMs = fit.Method == "audio" ? fit.ResidualMs : null, SyncWindows = fit.Method == "audio" ? $"{fit.Agreeing}/{fit.Windows}" : null,
                };
                s.Store.Put(Kinds.Asset, a.Id, a.ProjectId, a); used[i] = a;
                s.Log($"[{job.Id}] sync {name}: {fit.Method} offset {fit.Offset:F4}s drift {fit.DriftPpm:F1}ppm conf {fit.Confidence:F0} windows {fit.Agreeing}/{fit.Windows} residual {fit.ResidualMs:F2}ms");
            }
            switch (a.SyncMethod)
            {
                case "audio":
                    break;
                case "metadata":
                    warnings.Add($"{name} は音声で同期できないため、撮影日時（メタデータ）で合わせました（{a.SyncOffset:+0.0;-0.0} 秒、誤差 ±1 秒程度）。ずれていれば「ずれ(秒)」で直せます");
                    break;
                case "manual":
                    break;
                default:
                    if (a.SyncMethod == null && a.SyncConfidence == SyncResult.Manual) break;
                    if (a.Media.AudioChannels == 0 || main.Media.AudioChannels == 0)
                    {
                        warnings.Add($"{name} は音声も撮影日時もないため同期できません。開始が同時として扱います（ずれていれば「ずれ(秒)」で直せます）");
                        break;
                    }
                    warnings.Add($"{name} の音声が基準映像と一致しません（信頼度 {a.SyncConfidence}）。この素材は使いません");
                    used.RemoveAt(i--);
                    break;
            }
        }
        if (preset.Planner == "switch" && used.Count < 2) throw new PipelineException("E-SWT-SYNC", "カメラの音声を同期できませんでした。同じ場面を撮影した素材を選んでください");
        Mark("ingest");

        Stage(JobStatus.Transcribing, 0.15, s.Asr == null ? "書き起こしは無効です" : "書き起こしています（Azure AI Speech）");
        Transcript tr;
        try { tr = await TranscriptAsync(main, audio, ct); }
        catch (AsrException ex) { throw new PipelineException("E-ASR-PROVIDER", "書き起こしに失敗しました: " + ex.Message); }
        if (tr.Words.Count == 0 && main.Media.AudioChannels > 0 && s.Asr != null) warnings.Add("発話が検出されませんでした");
        // series cuts are placed between sentences: restore sentence ends the recogniser ran together (「…と思います　あともう一個…」)
        if (job.Options.IsSeries) tr = SplitCues.Resegment(tr);
        Mark("transcribe");

        const string sigMsg = "ショット・音量・無音を解析しています";
        Stage(JobStatus.Analyzing, 0.35, sigMsg);
        var sig = await SignalsAsync(main, proxy, main.Media.AudioChannels > 0 ? audio : null, ct, Sub(JobStatus.Analyzing, 0.35, 0.43, sigMsg, "ショットの切り替わり検出"));
        // laughter is only needed when planning; an external detector (minutes) runs while the AI selects
        var laughsTask = LaughsAsync(main, tr, sig, audio, ct);
        if (preset.Planner == "switch") await AutoAssignSpeakersAsync(used, main, tr, warnings, ct);
        ScreenContext? screen = null;
        if (preset.Planner == "screen")
        {
            Stage(JobStatus.Analyzing, 0.43, "画面収録と話し手のカメラを見分けています");
            screen = await AnalyzeScreenAsync(used, main, proxy, warnings, ct);
        }
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

        Stage(JobStatus.Selecting, 0.45, job.Options.IsSeries ? (s.Llm == null ? "話題の切れ目を書き起こしから探しています" : $"AI が話題の切れ目と章を探しています（{s.Llm.Model}）")
            : preset.Planner == "speed" ? "早送りに向く区間を探しています" : s.Llm == null ? "音声の盛り上がりから選んでいます" : $"AI がハイライトを選んでいます（{s.Llm.Model}）");
        var exclude = job.ExcludeCandidateIds.Select(id => s.Store.Get<Candidate>(Kinds.Candidate, id)).Where(c => c != null).SelectMany(c => c!.Segments).ToList();
        SelectionOutcome outcome;
        var seriesParts = new Dictionary<string, SeriesPart>();
        if (job.Options.IsSeries)
        {
            var (o, partsById) = await SelectSeriesAsync(tr, sig, preset, main, job.Options.PartMinutes, job.Options.Parts, await laughsTask, warnings, ct);
            outcome = o; seriesParts = partsById;
        }
        else if (preset.Planner == "speed")
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
            foreach (var a in used.Where(a => a.Media.Aspect >= 0.9 && a.Media.Width > 0 && a.Id != screen?.ScreenAssetId))
            {
                var ranges = outcome.Candidates.SelectMany(c => c.Segments).Select(x => new Interval(Math.Max(0, a.CameraTime(x.T0, main.Id)), a.CameraTime(x.T1, main.Id))).ToList();
                var apx = a.Id == main.Id ? proxy : (await s.Probe.MakeProxyAsync(a.Path, a.Media, CacheDir(a), ct)).Item1;
                faces[a.Id] = await fs.AnalyzeAsync(apx, ranges, Path.Combine(CacheDir(a), "faces.json"), ct);
            }
            Mark("faces");
        }

        var laughs = await laughsTask;

        Stage(JobStatus.Planning, 0.6, "編集計画を作成しています");
        var candidates = new List<Candidate>();
        foreach (var c0 in outcome.Candidates)
        {
            var cid = $"{job.Id}_{c0.Id}";
            (EditPlan plan, List<string> notes) built;
            try { built = await BuildPlanAsync(c0 with { Id = cid }, tr, sig, preset, main, used, project.Id, job, faces, bgm, null, ct, laughs, seriesParts.GetValueOrDefault(c0.Id), screen); }
            catch (PlanException ex) { warnings.Add($"{c0.Id}: {ex.Message}"); continue; }
            var (plan, notes) = built;
            var segments = CandidateValidator.MergeOverlaps(plan.Timeline.Select(c => new Interval(c.MasterIn, c.MasterOut)).ToList(), 0);
            if (candidates.Any(c => CandidateValidator.IoU(c.Segments, segments) > 0.5))
            {
                warnings.Add($"{c0.Id}: 編集後の区間が他の候補と重なるため除外しました");
                continue;
            }
            s.Store.Put(Kinds.EditPlan, plan.EditplanId, project.Id, plan);
            var cand = c0 with { Id = cid, Rank = candidates.Count + 1, Segments = segments, EditPlanId = plan.EditplanId, Notes = [.. c0.Notes, .. notes], Series = plan.Series };
            candidates.Add(cand);
            s.Store.Put(Kinds.Candidate, cid, job.Id, cand);
        }
        if (candidates.Count == 0) throw new PipelineException("E-SOL-INFEASIBLE", "条件を満たす区間が見つかりませんでした。別のパターンを試してください。");
        if (seriesParts.Count > 0 && candidates.Count != seriesParts.Count) throw new PipelineException("E-SOL-INFEASIBLE", "一部のパートの編集計画を作れませんでした: " + string.Join(" / ", warnings.TakeLast(seriesParts.Count - candidates.Count)));
        if (!job.Options.IsSeries && candidates.Count < SelectionService.PresentCount)
            warnings.Add($"条件を満たす重複しない区間が{candidates.Count}件だけのため、候補は{candidates.Count}本です（最大{SelectionService.PresentCount}本）。重複する区間や条件外の区間は追加しません");
        Mark("plan");

        for (int i = 0; i < candidates.Count; i++)
        {
            var label = seriesParts.Count > 0 ? $"{candidates[i].Series?.Label} {i + 1}/{candidates.Count}" : $"{i + 1}/{candidates.Count}";
            var msg = seriesParts.Count > 0 ? $"プレビューを作成しています（{label}・長いので数分かかります）" : $"プレビューを作成しています（{label}）";
            double from = 0.65 + 0.3 * i / candidates.Count, to = 0.65 + 0.3 * (i + 1) / candidates.Count;
            Stage(JobStatus.Previewing, from, msg, 0, $"プレビュー {label}");
            var plan = s.Store.Get<EditPlan>(Kinds.EditPlan, candidates[i].EditPlanId!)!;
            var render = await RenderAsync(plan, preview: true, ct, progress: new SyncProgress<(double Progress, string Step)>(p =>
                Stage(JobStatus.Previewing, from + (to - from) * p.Progress, msg, p.Progress, $"プレビュー {label}: {p.Step}")));
            candidates[i] = candidates[i] with { PreviewRenderId = render.Id };
            s.Store.Put(Kinds.Candidate, candidates[i].Id, job.Id, candidates[i]);
        }
        Mark("preview");

        var cost = new CostEstimate(Math.Round(tr.Duration / 60, 1), outcome.InputTokens, outcome.OutputTokens,
            Math.Round(tr.Duration / 3600 * s.Pricing.AsrJpyPerHour + outcome.InputTokens / 1e6 * s.Pricing.LlmInJpyPer1M + outcome.OutputTokens / 1e6 * s.Pricing.LlmOutJpyPer1M, 2),
            Pricing.Basis + "（実績）");
        if (outcome.FullTextChars > 0 && outcome.SentChars > 0)
            s.Log($"[{job.Id}] LLM input chars {outcome.SentChars}/{outcome.FullTextChars} ({100.0 * outcome.SentChars / outcome.FullTextChars:F0}%)");
        return job with
        {
            Status = JobStatus.Ready, Progress = 1, Message = seriesParts.Count > 0 ? $"{candidates.Count} 本のシリーズ（{string.Join("・", candidates.Select(c => c.Series?.Label))}）ができました" : $"{candidates.Count} 件の候補ができました",
            CandidateIds = candidates.Select(c => c.Id).ToList(),
            LlmModel = outcome.Model, PromptVersion = seriesParts.Count > 0 ? SeriesPromptVersion : SelectionPrompt.Version, StageSeconds = stageTimes, Cost = cost, Warnings = warnings,
        };
    }

    public const string SeriesPromptVersion = "series-1.1.0";

    /// <summary>Series mode: chapters (AI, or TextTiling without AI) → split into exactly <paramref name="parts"/> parts, or parts of about <paramref name="partMinutes"/> → one candidate per part, in order.</summary>
    async Task<(SelectionOutcome, Dictionary<string, SeriesPart>)> SelectSeriesAsync(Transcript tr, SignalSet sig, Preset preset, SourceAsset main, double? partMinutes, int? parts0, IReadOnlyList<Interval> laughs, List<string> warnings, CancellationToken ct)
    {
        if (preset.Planner is not ("speech" or "switch" or "screen")) throw new PipelineException("E-SERIES-PRESET", $"「{preset.Name}」は前編・後編の分割に使えません。発話を軸に編集するパターン（P1〜P4・P6・P7・P9・P10）を選んでください");
        if (tr.Sentences.Count == 0) throw new PipelineException("E-SERIES-NOSPEECH", "前編・後編の分割は、話の切れ目を書き起こしから探します。発話のある動画を選んでください");
        ChapterOutline? outline = null;
        if (s.Llm != null)
        {
            try { outline = await SeriesPlanner.OutlineAsync(s.Llm, tr, s.Log, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { s.Log("series outline failed: " + ex.Message); }
            if (outline == null) warnings.Add("AI による章分けに失敗したため、書き起こしの語彙の変化と間から話題の切れ目を探しました");
        }
        double[]? semantic = null;
        if (s.Embedder != null)
        {
            try
            {
                var vecs = await TopicEmbedding.LoadOrEmbedAsync(s.Embedder, tr, Path.Combine(CacheDir(main), "topic_embed.json"), ct);
                if (vecs != null) semantic = TopicEmbedding.Depth(tr, vecs);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { s.Log("topic embedding failed: " + ex.Message); }
            if (semantic == null) warnings.Add("話の意味の変化の解析に失敗したため、語彙の変化と間から話題の切れ目を探しました");
        }
        outline ??= SeriesPlanner.FallbackOutline(tr, sig, semantic);
        var notes = new List<string>();
        List<SeriesPart> parts;
        try { parts = SeriesPlanner.Split(tr, sig, preset, main.Media.Duration, partMinutes * 60, parts0, outline, notes, laughs, semantic); }
        catch (PlanException ex) { throw new PipelineException(ex.Code, ex.Message); }
        warnings.AddRange(notes);
        var cands = parts.Select(pt => new Candidate
        {
            Id = $"part{pt.Index + 1}", Rank = pt.Index + 1, Segments = [pt.Body], HookText = SeriesPlanner.HookText(pt), Headline = pt.SeriesTitle,
            EmphasisWords = pt.Emphasis, Reason = pt.Boundary, Source = "series",
        }).ToList();
        int inTok = outline.InputChars > 0 ? Pricing.EstimateTokens(outline.InputChars) : 0, outTok = outline.OutputChars;
        var outcome = new SelectionOutcome(cands, outline.Source == "llm" ? s.Llm?.Model ?? "none" : "none", inTok, outTok, outline.Source != "llm", [], tr.Sentences.Sum(x => x.Text.Length), outline.InputChars);
        return (outcome, parts.ToDictionary(pt => $"part{pt.Index + 1}"));
    }

    public async Task<(EditPlan plan, List<string> notes)> BuildPlanAsync(Candidate c, Transcript tr, SignalSet sig, Preset p, SourceAsset main, IReadOnlyList<SourceAsset> assets, string projectId, Job job,
        IReadOnlyDictionary<string, List<FaceSample>> faces, BgmTrack? bgm, IReadOnlyList<Interval>? overrideSegments, CancellationToken ct, IReadOnlyList<Interval>? bursts = null, SeriesPart? part = null, ScreenContext? screen = null)
        => await PlanBuilder.BuildAsync(new PlanRequest(c, tr, sig, p, assets, main, projectId, job, faces, bgm, s.Llm, overrideSegments, bursts, part, BrandKitFor(projectId), screen), ct);

    public const string ScreenActivityFile = "screen_activity.v3.json";

    /// <summary>
    /// P10: per asset, how often the picture is still (screen activity from its proxy) and how often a face is seen (6 spread samples of 4 s);
    /// then which one is the screen recording and which the presenter. The decision and its evidence are reported to the user.
    /// </summary>
    async Task<ScreenContext> AnalyzeScreenAsync(List<SourceAsset> used, SourceAsset main, string mainProxy, List<string> warnings, CancellationToken ct)
    {
        var profiles = new Dictionary<string, SourceProfile>(); var activity = new Dictionary<string, ScreenAnalysis>();
        foreach (var a in used)
        {
            var apx = a.Id == main.Id ? mainProxy : (await s.Probe.MakeProxyAsync(a.Path, a.Media, CacheDir(a), ct)).Item1;
            var act = await ScreenActivity.AnalyzeAsync(s.Ff, apx, Path.Combine(CacheDir(a), ScreenActivityFile), ct);
            activity[a.Id] = act;
            double faceRatio = -1;
            if (s.Faces is { Available: true } fs)
            {
                double d = a.Media.Duration;
                var ranges = Enumerable.Range(0, 6).Select(k => d * (k + 0.5) / 6).Select(t => new Interval(Math.Max(0, t - 2), Math.Min(d, t + 2))).ToList();
                var samples = await fs.AnalyzeAsync(apx, ranges, Path.Combine(CacheDir(a), "faces.json"), ct);
                var inside = samples.Where(x => ranges.Any(r => x.T >= r.T0 && x.T <= r.T1)).ToList();
                faceRatio = inside.Count == 0 ? 0 : inside.Count(x => x.Boxes.Count > 0) / (double)inside.Count;
            }
            profiles[a.Id] = new SourceProfile(a.Id, ScreenActivity.StaticRatio(act.Samples), faceRatio);
        }
        var (scr, pres, reason) = ScreenRoles.Classify(used, profiles);
        s.Log("screen roles: " + reason);
        if (scr == null) throw new PipelineException("E-SCR-NOSCREEN", reason + "。素材の役割で「画面」を指定してください");
        warnings.Add("自動判定 — " + reason);
        if (pres == null) warnings.Add("話し手のカメラがないため、ワイプなしで画面だけを使います");
        return new ScreenContext(scr, pres, activity[scr].Samples, activity[scr].Details);
    }

    public ScreenContext? LoadScreenContext(IReadOnlyList<SourceAsset> used, EditPlan old)
    {
        var scr = old.Timeline.FirstOrDefault(c => c.Reframe.Mode == "screen")?.AssetId ?? used.FirstOrDefault(a => a.Role == AssetRole.Screen)?.Id;
        if (scr == null || used.FirstOrDefault(a => a.Id == scr) is not { } sa) return null;
        var f = Path.Combine(CacheDir(sa), ScreenActivityFile);
        if (!File.Exists(f)) return null;
        var pres = old.Timeline.FirstOrDefault(c => c.Pip != null)?.Pip!.AssetId ?? old.Timeline.FirstOrDefault(c => c.AssetId != scr)?.AssetId;
        var an = Json.Deserialize<ScreenAnalysis>(File.ReadAllText(f));
        return new ScreenContext(scr, pres, an.Samples, an.Details);
    }

    public const string BrandKitId = "default";
    /// <summary>Variety thumbnails are graded this much more saturated (the references measured 1.7–1.8× the saturation of their own frames).</summary>
    public const double ThumbnailSaturation = 1.6;
    /// <summary>The channel's registered opening / closing / card files (one per installation).</summary>
    public BrandKit? BrandKit => s.Store.Get<BrandKit>(Kinds.BrandKit, BrandKitId);

    public BrandKit? BrandKitFor(string projectId)
    {
        var kit = BrandKit;
        if (kit is { RightsConfirmed: true } && kit.Files.Any(f => f.Kind == "card")) return kit;
        var project = s.Store.Get<Project>(Kinds.Project, projectId);
        if (project == null || !project.UsesSceneCardImages()) return kit;
        var rights = s.Store.List<RightsConfirmation>(Kinds.Rights, projectId).Select(r => r.AssetId).ToHashSet();
        bool Allowed(SceneCardImage i) => i.BrandPath != null
            ? kit is { RightsConfirmed: true } && kit.Files.Any(f => f.Kind is "opening" or "closing" && string.Equals(f.Path, i.BrandPath, StringComparison.OrdinalIgnoreCase))
            : i.AssetId != null && rights.Contains(i.AssetId);
        var cards = project.SceneCardImages.Where(i => Allowed(i) && File.Exists(i.Path))
            .Select(i => new BrandFile { Path = i.Path, Kind = "card", Width = i.Width, Height = i.Height, ImageAssetId = i.AssetId }).ToList();
        if (cards.Count == 0) return kit;
        return (kit is { RightsConfirmed: true } ? kit : new BrandKit()) with
        {
            RightsConfirmed = true, Files = [.. kit is { RightsConfirmed: true } ? kit.Files : [], .. cards],
        };
    }

    /// <summary>FR-SWT-01: cameras without a user-set speaker get one from their own microphone, when the evidence is clear.</summary>
    async Task AutoAssignSpeakersAsync(List<SourceAsset> used, SourceAsset main, Transcript tr, List<string> warnings, CancellationToken ct)
    {
        var cams = used.Where(a => a.Role != AssetRole.Wide && a.Media.AudioChannels > 0).ToList();
        if (cams.Count < 2 || cams.Any(a => a.Speaker != null)) return;
        var audios = new List<float[]>();
        foreach (var a in cams)
        {
            double off = a.CameraTime(0, main.Id);
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

    /// <summary>FR-SIG-04: local non-speech laughter detection, cached per asset/transcript.</summary>
    public async Task<List<Interval>> LaughsAsync(SourceAsset a, Transcript tr, SignalSet sig, string audio, CancellationToken ct)
    {
        var file = LocalLaughsFile(a);
        if (File.Exists(file)) return Json.Deserialize<List<Interval>>(await File.ReadAllTextAsync(file, ct));
        var res = a.Media.AudioChannels > 0 && File.Exists(audio) ? await LaughDetector.DetectAsync(s.Ff, audio, NonSpeech.Bursts(sig, tr), ct) : [];
        await File.WriteAllTextAsync(file, Json.Serialize(res), ct);
        return res;
    }

    string LocalLaughsFile(SourceAsset a) =>
        Path.Combine(CacheDir(a), "laughs_" + Path.GetFileNameWithoutExtension(Directory.GetFiles(CacheDir(a), "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "none") + ".json");

    public List<Interval> LoadLaughs(SourceAsset a)
    {
        var dir = CacheDir(a);
        if (!Directory.Exists(dir)) return [];
        var f = Directory.GetFiles(dir, "laughs_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return f != null ? Json.Deserialize<List<Interval>>(File.ReadAllText(f)) : [];
    }

    /// <summary>Re-plan an existing plan's candidate after the user edited the segments (PATCH /editplans).</summary>
    public async Task<(EditPlan plan, List<string> notes)> ReplanAsync(Candidate c, EditPlan old, Job job, IReadOnlyList<Interval> segments, CancellationToken ct)
    {
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, old.ProjectId);
        var main = assets.First(a => a.Id == old.Audio.Master);
        var p = s.Presets[old.Preset.Id];
        var used = p.Planner is "switch" or "screen"
            ? assets.Where(a => a.Id == main.Id || old.Timeline.Any(t => t.AssetId == a.Id || t.Pip?.AssetId == a.Id)).ToList() : [main];
        var faces = new Dictionary<string, List<FaceSample>>();
        foreach (var a in used) { var f = FaceService.Load(Path.Combine(CacheDir(a), "faces.json")); if (f.Count > 0) faces[a.Id] = f; }
        BgmTrack? bgm = old.Audio.Bgm is { } id && FindBgm(id) is { } t ? (p.Planner == "beat" ? await AnalyzedBgmAsync(t, ct) : t) : null;
        return await BuildPlanAsync(c, LoadTranscript(main), LoadSignals(main), p, main, used, old.ProjectId, job with { Options = job.Options with { Bgm = old.Audio.Bgm ?? "none" } }, faces, bgm, segments, ct, LoadLaughs(main),
            screen: p.Planner == "screen" ? LoadScreenContext(used, old) : null);
    }

    public QaContext QaContextFor(EditPlan plan)
    {
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, plan.ProjectId);
        var main = assets.First(a => a.Id == plan.Audio.Master);
        var tr = LoadTranscript(main);
        var rights = s.Store.List<RightsConfirmation>(Kinds.Rights, plan.ProjectId).Select(r => r.AssetId).ToHashSet();
        var mask = plan.Provenance.JobId is { } jobId ? s.Store.Get<Job>(Kinds.Job, jobId)?.Options.MaskWords : null;
        var approval = plan.Provenance.CandidateId is { } cid ? s.Store.Get<Candidate>(Kinds.Candidate, cid)?.Approval : null;
        return new QaContext(plan, s.Presets[plan.Preset.Id], tr, rights, DictionaryFor(plan.ProjectId), assets, mask, approval);
    }

    public Transcript LoadTranscript(SourceAsset a)
    {
        var dir = CacheDir(a);
        var expected = TranscriptFile(a, DictionaryFor(a.ProjectId));
        if (File.Exists(expected)) return Json.Deserialize<Transcript>(File.ReadAllText(expected));
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

    /// <summary>Signal cache name; bumped when the extracted signals change (v2: black frames) so older caches are recomputed.</summary>
    public const string SignalsFile = "signals.v2.json";

    public SignalSet LoadSignals(SourceAsset a)
    {
        var f = Path.Combine(CacheDir(a), SignalsFile);
        return File.Exists(f) ? Json.Deserialize<SignalSet>(File.ReadAllText(f)) : new SignalSet();
    }

    /// <summary>
    /// Render (preview or final) + QA. Final renders are blocked by hard QA failures (E-QA-HARDFAIL).
    /// When <paramref name="renderId"/> is given the caller owns that id and persists the Render itself (after adding its own outputs),
    /// so a final export is stored exactly once under the id the UI already polls.
    /// </summary>
    public async Task<Render> RenderAsync(EditPlan plan, bool preview, CancellationToken ct, string? exportBaseName = null, string? renderId = null, IProgress<(double Progress, string Step)>? progress = null)
    {
        var project = s.Store.Get<Project>(Kinds.Project, plan.ProjectId)!;
        var assets = s.Store.List<SourceAsset>(Kinds.Asset, plan.ProjectId).ToDictionary(a => a.Id);
        var preset = s.Presets[plan.Preset.Id];
        var ctx = QaContextFor(plan);
        var persist = renderId == null;
        renderId ??= $"r_{Guid.NewGuid():N}"[..14];
        if (!preview)
        {
            EnsureCurrentPlan(plan);
            var pre = QaEngine.PlanChecks(ctx).Where(q => q.Hard && q.Status == QaStatus.Fail).ToList();
            if (pre.Count > 0) throw new QaBlockedException(pre);
        }
        var dir = preview ? Path.Combine(project.Directory, "previews") : Path.Combine(project.Directory, "exports", renderId);
        var file = Path.Combine(dir, preview ? $"{renderId}.mp4" : exportBaseName + ".mp4");
        if (!preview) EnsureInside(project.Directory, file);
        var encodeStep = preview ? "映像と音声を合成しています" : "映像を書き出し、音量を 2 パスで合わせています";
        var outp = await s.Renderer.RenderAsync(plan, preset, assets, file, preview, ct,
            progress == null ? null : new SyncProgress<double>(v => progress.Report((0.9 * v, encodeStep))));
        progress?.Report((0.9, "自動チェック（QA）をしています"));
        var qa = await s.Qa.RunAsync(ctx, file, preview, Path.Combine(s.DataDir, "qa", renderId), ct);
        var outputs = new Dictionary<string, string> { ["mp4"] = file };
        RenderReproducibility? reproducibility = null;
        if (!preview)
        {
            var hard = qa.Where(q => q.Hard && q.Status is QaStatus.Fail or QaStatus.NotRun).ToList();
            if (hard.Count > 0)
            {
                var rejected = Path.Combine(project.Directory, "rejected", Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(rejected)!);
                File.Move(file, rejected, true);
                throw new QaBlockedException(hard);
            }
            progress?.Report((0.97, "字幕ファイルとサムネイル候補を作っています"));
            var srt = Path.ChangeExtension(file, ".srt"); await File.WriteAllTextAsync(srt, AssWriter.Srt(plan, preset), new UTF8Encoding(false), ct); outputs["srt"] = srt;
            var epj = Path.ChangeExtension(file, ".editplan.json"); await File.WriteAllTextAsync(epj, Json.Serialize(plan, true), ct); outputs["editplan"] = epj;
            Func<double, Task<double>>? faceScore = s.Faces is { Available: true } fs
                ? async t => (await fs.DetectAtAsync(file, t, ct)).Select(f => f.W * f.H).DefaultIfEmpty(0).Max()
                : null;
            var times = await ThumbnailPicker.PickAsync(plan, faceScore);
            var thumbs = await s.Renderer.ThumbnailsAsync(file, times, Path.ChangeExtension(file, null), preset.Telop.IsVariety ? ThumbnailSaturation : 1.0, ct);
            for (int i = 0; i < thumbs.Count; i++) outputs[$"thumb{i + 1}"] = thumbs[i];
            var ass = Path.ChangeExtension(file, ".ass"); if (File.Exists(ass)) outputs["ass"] = ass;
            if (!File.Exists(ass))
                await File.WriteAllTextAsync(ass, AssWriter.Write(plan, preset), new UTF8Encoding(false), ct);
            outputs["ass"] = ass;
            var sourcePaths = plan.Timeline.Select(c => assets[c.AssetId].Path)
                .Concat(plan.Timeline.Where(c => c.Pip != null).Select(c => assets[c.Pip!.AssetId].Path))
                .Append(assets[plan.Audio.Master].Path)
                .Concat(InsertMap.Active(plan).Where(i => i.Path != null).Select(i => i.Path!)).ToList();
            if (plan.Audio.Bgm is { } bgm)
            {
                var bgmPath = s.Audio?.Find(bgm)?.Path ?? s.Store.Get<BgmTrack>(Kinds.Bgm, bgm)?.Path;
                if (bgmPath != null) sourcePaths.Add(bgmPath);
            }
            if (plan.Audio.SfxEnabled && s.Audio != null)
                sourcePaths.AddRange(plan.Audio.Sfx.Select(e => s.Audio.SfxPath(e.Kind)).Where(path => path != null).Select(path => path!));
            reproducibility = await RenderProvenance.CaptureAsync(s.Ff, plan, preset, sourcePaths, ass, ct);
        }
        var render = new Render
        {
            Id = renderId, EditplanId = plan.EditplanId, Kind = preview ? "preview" : "final", Path = file, Duration = plan.TotalDuration,
            PlanHash = EditPlanFingerprint.Of(plan),
            FfmpegVersion = await s.Ff.VersionAsync(), Commands = outp.Commands, Qa = qa, Outputs = outputs,
            Reproducibility = reproducibility,
        };
        if (!preview)
        {
            try
            {
                s.Store.Atomic(() =>
                {
                    EnsureCurrentPlan(plan);
                    if (persist) s.Store.Put(Kinds.Render, render.Id, plan.EditplanId, render);
                    return true;
                });
            }
            catch (PipelineException ex) when (ex.Code == "E-PLAN-STALE") { Quarantine(render); throw; }
        }
        else if (persist) s.Store.Put(Kinds.Render, render.Id, plan.EditplanId, render);
        progress?.Report((1, "完了しました"));
        return render;
    }

    public void EnsureCurrentPlan(EditPlan plan)
    {
        var current = s.Store.Get<EditPlan>(Kinds.EditPlan, plan.EditplanId);
        if (current == null || EditPlanFingerprint.Of(current) != EditPlanFingerprint.Of(plan))
            throw new PipelineException("E-PLAN-STALE", "編集計画が変更されたため承認・QA・書き出し結果は失効しました。内容を確認して再承認してください");
    }

    public void Quarantine(Render render)
    {
        var plan = s.Store.Get<EditPlan>(Kinds.EditPlan, render.EditplanId);
        var project = plan == null ? null : s.Store.Get<Project>(Kinds.Project, plan.ProjectId);
        if (project == null) throw new PipelineException("E-NOT-FOUND", "失効した書き出しのプロジェクトがありません");
        var directory = Path.Combine(project.Directory, "rejected", render.Id);
        Directory.CreateDirectory(directory);
        foreach (var file in render.Outputs.Values.Append(render.Path).Distinct())
        {
            EnsureInside(project.Directory, file);
            if (File.Exists(file)) File.Move(file, Path.Combine(directory, Path.GetFileName(file)), true);
        }
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
        if (plan.Series is { } se) return $"{safe}_{plan.Preset.Id}_{se.Part:D2}{se.Label}_{DateTime.Now:yyyyMMdd-HHmm}";
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
        if (plan.Series is { } se) return SeriesMetadata(se, plan, titles, desc, tags);
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

    /// <summary>Series parts: label-prefixed titles, YouTube chapters (validated against YouTube's rules), links between parts, no #Shorts (vertical videos over 3 min are regular videos on YouTube).</summary>
    public static string SeriesMetadata(SeriesInfo se, EditPlan plan, string titles, string desc, string tags)
    {
        var label = $"【{se.Label}】";
        var prefixed = string.Join("\n", titles.Split('\n').Select(l =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^(\d+\.\s*)(.*)$");
            return m.Success && !m.Groups[2].Value.Contains(se.Label, StringComparison.Ordinal) ? $"{m.Groups[1].Value}{label}{m.Groups[2].Value}" : l;
        }));
        if (se.SeriesTitle.Length > 0) prefixed += $"\n{titles.Split('\n').Length + 1}. {label}{se.SeriesTitle}";
        double total = plan.TotalDuration;
        var chapters = SeriesPlanner.YouTubeChapters(QaEngine.FinalChapters(plan), total);
        var chapterText = chapters != null ? string.Join("\n", chapters)
            : "（チャプターが3つ未満、または10秒未満のため、YouTube のチャプター条件を満たしません。チャプターは付けずに投稿してください）";
        var links = string.Join("\n", se.Labels.Select((l, i) => i + 1 == se.Part ? $"{l}: この動画" : $"{l}: （ここに{l}の URL を記入）"));
        var length = total > 180
            ? $"長さ {SeriesPlanner.Stamp(total, total)}。縦型でも3分を超えるため、YouTube ではショートではなく通常の動画として扱われます（YouTube ヘルプ「Understand three-minute YouTube Shorts」）。TikTok のアップロードは60分まで"
            : $"長さ {SeriesPlanner.Stamp(total, total)}。3分以内の縦型動画は YouTube でショートとして扱われます";
        return $"""
# 投稿用メタデータ案 {label}（{se.Part}/{se.Parts}。AI による提案。投稿前に内容を確認してください）

## タイトル候補（シリーズで同じ書き方にそろえると、続きを探しやすくなります）
{prefixed}

## 説明文
{desc}

## チャプター（説明文に貼り付け。YouTube の条件: 先頭 00:00・3つ以上・各10秒以上）
{chapterText}

## シリーズ
{links}

元動画: （ここに元動画の URL を記入）

## ハッシュタグ
{tags}

## 長さ
{length}

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
