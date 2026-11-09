using System.Globalization;
using VideoCreator.Audio;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>Follow-ups to v1.1: cross-fade, verbatim captions, loop ending, laughter, speaker auto-assignment.</summary>
public class V12Tests : IAsyncLifetime
{
    static readonly Transcript Tr = Fixtures.Talk;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-v12-" + Guid.NewGuid().ToString("N")[..8]);
    CancellationToken Ct => TestContext.Current.CancellationToken;
    FFmpeg Ff => Fixtures.Ff ?? throw new InvalidOperationException("run scripts/get-tools.ps1");
    public ValueTask InitializeAsync() { Directory.CreateDirectory(_dir); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { try { Directory.Delete(_dir, true); } catch { } return ValueTask.CompletedTask; }

    async Task<(int r, int g, int b)> PixelAsync(string file, double t)
    {
        using var ms = new MemoryStream();
        await Ff.RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-ss", t.ToString("0.###", IC), "-i", file, "-frames:v", "1", "-vf", "scale=1:1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], Ct, stdout: ms);
        var b = ms.ToArray(); return (b[0], b[1], b[2]);
    }

    [Fact] // FR-SWT-05: video cross-fade at a scene change; duration and A/V length unchanged
    public async Task Scene_change_gets_a_crossfade_without_changing_duration()
    {
        var src = Path.Combine(_dir, "scenes.mp4");
        // red 0–10 s, blue 10–20 s; steady tone
        await Ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=0xDC1E1E:s=640x360:r=30:d=10", "-f", "lavfi", "-i", "color=c=0x1E1EDC:s=640x360:r=30:d=10",
            "-f", "lavfi", "-i", "sine=f=330:d=20,volume=0.3", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]", "-map", "2:a", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", src], Ct);
        var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = src, Sha256 = "x", Media = new MediaInfo { Duration = 20, Width = 640, Height = 360, Fps = 30, AudioChannels = 1 } };
        var p = Fixtures.Presets["P6"];
        Assert.Equal(0.3, p.Cut.SceneFadeSec);
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 5 }, new() { ClipId = "c2", AssetId = "a1", SrcIn = 12, SrcOut = 17 }, new() { ClipId = "c3", AssetId = "a1", SrcIn = 17.0, SrcOut = 19.0 } };
        var notes = new List<string>();
        clips = PlanBuilder.ApplyFades(clips, p, [asset], notes);
        Assert.Equal(0.3, clips[1].TransitionSec);
        Assert.Equal(0, clips[2].TransitionSec);        // contiguous: stays a hard cut
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = clips, Audio = new AudioSpec { Master = "a1", SfxEnabled = false } };
        var outp = await new Renderer(Ff).RenderAsync(plan, p, new Dictionary<string, SourceAsset> { ["a1"] = asset }, Path.Combine(_dir, "fade.mp4"), preview: true, Ct);
        var probe = await Ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "csv=p=0", outp.Path], Ct);
        var d = probe.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Split(',')).Where(x => x.Length == 2).ToDictionary(x => x[0], x => double.Parse(x[1], IC));
        Assert.InRange(d["video"], plan.Duration - 0.1, plan.Duration + 0.1);
        Assert.InRange(Math.Abs(d["video"] - d["audio"]), 0, 0.1);
        var before = await PixelAsync(outp.Path, 4.7); var mid = await PixelAsync(outp.Path, 5.15); var after = await PixelAsync(outp.Path, 5.7);
        Assert.True(before.r > 170 && before.b < 70, $"before {before}");
        Assert.True(mid.r > 60 && mid.b > 60, $"mid-fade should be a blend: {mid}");
        Assert.True(after.b > 170 && after.r < 70, $"after {after}");
        // the hard cut at 10 s (contiguous source) has no blend
        var hardBefore = await PixelAsync(outp.Path, 9.9); var hardAfter = await PixelAsync(outp.Path, 10.1);
        Assert.True(hardBefore.b > 170 && hardAfter.b > 170);
    }

    [Fact]
    public async Task Plan_generation_keeps_captions_verbatim_with_or_without_an_llm()
    {
        var p = Fixtures.Presets["P4"];
        var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = "unused.mp4", Sha256 = "x", Media = Fixtures.Landscape() };
        var candidate = new Candidate { Id = "c1", Segments = [new Interval(8.8, 40)] };
        var job = new Job { Id = "j", ProjectId = "p", PresetIds = ["P4"] };
        int calls = 0;
        var llm = new Json2("""{"items":[{"id":"t1","text":"短く"}]}""")
        {
            Respond = _ => { calls++; return """{"items":[{"id":"t1","text":"短く"}]}"""; },
        };
        var request = new PlanRequest(candidate, Tr, Fixtures.Signals(Tr), p, [asset], asset, "p", job, new Dictionary<string, List<FaceSample>>(), null, llm,
            OverrideSegments: candidate.Segments);
        var (withAi, _) = await PlanBuilder.BuildAsync(request, Ct);
        var (withoutAi, _) = await PlanBuilder.BuildAsync(request with { Llm = null }, Ct);
        var caps = withAi.Telops.Where(t => t.Type == TelopType.Caption).ToList();
        Assert.NotEmpty(caps);
        Assert.Equal(0, calls);
        Assert.Equal(withoutAi.Telops.Where(t => t.Type == TelopType.Caption).Select(t => t.Text), caps.Select(t => t.Text));
        var words = Tr.Words.ToDictionary(w => w.Id);
        Assert.All(caps, t => Assert.Equal(JapaneseText.NormalizeTelop(string.Concat(t.SourceWordIds.Select(id => words[id].Text))), t.Text));
        Assert.Equal(TelopBuilder.MapWords(withAi.Timeline, Tr).Where(w => !JapaneseText.IsPunct(w.Word.Text)).Select(w => w.Word.Id),
            caps.SelectMany(t => t.SourceWordIds).Where(id => !JapaneseText.IsPunct(words[id].Text)));
        Assert.All(caps, t => Assert.Contains(t.Text, AssWriter.Srt(withAi, p)));
        Assert.Contains(VideoCreator.Qa.QaEngine.PlanChecks(new(withAi, p, Tr, ["a1"], [])), q => q.CheckId == "S-01" && q.Status == QaStatus.Warn);
    }

    [Fact]
    public void Identical_emphasis_does_not_remove_the_caption_or_its_srt()
    {
        const string text = "あの今日は晴れですね";
        var tr = new Transcript
        {
            Provider = "fixed", Duration = 5, Words = [new Word("w0", 1, 3, text, 1)],
            Sentences = [new Sentence(0, 1, 3, text, 1, 0, 0)],
        };
        var clips = new List<Clip> { new() { ClipId = "c1", AssetId = "a1", SrcIn = 0, SrcOut = 5 } };
        var p = Fixtures.Presets["P4"];
        var ts = TelopBuilder.Build(clips, tr, p, "", "", [text], []);
        var caption = Assert.Single(ts);
        Assert.Equal(TelopType.Caption, caption.Type);
        Assert.Equal(text, caption.Text);
        Assert.Equal(["w0"], caption.SourceWordIds);
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = clips, Telops = ts, Audio = new AudioSpec { Master = "a1" } };
        Assert.Contains(text, AssWriter.Srt(plan, p));
    }

    [Fact] // FR-SOL-06: the end whose last sentence connects best to the opening wins
    public async Task Loop_ending_prefers_the_variant_the_llm_scores_highest()
    {
        var p = Fixtures.Presets["P2"]; Assert.True(p.Loop.Enabled);
        var sig = Fixtures.Signals(Tr);
        var start = Tr.Sentences.First(s => s.Text.Contains("一時間で1万円"));
        var segs = new List<Interval> { new(start.T0 - 0.1, start.T0 + 20) };
        var seen = new List<string>();
        var llm = new Json2("{}") { Respond = user => { seen.Add(user); var n = user.Split('\n').Count(l => l.Contains("の最後の文")); return "{\"items\":[" + string.Join(",", Enumerable.Range(0, n).Select(i => $"{{\"index\":{i},\"score\":{(i == n - 1 ? 9 : 3)}}}")) + "]}"; } };
        var r = await LoopEnding.ChooseAsync(llm, Tr, segs, sig, p, Fixtures.Landscape(), "a1", Ct);
        Assert.NotNull(r);
        Assert.Contains("ループ", r!.Value.Note);
        Assert.Contains("冒頭の文: ", seen[0]);
        Assert.True(seen[0].Split('\n').Count(l => l.Contains("の最後の文")) >= 2);
        Assert.NotEqual(segs[0].T1, r.Value.Segments[^1].T1);   // not the default (index 0); picked the best-scored (last) variant
    }

    static float[] Noise(int n, int seed, Func<int, double> amp) { var r = new Random(seed); return Enumerable.Range(0, n).Select(i => (float)((r.NextDouble() * 2 - 1) * amp(i))).ToArray(); }

    [Fact] // FR-SIG-04: laughter pulses; steady noise (crowd/applause-like) and a single impact do not
    public void Laughter_is_told_apart_from_steady_noise_and_impacts()
    {
        const int R = 8000; int n = R * 2;
        var laugh = Noise(n, 1, i => 0.3 * (0.55 + 0.45 * Math.Sin(2 * Math.PI * 5 * i / R)));        // 5 Hz "ha-ha-ha"
        var applause = Noise(n, 2, _ => 0.3);
        var impact = Noise(n, 3, i => 0.8 * Math.Exp(-i / (0.08 * R)));
        Assert.True(LaughDetector.IsLaughter(laugh, R));
        Assert.False(LaughDetector.IsLaughter(applause, R));
        Assert.False(LaughDetector.IsLaughter(impact, R));
    }

    [Fact] // FR-SWT-01: separate microphones identify the speaker of each camera; a shared mic returns "unknown"
    public void Speakers_are_assigned_to_cameras_from_their_microphones()
    {
        const int R = 8000;
        var sents = new List<Sentence>(); var words = new List<Word>();
        for (int i = 0; i < 10; i++) sents.Add(new Sentence(i, i * 3, i * 3 + 2.5, "x", i % 2 + 1, i, i));
        var tr = new Transcript { Provider = "t", Duration = 30, Sentences = sents, Words = words };
        float[] Cam(double own, double other) { var r = new Random(7); var a = new float[30 * R]; foreach (var s in sents) for (int k = (int)(s.T0 * R); k < (int)(s.T1 * R); k++) a[k] = (float)((r.NextDouble() * 2 - 1) * (s.Speaker == 2 ? own : other)); return a; }
        // camera 0 is speaker 2's mic, camera 1 is speaker 1's mic
        var res = SpeakerAssign.Assign([Cam(0.5, 0.05), Cam(0.05, 0.5)], R, tr);
        Assert.Equal([2, 1], res);
        Assert.Null(SpeakerAssign.Assign([Cam(0.3, 0.3), Cam(0.3, 0.3)], R, tr));
    }

    sealed class Json2(string json) : IHighlightSelector
    {
        public Func<string, string>? Respond { get; init; }
        public string Name => "fake";
        public string Model => "fake";
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct) => Task.FromResult(Respond?.Invoke(user) ?? json);
    }
}
