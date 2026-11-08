using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.App;

public static class RenderMaintenance
{
    public static int MarkLegacyPreviewChecksPending(Store store)
    {
        int changed = 0;
        foreach (var render in store.List<Render>(Kinds.Render).Where(r => r.Kind == "preview"))
        {
            var checks = render.Qa.Select(q => q.Stage == null && q.CheckId is "H-02" or "H-03" or "H-04"
                ? q with { Status = QaStatus.NotRun, Stage = "final", Measured = "未実施（書き出し時に検査）" } : q).ToList();
            if (checks.SequenceEqual(render.Qa)) continue;
            store.Put(Kinds.Render, render.Id, render.EditplanId, render with { Qa = checks });
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Older builds stored every final export twice (the runner's internal id + the export id the UI polls).
    /// Keeps the most recently written doc per (edit plan, output file) — the export id, which also carries the metadata output —
    /// and removes the rest together with their QA scratch folders. Idempotent; run at startup.
    /// </summary>
    public static int RemoveDuplicateFinals(Store store, string dataDir)
    {
        var stale = store.List<Render>(Kinds.Render)   // newest first
            .Where(r => r.Kind == "final")
            .GroupBy(r => (r.EditplanId, Path.GetFullPath(r.Path).ToUpperInvariant()))
            .SelectMany(g => g.OrderByDescending(r => r.Outputs.ContainsKey("metadata")).Skip(1))
            .ToList();
        if (stale.Count == 0) return 0;
        store.Delete(stale.Select(r => (Kinds.Render, r.Id)));
        foreach (var r in stale) ProjectDeletion.TryDeleteInside(Path.Combine(dataDir, "qa"), r.Id);
        return stale.Count;
    }
}
