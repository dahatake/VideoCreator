using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Storage;

namespace VideoCreator.App;

public static class Api
{
    public sealed record CreateProject(string Name);
    public sealed record RenameProject(string? Name);
    public sealed record PutProjectProperNouns(List<ProjectProperNoun?>? ProperNouns);
    public sealed record AddAsset(string Path);
    public sealed record ConfirmRights(string AssetId, bool Confirmed);
    public sealed record StartJob(List<string> PresetIds, string? Instruction, JobOptions? Options = null, string? DerivedFromJobId = null);
    public sealed record PatchAsset(AssetRole? Role, int? Speaker, double? SyncOffset, bool? ClearSpeaker);
    public sealed record Regenerate(string? Instruction, List<string>? ExcludeCandidateIds);
    public sealed record PatchPlan(List<PlanOp> Ops);
    /// <summary>A BGM file that could not be added (the others in the same upload still are).</summary>
    public sealed record SkippedBgm(string Name, string Reason);
    public const int MaxBgmPerUpload = 20;
    /// <summary>「Copilotで自動修復」: the checks to repair (null/empty = every repairable warn/fail).</summary>
    public sealed record QaRepairRequest(List<string>? CheckIds);
    public sealed record QaApprovalTarget(string CheckId, string ResultHash);
    public sealed record QaApprovalRequest(string? PlanHash, List<QaApprovalTarget>? Checks, bool Approved);
    /// <summary>Plan as it was before an auto-repair; undo restores it only while the plan is still exactly the repaired one.</summary>
    public sealed record QaRepairSnapshot(string Id, string EditplanId, EditPlan Before, string AfterHash, DateTimeOffset At);
    public sealed record Settings(string LlmProvider);
    public sealed record WordEdit(string WordId, string Text);
    public sealed record PatchTranscript(List<WordEdit> Edits);
    public sealed record PutBrandKit(List<string>? Paths, bool RightsConfirmed, double? CardSec, bool? Upscale = null);
    /// <summary>Initial directory for the Windows folder selection dialog.</summary>
    public sealed record PickFolderRequest(string? InitialPath);
    /// <summary>Selected absolute directory, or an explicit null when the user cancels.</summary>
    public sealed record PickFolderResponse(
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)] string? Path);
    /// <summary>Source — a project video (<c>asset_id</c>) or a registered opening / closing video (<c>brand_path</c>), exactly one — and the target number of distinct images (2–6).</summary>
    public sealed record GenerateSceneCardsRequest(string? AssetId, int Count = 4, string? BrandPath = null);
    /// <summary>作る (true) / 作らない (false) for this project's video-extracted scene-card images.</summary>
    public sealed record SceneCardSettingsRequest(bool Make);
    /// <summary>Approve (true) or withdraw the approval of (false) a candidate as it is now.</summary>
    public sealed record ApproveCandidateRequest(bool Approved);
    /// <summary>New projects folder and whether to move everything in the current one there (true) or leave it (false).</summary>
    public sealed record ChangeProjectsDirRequest(string? Path, bool Move);
    /// <summary>Current projects folder; <c>source</c> = default | config | app (chosen in the settings screen).</summary>
    public sealed record ProjectsDirResponse(string Path, string Source, string ConfiguredPath, int Projects, MoveProgress Progress);
    /// <summary>Approved candidates of one job to export, in any order (exported in rank / part order).</summary>
    public sealed record BatchExportRequest(List<string>? CandidateIds);
    /// <summary>One started export of a batch: poll <c>/api/renders/{render_id}</c>.</summary>
    public sealed record BatchExportItem(string CandidateId, string RenderId);
    /// <summary>The exports started by a batch request.</summary>
    public sealed record BatchExportResponse(List<BatchExportItem> Exports);
    /// <summary>Why one candidate of a refused batch cannot be exported. <c>code</c> is what <c>/editplans/{id}/render</c> would return for it;
    /// <c>detail</c> is the failed hard QA checks (<c>E-QA-HARDFAIL</c>) or the running export (<c>E-EXPORT-BUSY</c>).</summary>
    public sealed record BatchExportProblem(string CandidateId, string Name, string Code, string Reason, object? Detail = null)
    {
        /// <summary>Order of the single-export checks: a refused batch answers with the first code that any candidate hit.</summary>
        public static readonly (string Code, int Status)[] Precedence =
            [("E-NOT-FOUND", 404), ("E-NOT-APPROVED", 409), ("E-PLAN-STALE", 409), ("E-QA-HARDFAIL", 422), ("E-EXPORT-BUSY", 409)];
    }

    /// <summary>Approval as shown in the UI: <c>stale</c> = approved, but the plan was edited afterwards.</summary>
    static object? ApprovalView(Candidate c, EditPlan? plan) => c.Approval is not { } a ? null : new
    {
        a.At, a.By, Stale = plan == null || EditPlanFingerprint.Of(plan) != a.PlanHash,
    };

    static bool Stale(Runtime rt, Render r) => RenderValidity.IsStale(r, rt.Store.Get<EditPlan>(Kinds.EditPlan, r.EditplanId));
    static IResult StaleError() => Error(409, "E-PLAN-STALE", "編集計画が変更されたため承認・QA・書き出し結果は失効しました。内容を確認して再承認してください");

    static ProjectWorkflowState ResolveWorkflowState(Runtime rt, JobQueue q, Project project, List<Job> jobs)
    {
        var saved = project.WorkflowState;
        if (saved != null)
        {
            if (saved.Step is "project" or "settings" && saved.JobId == null && saved.EditPlanId == null && saved.RenderId == null)
                return saved;
            if (saved.Step is "job" or "candidates" or "exports" && saved.JobId is string jobId
                && rt.Store.Get<Job>(Kinds.Job, jobId) is { ProjectId: var jobProject } job && jobProject == project.Id)
            {
                if (saved.Step == "job" || job.Status is JobStatus.Ready or JobStatus.Done)
                    return saved;
                return saved with { Step = "job", EditPlanId = null, RenderId = null };
            }
            if (saved.Step == "review" && saved.EditPlanId is string planId
                && rt.Store.Get<EditPlan>(Kinds.EditPlan, planId) is { ProjectId: var planProject } && planProject == project.Id)
                return saved;
            if (saved.Step == "render" && saved.RenderId is string renderId)
            {
                var render = rt.Store.Get<Render>(Kinds.Render, renderId);
                var export = q.Exports.TryGetValue(renderId, out var currentExport) ? currentExport : null;
                var editPlanId = render?.EditplanId ?? export?.EditplanId ?? saved.EditPlanId;
                var plan = editPlanId == null ? null : rt.Store.Get<EditPlan>(Kinds.EditPlan, editPlanId);
                if (plan?.ProjectId == project.Id)
                    return render != null && File.Exists(render.Path) && !RenderValidity.IsStale(render, plan)
                        ? saved with { EditPlanId = editPlanId }
                        : new ProjectWorkflowState { Step = "review", EditPlanId = editPlanId };
            }
        }

        var resumeJob = jobs.FirstOrDefault(j => j.Status is not (JobStatus.Failed or JobStatus.Cancelled));
        return resumeJob == null
            ? new ProjectWorkflowState { Step = "project" }
            : new ProjectWorkflowState
            {
                Step = resumeJob.Status is JobStatus.Ready or JobStatus.Done ? "candidates" : "job",
                JobId = resumeJob.Id,
            };
    }

    /// <summary>The kit plus which variant a 1080x1920 output would use, how much it is upscaled at render time, and the registration-time upscale.</summary>
    static object BrandKitView(BrandKit? kit, List<string> problems, BrandKitWorker worker)
    {
        var canvas = new Canvas();
        object? Pick(string kind) => kit == null ? null : Planning.BrandKitService.Pick(kit.Files, kind, canvas) is { } f
            ? new
            {
                f.Path, f.Width, f.Height, f.Duration, f.HasAudio, Lufs = double.IsNaN(f.Lufs) ? (double?)null : Math.Round(f.Lufs, 1),
                Upscale = Math.Round(Planning.BrandKitService.Upscale(f.Width, f.Height, canvas), 2), f.Enhancement,
                Needs = Planning.BrandKitEnhancer.Needs(f, canvas),
            } : null;
        return new
        {
            Registered = kit != null, Kit = kit, Problems = problems,
            Opening = Pick("opening"), Closing = Pick("closing"), CardImages = kit?.Files.Count(f => f.Kind == "card") ?? 0,
            Enhance = worker.Current,
        };
    }

    static IResult Error(int status, string code, string message, object? detail = null) =>
        Results.Json(new { error = new { code, message, detail } }, Json.Options, statusCode: status);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", async (Runtime rt) => Results.Json(new
        {
            ffmpeg = await rt.Ff.VersionAsync(),
            missing_filters = await rt.Ff.MissingFiltersAsync(),
            asr = rt.Asr?.Name ?? "none",
            llm_provider = rt.LlmProvider,
            llm_model = rt.Llm?.Model,
            copilot_cli = rt.CopilotCli,
            local_only = rt.LocalOnly,
            transmission = rt.Transmission,
            faces_model = rt.Faces.Available,
            bgm_tracks = rt.Audio.Tracks.Count,
            presets = rt.Presets.Count,
        }, Json.Options));

        api.MapGet("/settings", (Runtime rt) => Results.Json(new
        {
            llm_provider = rt.LlmProvider,
            local_only = rt.LocalOnly,
            transmission = rt.Transmission,
            copilot_cli = rt.CopilotCli,
            copilot_model = rt.Options.Llm.CopilotModel,
            azure_deployment = rt.Options.Llm.Deployment,
            azure_configured = !string.IsNullOrWhiteSpace(rt.Options.Llm.Endpoint),
            asr_configured = rt.Asr != null,
        }, Json.Options));
        api.MapPut("/settings", (Runtime rt, Settings s) =>
        {
            try { rt.SetLlmProvider(s.LlmProvider); return Results.Json(new { llm_provider = rt.LlmProvider }, Json.Options); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Error(400, "E-SETTINGS", ex.Message); }
        });

        // ---- projects folder: where projects are saved; changing it can move everything in the current folder ----
        api.MapGet("/settings/projects-dir", (Runtime rt, ProjectsLocation loc) => Results.Json(new ProjectsDirResponse(
            loc.Root, loc.Source, rt.Options.ResolvedProjectsDir, rt.Store.List<Project>(Kinds.Project).Count, loc.Progress), Json.Options))
            .WithSummary("Current projects folder, where it comes from (default / config / app) and the progress of a move in flight");
        api.MapPost("/settings/projects-dir/plan", (ProjectsLocation loc, ChangeProjectsDirRequest req) => Results.Json(loc.Plan(req.Path, req.Move), Json.Options))
            .WithSummary("Preview a change of the projects folder (what would move, size, free space, problems); changes nothing");
        api.MapPut("/settings/projects-dir", async (ProjectsLocation loc, ChangeProjectsDirRequest req, CancellationToken ct) =>
        {
            try { return Results.Json(await loc.ChangeAsync(req.Path, req.Move, ct), Json.Options); }
            catch (ProjectsBusyException ex) { return Error(409, "E-PROJECTS-BUSY", ex.Message); }
            catch (ArgumentException ex) { return Error(422, "E-VALIDATION", ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                app.Logger.LogWarning(ex, "Changing the projects folder failed");
                return Error(500, "E-PROJECTS-DIR", "保存先を変更できませんでした。フォルダーの権限と空き容量を確認してください");
            }
        }).WithSummary("Change the projects folder; move=true carries every item of the current folder over and re-roots the stored paths");

        api.MapGet("/presets", (Runtime rt) => Results.Json(rt.Presets.Values.OrderBy(p => int.Parse(p.Id[1..])), Json.Options));

        // ---- projects ----
        api.MapPost("/projects", (Runtime rt, ProjectsLocation loc, CreateProject req) =>
        {
            var name = string.IsNullOrWhiteSpace(req.Name) ? "新しいプロジェクト" : req.Name;
            if (ValidateProjectName(name, out name) is string err) return Error(422, "E-VALIDATION", err);
            var id = "p_" + Guid.NewGuid().ToString("N")[..10];
            var dir = Path.Combine(loc.Root, $"{SafeName(name)}_{id[2..8]}");
            Directory.CreateDirectory(dir);
            var p = new Project { Id = id, Name = name, Directory = dir };
            rt.Store.Put(Kinds.Project, id, null, p);
            return Results.Json(p, Json.Options, statusCode: 201);
        });
        api.MapGet("/projects", (Runtime rt) => Results.Json(rt.Store.List<Project>(Kinds.Project).Select(p => new
        {
            p.Id, p.Name, p.CreatedAt, p.Directory,
            assets = rt.Store.List<SourceAsset>(Kinds.Asset, p.Id).Count,
            last_job = rt.Store.List<Job>(Kinds.Job, p.Id).FirstOrDefault(),
        }), Json.Options));
        api.MapGet("/projects/{id}", (Runtime rt, JobQueue q, string id) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            var jobs = rt.Store.List<Job>(Kinds.Job, id);
            var resumeState = ResolveWorkflowState(rt, q, p, jobs);
            return Results.Json(new
            {
                project = p,
                assets = rt.Store.List<SourceAsset>(Kinds.Asset, id),
                rights = rt.Store.List<RightsConfirmation>(Kinds.Rights, id),
                jobs,
                resume_state = resumeState,
                rights_statement = RightsStatement.Text,
            }, Json.Options);
        });
        // Display name only: the folder and every stored absolute path stay as they are.
        api.MapPatch("/projects/{id}", (Runtime rt, string id, RenameProject req) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            if (ValidateProjectName(req.Name, out var name) is string err) return Error(422, "E-VALIDATION", err);
            if (name == p.Name) return Results.Json(p, Json.Options);
            p = p with { Name = name };
            rt.Store.Put(Kinds.Project, id, null, p);
            return Results.Json(p, Json.Options);
        });
        api.MapPut("/projects/{id}/workflow-state", (Runtime rt, JobQueue q, string id, ProjectWorkflowState? state) =>
            rt.Store.Atomic<IResult>(() =>
            {
                var p = rt.Store.Get<Project>(Kinds.Project, id);
                if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
                if (state == null) return Error(422, "E-VALIDATION", "ワークフロー状態がありません");

                ProjectWorkflowState saved;
                switch (state.Step)
                {
                    case "project":
                    case "settings":
                        if (state.JobId != null || state.EditPlanId != null || state.RenderId != null)
                            return Error(422, "E-VALIDATION", "プロジェクト画面にリソースIDは指定できません");
                        saved = state with { UpdatedAt = DateTimeOffset.UtcNow };
                        break;
                    case "job":
                    case "candidates":
                    case "exports":
                    {
                        var job = state.JobId == null ? null : rt.Store.Get<Job>(Kinds.Job, state.JobId);
                        if (job?.ProjectId != id || state.EditPlanId != null || state.RenderId != null)
                            return Error(422, "E-VALIDATION", "このプロジェクトの生成ジョブを指定してください");
                        if (state.Step != "job" && job!.Status is not (JobStatus.Ready or JobStatus.Done))
                            return Error(422, "E-VALIDATION", "完了した生成ジョブだけ候補画面を保存できます");
                        saved = state with { UpdatedAt = DateTimeOffset.UtcNow };
                        break;
                    }
                    case "review":
                    {
                        var plan = state.EditPlanId == null ? null : rt.Store.Get<EditPlan>(Kinds.EditPlan, state.EditPlanId);
                        if (plan?.ProjectId != id || state.JobId != null || state.RenderId != null)
                            return Error(422, "E-VALIDATION", "このプロジェクトの編集計画を指定してください");
                        saved = state with { UpdatedAt = DateTimeOffset.UtcNow };
                        break;
                    }
                    case "render":
                    {
                        var render = state.RenderId == null ? null : rt.Store.Get<Render>(Kinds.Render, state.RenderId);
                        var export = state.RenderId != null && q.Exports.TryGetValue(state.RenderId, out var currentExport) ? currentExport : null;
                        var editPlanId = render?.EditplanId ?? export?.EditplanId;
                        var plan = editPlanId == null ? null : rt.Store.Get<EditPlan>(Kinds.EditPlan, editPlanId);
                        if (plan?.ProjectId != id || (state.EditPlanId != null && state.EditPlanId != editPlanId) || state.JobId != null)
                            return Error(422, "E-VALIDATION", "このプロジェクトの書き出しを指定してください");
                        saved = state with { EditPlanId = editPlanId, UpdatedAt = DateTimeOffset.UtcNow };
                        break;
                    }
                    default:
                        return Error(422, "E-VALIDATION", "ワークフローの段階が正しくありません");
                }

                rt.Store.Put(Kinds.Project, id, null, p with { WorkflowState = saved });
                return Results.Json(saved, Json.Options);
            }));
        api.MapPut("/projects/{id}/generation-settings", (Runtime rt, string id, ProjectGenerationSettings? settings) =>
            rt.Store.Atomic<IResult>(() =>
            {
                var p = rt.Store.Get<Project>(Kinds.Project, id);
                if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
                if (settings == null || settings.MaskWords == null || string.IsNullOrWhiteSpace(settings.Bgm))
                    return Error(422, "E-VALIDATION", "生成設定の形式が正しくありません");
                if (string.IsNullOrWhiteSpace(settings.PresetId) || !rt.Presets.TryGetValue(settings.PresetId, out var preset) || !preset.Available)
                    return Error(422, "E-VALIDATION", "利用できるパターンを選んでください");
                if (settings.Scope is not ("full" or "balanced" or "economy"))
                    return Error(422, "E-VALIDATION", "絞り込みは full / balanced / economy のどれかを指定してください");
                if (settings.SeriesBy is not ("parts" or "minutes"))
                    return Error(422, "E-VALIDATION", "シリーズの分け方は parts / minutes のどちらかを指定してください");
                if (!Planning.TsukkomiPlacement.Styles.Contains(settings.TsukkomiStyle))
                    return Error(422, "E-VALIDATION", "ツッコミの見せ方は auto / telop / card のどれかを指定してください");
                p = p with { GenerationSettings = settings };
                rt.Store.Put(Kinds.Project, id, null, p);
                return Results.Json(p.GenerationSettings, Json.Options);
            }));
        api.MapPut("/projects/{id}/proper-nouns", (Runtime rt, string id, PutProjectProperNouns req) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            if (req.ProperNouns == null || req.ProperNouns.Count > 100)
                return Error(422, "E-VALIDATION", "固有名詞は1プロジェクトあたり100件以内で指定してください");

            var properNouns = new List<ProjectProperNoun>(req.ProperNouns.Count);
            foreach (var item in req.ProperNouns)
            {
                if (item == null) return Error(422, "E-VALIDATION", "空の固有名詞は登録できません");
                var display = NormalizeProperNounText(item.DisplayText);
                var reading = NormalizeProperNounText(item.Reading);
                if (display.Length == 0 || TextLength(display) > 100 || TextLength(reading) > 100 ||
                    display.Any(char.IsControl) || reading.Any(char.IsControl))
                    return Error(422, "E-VALIDATION", "表記は必須、表記と読みはそれぞれ100文字以内で指定してください");
                if (!Enum.IsDefined(item.Type))
                    return Error(422, "E-VALIDATION", "固有名詞の種類が正しくありません");
                if (item.Variants == null || item.Variants.Count > 10)
                    return Error(422, "E-VALIDATION", "音声認識の別表記は1件あたり10個以内で指定してください");

                var variants = item.Variants.Select(NormalizeProperNounText).Where(v => v.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (variants.Any(v => TextLength(v) > 100 || v.Any(char.IsControl)))
                    return Error(422, "E-VALIDATION", "音声認識の別表記はそれぞれ100文字以内で指定してください");
                properNouns.Add(new ProjectProperNoun { DisplayText = display, Reading = reading, Variants = variants, Type = item.Type });
            }
            var phraseCount = rt.Options.Dictionary.Concat(properNouns.SelectMany(n => new[] { n.DisplayText, n.Reading }.Concat(n.Variants)))
                .Where(text => text.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (phraseCount > 500) return Error(422, "E-VALIDATION", "共有辞書を含む音声認識フレーズは500件以内にしてください");

            p = p with { ProperNouns = properNouns };
            rt.Store.Put(Kinds.Project, id, null, p);
            return Results.Json(p, Json.Options);
        });
        api.MapDelete("/projects/{id}", (Runtime rt, JobQueue q, SceneCardService cards, ProjectsLocation loc, RemoveFolder remove, string id) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            if (q.IsBusy(id) || cards.IsBusy(id)) return Error(409, "E-PROJECT-BUSY", "このプロジェクトは生成または書き出しの途中です。終わるか、キャンセルしてから削除してください");
            try
            {
                var r = ProjectDeletion.Delete(rt.Store, p, loc.KnownRoots, rt.Options.ResolvedDataDir, remove, out var jobs, out var renders);
                q.Forget(jobs, renders);
                return Results.Json(r, Json.Options);
            }
            catch (FolderInUseException ex) { return Error(409, "E-FILE-IN-USE", ex.Message); }
        });

        // ---- assets (FR-ING-01/02/06) ----
        api.MapPost("/projects/{id}/assets", async (Runtime rt, string id, AddAsset req, CancellationToken ct) =>
        {
            if (rt.Store.Get<Project>(Kinds.Project, id) == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            var path = req.Path?.Trim().Trim('"') ?? "";
            if (path.Length == 0 || !Path.IsPathFullyQualified(path)) return Error(422, "E-VALIDATION", "動画ファイルのフルパスを指定してください");
            return await RegisterAsset(rt, id, Path.GetFullPath(path), ct);
        });
        api.MapPost("/projects/{id}/assets/upload", async (Runtime rt, string id, HttpRequest request, CancellationToken ct) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = null;
            if (!request.HasFormContentType) return Error(422, "E-VALIDATION", "multipart/form-data で送信してください");
            // Match the unlimited HTTP body policy for source videos, without lifting limits on other endpoints.
            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = long.MaxValue }, ct);
            var file = form.Files.FirstOrDefault();
            if (file == null) return Error(422, "E-VALIDATION", "ファイルがありません");
            var dir = Path.Combine(p.Directory, "assets"); Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, SafeName(Path.GetFileNameWithoutExtension(file.FileName)) + Path.GetExtension(file.FileName).ToLowerInvariant());
            PipelineRunner.EnsureInside(p.Directory, dest);   // NFR-SEC-05
            await using (var fs = File.Create(dest)) await file.CopyToAsync(fs, ct);
            return await RegisterAsset(rt, id, dest, ct);
        }).DisableAntiforgery();
        api.MapPost("/projects/{id}/rights", (Runtime rt, string id, ConfirmRights req) =>
        {
            if (!req.Confirmed) return Error(422, "E-VALIDATION", "権利の確認にチェックしてください");
            var asset = rt.Store.Get<SourceAsset>(Kinds.Asset, req.AssetId);
            if (asset == null || asset.ProjectId != id) return Error(404, "E-NOT-FOUND", "素材がありません");
            var r = new RightsConfirmation { Id = "rc_" + req.AssetId, ProjectId = id, AssetId = req.AssetId };
            rt.Store.Put(Kinds.Rights, r.Id, id, r);
            return Results.Json(r, Json.Options, statusCode: 201);
        });
        api.MapGet("/projects/{id}/estimate", (Runtime rt, string id, string? preset) =>
        {
            var assets = rt.Store.List<SourceAsset>(Kinds.Asset, id);
            var main = assets.FirstOrDefault(a => a.Role == AssetRole.Main) ?? assets.FirstOrDefault();
            if (main == null) return Error(404, "E-NOT-FOUND", "素材がありません");
            var runner = rt.CreateRunner();
            // series preview: the length after jump cuts when the source has been transcribed before (the transcript is cached), else null
            var tr = runner.LoadTranscript(main);
            var p = rt.Presets.GetValueOrDefault(preset ?? "P4") ?? rt.Presets["P4"];
            double? edited = tr.Sentences.Count > 0 ? Math.Round(Planning.SeriesPlanner.EditedLength(tr, p, main.Media.Duration), 1) : null;
            var series = new
            {
                edited_sec = edited,
                max_parts = Planning.SeriesPlanner.MaxPartsFor(edited ?? main.Media.Duration, tr.Sentences.Count > 0 ? tr.Sentences.Count : int.MaxValue),
            };
            var (transcriptCached, analyzedAt) = runner.CachedAnalysis(main);
            // reuse = what the default run costs (a cached transcript is free); redo = the same with the transcript made and paid for again
            var analysis = new { cached = analyzedAt != null, transcript_cached = transcriptCached, updated_at = analyzedAt, redo_scopes = runner.EstimateScopes(main, reuseAnalysis: false) };
            return Results.Json(new { cost = runner.EstimateCost(main), scopes = runner.EstimateScopes(main), duration = main.Media.Duration, series, analysis }, Json.Options);
        });

        api.MapPatch("/assets/{id}", (Runtime rt, string id, PatchAsset req) =>
        {
            var a = rt.Store.Get<SourceAsset>(Kinds.Asset, id);
            if (a == null) return Error(404, "E-NOT-FOUND", "素材がありません");
            var siblings = rt.Store.List<SourceAsset>(Kinds.Asset, a.ProjectId);
            if (req.Role is AssetRole.Main && a.Role != AssetRole.Main)
                foreach (var o in siblings.Where(o => o.Role == AssetRole.Main && o.Id != id)) rt.Store.Put(Kinds.Asset, o.Id, o.ProjectId, o with { Role = AssetRole.CamB });
            var u = a with
            {
                Role = req.Role ?? a.Role,
                Speaker = req.ClearSpeaker == true ? null : req.Speaker is int sp ? Math.Clamp(sp, 1, 8) : a.Speaker,
                SyncOffset = req.SyncOffset is double so ? Math.Round(Math.Clamp(so, -600, 600), 3) : a.SyncOffset,
                SyncConfidence = req.SyncOffset != null ? Audio.SyncResult.Manual : a.SyncConfidence,   // a manual offset is trusted
                SyncDriftPpm = req.SyncOffset != null ? 0 : a.SyncDriftPpm,
                SyncMethod = req.SyncOffset != null ? "manual" : a.SyncMethod,
                SyncResidualMs = req.SyncOffset != null ? null : a.SyncResidualMs,
                SyncWindows = req.SyncOffset != null ? null : a.SyncWindows,
            };
            rt.Store.Put(Kinds.Asset, id, a.ProjectId, u);
            return Results.Json(u, Json.Options);
        });

        // ---- transcript (FR-ASR-06) ----
        api.MapGet("/assets/{id}/transcript", (Runtime rt, string id) =>
        {
            var a = rt.Store.Get<SourceAsset>(Kinds.Asset, id);
            return a == null ? Error(404, "E-NOT-FOUND", "素材がありません") : Results.Json(rt.CreateRunner().LoadTranscript(a), Json.Options);
        });
        api.MapPatch("/assets/{id}/transcript", (Runtime rt, JobQueue q, string id, PatchTranscript req) => rt.Store.Atomic<IResult>(() =>
        {
            var a = rt.Store.Get<SourceAsset>(Kinds.Asset, id);
            if (a == null) return Error(404, "E-NOT-FOUND", "素材がありません");
            var masters = rt.Store.List<SourceAsset>(Kinds.Asset).Where(x => x.Sha256[..16] == a.Sha256[..16]).Select(x => x.Id).ToHashSet();
            var plans = rt.Store.List<EditPlan>(Kinds.EditPlan).Where(p => masters.Contains(p.Audio.Master)).ToList();
            if (plans.Any(p => q.ActiveExport(p.EditplanId) != null)) return StaleError();
            try
            {
                var transcript = rt.CreateRunner().EditTranscript(a, req.Edits.Select(e => (e.WordId, e.Text)));
                foreach (var plan in plans)
                    rt.Store.Put(Kinds.EditPlan, plan.EditplanId, plan.ProjectId, plan with
                    {
                        Provenance = plan.Provenance with { Edits = [.. plan.Provenance.Edits,
                            new EditRecord(DateTimeOffset.Now, "transcript", a.Id, null, string.Join(",", req.Edits.Select(e => e.WordId)))] },
                    });
                return Results.Json(transcript, Json.Options);
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { return Error(422, "E-VALIDATION", ex.Message); }
        }));

        // ---- BGM (FR-AUD-01..04): synthesised library + user-supplied tracks with a rights confirmation; each track is measured at registration ----
        api.MapGet("/bgm", async (Runtime rt, string? project_id, CancellationToken ct) =>
        {
            var lib = await rt.Audio.MeasuredTracksAsync(ct);
            var user = project_id == null ? [] : rt.Store.List<BgmTrack>(Kinds.Bgm, project_id).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            return Results.Json(lib.Concat(user), Json.Options);
        });
        api.MapPost("/projects/{id}/bgm", async (Runtime rt, string id, HttpRequest request, CancellationToken ct) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = 2000L * 1024 * 1024;
            if (!request.HasFormContentType) return Error(422, "E-VALIDATION", "multipart/form-data で送信してください");
            var form = await request.ReadFormAsync(ct);
            if (form["rights_confirmed"] != "true") return Error(422, "E-RIGHTS-REQUIRED", "BGM の権利（著作権・利用許諾）の確認にチェックしてください");
            if (form.Files.Count == 0) return Error(422, "E-VALIDATION", "ファイルがありません");
            if (form.Files.Count > MaxBgmPerUpload) return Error(422, "E-VALIDATION", $"一度に追加できるのは {MaxBgmPerUpload} 曲までです");
            var dir = Path.Combine(p.Directory, "bgm"); Directory.CreateDirectory(dir);
            var added = new List<BgmTrack>(); var skipped = new List<SkippedBgm>();
            var existing = rt.Store.List<BgmTrack>(Kinds.Bgm, id);
            foreach (var file in form.Files)
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext is not (".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg")) { skipped.Add(new SkippedBgm(file.FileName, "音声ファイル（mp3 / wav / m4a / aac / flac / ogg）ではありません")); continue; }
                var dest = Path.Combine(dir, SafeName(Path.GetFileNameWithoutExtension(file.FileName)) + ext);
                PipelineRunner.EnsureInside(p.Directory, dest);
                await using (var fs = File.Create(dest)) await file.CopyToAsync(fs, ct);
                (double Duration, int Channels) probe;
                try { probe = await new MediaProbe(rt.Ff).ProbeAudioAsync(dest, ct); }
                catch (IngestException ex) { File.Delete(dest); skipped.Add(new SkippedBgm(file.FileName, ex.Message)); continue; }
                if (probe.Channels == 0 || probe.Duration < 5) { File.Delete(dest); skipped.Add(new SkippedBgm(file.FileName, "5 秒以上の音声ではありません")); continue; }
                BgmAnalysis analysis;
                try { analysis = await Audio.BgmAnalyzer.AnalyzeAsync(rt.Ff, dest, ct); }
                catch (FFmpegException ex) { File.Delete(dest); skipped.Add(new SkippedBgm(file.FileName, ex.Message)); continue; }
                var (bpm, phase) = await Audio.TempoEstimator.AnalyzeFileAsync(rt.Ff, dest, ct);
                // the same file name again replaces that track (same id), instead of adding a duplicate that points at the overwritten file
                var same = existing.FirstOrDefault(t => string.Equals(t.Path, dest, StringComparison.OrdinalIgnoreCase));
                var track = new BgmTrack
                {
                    Id = same?.Id ?? "bgm_" + Guid.NewGuid().ToString("N")[..8], Name = Path.GetFileName(file.FileName), Path = dest, Bpm = Math.Round(bpm, 1), Phase = Math.Round(phase, 3),
                    Source = "user", ProjectId = id, RightsConfirmed = true, Analysis = analysis,
                };
                rt.Store.Put(Kinds.Bgm, track.Id, id, track);
                added.Add(track);
            }
            if (added.Count == 0) return Error(422, "E-ING-UNSUPPORTED", "追加できる曲がありませんでした: " + string.Join(" / ", skipped.Select(s => $"{s.Name}: {s.Reason}")));
            return Results.Json(new { tracks = added, skipped }, Json.Options, statusCode: 201);
        }).DisableAntiforgery();
        api.MapDelete("/projects/{id}/bgm/{bgmId}", (Runtime rt, string id, string bgmId) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            var t = rt.Store.Get<BgmTrack>(Kinds.Bgm, bgmId);
            if (p == null || t == null || t.ProjectId != id) return Error(404, "E-NOT-FOUND", "BGM がありません");
            var using_ = rt.Store.List<EditPlan>(Kinds.EditPlan, id).Where(e => e.Audio.Bgm == bgmId || e.Audio.BgmCues.Any(c => c.Track == bgmId)).Select(e => e.EditplanId).ToList();
            if (using_.Count > 0) return Error(409, "E-BGM-IN-USE", $"この曲は {using_.Count} 本の動画の編集計画で使われています。確認画面で別の曲に変えるか、その動画を削除してから外してください");
            rt.Store.Delete([(Kinds.Bgm, bgmId)]);
            try { PipelineRunner.EnsureInside(p.Directory, t.Path); if (File.Exists(t.Path)) File.Delete(t.Path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PipelineException) { }
            return Results.Json(new { deleted = bgmId }, Json.Options);
        });

        // ---- brand kit: opening / closing videos and scene-card images (one per installation, rights confirmed at registration) ----
        api.MapPost("/dialogs/folder", async (IFolderPicker picker, PickFolderRequest req, CancellationToken ct) =>
        {
            try { return Results.Json(new PickFolderResponse(await picker.PickAsync(req.InitialPath, ct)), Json.Options); }
            catch (PlatformNotSupportedException ex) { return Error(501, "E-FOLDER-PICKER", ex.Message); }
            catch (InvalidOperationException ex) { return Error(409, "E-FOLDER-PICKER", ex.Message); }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or JsonException)
            {
                app.Logger.LogWarning(ex, "Folder selection failed");
                return Error(500, "E-FOLDER-PICKER", "フォルダー選択に失敗しました。フルパスを手入力するか、もう一度選択してください");
            }
        }).WithSummary("Open the Windows folder picker; cancellation returns a null path");
        api.MapPost("/projects/{id}/scene-cards", async (Runtime rt, SceneCardService service, HttpContext ctx, string id, GenerateSceneCardsRequest req, CancellationToken ct) =>
        {
            var project = rt.Store.Get<Project>(Kinds.Project, id);
            if (project == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            var assetId = string.IsNullOrWhiteSpace(req.AssetId) ? null : req.AssetId;
            var brandPath = string.IsNullOrWhiteSpace(req.BrandPath) ? null : req.BrandPath;
            if ((assetId == null) == (brandPath == null)) return Error(422, "E-VALIDATION", "画像を取り出す元動画を1つ選んでください");
            if (req.Count is < 2 or > 6) return Error(422, "E-VALIDATION", "画像の枚数は 2〜6 枚で指定してください");
            try
            {
                var images = await service.GenerateAsync(project, assetId, brandPath, req.Count, ct);
                ctx.Response.Headers.Location = $"/api/projects/{Uri.EscapeDataString(id)}";
                return Results.Json(new { images, message = images.Count < req.Count ? $"類似画像・黒画面を除き、{images.Count} 枚を作成しました" : $"{images.Count} 枚を作成しました" }, Json.Options, statusCode: 201);
            }
            catch (KeyNotFoundException ex) { return Error(404, "E-NOT-FOUND", ex.Message); }
            catch (ArgumentException ex) { return Error(422, "E-VALIDATION", ex.Message); }
            catch (InvalidOperationException ex) { return Error(409, "E-CARD-IMAGES", ex.Message); }
            catch (IngestException ex) { return Error(422, ex.Code, ex.Message); }
            catch (Exception ex) when (ex is FFmpegException or IOException or UnauthorizedAccessException)
            {
                app.Logger.LogWarning(ex, "Scene-card generation failed for {Project}", id);
                return Error(500, "E-CARD-IMAGES", "カード画像の作成に失敗しました。元動画と保存先の空き容量を確認してください");
            }
        }).WithSummary("Extract distinct representative scene-card images from a project video or a registered opening / closing video");
        api.MapPut("/projects/{id}/scene-cards/settings", (SceneCardService service, string id, SceneCardSettingsRequest req) =>
            service.SetMake(id, req.Make) is { } p ? Results.Json(p, Json.Options) : Error(404, "E-NOT-FOUND", "プロジェクトがありません"))
            .WithSummary("作る / 作らない: whether this project makes and uses video-extracted scene-card images (existing images are kept)");
        api.MapGet("/projects/{id}/scene-cards/{index:int}", (Runtime rt, string id, int index) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null || index < 0 || index >= p.SceneCardImages.Count) return Error(404, "E-NOT-FOUND", "カード画像がありません");
            var path = p.SceneCardImages[index].Path;
            PipelineRunner.EnsureInside(p.Directory, path);
            return File.Exists(path) ? Results.File(path, "image/png") : Error(404, "E-NOT-FOUND", "カード画像が見つかりません");
        }).WithSummary("Preview a generated project scene-card image");
        api.MapGet("/brandkit", (Runtime rt, BrandKitWorker w) => Results.Json(BrandKitView(rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId), [], w), Json.Options));
        api.MapPut("/brandkit", async (Runtime rt, BrandKitWorker w, PutBrandKit req, CancellationToken ct) =>
        {
            if (!req.RightsConfirmed) return Error(422, "E-RIGHTS-REQUIRED", "オープニング・クロージング・カード画像を使う権利（著作権・ロゴ・肖像の利用許諾）の確認にチェックしてください");
            var paths = (req.Paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (paths.Count == 0) return Error(422, "E-VALIDATION", "フォルダーかファイルのフルパスを指定してください");
            var (kit, problems) = await Planning.BrandKitService.ScanAsync(rt.Ff, paths, true, req.CardSec ?? 2.5, ct);
            if (kit.Files.Count == 0) return Error(422, "E-BRANDKIT", "使えるファイルが見つかりませんでした" + (problems.Count > 0 ? ": " + string.Join(" / ", problems) : "（ファイル名に Opening / Closing を含む動画か、PNG/JPG 画像を置いてください）"));
            kit = kit with { Upscale = req.Upscale ?? true };
            rt.Store.Put(Kinds.BrandKit, PipelineRunner.BrandKitId, null, kit);
            // small openings / closings are upscaled in the background (cached per file, so re-registering is instant)
            if (kit.Upscale) w.Start(); else w.Cancel();
            return Results.Json(BrandKitView(kit, problems, w), Json.Options);
        });
        api.MapPost("/brandkit/enhance", (Runtime rt, BrandKitWorker w) =>
        {
            var kit = rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId);
            if (kit == null) return Error(404, "E-NOT-FOUND", "オープニング・クロージングが登録されていません");
            if (!kit.Upscale) rt.Store.Put(Kinds.BrandKit, PipelineRunner.BrandKitId, null, kit = kit with { Upscale = true });
            w.Start();
            return Results.Json(BrandKitView(kit, [], w), Json.Options, statusCode: 202);
        });
        api.MapDelete("/brandkit", (Runtime rt, BrandKitWorker w) =>
        {
            w.Cancel();
            rt.Store.Delete([(Kinds.BrandKit, PipelineRunner.BrandKitId)]);
            return Results.Json(BrandKitView(null, [], w), Json.Options);
        });

        // ---- jobs (FR-SEL, SSE) ----
        api.MapPost("/projects/{id}/jobs", (Runtime rt, JobQueue q, string id, StartJob req) => StartGeneration(rt, q, id, req.PresetIds, req.Instruction, [], req.Options,
            req.DerivedFromJobId != null && rt.Store.Get<Job>(Kinds.Job, req.DerivedFromJobId)?.ProjectId == id ? req.DerivedFromJobId : null));
        api.MapGet("/jobs/{jobId}", (Runtime rt, string jobId) => rt.Store.Get<Job>(Kinds.Job, jobId) is Job j ? Results.Json(j, Json.Options) : Error(404, "E-NOT-FOUND", "ジョブがありません"));
        api.MapPost("/jobs/{jobId}/cancel", (JobQueue q, string jobId) => q.Cancel(jobId) ? Results.Accepted() : Error(409, "E-NOT-RUNNING", "実行中ではありません"));
        api.MapGet("/jobs/{jobId}/events", async (HttpContext ctx, JobEvents ev, Runtime rt, string jobId, CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            if (rt.Store.Get<Job>(Kinds.Job, jobId) is Job j && j.Status is JobStatus.Ready or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Done)
            {
                await ctx.Response.WriteAsync($"data: {Json.Serialize(new JobUpdate(j.Id, j.Status, j.Progress, j.Message))}\n\n", ct);
                return;
            }
            var (sid, reader) = ev.Subscribe(jobId);
            try
            {
                await foreach (var u in reader.ReadAllAsync(ct))
                {
                    await ctx.Response.WriteAsync($"data: {Json.Serialize(u)}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                    if (u.Stage is JobStatus.Ready or JobStatus.Failed or JobStatus.Cancelled) break;
                }
            }
            catch (OperationCanceledException) { }
            finally { ev.Unsubscribe(jobId, sid); }
        });
        api.MapGet("/jobs/{jobId}/candidates", (Runtime rt, JobQueue q, string jobId) =>
        {
            var cands = rt.Store.List<Candidate>(Kinds.Candidate, jobId).OrderBy(c => c.Rank).Select(c =>
            {
                var plan = c.EditPlanId is string ep ? rt.Store.Get<EditPlan>(Kinds.EditPlan, ep) : null;
                return new
                {
                    candidate = c,
                    preview = c.PreviewRenderId is string r ? rt.Store.Get<Render>(Kinds.Render, r) : null,
                    preview_stale = c.PreviewRenderId is string pr && rt.Store.Get<Render>(Kinds.Render, pr) is { } preview && RenderValidity.IsStale(preview, plan),
                    preview_url = c.PreviewRenderId is string r2 ? $"/api/media/{r2}" : null,
                    approval = ApprovalView(c, plan),
                    // newest export of this plan in this session (queued / rendering / failed / cancelled / done)
                    export = plan == null ? null : q.LatestExport(plan.EditplanId),
                    final_render_id = plan == null ? null : rt.Store.List<Render>(Kinds.Render, plan.EditplanId).FirstOrDefault(x => x.Kind == "final" && !RenderValidity.IsStale(x, plan))?.Id,
                    final_render_stale = plan != null && rt.Store.List<Render>(Kinds.Render, plan.EditplanId).FirstOrDefault(x => x.Kind == "final") is { } latest && RenderValidity.IsStale(latest, plan),
                };
            });
            return Results.Json(cands, Json.Options);
        });
        api.MapPut("/jobs/{jobId}/candidates/{id}/approval", (Runtime rt, JobQueue q, string jobId, string id, ApproveCandidateRequest req) => rt.Store.Atomic<IResult>(() =>
        {
            var c = rt.Store.Get<Candidate>(Kinds.Candidate, id);
            if (c == null || !rt.Store.Ids(Kinds.Candidate, jobId).Contains(id)) return Error(404, "E-NOT-FOUND", "候補がありません");
            var plan = c.EditPlanId is string ep ? rt.Store.Get<EditPlan>(Kinds.EditPlan, ep) : null;
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            if (q.ActiveExport(plan.EditplanId) != null) return StaleError();
            if (req.Approved)
            {
                var hard = QaEngine.PlanChecks(rt.CreateRunner().QaContextFor(plan) with { Approval = null }).Where(x => x.Hard && x.Status == QaStatus.Fail).ToList();
                if (hard.Count > 0) return Error(422, "E-QA-HARDFAIL", "自動チェックで不合格の項目があるため承認できません。「確認・修正」で直してください", hard);
            }
            c = c with { Approval = req.Approved ? new CandidateApproval(DateTimeOffset.Now, Environment.UserName, EditPlanFingerprint.Of(plan)) : null };
            rt.Store.Put(Kinds.Candidate, c.Id, jobId, c);
            return Results.Json(new { candidate_id = c.Id, approval = ApprovalView(c, plan) }, Json.Options);
        })).WithSummary("Approve (bound to the current edit plan) or withdraw the approval of a candidate");
        api.MapPost("/jobs/{jobId}/exports", (Runtime rt, JobQueue q, HttpContext ctx, string jobId, BatchExportRequest req) => rt.Store.Atomic<IResult>(() =>
        {
            if (rt.Store.Get<Job>(Kinds.Job, jobId) == null) return Error(404, "E-NOT-FOUND", "ジョブがありません");
            var ids = (req.CandidateIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (ids.Count == 0) return Error(422, "E-VALIDATION", "書き出す候補を1つ以上選んでください");
            var all = rt.Store.List<Candidate>(Kinds.Candidate, jobId).ToDictionary(c => c.Id);
            var runner = rt.CreateRunner();
            var picked = new List<(Candidate C, EditPlan Plan)>();
            var problems = new List<BatchExportProblem>();
            // all-or-nothing: every selected candidate is checked before any export starts, in the same order as /editplans/{id}/render
            foreach (var id in ids)
            {
                if (!all.TryGetValue(id, out var c)) return Error(404, "E-NOT-FOUND", "この候補一覧にない候補が含まれています");
                var plan = c.EditPlanId is string ep ? rt.Store.Get<EditPlan>(Kinds.EditPlan, ep) : null;
                string name = c.Series?.Label ?? $"#{c.Rank}";
                BatchExportProblem Problem(string code, string reason, object? failures = null) => new(id, name, code, reason, failures);
                if (plan == null) { problems.Add(Problem("E-NOT-FOUND", "編集計画がありません")); continue; }
                if (c.Approval == null) { problems.Add(Problem("E-NOT-APPROVED", "承認されていません")); continue; }
                if (c.Approval.PlanHash != EditPlanFingerprint.Of(plan)) { problems.Add(Problem("E-PLAN-STALE", "承認後に修正されています。もう一度確認して承認してください")); continue; }
                var hard = QaEngine.PlanChecks(runner.QaContextFor(plan)).Where(x => x.Hard && x.Status == QaStatus.Fail).ToList();
                if (hard.Count > 0) { problems.Add(Problem("E-QA-HARDFAIL", "自動チェックで不合格の項目があります", hard)); continue; }
                if (q.ActiveExport(plan.EditplanId) is { } active) { problems.Add(Problem("E-EXPORT-BUSY", "書き出し中です", new { render_id = active.RenderId })); continue; }
                picked.Add((c, plan));
            }
            if (problems.Count > 0)
            {
                // the response code is the one the single-export route would return first; each item keeps its own code
                var top = BatchExportProblem.Precedence.First(p => problems.Any(x => x.Code == p.Code));
                return Error(top.Status, top.Code, "書き出せない候補があるため、どれも書き出していません", problems);
            }
            var started = new List<BatchExportItem>();
            foreach (var (c, plan) in picked.OrderBy(x => x.C.Series?.Part ?? x.C.Rank))
            {
                var renderId = "r_" + Guid.NewGuid().ToString("N")[..12];
                // the approved snapshot is what gets rendered, even if it is edited while waiting in the queue
                q.EnqueueExport(renderId, plan, c.Rank);
                started.Add(new(c.Id, renderId));
            }
            ctx.Response.Headers.Location = $"/api/jobs/{Uri.EscapeDataString(jobId)}/candidates";
            return Results.Json(new BatchExportResponse(started), Json.Options, statusCode: 202);
        })).WithSummary("Export one or more approved candidates (validated together; none starts if any is not exportable)");
        api.MapPost("/jobs/{jobId}/regenerate", (Runtime rt, JobQueue q, string jobId, Regenerate req) =>
        {
            var prev = rt.Store.Get<Job>(Kinds.Job, jobId);
            if (prev == null) return Error(404, "E-NOT-FOUND", "ジョブがありません");
            var exclude = (req.ExcludeCandidateIds ?? []).Concat(prev.ExcludeCandidateIds).Concat(prev.CandidateIds).Distinct().ToList();
            // "別の候補を出す" always reuses the analysis: redoing it is a one-off choice made when starting a generation
            return StartGeneration(rt, q, prev.ProjectId, prev.PresetIds, req.Instruction, exclude, prev.Options with { ReuseAnalysis = true }, derivedFrom: prev.Id);
        });

        // ---- patterns: every generation (job) of a project, kept side by side ----
        api.MapGet("/projects/{id}/patterns", (Runtime rt, JobQueue q, string id) =>
        {
            if (rt.Store.Get<Project>(Kinds.Project, id) == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            var patterns = rt.Store.List<Job>(Kinds.Job, id).Select(j =>
            {
                var cands = rt.Store.List<Candidate>(Kinds.Candidate, j.Id);
                var planIds = JobDeletion.PlanIds(rt.Store, j);
                var finals = planIds.SelectMany(e => rt.Store.List<Render>(Kinds.Render, e).Where(r => r.Kind == "final" && File.Exists(r.Path)
                    && !RenderValidity.IsStale(r, rt.Store.Get<EditPlan>(Kinds.EditPlan, e)))).Count();
                return new
                {
                    job = j,
                    preset_names = j.PresetIds.Select(p => rt.Presets.TryGetValue(p, out var pr) ? pr.Name : p).ToList(),
                    candidates = cands.Count,
                    approved = cands.Count(c => c.Approval != null),
                    exported = finals,
                    busy = q.IsJobBusy(j.Id, planIds),
                };
            });
            return Results.Json(patterns, Json.Options);
        });
        api.MapDelete("/jobs/{jobId}", (Runtime rt, JobQueue q, ProjectsLocation loc, RemoveFolder remove, string jobId) => rt.Store.Atomic<IResult>(() =>
        {
            var job = rt.Store.Get<Job>(Kinds.Job, jobId);
            if (job == null) return Error(404, "E-NOT-FOUND", "ジョブがありません");
            var p = rt.Store.Get<Project>(Kinds.Project, job.ProjectId);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            if (q.IsJobBusy(job.Id, JobDeletion.PlanIds(rt.Store, job))) return Error(409, "E-JOB-BUSY", "このパターンは生成または書き出しの途中です。終わるか、キャンセルしてから削除してください");
            try
            {
                var r = JobDeletion.Delete(rt.Store, p, job, rt.Options.ResolvedDataDir, remove, out var renders);
                q.Forget([job.Id], renders);
                if (p.WorkflowState is { } ws && (ws.JobId == job.Id || renders.Contains(ws.RenderId ?? "") || ws.EditPlanId is string ep && rt.Store.Get<EditPlan>(Kinds.EditPlan, ep) == null))
                    rt.Store.Put(Kinds.Project, p.Id, null, p with { WorkflowState = new ProjectWorkflowState { Step = "settings" } });
                return Results.Json(r, Json.Options);
            }
            catch (FolderInUseException ex) { return Error(409, "E-FILE-IN-USE", ex.Message); }
        })).WithSummary("Delete one pattern (job) with its candidates, edit plans and renders; other patterns stay");

        // ---- edit plans (FR-REV) ----
        api.MapGet("/editplans/{id}", (Runtime rt, string id) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            var candidate = plan.Provenance.CandidateId is { } cid ? rt.Store.Get<Candidate>(Kinds.Candidate, cid) : null;
            var staleFinal = rt.Store.List<Render>(Kinds.Render, id).FirstOrDefault(r => r.Kind == "final") is { } latest && RenderValidity.IsStale(latest, plan);
            return Results.Json(new { plan, plan_hash = EditPlanFingerprint.Of(plan), qa = ShownQa(rt, rt.CreateRunner(), plan),
                approval = candidate == null ? null : ApprovalView(candidate, plan), final_render_stale = staleFinal, qa_stale = staleFinal, duration = plan.Duration }, Json.Options);
        });
        api.MapPatch("/editplans/{id}", async (Runtime rt, JobQueue q, string id, PatchPlan req, CancellationToken ct) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            if (q.ActiveExport(id) != null) return StaleError();
            var runner = rt.CreateRunner();
            try
            {
                if (req.Ops.Any(o => o.Op == "set_bgm" && o.Target != "none") && rt.Presets[plan.Preset.Id].Planner == "beat")
                    return Error(422, "E-VALIDATION", "ビート同期では BGM を変えるとカットも変わります。条件を変えて作り直してください");
                if (plan.Series != null && req.Ops.Any(o => o.Op is "adjust" or "replan"))
                    return Error(422, "E-VALIDATION", "前編・後編の区切りは前後のパートとつながっているため、ここでは動かせません。本数か1本あたりの長さを変えて作り直してください");
                if (req.Ops.Any(o => o.Op == "set_bgm" && !string.IsNullOrEmpty(o.Target) && o.Target is not ("none" or "scene") && rt.Audio.Find(o.Target) == null && rt.Store.Get<BgmTrack>(Kinds.Bgm, o.Target) == null))
                    return Error(422, "E-BGM-NOTFOUND", "指定した BGM が見つかりません");
                // scene-matched music: edits look tracks up (measured), and duck depths are re-derived after every edit (inserts move the music too)
                var bgmCtx = plan.Audio.BgmCues.Count > 0 ? await runner.BgmContextAsync(plan.ProjectId, ct) : null;
                var simple = req.Ops.Where(o => o.Op is not ("adjust" or "replan")).ToList();
                var updated = PlanEditor.Apply(plan, simple, bgmCtx);
                foreach (var adj in req.Ops.Where(o => o.Op is "adjust" or "replan"))
                {
                    var cand = rt.Store.Get<Candidate>(Kinds.Candidate, plan.Provenance.CandidateId ?? "") ?? throw new KeyNotFoundException("candidate");
                    var job = rt.Store.Get<Job>(Kinds.Job, plan.Provenance.JobId ?? "") ?? throw new KeyNotFoundException("job");
                    var main = rt.Store.List<SourceAsset>(Kinds.Asset, plan.ProjectId).First(a => a.Id == plan.Audio.Master);
                    var tr = runner.LoadTranscript(main);
                    var segs = PlanEditor.AdjustSegments(MergedSegments(updated), tr, adj.Edge ?? "end", adj.Op == "replan" ? 0 : adj.Delta ?? 1);
                    var (rebuilt, _) = await runner.ReplanAsync(cand, updated, job, segs, ct);
                    updated = PlanEditor.Reapply(rebuilt with { EditplanId = plan.EditplanId }, updated, adj.Op == "replan");
                    updated = updated with { Provenance = updated.Provenance with { Edits = [.. updated.Provenance.Edits, new EditRecord(DateTimeOffset.Now, "adjust", adj.Edge ?? "end", null, (adj.Delta ?? 1).ToString())] } };
                }
                if (updated.Audio.BgmCues.Count > 0) updated = PlanEditor.RecomputeBgm(updated, bgmCtx ?? await runner.BgmContextAsync(plan.ProjectId, ct));
                return rt.Store.Atomic<IResult>(() =>
                {
                    if (q.ActiveExport(id) != null || rt.Store.Get<EditPlan>(Kinds.EditPlan, id) is not { } current
                        || EditPlanFingerprint.Of(current) != EditPlanFingerprint.Of(plan)) return StaleError();
                    rt.Store.Put(Kinds.EditPlan, id, plan.ProjectId, updated);
                    var c = plan.Provenance.CandidateId is { } cid ? rt.Store.Get<Candidate>(Kinds.Candidate, cid) : null;
                    var finalStale = rt.Store.List<Render>(Kinds.Render, id).FirstOrDefault(r => r.Kind == "final") is { } latest && RenderValidity.IsStale(latest, updated);
                    return Results.Json(new { plan = updated, plan_hash = EditPlanFingerprint.Of(updated),
                        qa = ShownQa(rt, runner, updated), duration = updated.Duration,
                        preview_stale = EditPlanFingerprint.Of(updated) != EditPlanFingerprint.Of(plan),
                        approval = c == null ? null : ApprovalView(c, updated), qa_stale = finalStale, final_render_stale = finalStale }, Json.Options);
                });
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { return Error(422, "E-VALIDATION", ex.Message); }
        });
        // plan checks as shown in 自動チェック: each finding marked with whether 「Copilotで自動修復」 will fix it
        static List<QaResult> ShownQa(Runtime rt, PipelineRunner runner, EditPlan plan)
        {
            var ctx = runner.QaContextFor(plan);
            var checks = QaRepair.Assess(ctx, QaEngine.PlanChecks(ctx), rt.Llm != null);
            var hash = EditPlanFingerprint.Of(plan);
            var preview = rt.Store.List<Render>(Kinds.Render, plan.EditplanId)
                .FirstOrDefault(r => r.Kind == "preview" && !RenderValidity.IsStale(r, plan));
            if (preview != null)
                checks.AddRange(preview.Qa.Where(q => q.PlanHash == hash && checks.All(c => c.CheckId != q.CheckId)));
            return QaApproval.Attach(plan, checks);
        }
        object PlanView(Runtime rt, PipelineRunner runner, EditPlan before, EditPlan updated)
        {
            var c = before.Provenance.CandidateId is { } cid ? rt.Store.Get<Candidate>(Kinds.Candidate, cid) : null;
            var finalStale = rt.Store.List<Render>(Kinds.Render, before.EditplanId).FirstOrDefault(r => r.Kind == "final") is { } latest && RenderValidity.IsStale(latest, updated);
            return new { plan = updated, plan_hash = EditPlanFingerprint.Of(updated), qa = ShownQa(rt, runner, updated), duration = updated.Duration,
                preview_stale = EditPlanFingerprint.Of(updated) != EditPlanFingerprint.Of(before),
                approval = c == null ? null : ApprovalView(c, updated), qa_stale = finalStale, final_render_stale = finalStale };
        }
        api.MapPut("/editplans/{id}/qa/approval", (Runtime rt, string id, QaApprovalRequest req) => rt.Store.Atomic<IResult>(() =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            if (req.Checks == null || req.Checks.Count == 0 || req.Checks.Any(c => c == null || string.IsNullOrWhiteSpace(c.CheckId) || string.IsNullOrWhiteSpace(c.ResultHash))
                || req.Checks.Select(c => c.CheckId).Distinct().Count() != req.Checks.Count)
                return Error(422, "E-VALIDATION", "承認するチェック項目を重複なく指定してください");
            var planHash = EditPlanFingerprint.Of(plan);
            if (req.PlanHash != planHash) return StaleError();
            var runner = rt.CreateRunner();
            var checks = ShownQa(rt, runner, plan);
            foreach (var target in req.Checks)
            {
                var check = checks.FirstOrDefault(c => c.CheckId == target.CheckId);
                if (check == null || check.Status == QaStatus.NotRun)
                    return Error(422, "E-VALIDATION", "存在しない項目・未実施の項目は承認できません");
                if (check.ReviewHash != target.ResultHash)
                    return Error(409, "E-QA-STALE", "チェック結果が変更されています。画面を再読み込みして内容を確認してください");
            }
            var ids = req.Checks.Select(c => c.CheckId).ToHashSet();
            var approvals = (plan.QaApprovals ?? []).Where(a => !ids.Contains(a.CheckId)).ToList();
            var now = DateTimeOffset.Now;
            if (req.Approved)
                approvals.AddRange(req.Checks.Select(c => plan.QaApprovals?.FirstOrDefault(a =>
                    a.CheckId == c.CheckId && a.PlanHash == planHash && a.ResultHash == c.ResultHash)
                    ?? new QaCheckApproval(c.CheckId, planHash, c.ResultHash, now, Environment.UserName)));
            var updated = plan with { QaApprovals = approvals.Count == 0 ? null : approvals };
            rt.Store.Put(Kinds.EditPlan, id, plan.ProjectId, updated);
            return Results.Json(PlanView(rt, runner, plan, updated), Json.Options);
        })).WithSummary("Acknowledge or withdraw reviewed QA results without changing verdicts or export constraints");
        api.MapPost("/editplans/{id}/qa/repair", async (Runtime rt, JobQueue q, string id, QaRepairRequest? req, CancellationToken ct) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            if (q.ActiveExport(id) != null) return StaleError();
            var runner = rt.CreateRunner();
            var res = await QaRepair.RunAsync(runner.QaContextFor(plan), req?.CheckIds, rt.Llm, ct);
            return rt.Store.Atomic<IResult>(() =>
            {
                if (q.ActiveExport(id) != null || rt.Store.Get<EditPlan>(Kinds.EditPlan, id) is not { } current
                    || EditPlanFingerprint.Of(current) != EditPlanFingerprint.Of(plan)) return StaleError();
                string? repairId = null;
                if (res.Changes.Count > 0)
                {
                    rt.Store.Put(Kinds.EditPlan, id, plan.ProjectId, res.Plan);
                    repairId = "qr_" + Guid.NewGuid().ToString("N")[..12];
                    rt.Store.Put(Kinds.QaRepair, repairId, id, new QaRepairSnapshot(repairId, id, plan, EditPlanFingerprint.Of(res.Plan), DateTimeOffset.Now));
                }
                var view = PlanView(rt, runner, plan, res.Changes.Count > 0 ? res.Plan : plan);
                return Results.Json(new { repair = new { repair_id = repairId, changes = res.Changes, skipped = res.Skipped, llm = res.Llm,
                    llm_destination = rt.LlmDestination, fixed_count = res.Changes.Count }, result = view }, Json.Options);
            });
        }).WithSummary("Auto-repair QA findings (rules first, then the configured LLM); every change is re-verified by the plan checks");
        api.MapPost("/editplans/{id}/qa/repair/{repairId}/undo", (Runtime rt, JobQueue q, string id, string repairId) => rt.Store.Atomic<IResult>(() =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            var snap = rt.Store.Get<QaRepairSnapshot>(Kinds.QaRepair, repairId);
            if (plan == null || snap == null || snap.EditplanId != id) return Error(404, "E-NOT-FOUND", "元に戻せる自動修復がありません");
            if (q.ActiveExport(id) != null) return StaleError();
            if (EditPlanFingerprint.Of(plan) != snap.AfterHash) return Error(409, "E-PLAN-STALE", "自動修復の後に編集されたため、元に戻せません");
            // exact restore (no undo record): the hash returns to the one an approval / final render was made for
            var restored = snap.Before;
            rt.Store.Put(Kinds.EditPlan, id, plan.ProjectId, restored);
            rt.Store.Delete([(Kinds.QaRepair, repairId)]);
            return Results.Json(PlanView(rt, rt.CreateRunner(), plan, restored), Json.Options);
        })).WithSummary("Undo one auto-repair while the plan is unchanged since");
        api.MapPost("/editplans/{id}/preview", async (Runtime rt, JobQueue q, string id, CancellationToken ct) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            q.Previews[id] = new PreviewProgress(true, 0, "準備しています");
            Render render;
            try
            {
                render = await rt.CreateRunner().RenderAsync(plan, preview: true, ct,
                    progress: new Media.SyncProgress<(double Progress, string Step)>(p => q.Previews[id] = new PreviewProgress(true, Math.Round(p.Progress, 4), p.Step)));
            }
            finally { q.Previews.TryRemove(id, out _); }
            if (plan.Provenance.CandidateId is string cid && rt.Store.Get<Candidate>(Kinds.Candidate, cid) is Candidate c)
                rt.Store.Put(Kinds.Candidate, cid, plan.Provenance.JobId, c with { PreviewRenderId = render.Id });
            return Results.Json(new { render, preview_url = $"/api/media/{render.Id}" }, Json.Options);
        });
        api.MapGet("/editplans/{id}/preview/progress", (JobQueue q, string id) =>
            Results.Json(q.Previews.TryGetValue(id, out var p) ? p : new PreviewProgress(false, 0, null), Json.Options));
        api.MapPost("/editplans/{id}/render", (Runtime rt, JobQueue q, string id) => rt.Store.Atomic<IResult>(() =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            var candidate = plan.Provenance.CandidateId is { } cId ? rt.Store.Get<Candidate>(Kinds.Candidate, cId) : null;
            if (candidate?.Approval == null) return Error(409, "E-NOT-APPROVED", "内容を確認し、候補を承認してから書き出してください");
            if (candidate.Approval.PlanHash != EditPlanFingerprint.Of(plan)) return StaleError();
            var hard = QaEngine.PlanChecks(rt.CreateRunner().QaContextFor(plan)).Where(x => x.Hard && x.Status == QaStatus.Fail).ToList();
            if (hard.Count > 0) return Error(422, "E-QA-HARDFAIL", "自動QAのハード制約に違反しています", hard);
            if (q.ActiveExport(id) is { } active) return Error(409, "E-EXPORT-BUSY", "この動画は書き出し中です。完了を待ってください", new { render_id = active.RenderId });
            var rank = plan.Provenance.CandidateId is string cid ? rt.Store.Get<Candidate>(Kinds.Candidate, cid)?.Rank ?? 1 : 1;
            var renderId = "r_" + Guid.NewGuid().ToString("N")[..12];
            q.EnqueueExport(renderId, plan, rank);
            return Results.Json(new { render_id = renderId }, Json.Options, statusCode: 202);
        }));
        api.MapGet("/renders/{id}", (Runtime rt, JobQueue q, string id) =>
        {
            if (rt.Store.Get<Render>(Kinds.Render, id) is Render r)
            {
                var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, r.EditplanId);
                var stale = Stale(rt, r);
                return Results.Json(new { state = stale ? "stale" : "done", render = r,
                    qa_stale = stale, error_code = stale ? "E-PLAN-STALE" : null, editplan_id = r.EditplanId, project_id = plan?.ProjectId }, Json.Options);
            }
            if (q.Exports.TryGetValue(id, out var s))
            {
                var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, s.EditplanId);
                return Results.Json(new { s.RenderId, s.EditplanId, s.State, s.Message, s.Failures, s.ErrorCode, s.Progress, s.Step, s.QueuedAt, s.PlanHash, project_id = plan?.ProjectId }, Json.Options);
            }
            return Error(404, "E-NOT-FOUND", "書き出しがありません");
        });
        api.MapPost("/renders/{id}/cancel", (JobQueue q, string id) =>
            q.CancelExport(id) ? Results.Json(q.Exports[id], Json.Options)
            : q.Exports.ContainsKey(id) ? Error(409, "E-NOT-QUEUED", "書き出しが始まっているため取り消せません") : Error(404, "E-NOT-FOUND", "書き出しがありません"))
            .WithSummary("Withdraw a queued export (a running export finishes)");
        api.MapGet("/renders/{id}/artifacts", (Runtime rt, string id) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, id);
            if (r == null || r.Kind != "final") return Error(404, "E-NOT-FOUND", "書き出しがありません");
            if (Stale(rt, r)) return StaleError();
            return Results.Json(new
            {
                path = Path.GetFullPath(r.Path), available = File.Exists(r.Path),
                files = r.Outputs.Select(f => new
                {
                    key = f.Key, path = Path.GetFullPath(f.Value), available = File.Exists(f.Value),
                    preview_type = ArtifactPreviewType(f.Value),
                }),
            }, Json.Options);
        }).WithSummary("Recorded export artifacts, absolute paths and current availability (no generation)");
        api.MapGet("/renders/{id}/files/{key}/preview", async (Runtime rt, string id, string key, CancellationToken ct) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, id);
            if (r != null && Stale(rt, r)) return StaleError();
            if (r == null || !r.Outputs.TryGetValue(key, out var file) || !File.Exists(file))
                return Error(404, "E-NOT-FOUND", "ファイルがありません");
            if (ArtifactPreviewType(file) != "text") return Error(422, "E-VALIDATION", "この形式はテキストプレビューに対応していません");
            const int limit = 64 * 1024;
            using var reader = File.OpenText(file);
            var buffer = new char[limit + 1];
            var length = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            return Results.Json(new { text = new string(buffer, 0, Math.Min(length, limit)), truncated = length > limit }, Json.Options);
        }).WithSummary("Plain-text preview of a recorded artifact (up to 65536 characters)");
        api.MapGet("/renders/{id}/files/{key}", (Runtime rt, string id, string key) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, id);
            if (r != null && Stale(rt, r)) return StaleError();
            if (r == null || !r.Outputs.TryGetValue(key, out var f) || !File.Exists(f)) return Error(404, "E-NOT-FOUND", "ファイルがありません");
            var type = Path.GetExtension(f).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp",
                ".mp4" => "video/mp4", ".srt" or ".md" or ".ass" or ".txt" => "text/plain; charset=utf-8",
                ".json" => "application/json", _ => "application/octet-stream",
            };
            return Results.File(f, type, enableRangeProcessing: true);
        });
        api.MapGet("/renders/{id}/post", (Runtime rt, string id, CancellationToken ct) => PostKitFor(rt, id, false, ct))
            .WithSummary("Copy-and-paste YouTube / Viva Engage texts and chapters of an export (made once, then read from <video>.post.json)");
        api.MapPost("/renders/{id}/post", (Runtime rt, string id, CancellationToken ct) => PostKitFor(rt, id, true, ct))
            .WithSummary("Write the posting texts again (another AI draft)");
        api.MapPost("/renders/{id}/open-folder", (Runtime rt, ILoggerFactory logs, string id) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, id);
            if (r == null) return Error(404, "E-NOT-FOUND", "書き出しがありません");
            if (Stale(rt, r)) return StaleError();
            if (!File.Exists(r.Path)) return Error(404, "E-NOT-FOUND", "動画ファイルが見つかりません。表示された保存先を確認してください");
            if (!OperatingSystem.IsWindows()) return Error(501, "E-PLATFORM", "フォルダーを開く操作は Windows のみ対応しています。保存先パスをコピーしてください");
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"/select,\"{Path.GetFullPath(r.Path)}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                logs.CreateLogger("ExportArtifacts").LogWarning(ex, "Could not open export folder for {RenderId}", id);
                return Error(500, "E-OPEN-FOLDER", "フォルダーを開けませんでした。保存先パスをコピーしてエクスプローラーで開いてください");
            }
            return Results.Accepted();
        });
        api.MapGet("/media/{renderId}", (Runtime rt, string renderId) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, renderId);
            if (r != null && r.Kind == "final" && Stale(rt, r)) return StaleError();
            return r != null && File.Exists(r.Path) ? Results.File(r.Path, "video/mp4", enableRangeProcessing: true) : Error(404, "E-NOT-FOUND", "動画がありません");
        });
    }

    static string? ArtifactPreviewType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video",
        ".png" or ".jpg" or ".jpeg" or ".webp" => "image",
        ".srt" or ".ass" or ".md" or ".json" or ".txt" => "text",
        _ => null,
    };

    static async Task<IResult> PostKitFor(Runtime rt, string id, bool regenerate, CancellationToken ct)
    {
        var r = rt.Store.Get<Render>(Kinds.Render, id);
        if (r == null || r.Kind != "final") return Error(404, "E-NOT-FOUND", "書き出しがありません");
        if (Stale(rt, r)) return StaleError();
        if (!regenerate && r.Outputs.TryGetValue("post", out var existing) && File.Exists(existing))
            return Results.Text(await File.ReadAllTextAsync(existing, ct), "application/json; charset=utf-8");
        var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, r.EditplanId);
        var cand = plan?.Provenance.CandidateId is { } cid ? rt.Store.Get<Candidate>(Kinds.Candidate, cid) : null;
        if (plan == null || cand == null) return Error(404, "E-NOT-FOUND", "この書き出しの候補がありません");
        string? previous = null;
        if (regenerate && r.Outputs.TryGetValue("post", out var old) && File.Exists(old))
            try { previous = Json.Deserialize<PostKit>(await File.ReadAllTextAsync(old, ct))?.Youtube; } catch (System.Text.Json.JsonException) { }
        var kit = await rt.CreateRunner().PostKitAsync(cand, plan, ct, previous);
        var file = Path.ChangeExtension(r.Path, ".post.json");
        await File.WriteAllTextAsync(file, Json.Serialize(kit), ct);
        if (!r.Outputs.ContainsKey("post"))
            rt.Store.Atomic(() =>
            {
                if (rt.Store.Get<Render>(Kinds.Render, id) is { } cur) rt.Store.Put(Kinds.Render, id, cur.EditplanId, cur with { Outputs = new Dictionary<string, string>(cur.Outputs) { ["post"] = file } });
                return true;
            });
        return Results.Json(kit, Json.Options);
    }

    static List<Interval> MergedSegments(EditPlan plan)
    {
        var body = plan.Timeline.Where(c => c.Role == "body").Select(c => new Interval(c.MasterIn, c.MasterOut)).OrderBy(i => i.T0).ToList();
        return Selection.CandidateValidator.MergeOverlaps(body, 2.0);
    }

    static IResult StartGeneration(Runtime rt, JobQueue q, string projectId, List<string> presetIds, string? instruction, List<string> exclude, JobOptions? options = null, string? derivedFrom = null)
    {
        options ??= new JobOptions();
        if (options.Scope is not ("full" or "balanced" or "economy")) return Error(422, "E-VALIDATION", "絞り込みは full / balanced / economy のどれかを指定してください");
        if (!Planning.TsukkomiPlacement.Styles.Contains(options.TsukkomiStyle)) return Error(422, "E-VALIDATION", "ツッコミの見せ方は auto / telop / card のどれかを指定してください");
        if (options.MaskWords.Count > Planning.Masking.MaxWords || options.MaskWords.Any(w => w.Trim().Length > Planning.Masking.MaxWordLength))
            return Error(422, "E-VALIDATION", $"伏せる語は {Planning.Masking.MaxWords} 語まで、1語 {Planning.Masking.MaxWordLength} 文字までです");
        options = options with { MaskWords = Planning.Masking.Clean(options.MaskWords) };
        if (options.Bgm is not ("auto" or "none" or "scene") && rt.Audio.Find(options.Bgm) == null && rt.Store.Get<BgmTrack>(Kinds.Bgm, options.Bgm) == null) return Error(422, "E-BGM-NOTFOUND", "指定した BGM が見つかりません");
        if (options.Opening || options.Closing)
        {
            var kit = rt.Store.Get<BrandKit>(Kinds.BrandKit, PipelineRunner.BrandKitId);
            if (options.Opening && kit?.Files.Any(f => f.Kind == "opening") != true) return Error(422, "E-BRANDKIT", "オープニング動画が登録されていません。「オープニング・クロージング」で登録してください");
            if (options.Closing && kit?.Files.Any(f => f.Kind == "closing") != true) return Error(422, "E-BRANDKIT", "クロージング動画が登録されていません。「オープニング・クロージング」で登録してください");
        }
        if (rt.Store.Get<Project>(Kinds.Project, projectId) == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
        if (presetIds is not { Count: > 0 } || presetIds.Any(id => !rt.Presets.ContainsKey(id))) return Error(422, "E-VALIDATION", "パターンを選んでください");
        var main = rt.Presets[presetIds[0]];
        if (!main.Available) return Error(422, "E-PRESET-UNAVAILABLE", $"「{main.Name}」は未対応です（{main.UnavailableReason}）");
        if (presetIds.Any(a => presetIds.Any(b => rt.Presets[a].IncompatibleWith.Contains(b)))) return Error(422, "E-PRESET-CONFLICT", "同時に選べないパターンの組み合わせです（§8.2）");
        var assets = rt.Store.List<SourceAsset>(Kinds.Asset, projectId);
        if (assets.Count == 0) return Error(422, "E-ING-UNSUPPORTED", "素材を追加してください");
        var rights = rt.Store.List<RightsConfirmation>(Kinds.Rights, projectId).Select(r => r.AssetId).ToHashSet();
        if (assets.Any(a => !rights.Contains(a.Id))) return Error(409, "E-RIGHTS-REQUIRED", "素材の権利確認が完了していません（AT-08）");
        if (instruction is { Length: > 200 }) return Error(422, "E-VALIDATION", "追加指示は200文字以内にしてください");
        var runner = rt.CreateRunner();
        var mainAsset = assets.FirstOrDefault(a => a.Role == AssetRole.Main) ?? assets[0];
        if (!options.IsSeries && mainAsset.Media.Duration < main.Target.Min)
            return Error(422, "E-SOL-INFEASIBLE", $"素材が{main.Id}の尺の下限（{main.Target.Min:0.#}秒）より短い");
        if (options.PartMinutes != null && options.Parts != null) return Error(422, "E-VALIDATION", "本数と1本あたりの長さは、どちらか一方だけを指定してください");
        if (options.Parts is int np)
        {
            if (np < 2 || np > Planning.SeriesPlanner.MaxParts) return Error(422, "E-VALIDATION", $"本数は 2〜{Planning.SeriesPlanner.MaxParts} 本で指定してください");
            int max = Planning.SeriesPlanner.MaxPartsFor(mainAsset.Media.Duration, int.MaxValue);
            if (np > max) return Error(422, "E-SERIES-PARTS", max < 2 ? $"動画（{mainAsset.Media.Duration / 60:F1}分）が短いため分けられません" : $"{np}本に分けると1本あたり{Planning.SeriesPlanner.MinPartSec / 60:0}分未満になります。この動画（{mainAsset.Media.Duration / 60:F1}分）は最大 {max} 本まで分けられます");
        }
        if (options.IsSeries && main.Planner is not ("speech" or "switch" or "screen")) return Error(422, "E-SERIES-PRESET", $"「{main.Name}」は前編・後編の分割に使えません。発話を軸に編集するパターン（P1〜P4・P6・P7・P9・P10）を選んでください");
        if (options.PartMinutes is double pm)
        {
            if (!double.IsFinite(pm) || pm < MinPartMinutes || pm > MaxPartMinutes) return Error(422, "E-VALIDATION", $"1本あたりの長さは {MinPartMinutes}〜{MaxPartMinutes} 分で指定してください");
            if (mainAsset.Media.Duration < 1.5 * pm * 60) return Error(422, "E-SERIES-SHORT", $"動画（{mainAsset.Media.Duration / 60:F1}分）が短いため、{pm:0.#}分ずつに分けられません。1本あたりの長さを {Math.Floor(mainAsset.Media.Duration / 60 / 2 * 10) / 10:0.#}分以下にしてください");
        }
        var job = new Job
        {
            Id = "job_" + Guid.NewGuid().ToString("N")[..10], ProjectId = projectId, PresetIds = presetIds, Instruction = instruction, Options = options,
            ExcludeCandidateIds = exclude, DerivedFromJobId = derivedFrom, Cost = runner.EstimateCost(mainAsset, options.IsSeries ? "full" : options.Scope, options.ReuseAnalysis),
        };
        q.EnqueueJob(job);
        return Results.Json(new { job_id = job.Id, cost = job.Cost }, Json.Options, statusCode: 202);
    }

    static async Task<IResult> RegisterAsset(Runtime rt, string projectId, string path, CancellationToken ct)
    {
        try
        {
            var probe = new MediaProbe(rt.Ff);
            var media = await probe.ProbeAsync(path, ct);
            var warnings = MediaProbe.Validate(media);
            var sha = await MediaProbe.Sha256Async(path, ct);
            var existing = rt.Store.List<SourceAsset>(Kinds.Asset, projectId);
            var id = $"a_{sha[..10]}_{projectId[2..Math.Min(projectId.Length, 8)]}";   // per project: the same file can be used in several projects
            var prev = existing.FirstOrDefault(a => a.Id == id);
            var asset = new SourceAsset
            {
                Id = id, ProjectId = projectId, Path = path, Sha256 = sha, Media = media, Warnings = warnings,
                Role = prev?.Role ?? (existing.Count == 0 ? AssetRole.Main : AssetRole.CamB),
                Speaker = prev?.Speaker, SyncOffset = prev?.SyncOffset ?? 0, SyncConfidence = prev?.SyncConfidence,
                SyncDriftPpm = prev?.SyncDriftPpm ?? 0, SyncMethod = prev?.SyncMethod, SyncResidualMs = prev?.SyncResidualMs, SyncWindows = prev?.SyncWindows,
            };
            rt.Store.Put(Kinds.Asset, asset.Id, projectId, asset);
            return Results.Json(asset, Json.Options, statusCode: 201);
        }
        catch (IngestException ex) { return Error(422, ex.Code, ex.Message); }
    }

    public const int MaxProjectNameLength = 80;
    public const double MinPartMinutes = 1, MaxPartMinutes = 60;

    static string NormalizeProperNounText(string? value) =>
        (value ?? "").Trim().Normalize(System.Text.NormalizationForm.FormC);

    static int TextLength(string value) => new System.Globalization.StringInfo(value).LengthInTextElements;

    /// <summary>Shared by create and rename. Returns an error message, or null with the normalized name.</summary>
    public static string? ValidateProjectName(string? raw, out string name)
    {
        name = (raw ?? "").Normalize(System.Text.NormalizationForm.FormC).Trim();
        if (name.Length == 0) return "プロジェクト名を入力してください";
        if (name.Any(char.IsControl)) return "プロジェクト名に改行やタブなどの制御文字は使えません";
        if (new System.Globalization.StringInfo(name).LengthInTextElements > MaxProjectNameLength) return $"プロジェクト名は{MaxProjectNameLength}文字以内にしてください";
        return null;
    }

    static string SafeName(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var r = new string(s.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return r.Length == 0 ? "file" : r.Length > 60 ? r[..60] : r;
    }
}
