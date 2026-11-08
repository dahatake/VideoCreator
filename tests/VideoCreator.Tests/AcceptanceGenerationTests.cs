using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

public sealed class AcceptanceGenerationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-at14-" + Guid.NewGuid().ToString("N")[..8]);
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData(10, false)]
    [InlineData(27, true)]
    public async Task AT14_short_input_fails_and_one_feasible_scene_is_not_inflated(double duration, bool feasible)
    {
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        Directory.CreateDirectory(_root);
        var src = Path.Combine(_root, "source.mp4");
        await ff.RunFfmpegCheckedAsync(["-nostdin", "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"testsrc2=s=360x640:r=30:d={duration}",
            "-f", "lavfi", "-i", $"sine=f=330:d={duration}", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", src], Ct);
        using var store = new Store(Path.Combine(_root, "data", "db.sqlite"));
        var project = new Project { Id = "p_at14", Name = "AT14", Directory = Path.Combine(_root, "project") };
        store.Put(Kinds.Project, project.Id, null, project);
        var asset = new SourceAsset { Id = "a", ProjectId = project.Id, Path = src, Sha256 = new string('a', 64), Media = Fixtures.Landscape(duration) with { Width = 360, Height = 640 } };
        store.Put(Kinds.Asset, asset.Id, project.Id, asset);
        store.Put(Kinds.Rights, "rights", project.Id, new RightsConfirmation { Id = "rights", ProjectId = project.Id, AssetId = asset.Id });
        var asr = new FixedTranscriber(duration);
        var runner = new PipelineRunner(new PipelineServices { Ff = ff, Store = store, Presets = Fixtures.Presets, DataDir = Path.Combine(_root, "data"), Asr = asr });
        var job = new Job { Id = "job", ProjectId = project.Id, PresetIds = ["P4"], Options = new JobOptions { Bgm = "none", Sfx = false } };
        var progress = new VideoCreator.Media.SyncProgress<JobUpdate>(_ => { });
        if (!feasible)
        {
            var ex = await Assert.ThrowsAsync<PipelineException>(() => runner.GenerateAsync(job, progress, Ct));
            Assert.Equal("E-SOL-INFEASIBLE", ex.Code);
            Assert.Equal("素材がP4の尺の下限（25秒）より短い", ex.Message);
            Assert.Equal(0, asr.Calls);
            Assert.Empty(store.List<Candidate>(Kinds.Candidate));
            return;
        }
        var result = await runner.GenerateAsync(job, progress, Ct);
        Assert.Equal(JobStatus.Ready, result.Status);
        var candidate = Assert.Single(store.List<Candidate>(Kinds.Candidate, job.Id));
        Assert.Single(result.CandidateIds);
        Assert.Contains(result.Warnings, w => w.Contains("候補は1本") && w.Contains("重複"));
        var plan = store.Get<EditPlan>(Kinds.EditPlan, candidate.EditPlanId!)!;
        Assert.InRange(plan.Duration, 25, 35);
        Assert.NotNull(candidate.PreviewRenderId);
        Assert.Equal(1, asr.Calls);
        Assert.True(File.Exists(store.Get<Render>(Kinds.Render, candidate.PreviewRenderId!)!.Path));
    }

    sealed class FixedTranscriber(double duration) : ITranscriber
    {
        public int Calls;
        public string Name => "fixed-at14";
        public Task<Transcript> TranscribeAsync(string audio, double seconds, IReadOnlyList<string> dictionary, int speakers, CancellationToken ct)
        {
            Calls++;
            var words = Enumerable.Range(0, (int)duration).Select(i => new Word($"w{i}", i, i + 0.8, "話です。", 1)).ToList();
            return Task.FromResult(new Transcript { Provider = Name, Duration = duration, Words = words,
                Sentences = words.Select((w, i) => new Sentence(i, w.T0, w.T1, w.Text, w.Speaker, i, i)).ToList() });
        }
    }
}
