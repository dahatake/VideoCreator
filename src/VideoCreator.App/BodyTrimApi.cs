using VideoCreator.Asr;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Storage;

namespace VideoCreator.App;

public static class BodyTrimApi
{
    /// <summary>Save main-video boundaries, or clear them to restore ordinary highlight generation.</summary>
    public sealed record UpdateBodyTrimRequest(string Mode, string? AssetId = null, double? Start = null, double? End = null);

    static IResult Error(int status, string message) =>
        Results.Json(new { error = new { code = "E-TRIM-RANGE", message } }, Json.Options, statusCode: status);

    static bool Busy(Runtime rt, string projectId) => rt.Store.List<Job>(Kinds.Job, projectId)
        .Any(j => j.Status is not (JobStatus.Ready or JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled));

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/projects/{id}/body-trim/analyze", async (Runtime rt, string id, CancellationToken ct) =>
        {
            if (rt.Store.Get<Project>(Kinds.Project, id) == null) return Error(404, "プロジェクトがありません。");
            var main = BodyTrimmer.Main(rt.Store.Assets(id));
            if (main == null) return Error(404, "主動画がありません。");
            if (Busy(rt, id)) return Error(409, "生成中です。完了後に本編を解析してください。");
            if (!rt.Store.List<RightsConfirmation>(Kinds.Rights, id).Any(r => r.AssetId == main.Id))
                return Error(409, "主動画の権利確認を完了してください。");
            try { return Results.Json(await rt.CreateRunner().AnalyzeBodyAsync(main, ct), Json.Options); }
            catch (AsrException ex) { return Error(422, "文字起こしに失敗しました: " + ex.Message); }
            catch (FFmpegException ex) { return Error(422, "動画解析に失敗しました: " + ex.Message); }
        }).WithSummary("主動画を解析して手動・自動の本編編集点を提案する").Produces<BodyTrimAnalysis>();

        api.MapPut("/projects/{id}/body-trim", async (Runtime rt, string id, UpdateBodyTrimRequest req, CancellationToken ct) =>
        {
            var project = rt.Store.Get<Project>(Kinds.Project, id);
            if (project == null) return Error(404, "プロジェクトがありません。");
            if (Busy(rt, id)) return Error(409, "生成中です。完了後に本編範囲を変更してください。");
            if (req.Mode is not ("off" or "manual" or "auto")) return Error(422, "編集方法は off / manual / auto で指定してください。");
            var main = BodyTrimmer.Main(rt.Store.Assets(id));
            BodyTrim? trim = null;
            if (req.Mode != "off")
            {
                if (main == null || req.AssetId != main.Id) return Error(422, "主動画が変わっています。編集点を指定し直してください。");
                if (req.Mode == "auto")
                {
                    if (!rt.Store.List<RightsConfirmation>(Kinds.Rights, id).Any(r => r.AssetId == main.Id))
                        return Error(409, "主動画の権利確認を完了してください。");
                    try
                    {
                        var analysis = await rt.CreateRunner().AnalyzeBodyAsync(main, ct);
                        trim = new(main.Id, analysis.Automatic.T0, analysis.Automatic.T1, "auto");
                    }
                    catch (AsrException ex) { return Error(422, "文字起こしに失敗しました: " + ex.Message); }
                    catch (FFmpegException ex) { return Error(422, "動画解析に失敗しました: " + ex.Message); }
                }
                else
                {
                    if (req.Start == null || req.End == null) return Error(422, "本編の開始・終了を指定してください。");
                    trim = new(main.Id, req.Start.Value, req.End.Value);
                }
                if (!BodyTrimmer.Valid(trim, main)) return Error(422, "本編は主動画内で、開始 < 終了となる0.1秒以上の範囲を指定してください。");
            }
            return rt.Store.Atomic<IResult>(() =>
            {
                var current = rt.Store.Get<Project>(Kinds.Project, id);
                if (current == null) return Error(404, "プロジェクトがありません。");
                if (Busy(rt, id) || trim != null && BodyTrimmer.Main(rt.Store.Assets(id))?.Id != trim.AssetId)
                    return Error(409, "解析中に設定が変わりました。再確認してください。");
                rt.Store.Put(Kinds.Project, id, null, current with { BodyTrim = trim });
                return Results.Json(new { body_trim = trim }, Json.Options);
            });
        }).WithSummary("冒頭・末尾の非破壊トリミングを保存する");

        api.MapGet("/assets/{id}/source-preview", async (Runtime rt, string id, CancellationToken ct) =>
        {
            var asset = rt.Store.Get<SourceAsset>(Kinds.Asset, id);
            if (asset == null || !File.Exists(asset.Path)) return Error(404, "素材がありません。");
            try { return Results.File(await rt.CreateRunner().SourcePreviewAsync(asset, ct), "video/mp4", enableRangeProcessing: true); }
            catch (FFmpegException ex) { return Error(422, "プレビューの作成に失敗しました: " + ex.Message); }
        }).WithSummary("編集点の確認用に元動画を再生する");
    }
}
