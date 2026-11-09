using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Pipeline;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>JobOptions.ReuseAnalysis: a later pattern reuses the cached analysis by default; "redo" makes every cached analysis again.</summary>
public sealed class AnalysisReuseTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-reuse-" + Guid.NewGuid().ToString("N")[..8]);
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Later_runs_reuse_the_analysis_unless_redo_is_chosen()
    {
        const double duration = 27;
        var ff = Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
        Directory.CreateDirectory(_root);
        var src = Path.Combine(_root, "source.mp4");
        await ff.RunFfmpegCheckedAsync(["-nostdin", "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", $"testsrc2=s=360x640:r=30:d={duration}",
            "-f", "lavfi", "-i", $"sine=f=330:d={duration}", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-shortest", src], Ct);
        using var store = new Store(Path.Combine(_root, "data", "db.sqlite"));
        var project = new Project { Id = "p_reuse", Name = "reuse", Directory = Path.Combine(_root, "project") };
        store.Put(Kinds.Project, project.Id, null, project);
        var asset = new SourceAsset { Id = "a", ProjectId = project.Id, Path = src, Sha256 = new string('a', 64), Media = Fixtures.Landscape(duration) with { Width = 360, Height = 640 } };
        store.Put(Kinds.Asset, asset.Id, project.Id, asset);
        store.Put(Kinds.Rights, "rights", project.Id, new RightsConfirmation { Id = "rights", ProjectId = project.Id, AssetId = asset.Id });
        var asr = new CountingTranscriber(duration);
        var dataDir = Path.Combine(_root, "data");
        var cache = Path.Combine(dataDir, "cache", asset.Sha256[..16]);
        // the queue makes a fresh runner per job
        PipelineRunner Runner() => new(new PipelineServices { Ff = ff, Store = store, Presets = Fixtures.Presets, DataDir = dataDir, Asr = asr });
        var progress = new VideoCreator.Media.SyncProgress<JobUpdate>(_ => { });
        Task<Job> Run(string id, bool reuse) => Runner().GenerateAsync(
            new Job { Id = id, ProjectId = project.Id, PresetIds = ["P4"], Options = new JobOptions { Bgm = "none", Sfx = false, ReuseAnalysis = reuse } }, progress, Ct);
        string[] Files() => [Path.Combine(cache, "proxy.mp4"), Path.Combine(cache, "signals.v2.json"), .. Directory.GetFiles(cache, "transcript_*.json")];
        DateTime[] Stamps() => Files().Select(File.GetLastWriteTimeUtc).ToArray();

        Assert.False(Runner().CachedAnalysis(asset).TranscriptCached);
        Assert.True(Runner().EstimateCost(asset).AsrMinutes > 0);

        var first = await Run("job1", reuse: true);
        Assert.Equal(JobStatus.Ready, first.Status);
        Assert.Equal(1, asr.Calls);
        Assert.True(first.Cost!.AsrMinutes > 0);
        Assert.All(Files(), f => Assert.True(File.Exists(f), f));
        var analysed = Runner().CachedAnalysis(asset);
        Assert.True(analysed.TranscriptCached);
        Assert.NotNull(analysed.UpdatedAt);
        Assert.Equal(0, Runner().EstimateCost(asset, reuseAnalysis: true).AsrMinutes);
        Assert.True(Runner().EstimateCost(asset, reuseAnalysis: false).AsrMinutes > 0);

        // backdate the caches so a rewrite is visible in the timestamps whatever the file system's resolution
        var old = DateTime.UtcNow.AddHours(-1);
        foreach (var f in Files()) File.SetLastWriteTimeUtc(f, old);

        var reused = await Run("job2", reuse: true);
        Assert.Equal(JobStatus.Ready, reused.Status);
        Assert.Equal(1, asr.Calls);
        Assert.Equal(0, reused.Cost!.AsrMinutes);
        Assert.All(Stamps(), t => Assert.Equal(old, t));
        Assert.NotEmpty(store.List<Candidate>(Kinds.Candidate, reused.Id));

        var redone = await Run("job3", reuse: false);
        Assert.Equal(JobStatus.Ready, redone.Status);
        Assert.Equal(2, asr.Calls);
        Assert.True(redone.Cost!.AsrMinutes > 0);
        Assert.All(Stamps(), t => Assert.True(t > old));
        Assert.False(File.Exists(Path.Combine(cache, "signals.v2.json.tmp")));
        Assert.NotEmpty(store.List<Candidate>(Kinds.Candidate, redone.Id));
    }

    sealed class CountingTranscriber(double duration) : ITranscriber
    {
        public int Calls;
        public string Name => "counting";
        public Task<Transcript> TranscribeAsync(string audio, double seconds, IReadOnlyList<string> dictionary, int speakers, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var words = Enumerable.Range(0, (int)duration).Select(i => new Word($"w{i}", i, i + 0.8, "話です。", 1)).ToList();
            return Task.FromResult(new Transcript { Provider = Name, Duration = duration, Words = words,
                Sentences = words.Select((w, i) => new Sentence(i, w.T0, w.T1, w.Text, w.Speaker, i, i)).ToList() });
        }
    }
}
