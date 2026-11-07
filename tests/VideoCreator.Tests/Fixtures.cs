using VideoCreator.Asr;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

public static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "VideoCreator.slnx"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }
    }

    static Transcript? _talk;
    /// <summary>Real Azure fast-transcription response (spikes/04-asr.cs) for a 125 s two-speaker Japanese clip.</summary>
    public static Transcript Talk => _talk ??= AzureFastTranscriber.Parse(File.ReadAllText(Path.Combine(Dir, "talk_transcript.json")), 125, "fixture");

    static IReadOnlyDictionary<string, Preset>? _presets;
    public static IReadOnlyDictionary<string, Preset> Presets => _presets ??= PresetStore.LoadDirectory(Path.Combine(RepoRoot, "presets")).ToDictionary(p => p.Id);

    public static MediaInfo Landscape(double duration = 125) => new() { Duration = duration, Width = 1920, Height = 1080, Fps = 30, AudioChannels = 1, VideoCodec = "h264" };

    public static SignalSet Signals(Transcript tr) => new()
    {
        Shots = tr.Sentences.Select(s => Math.Round(s.T0 - 0.05, 2)).ToList(),
        Loudness100Ms = Enumerable.Repeat(-25.0, (int)(tr.Duration * 10) + 10).ToList(),
        MedianLoudness = -25,
    };

    static FFmpeg? _ff;
    public static FFmpeg? Ff
    {
        get
        {
            if (_ff != null) return _ff;
            try { _ff = FFmpeg.Locate(start: AppContext.BaseDirectory); } catch (FileNotFoundException) { }
            return _ff;
        }
    }
}

/// <summary>Scriptable LLM for selection tests.</summary>
public sealed class FakeSelector(Func<int, string, LlmResult> respond) : IHighlightSelector
{
    public FakeSelector(Func<int, LlmResult> respond) : this((n, _) => respond(n)) { }
    public int Calls;
    public List<string> Prompts { get; } = [];
    public string Name => "fake";
    public string Model => "fake-model";
    public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct)
    {
        lock (Prompts) { Prompts.Add(user); return Task.FromResult(respond(Interlocked.Increment(ref Calls), user)); }
    }
    public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct) => Task.FromResult("""{"ranking":[]}""");
}
