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
    public sealed record AddAsset(string Path);
    public sealed record ConfirmRights(string AssetId, bool Confirmed);
    public sealed record StartJob(List<string> PresetIds, string? Instruction, JobOptions? Options = null);
    public sealed record PatchAsset(AssetRole? Role, int? Speaker, double? SyncOffset, bool? ClearSpeaker);
    public sealed record Regenerate(string? Instruction, List<string>? ExcludeCandidateIds);
    public sealed record PatchPlan(List<PlanOp> Ops);
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

    /// <summary>Approval as shown in the UI: <c>stale</c> = approved, but the plan was edited afterwards.</summary>
    static object? ApprovalView(Candidate c, EditPlan? plan) => c.Approval is not { } a ? null : new
    {
        a.At, a.By, Stale = plan == null || EditPlanFingerprint.Of(plan) != a.PlanHash,
    };

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
            faces_model = rt.Faces.Available,
            bgm_tracks = rt.Audio.Tracks.Count,
            presets = rt.Presets.Count,
        }, Json.Options));

        api.MapGet("/settings", (Runtime rt) => Results.Json(new
        {
            llm_provider = rt.LlmProvider,
            local_only = rt.LocalOnly,
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
        api.MapGet("/projects/{id}", (Runtime rt, string id) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            return Results.Json(new
            {
                project = p,
                assets = rt.Store.List<SourceAsset>(Kinds.Asset, id),
                rights = rt.Store.List<RightsConfirmation>(Kinds.Rights, id),
                jobs = rt.Store.List<Job>(Kinds.Job, id),
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
            return Results.Json(new { cost = runner.EstimateCost(main), scopes = runner.EstimateScopes(main), duration = main.Media.Duration, series }, Json.Options);
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
        api.MapPatch("/assets/{id}/transcript", (Runtime rt, string id, PatchTranscript req) =>
        {
            var a = rt.Store.Get<SourceAsset>(Kinds.Asset, id);
            if (a == null) return Error(404, "E-NOT-FOUND", "素材がありません");
            try { return Results.Json(rt.CreateRunner().EditTranscript(a, req.Edits.Select(e => (e.WordId, e.Text))), Json.Options); }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { return Error(422, "E-VALIDATION", ex.Message); }
        });

        // ---- BGM (FR-AUD-01..04): synthesised library + user-supplied tracks with a rights confirmation ----
        api.MapGet("/bgm", async (Runtime rt, string? project_id, CancellationToken ct) =>
        {
            await rt.Audio.EnsureAsync(ct);
            var user = project_id == null ? [] : rt.Store.List<BgmTrack>(Kinds.Bgm, project_id);
            return Results.Json(rt.Audio.Tracks.Concat(user), Json.Options);
        });
        api.MapPost("/projects/{id}/bgm", async (Runtime rt, string id, HttpRequest request, CancellationToken ct) =>
        {
            var p = rt.Store.Get<Project>(Kinds.Project, id);
            if (p == null) return Error(404, "E-NOT-FOUND", "プロジェクトがありません");
            request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = 500L * 1024 * 1024;
            if (!request.HasFormContentType) return Error(422, "E-VALIDATION", "multipart/form-data で送信してください");
            var form = await request.ReadFormAsync(ct);
            if (form["rights_confirmed"] != "true") return Error(422, "E-RIGHTS-REQUIRED", "BGM の権利（著作権・利用許諾）の確認にチェックしてください");
            var file = form.Files.FirstOrDefault();
            if (file == null) return Error(422, "E-VALIDATION", "ファイルがありません");
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg")) return Error(422, "E-ING-UNSUPPORTED", "音声ファイル（mp3 / wav / m4a / aac / flac / ogg）を選んでください");
            var dir = Path.Combine(p.Directory, "bgm"); Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, SafeName(Path.GetFileNameWithoutExtension(file.FileName)) + ext);
            PipelineRunner.EnsureInside(p.Directory, dest);
            await using (var fs = File.Create(dest)) await file.CopyToAsync(fs, ct);
            var probe = await new MediaProbe(rt.Ff).ProbeAsync(dest, ct);
            if (probe.AudioChannels == 0 || probe.Duration < 5) { File.Delete(dest); return Error(422, "E-ING-UNSUPPORTED", "5 秒以上の音声ファイルを選んでください"); }
            var (bpm, phase) = await Audio.TempoEstimator.AnalyzeFileAsync(rt.Ff, dest, ct);
            var track = new BgmTrack { Id = "bgm_" + Guid.NewGuid().ToString("N")[..8], Name = Path.GetFileName(file.FileName), Path = dest, Bpm = Math.Round(bpm, 1), Phase = Math.Round(phase, 3), Source = "user", ProjectId = id, RightsConfirmed = true };
            rt.Store.Put(Kinds.Bgm, track.Id, id, track);
            return Results.Json(track, Json.Options, statusCode: 201);
        }).DisableAntiforgery();

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
            var (kit, problems) = await Planning.BrandKitService.ScanAsync(rt.Ff, paths, true, req.CardSec ?? 1.0, ct);
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
        api.MapPost("/projects/{id}/jobs", (Runtime rt, JobQueue q, string id, StartJob req) => StartGeneration(rt, q, id, req.PresetIds, req.Instruction, [], req.Options));
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
                    preview_url = c.PreviewRenderId is string r2 ? $"/api/media/{r2}" : null,
                    approval = ApprovalView(c, plan),
                    // newest export of this plan in this session (queued / rendering / failed / cancelled / done)
                    export = plan == null ? null : q.LatestExport(plan.EditplanId),
                    final_render_id = plan == null ? null : rt.Store.List<Render>(Kinds.Render, plan.EditplanId).FirstOrDefault(x => x.Kind == "final")?.Id,
                };
            });
            return Results.Json(cands, Json.Options);
        });
        api.MapPut("/jobs/{jobId}/candidates/{id}/approval", (Runtime rt, string jobId, string id, ApproveCandidateRequest req) =>
        {
            var c = rt.Store.Get<Candidate>(Kinds.Candidate, id);
            if (c == null || !rt.Store.Ids(Kinds.Candidate, jobId).Contains(id)) return Error(404, "E-NOT-FOUND", "候補がありません");
            var plan = c.EditPlanId is string ep ? rt.Store.Get<EditPlan>(Kinds.EditPlan, ep) : null;
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            if (req.Approved)
            {
                var hard = QaEngine.PlanChecks(rt.CreateRunner().QaContextFor(plan)).Where(x => x.Hard && x.Status == QaStatus.Fail).ToList();
                if (hard.Count > 0) return Error(422, "E-QA-HARDFAIL", "自動チェックで不合格の項目があるため承認できません。「確認・修正」で直してください", hard);
            }
            c = c with { Approval = req.Approved ? new CandidateApproval(DateTimeOffset.Now, Environment.UserName, EditPlanFingerprint.Of(plan)) : null };
            rt.Store.Put(Kinds.Candidate, c.Id, jobId, c);
            return Results.Json(new { candidate_id = c.Id, approval = ApprovalView(c, plan) }, Json.Options);
        }).WithSummary("Approve (bound to the current edit plan) or withdraw the approval of a candidate");
        api.MapPost("/jobs/{jobId}/exports", (Runtime rt, JobQueue q, HttpContext ctx, string jobId, BatchExportRequest req) =>
        {
            if (rt.Store.Get<Job>(Kinds.Job, jobId) == null) return Error(404, "E-NOT-FOUND", "ジョブがありません");
            var ids = (req.CandidateIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (ids.Count == 0) return Error(422, "E-VALIDATION", "書き出す候補を1つ以上選んでください");
            var all = rt.Store.List<Candidate>(Kinds.Candidate, jobId).ToDictionary(c => c.Id);
            var runner = rt.CreateRunner();
            var picked = new List<(Candidate C, EditPlan Plan)>();
            var problems = new List<object>();
            // all-or-nothing: every selected candidate is checked before any export starts
            foreach (var id in ids)
            {
                if (!all.TryGetValue(id, out var c)) return Error(404, "E-NOT-FOUND", "この候補一覧にない候補が含まれています");
                var plan = c.EditPlanId is string ep ? rt.Store.Get<EditPlan>(Kinds.EditPlan, ep) : null;
                string name = c.Series?.Label ?? $"#{c.Rank}";
                if (plan == null) { problems.Add(new { candidate_id = id, name, reason = "編集計画がありません" }); continue; }
                if (c.Approval == null) problems.Add(new { candidate_id = id, name, reason = "承認されていません" });
                else if (c.Approval.PlanHash != EditPlanFingerprint.Of(plan)) problems.Add(new { candidate_id = id, name, reason = "承認後に修正されています。もう一度確認して承認してください" });
                else if (q.ActiveExport(plan.EditplanId) != null) problems.Add(new { candidate_id = id, name, reason = "書き出し中です" });
                else if (QaEngine.PlanChecks(runner.QaContextFor(plan)).Any(x => x.Hard && x.Status == QaStatus.Fail)) problems.Add(new { candidate_id = id, name, reason = "自動チェックで不合格の項目があります" });
                else picked.Add((c, plan));
            }
            if (problems.Count > 0) return Error(409, "E-NOT-APPROVED", "書き出せない候補があるため、どれも書き出していません", problems);
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
        }).WithSummary("Export one or more approved candidates (validated together; none starts if any is not exportable)");
        api.MapPost("/jobs/{jobId}/regenerate", (Runtime rt, JobQueue q, string jobId, Regenerate req) =>
        {
            var prev = rt.Store.Get<Job>(Kinds.Job, jobId);
            if (prev == null) return Error(404, "E-NOT-FOUND", "ジョブがありません");
            var exclude = (req.ExcludeCandidateIds ?? []).Concat(prev.ExcludeCandidateIds).Concat(prev.CandidateIds).Distinct().ToList();
            return StartGeneration(rt, q, prev.ProjectId, prev.PresetIds, req.Instruction, exclude, prev.Options);
        });

        // ---- edit plans (FR-REV) ----
        api.MapGet("/editplans/{id}", (Runtime rt, string id) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            return Results.Json(new { plan, qa = QaEngine.PlanChecks(rt.CreateRunner().QaContextFor(plan)), duration = plan.Duration }, Json.Options);
        });
        api.MapPatch("/editplans/{id}", async (Runtime rt, string id, PatchPlan req, CancellationToken ct) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            var runner = rt.CreateRunner();
            try
            {
                if (req.Ops.Any(o => o.Op == "set_bgm") && rt.Presets[plan.Preset.Id].Planner == "beat")
                    return Error(422, "E-VALIDATION", "ビート同期では BGM を変えるとカットも変わります。条件を変えて作り直してください");
                if (plan.Series != null && req.Ops.Any(o => o.Op is "adjust" or "replan"))
                    return Error(422, "E-VALIDATION", "前編・後編の区切りは前後のパートとつながっているため、ここでは動かせません。本数か1本あたりの長さを変えて作り直してください");
                if (req.Ops.Any(o => o.Op == "set_bgm" && !string.IsNullOrEmpty(o.Target) && o.Target != "none" && rt.Audio.Find(o.Target) == null && rt.Store.Get<BgmTrack>(Kinds.Bgm, o.Target) == null))
                    return Error(422, "E-BGM-NOTFOUND", "指定した BGM が見つかりません");
                var simple = req.Ops.Where(o => o.Op is not ("adjust" or "replan")).ToList();
                var updated = PlanEditor.Apply(plan, simple);
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
                rt.Store.Put(Kinds.EditPlan, id, plan.ProjectId, updated);
                return Results.Json(new { plan = updated, qa = QaEngine.PlanChecks(runner.QaContextFor(updated)), duration = updated.Duration, preview_stale = req.Ops.Any(o => o.Op is "adjust" or "replan") }, Json.Options);
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException) { return Error(422, "E-VALIDATION", ex.Message); }
        });
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
        api.MapPost("/editplans/{id}/render", (Runtime rt, JobQueue q, string id) =>
        {
            var plan = rt.Store.Get<EditPlan>(Kinds.EditPlan, id);
            if (plan == null) return Error(404, "E-NOT-FOUND", "編集計画がありません");
            var hard = QaEngine.PlanChecks(rt.CreateRunner().QaContextFor(plan)).Where(x => x.Hard && x.Status == QaStatus.Fail).ToList();
            if (hard.Count > 0) return Error(422, "E-QA-HARDFAIL", "自動QAのハード制約に違反しています", hard);
            if (q.ActiveExport(id) is { } active) return Error(409, "E-EXPORT-BUSY", "この動画は書き出し中です。完了を待ってください", new { render_id = active.RenderId });
            var rank = plan.Provenance.CandidateId is string cid ? rt.Store.Get<Candidate>(Kinds.Candidate, cid)?.Rank ?? 1 : 1;
            var renderId = "r_" + Guid.NewGuid().ToString("N")[..12];
            q.EnqueueExport(renderId, plan, rank);
            return Results.Json(new { render_id = renderId }, Json.Options, statusCode: 202);
        });
        api.MapGet("/renders/{id}", (Runtime rt, JobQueue q, string id) =>
        {
            if (rt.Store.Get<Render>(Kinds.Render, id) is Render r) return Results.Json(new { state = "done", render = r }, Json.Options);
            return q.Exports.TryGetValue(id, out var s) ? Results.Json(s, Json.Options) : Error(404, "E-NOT-FOUND", "書き出しがありません");
        });
        api.MapPost("/renders/{id}/cancel", (JobQueue q, string id) =>
            q.CancelExport(id) ? Results.Json(q.Exports[id], Json.Options)
            : q.Exports.ContainsKey(id) ? Error(409, "E-NOT-QUEUED", "書き出しが始まっているため取り消せません") : Error(404, "E-NOT-FOUND", "書き出しがありません"))
            .WithSummary("Withdraw a queued export (a running export finishes)");
        api.MapGet("/renders/{id}/files/{key}", (Runtime rt, string id, string key) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, id);
            if (r == null || !r.Outputs.TryGetValue(key, out var f) || !File.Exists(f)) return Error(404, "E-NOT-FOUND", "ファイルがありません");
            var type = Path.GetExtension(f) switch { ".png" => "image/png", ".mp4" => "video/mp4", ".srt" or ".md" or ".ass" => "text/plain; charset=utf-8", ".json" => "application/json", _ => "application/octet-stream" };
            return Results.File(f, type, enableRangeProcessing: true);
        });
        api.MapPost("/renders/{id}/open-folder", (Runtime rt, string id) =>
        {
            var r = rt.Store.Get<Render>(Kinds.Render, id);
            if (r == null) return Error(404, "E-NOT-FOUND", "書き出しがありません");
            if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{r.Path}\"");
            return Results.Accepted();
        });
        api.MapGet("/media/{renderId}", (Runtime rt, string renderId) =>
            rt.Store.Get<Render>(Kinds.Render, renderId) is Render r && File.Exists(r.Path) ? Results.File(r.Path, "video/mp4", enableRangeProcessing: true) : Error(404, "E-NOT-FOUND", "動画がありません"));
    }

    static List<Interval> MergedSegments(EditPlan plan)
    {
        var body = plan.Timeline.Where(c => c.Role == "body").Select(c => new Interval(c.MasterIn, c.MasterOut)).OrderBy(i => i.T0).ToList();
        return Selection.CandidateValidator.MergeOverlaps(body, 2.0);
    }

    static IResult StartGeneration(Runtime rt, JobQueue q, string projectId, List<string> presetIds, string? instruction, List<string> exclude, JobOptions? options = null)
    {
        options ??= new JobOptions();
        if (options.Scope is not ("full" or "balanced" or "economy")) return Error(422, "E-VALIDATION", "絞り込みは full / balanced / economy のどれかを指定してください");
        if (options.Bgm is not ("auto" or "none") && rt.Audio.Find(options.Bgm) == null && rt.Store.Get<BgmTrack>(Kinds.Bgm, options.Bgm) == null) return Error(422, "E-BGM-NOTFOUND", "指定した BGM が見つかりません");
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
            ExcludeCandidateIds = exclude, Cost = runner.EstimateCost(mainAsset, options.IsSeries ? "full" : options.Scope),
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
