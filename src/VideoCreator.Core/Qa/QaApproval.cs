using System.Security.Cryptography;
using System.Text;
using VideoCreator.Models;

namespace VideoCreator.Qa;

/// <summary>Reviewing a check never changes its verdict or waives an export constraint.</summary>
public static class QaApproval
{
    public static string ResultHash(QaResult result) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Serialize(new
    {
        result.CheckId, result.Status, result.Measured, result.Threshold, result.Message, result.Stage,
        Items = result.Items?.Select(i => new { i.Target, i.Kind, i.Detail, i.Start, i.End }).ToList(),
    }))));

    public static List<QaResult> Attach(EditPlan plan, IEnumerable<QaResult> results)
    {
        var planHash = EditPlanFingerprint.Of(plan);
        return results.Select(q =>
        {
            var hash = ResultHash(q);
            return q with
            {
                ReviewHash = hash,
                ReviewApproval = q.Status == QaStatus.NotRun ? null : plan.QaApprovals?.FirstOrDefault(a =>
                    a.CheckId == q.CheckId && a.PlanHash == planHash && a.ResultHash == hash),
            };
        }).ToList();
    }
}
