// Spike 26: render P12 (vertical short) from fixed inputs, without the app, the LLM or Azure — so the same edit can be rendered
// before and after a change to the look and measured with spikes/25-shorts-style.cs.
//  talk   : spikes/media/talk.mp4 (two speakers over a colour-bar test pattern: worst case for coloured telops) + the test fixture transcript.
//  motiv  : a real one-person vertical recording with its cached transcript, faces and signals from the local data folder (optional).
// Usage: dotnet run spikes/26-short-render.cs <out-dir> [P12] [tsukkomi]
//  tsukkomi | cards: turn on AI tsukkomi (as overlay telops | as pause cards) with a fixed stub (the same three lines on the 3rd, 7th and 11th caption) so two checkouts render the same edit.
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using VideoCreator;
using VideoCreator.Asr;
using VideoCreator.Audio;
using VideoCreator.Faces;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root, "spikes", "out", "short"));
var presetId = args.Length > 1 ? args[1] : "P12";
bool withTsukkomi = args.Contains("tsukkomi") || args.Contains("cards");
Directory.CreateDirectory(outDir);
var ff = FFmpeg.Locate(start: root);
var preset = PresetStore.LoadDirectory(Path.Combine(root, "presets")).Single(p => p.Id == presetId);
var audio = new AudioLibrary(ff, Path.Combine(outDir, "audio"));
await audio.EnsureAsync();
var renderer = new Renderer(ff) { SfxResolver = k => audio.SfxPath(k) };
var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoCreator", "cache");

