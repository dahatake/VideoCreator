using System.Collections.Concurrent;
using System.Threading.Channels;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>Fan-out of job progress to SSE subscribers.</summary>
public sealed class JobEvents
{
    readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<JobUpdate>>> _subs = new();
    readonly ConcurrentDictionary<string, JobUpdate> _last = new();

    public (Guid id, ChannelReader<JobUpdate> reader) Subscribe(string jobId)
    {
        var ch = Channel.CreateUnbounded<JobUpdate>();
        var id = Guid.NewGuid();
        _subs.GetOrAdd(jobId, _ => new())[id] = ch;
        if (_last.TryGetValue(jobId, out var last)) ch.Writer.TryWrite(last);
        return (id, ch.Reader);
    }

    public void Unsubscribe(string jobId, Guid id) { if (_subs.TryGetValue(jobId, out var d) && d.TryRemove(id, out var ch)) ch.Writer.TryComplete(); }

    public void Forget(string jobId) => _last.TryRemove(jobId, out _);

    public void Publish(JobUpdate u)
    {
        _last[u.JobId] = u;
        if (_subs.TryGetValue(u.JobId, out var d)) foreach (var ch in d.Values) ch.Writer.TryWrite(u);
    }
}

/// <param name="Progress">Measured 0..1 of the whole export (null while queued).</param>
/// <param name="Step">What is running right now, e.g. the ffmpeg pass or QA.</param>
public sealed record ExportStatus(string RenderId, string EditplanId, string State, string? Message = null, List<QaResult>? Failures = null, string? ErrorCode = null, double? Progress = null, string? Step = null, DateTimeOffset? QueuedAt = null, string? PlanHash = null);

/// <summary>Progress of an on-demand preview re-render (review screen); <paramref name="Running"/> is false when none is in flight.</summary>
public sealed record PreviewProgress(bool Running, double Progress, string? Step);

/// <summary>Single worker: generation jobs and final exports run one at a time (predictable CPU, deterministic output).</summary>
public sealed class JobQueue(Store store, Func<PipelineRunner> runnerFactory, JobEvents events, ILogger<JobQueue> log) : BackgroundService
{
    readonly Channel<Func<CancellationToken, Task>> _work = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
    readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    public ConcurrentDictionary<string, ExportStatus> Exports { get; } = new();
    public ConcurrentDictionary<string, PreviewProgress> Previews { get; } = new();

