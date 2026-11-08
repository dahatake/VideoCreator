using Microsoft.Extensions.Logging.Abstractions;
using VideoCreator.App;
using VideoCreator.Asr;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Planning;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>Progress shown on screen is measured from ffmpeg's own -progress output, never moves backwards, and reaches 100%.</summary>
public sealed class ProgressTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-pg-" + Guid.NewGuid().ToString("N")[..8]);
    string Data => Path.Combine(_dir, "data");
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    sealed class Recorder<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) { lock (Values) Values.Add(value); }
    }

    [Fact]
    public void Meter_parses_ffmpeg_progress_lines_monotonically_and_throttled()
    {
        var rec = new Recorder<double>();
        var m = new FFmpeg.ProgressMeter(rec, 10);
        foreach (var l in new[] { "frame=1", "out_time_us=N/A", "out_time_us=1000000", "out_time_ms=1001000", "out_time_us=500000", "out_time_us=5000000",
                     "speed=2x", "progress=continue", "out_time_us=12000000", "progress=end", "progress=end" })
            m.Line(l);
        // 0.1 → (0.1001 below the 1% step) → (0.05 backwards ignored) → 0.5 → capped 0.999 until progress=end → 1 once
        Assert.Equal([0.1, 0.5, 0.999, 1.0], rec.Values);
    }

    [Fact]
    public async Task Real_ffmpeg_reports_measured_progress_while_encoding()
    {
        Directory.CreateDirectory(_dir);
        var rec = new Recorder<double>();
        var r = await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=20",
            "-c:v", "libx264", "-preset", "medium", Path.Combine(_dir, "out.mp4")], Ct, progress: rec, duration: 20);
        Assert.Equal(0, r.ExitCode);
        Assert.True(rec.Values.Count >= 3, $"only {rec.Values.Count} updates: {string.Join(", ", rec.Values)}");
        Assert.Equal(1.0, rec.Values[^1]);
        Assert.Contains(rec.Values, v => v is > 0 and < 1);
        Assert.Equal(rec.Values.Order(), rec.Values);
        Assert.Equal(rec.Values.Count, rec.Values.Distinct().Count());
    }

    sealed class FixtureAsr : ITranscriber
    {
        public string Name => "fixture";
        public Task<Transcript> TranscribeAsync(string audioFile, double duration, IReadOnlyList<string> phrases, int maxSpeakers, CancellationToken ct) => Task.FromResult(Fixtures.Talk);
    }

    async Task<(Store Store, Project Project, SourceAsset Asset)> SetupAsync()
    {
        Directory.CreateDirectory(_dir);
        var src = Path.Combine(_dir, "land.mp4");
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=130", "-f", "lavfi", "-i", "sine=f=330:d=130,volume=0.3",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", src], Ct);
        var store = new Store(Path.Combine(Data, "db.sqlite"));
        var project = new Project { Id = "p_pg00000000", Name = "pg", Directory = Path.Combine(_dir, "pg_pg0000") };
        store.Put(Kinds.Project, project.Id, null, project);
        var media = new MediaInfo { Duration = 130, Width = 1280, Height = 720, Fps = 30, AudioChannels = 1 };
        var asset = new SourceAsset { Id = "a1", ProjectId = project.Id, Path = src, Sha256 = new string('7', 64), Media = media };
        store.Put(Kinds.Asset, asset.Id, project.Id, asset);
        store.Put(Kinds.Rights, "rt1", project.Id, new RightsConfirmation { Id = "rt1", ProjectId = project.Id, AssetId = asset.Id });
        return (store, project, asset);
    }

    [Fact]
    public async Task Generation_job_reports_overall_and_measured_sub_task_progress()
    {
        var (store, project, _) = await SetupAsync();
        using var _s = store;
        var runner = new PipelineRunner(new PipelineServices { Ff = Ff, Store = store, Presets = Fixtures.Presets, DataDir = Data, Asr = new FixtureAsr() });
        var job = new Job { Id = "job_pg", ProjectId = project.Id, PresetIds = ["P4"] };
        store.Put(Kinds.Job, job.Id, project.Id, job);
        var rec = new Recorder<JobUpdate>();
        var done = await runner.GenerateAsync(job, rec, Ct);
        Assert.Equal(JobStatus.Ready, done.Status);
        var u = rec.Values;

        // the whole-job bar never goes backwards
        Assert.Equal(u.Select(x => x.Progress).Order(), u.Select(x => x.Progress));
        // proxy creation and shot detection are measured from ffmpeg, not guessed
        Assert.Contains(u, x => x.Stage == JobStatus.Ingesting && x.StepProgress is > 0 and < 1);
        Assert.Contains(u, x => x.Stage == JobStatus.Analyzing && x.StepProgress is > 0 and < 1);
        // transcription has no measurable progress → indeterminate (null), not a fake number
        Assert.All(u.Where(x => x.Stage == JobStatus.Transcribing), x => Assert.Null(x.StepProgress));

        // every preview reports its own encode progress, monotonic per preview and finishing at 100%
        var previews = u.Where(x => x.Stage == JobStatus.Previewing).GroupBy(x => x.Step!.Split(':')[0]).ToList();
        Assert.Equal(done.CandidateIds.Count, previews.Count);
        foreach (var g in previews)
        {
            var steps = g.Select(x => x.StepProgress!.Value).ToList();
            Assert.Equal(steps.Order(), steps);
            Assert.Equal(1.0, steps[^1]);
            Assert.Contains(g, x => x.StepProgress is > 0 and < 0.9 && x.Step!.Contains("合成"));
            Assert.Contains(g, x => x.Step!.Contains("自動チェック"));
        }
        Assert.InRange(u.Where(x => x.Stage == JobStatus.Previewing).Max(x => x.Progress), 0.949, 0.951);
    }

    [Fact]
    public async Task Export_status_exposes_measured_progress_until_done()
    {
        var (store, project, asset) = await SetupAsync();
        using var _s = store;
        var p = Fixtures.Presets["P4"]; var tr = Fixtures.Talk;
        var res = Solver.Plan([new Interval(74, 103)], tr, Fixtures.Signals(tr), p, asset.Media, asset.Id, ["300円の商品"]);
        var plan = new EditPlan
        {
            EditplanId = "ep_pg", ProjectId = project.Id, Preset = p.Ref, Timeline = res.Clips,
            Audio = new AudioSpec { Master = asset.Id },
        };
        store.Put(Kinds.EditPlan, plan.EditplanId, project.Id, plan);
        var q = new JobQueue(store, () => new PipelineRunner(new PipelineServices { Ff = Ff, Store = store, Presets = Fixtures.Presets, DataDir = Data }), new JobEvents(), NullLogger<JobQueue>.Instance);
        await q.StartAsync(Ct);
        var seen = new List<(double Progress, string? Step)>();
        try
        {
            q.EnqueueExport("r_pg00000001", plan, 1);
            var until = DateTime.UtcNow.AddMinutes(3);
            while (q.Exports["r_pg00000001"].State is "queued" or "rendering" && DateTime.UtcNow < until)
            {
                if (q.Exports["r_pg00000001"] is { State: "rendering", Progress: double v } st) seen.Add((v, st.Step));
                await Task.Delay(50, Ct);
            }
        }
        finally { await q.StopAsync(CancellationToken.None); }
        var fin = q.Exports["r_pg00000001"];
        Assert.True(fin.State == "done", $"{fin.State}: {fin.ErrorCode} {fin.Message}");
        Assert.Equal(1.0, fin.Progress);
        Assert.Equal(seen.Select(x => x.Progress).Order(), seen.Select(x => x.Progress));
        Assert.Contains(seen, x => x.Progress is > 0.05 and < 0.85);
        Assert.Contains(seen, x => x.Step?.Contains("2 パス") == true);
        Assert.True(seen.Select(x => x.Progress).Distinct().Count() >= 3, string.Join(", ", seen.Select(x => x.Progress).Distinct()));
    }
}