var cases = new List<(string Name, string Video, Transcript Tr, SignalSet Sig, List<FaceSample> Faces, Candidate Cand)>();
{
    var tr = AzureFastTranscriber.Parse(File.ReadAllText(Path.Combine(root, "tests", "VideoCreator.Tests", "Fixtures", "talk_transcript.json")), 125, "fixture");
    var sig = new SignalSet
    {
        Shots = tr.Sentences.Select(s => Math.Round(s.T0 - 0.05, 2)).ToList(),
        Loudness100Ms = Enumerable.Repeat(-25.0, (int)(tr.Duration * 10) + 10).ToList(), MedianLoudness = -25,
    };
    cases.Add(("talk", Path.Combine(root, "spikes", "media", "talk.mp4"), tr, sig, [],
        new Candidate { Id = "c_talk", Segments = [new Interval(8.8, 40)], HookText = "帰れません", Headline = "100円のものだけ", EmphasisWords = ["1万円", "100円のものだけ", "百個"] }));
}
var motiv = Directory.Exists(@"C:\Work\VideoCreator") ? Directory.GetFiles(@"C:\Work\VideoCreator", "MOTIV_*.mov", SearchOption.AllDirectories).FirstOrDefault() : null;
{
    // duo: a landscape group shot (two public-domain NASA portraits from spikes/faces side by side) with the two-speaker talk audio —
    // the usual source of a short (a horizontal talk recording made vertical); faces come from the app's own detector
    var duo = Path.Combine(outDir, "duo-source.mp4");
    if (!File.Exists(duo))
    {
        var f = Path.Combine(root, "spikes", "faces");
        var r = await ff.RunFfmpegAsync(["-v", "error", "-y", "-loop", "1", "-i", Path.Combine(f, "face1.jpg"), "-loop", "1", "-i", Path.Combine(f, "face3.jpg"), "-i", cases[0].Video,
            "-filter_complex", "[0]scale=960:-2,crop=960:1080:0:60,setsar=1[a];[1]scale=960:-2,crop=960:1080:0:60,setsar=1[b];[a][b]hstack,format=yuv420p,fps=30,noise=alls=6:allf=t[v]",
            "-map", "[v]", "-map", "2:a", "-t", "124.9", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-c:a", "aac", duo], CancellationToken.None);
        if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
    }
    var fs = new FaceService(ff, Path.Combine(root, "tools", "models", "ultraface.onnx"));
    var samples = await fs.AnalyzeAsync(duo, [new Interval(0, 124.9)], Path.Combine(outDir, "duo-faces.json"), CancellationToken.None);
    cases.Add(("duo", duo, cases[0].Tr, cases[0].Sig, samples, cases[0].Cand with { Id = "c_duo" }));
}
var motivCache = Path.Combine(data, "799b8a81caa37be4");
if (motiv != null && File.Exists(Path.Combine(motivCache, "transcript_1db6df222835.json")))
{
    var tr = Json.Deserialize<Transcript>(File.ReadAllText(Path.Combine(motivCache, "transcript_1db6df222835.json")));
    var sig = Json.Deserialize<SignalSet>(File.ReadAllText(Path.Combine(motivCache, "signals.v2.json")));
    cases.Add(("motiv", motiv, tr, sig, FaceService.Load(Path.Combine(motivCache, "faces.json")),
        new Candidate { Id = "c_motiv", Segments = [new Interval(1.5, 33.0)], HookText = "日本語だよ", Headline = "音が取れているはず", EmphasisWords = ["画面共有", "日本語", "バーン"] }));
}

foreach (var c in cases)
{
    var media = await new MediaProbe(ff).ProbeAsync(c.Video);
    var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = c.Video, Sha256 = "x", Role = AssetRole.Main, Media = media };
    var job = new Job { Id = "j_" + c.Name, ProjectId = "p", PresetIds = [preset.Id], Options = new JobOptions { Bgm = "none", Tsukkomi = withTsukkomi, TsukkomiStyle = args.Contains("cards") ? "card" : "telop" } };
    var faces = new Dictionary<string, List<FaceSample>>();
    // no detector data for the test pattern: the face analysis of a faceless video (a sample every 0.5 s, no boxes)
    faces["a1"] = c.Faces.Count > 0 ? c.Faces : Enumerable.Range(0, (int)(media.Duration * 2) + 1).Select(i => new FaceSample(i * 0.5, [])).ToList();
    var (plan, notes) = await PlanBuilder.BuildAsync(new PlanRequest(c.Cand, c.Tr, c.Sig, preset, [asset], asset, "p", job, faces, null, withTsukkomi ? new FixedTsukkomi() : null), CancellationToken.None);
    foreach (var n in notes) Console.WriteLine($"  [{c.Name}] {n}");
    foreach (var t in plan.Telops.Where(t => t.Type is TelopType.Headline or TelopType.Hook))
    {
        var obs = TelopLayout.Obstacles(plan, t, out bool known);
        Console.WriteLine($"  [{c.Name}] {t.Type} {t.Start:F1}-{t.End:F1}s faces: {(obs.Count == 0 ? "none" : string.Join(" ", obs.Select(o => $"y{o.Top:F0}-{o.Bottom:F0} x{o.Left:F0}-{o.Right:F0}")))} known={known}");
    }
    File.WriteAllText(Path.Combine(outDir, c.Name + ".plan.json"), Json.Serialize(plan));
    var outFile = Path.Combine(outDir, c.Name + ".mp4");
    var r = await renderer.RenderAsync(plan, preset, new Dictionary<string, SourceAsset> { ["a1"] = asset }, outFile, preview: false, CancellationToken.None);
    var types = plan.Telops.Where(t => t.Enabled).GroupBy(t => t.Type).Select(g => $"{g.Key}:{g.Count()}");
    Console.WriteLine($"{c.Name}: {plan.Duration:F1}s, {plan.Timeline.Count} clips, telops {string.Join(" ", types)}, sfx {plan.Audio.Sfx.Count}, I {r.LoudnessI:F1} LUFS → {outFile}");
}

/// <summary>Deterministic "LLM" for the tsukkomi request: the same three lines on the 3rd, 7th and 11th caption of the prompt.</summary>
sealed class FixedTsukkomi : VideoCreator.Selection.IHighlightSelector
{
    public string Name => "fixed";
    public string Model => "fixed";
    public Task<VideoCreator.Selection.LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
    public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
    {
        var ids = System.Text.RegularExpressions.Regex.Matches(user, @"^(t\d+):", System.Text.RegularExpressions.RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        string[] lines = ["全員迷子。", "話が進まない。", "準備ゼロ。"];
        var items = new[] { 2, 6, 10 }.Where(i => i < ids.Count).Select((i, k) => new { caption_id = ids[i], text = lines[k] });
        return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new { items }));
    }
}