    public void EnqueueJob(Job job)
    {
        store.Put(Kinds.Job, job.Id, job.ProjectId, job);
        events.Publish(new JobUpdate(job.Id, JobStatus.Queued, 0, "待機中"));
        var cts = new CancellationTokenSource();
        _running[job.Id] = cts;
        _work.Writer.TryWrite(ct => RunJobAsync(job.Id, CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token).Token));
    }

    public bool Cancel(string jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts)) return false;
        cts.Cancel(); return true;
    }

    /// <summary>True while a generation job is queued/running or a final export is queued/rendering for the project.</summary>
    public bool IsBusy(string projectId) =>
        _running.Keys.Any(id => store.Get<Job>(Kinds.Job, id)?.ProjectId == projectId)
        || Exports.Values.Any(e => e.State is "queued" or "rendering" && store.Get<EditPlan>(Kinds.EditPlan, e.EditplanId)?.ProjectId == projectId);

    /// <summary>No generation job, export or preview re-render is queued or running (nothing holds project files open).</summary>
    public bool IsIdle => _running.IsEmpty && Previews.IsEmpty && !Exports.Values.Any(e => e.State is "queued" or "rendering");

    /// <summary>Drops in-memory state for deleted documents.</summary>
    public void Forget(IEnumerable<string> jobIds, IEnumerable<string> renderIds)
    {
        foreach (var j in jobIds) events.Forget(j);
        foreach (var r in renderIds) Exports.TryRemove(r, out _);
    }

    public void EnqueueExport(string renderId, EditPlan plan, int rank)
    {
        Exports[renderId] = new ExportStatus(renderId, plan.EditplanId, "queued", "待機中", QueuedAt: DateTimeOffset.UtcNow, PlanHash: EditPlanFingerprint.Of(plan));
        _work.Writer.TryWrite(async ct =>
        {
            // queued → rendering only if nobody cancelled it meanwhile (CancelExport makes the opposite transition atomically)
            if (!Exports.TryGetValue(renderId, out var queued) || queued.State != "queued"
                || !Exports.TryUpdate(renderId, queued with { State = "rendering", Message = "書き出しています（2パス音量調整・QA を含みます）", Progress = 0 }, queued)) return;
            try
            {
                var runner = runnerFactory();
                var project = store.Get<Project>(Kinds.Project, plan.ProjectId)!;
                var report = new Media.SyncProgress<(double Progress, string Step)>(p =>
                    Exports[renderId] = Exports[renderId] with { Progress = Math.Round(Math.Max(Exports[renderId].Progress ?? 0, 0.95 * p.Progress), 4), Step = p.Step });
                var render = await runner.RenderAsync(plan, preview: false, ct, PipelineRunner.ExportBaseName(project, plan, rank), renderId, report);
                var cand = plan.Provenance.CandidateId is string cid ? store.Get<Candidate>(Kinds.Candidate, cid) : null;
                if (cand != null)
                {
                    Exports[renderId] = Exports[renderId] with { Progress = 0.95, Step = "投稿用のタイトル・説明文の案を作っています" };
                    var md = await runner.MetadataAsync(cand, plan, ct);
                    var mdFile = Path.ChangeExtension(render.Path, ".metadata.md");
                    await File.WriteAllTextAsync(mdFile, md, ct);
                    render = render with { Outputs = new Dictionary<string, string>(render.Outputs) { ["metadata"] = mdFile } };
                }
                try
                {
                    store.Atomic(() =>
                    {
                        runner.EnsureCurrentPlan(plan);
                        store.Put(Kinds.Render, renderId, plan.EditplanId, render);
                        return true;
                    });
                }
                catch (PipelineException ex) when (ex.Code == "E-PLAN-STALE") { runner.Quarantine(render); throw; }
                Exports[renderId] = Exports[renderId] with { State = "done", Message = "書き出しが完了しました", Progress = 1, Step = null };
            }
            catch (QaBlockedException qa) { Exports[renderId] = Exports[renderId] with { State = "failed", Message = qa.Message, Failures = qa.Failures, ErrorCode = "E-QA-HARDFAIL" }; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "export {Render} failed", renderId);
                Exports[renderId] = Exports[renderId] with { State = "failed", Message = ex.Message, ErrorCode = (ex as Media.FFmpegException)?.Code ?? (ex as PipelineException)?.Code ?? "E-RND-FFMPEG" };
            }
        });
    }

    /// <summary>Withdraws an export that has not started yet. A running export is left to finish (no half-written files).</summary>
    public bool CancelExport(string renderId) =>
        Exports.TryGetValue(renderId, out var cur) && cur.State == "queued"
        && Exports.TryUpdate(renderId, cur with { State = "cancelled", Message = "書き出しを取り消しました" }, cur);

    /// <summary>The queued or rendering export of an edit plan, if any.</summary>
    public ExportStatus? ActiveExport(string editplanId) =>
        Exports.Values.FirstOrDefault(e => e.EditplanId == editplanId && e.State is "queued" or "rendering");

    /// <summary>The most recently queued export of an edit plan in this run of the app, whatever its state.</summary>
    public ExportStatus? LatestExport(string editplanId) =>
        Exports.Values.Where(e => e.EditplanId == editplanId).MaxBy(e => e.QueuedAt);

    async Task RunJobAsync(string jobId, CancellationToken ct)
    {
        var job = store.Get<Job>(Kinds.Job, jobId);
        if (job == null || job.Status is JobStatus.Cancelled or JobStatus.Done or JobStatus.Ready) return;
        job = job with { Status = JobStatus.Ingesting, StartedAt = DateTimeOffset.UtcNow };
        store.Put(Kinds.Job, job.Id, job.ProjectId, job);
        DateTime lastPut = default; string? lastKey = null;
        // synchronous + in order (Progress<T> would post to the thread pool and could show an older value last)
        var progress = new Media.SyncProgress<JobUpdate>(u =>
        {
            var key = $"{u.Stage}|{u.Message}";
            if (key != lastKey || DateTime.UtcNow - lastPut > TimeSpan.FromSeconds(1))
            {
                var cur = store.Get<Job>(Kinds.Job, jobId);
                if (cur != null && cur.Status is not (JobStatus.Cancelled or JobStatus.Failed)) store.Put(Kinds.Job, jobId, cur.ProjectId, cur with { Status = u.Stage, Progress = u.Progress, Message = u.Message });
                lastKey = key; lastPut = DateTime.UtcNow;
            }
            events.Publish(u);
        });
        try
        {
            var done = await runnerFactory().GenerateAsync(job, progress, ct);
            done = done with { FinishedAt = DateTimeOffset.UtcNow };
            store.Put(Kinds.Job, job.Id, job.ProjectId, done);
            events.Publish(new JobUpdate(job.Id, JobStatus.Ready, 1, done.Message));
        }
        catch (OperationCanceledException)
        {
            store.Put(Kinds.Job, job.Id, job.ProjectId, job with { Status = JobStatus.Cancelled, Message = "キャンセルしました", FinishedAt = DateTimeOffset.UtcNow });
            events.Publish(new JobUpdate(job.Id, JobStatus.Cancelled, 0, "キャンセルしました"));
        }
        catch (Exception ex)
        {
            var code = ex switch { PipelineException p => p.Code, Media.IngestException i => i.Code, Media.FFmpegException f => f.Code, _ => "E-INTERNAL" };
            log.LogError(ex, "job {Job} failed", jobId);
            var cur = store.Get<Job>(Kinds.Job, jobId) ?? job;
            store.Put(Kinds.Job, job.Id, job.ProjectId, cur with { Status = JobStatus.Failed, ErrorCode = code, Message = ex.Message, FinishedAt = DateTimeOffset.UtcNow });
            events.Publish(new JobUpdate(job.Id, JobStatus.Failed, 0, ex.Message));
        }
        finally { _running.TryRemove(jobId, out _); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // NFR-REL-02 / UX-05: resume jobs interrupted by a restart (stages are cached, so this is cheap)
        foreach (var j in store.List<Job>(Kinds.Job).Where(j => j.Status is not (JobStatus.Ready or JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled)))
        {
            log.LogInformation("resuming job {Job} ({Status})", j.Id, j.Status);
            EnqueueJob(j with { Status = JobStatus.Queued, Message = "再開待ち" });
        }
        await foreach (var work in _work.Reader.ReadAllAsync(stoppingToken))
        {
            try { await work(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "work item failed"); }
        }
    }
}
