// Spike 20: scene-matched BGM and speech-keyed ducking on REAL recorded speech, with independent checks.
//  1. Takes a talk the app has already analysed (DataDir/cache/<sha>/: audio16k.flac + ASR transcript + signals + laughter) and builds a 90 s edit
//     from three far-apart 30 s stretches (two topic jumps = scene boundaries).
//  2. Plans the music with Planning.BgmPlanner from the measured library tracks: scenes, scene energy, chosen track and why, duck depths.
//  3. Renders the dialogue and music stems exactly as the app does and measures them with FFmpeg's ebur128 (Renderer.MixReport): speech − music
//     while speaking (target 14 LU, research floor 10 LU), and the music level in pauses.
//  4. Independent intelligibility check: Azure AI Speech re-transcribes the dialogue alone, the ducked mix and an un-ducked mix (music at the
//     pause level all the time); char-bigram recall of each against the dialogue-alone transcript.
// Usage: dotnet run spikes/20-bgm-scene.cs [cache-dir]
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Identity;
using VideoCreator;
using VideoCreator.Asr;
using VideoCreator.Audio;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Rendering;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var ff = FFmpeg.Locate(start: root);
var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoCreator", "cache");
var cache = args.Length > 0 ? args[0] : Directory.GetDirectories(cacheRoot)
    .Where(d => File.Exists(Path.Combine(d, "audio16k.flac")) && Directory.GetFiles(d, "transcript_*.json").Length > 0)
    .OrderByDescending(d => new FileInfo(Path.Combine(d, "audio16k.flac")).Length).First();
var tr = Json.Deserialize<Transcript>(File.ReadAllText(Directory.GetFiles(cache, "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).First()));
var sigFile = new[] { "signals.v2.json", "signals.json" }.Select(f => Path.Combine(cache, f)).FirstOrDefault(File.Exists);
var sig = sigFile != null ? Json.Deserialize<SignalSet>(File.ReadAllText(sigFile)) : new SignalSet();
var laughFile = Directory.GetFiles(cache, "laughs_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
var laughs = laughFile != null ? Json.Deserialize<List<Interval>>(File.ReadAllText(laughFile)) : [];
var flac = Path.Combine(cache, "audio16k.flac");
Console.WriteLine($"source: {cache} ({tr.Duration / 60:F1} min, {tr.Words.Count} words, {tr.Sentences.Count} sentences)");

// three ~30 s stretches starting at sentence starts near 15 %, 50 % and 85 % of the talk
var clips = new List<Clip>();
foreach (var (f, i) in new[] { 0.15, 0.5, 0.85 }.Select((f, i) => (f, i)))
{
    var s0 = tr.Sentences.First(s => s.T0 >= tr.Duration * f);
    var s1 = tr.Sentences.Last(s => s.T1 <= s0.T0 + 31);
    clips.Add(new Clip { ClipId = $"c{i + 1}", AssetId = "a1", SrcIn = Math.Max(0, s0.T0 - 0.15), SrcOut = s1.T1 + 0.25 });
}
double body = clips.Sum(c => c.Length);
var work = Path.Combine(Path.GetTempPath(), "vc-spike20"); Directory.CreateDirectory(work);
var lib = await new AudioLibrary(ff, Path.Combine(work, "lib")).MeasuredTracksAsync();
foreach (var t in lib) Console.WriteLine($"track {t.Id}: {t.Analysis!.Lufs:F1} LUFS, LRA {t.Analysis.Lra:F1}, {t.Analysis.Bpm:F0} BPM, attacks {t.Analysis.OnsetRate:F1}/s, centroid {t.Analysis.Brightness:F0} Hz → energy {t.Analysis.Energy:F2}");

var preset = PresetStore.LoadDirectory(Path.Combine(root, "presets")).Single(p => p.Id == "P3");
var notes = new List<string>();
var audio = BgmPlanner.Build(new AudioSpec { Master = "a1", SfxEnabled = false }, clips, [], null, tr, sig, laughs, "scene", lib, 0, preset.Audio.BgmGapLu, preset.Audio.BgmSpeechLd, notes);
audio = BgmPlanner.Recompute(audio, [], body, id => lib.FirstOrDefault(t => t.Id == id));
foreach (var n in notes) Console.WriteLine("note: " + n);
foreach (var c in audio.BgmCues) Console.WriteLine($"scene {c.T0,6:F1}–{c.T1,6:F1}s energy {c.SceneEnergy:F2} [{c.Scene}] → {c.Track} ({c.Reason})");
var depths = audio.DuckSpans.Select(d => d.Db).OrderBy(x => x).ToList();
Console.WriteLine($"duck spans: {audio.DuckSpans.Count}, speech covers {audio.DuckSpans.Sum(d => d.T1 - d.T0) / body:P0} of the edit, depth {depths.First():F1}–{depths.Last():F1} dB (median {depths[depths.Count / 2]:F1})");

var plan = new EditPlan { EditplanId = "spike20", ProjectId = "p", Preset = preset.Ref, Timeline = clips, Audio = audio };
var asset = new SourceAsset { Id = "a1", ProjectId = "p", Path = flac, Sha256 = "x", Media = new MediaInfo { Duration = tr.Duration, AudioChannels = 1, Width = 0, Height = 0, Fps = 30 } };
var assets = new Dictionary<string, SourceAsset> { ["a1"] = asset };
var r = new Renderer(ff) { BgmResolver = id => lib.FirstOrDefault(t => t.Id == id)?.Path };
// the gain envelope is written with the inputs, so every variant gets its own
Renderer.Inputs Inputs(EditPlan p) => r.ResolveInputs(p, assets, Path.Combine(work, $"env-{Guid.NewGuid():N}.wav"));
var inp = Inputs(plan);
var dm = await ff.RunFfmpegAsync(["-hide_banner", .. inp.Flat, "-filter_complex", Renderer.BuildDialogueGraph(plan, inp), "-map", "[m]", "-f", "null", "-"]);
double speechI = double.Parse(Regex.Matches(dm.StdErr, @"I:\s+(-?[\d.]+) LUFS")[^1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
plan = plan with { Audio = plan.Audio with { SpeechLufs = speechI } };
Console.WriteLine($"edited dialogue: {speechI:F1} LUFS (planning estimate from the 100 ms signal: {audio.SpeechLufs:F1})");

async Task<BgmMixReport> Measure(EditPlan p)
{
    var inp = Inputs(p);
    var st = await ff.RunFfmpegAsync(["-hide_banner", "-nostats", .. inp.Flat, "-filter_complex", Renderer.BuildStemGraph(p, inp), "-map", "[d]", "-f", "null", "-", "-map", "[mu]", "-f", "null", "-"]);
    return Renderer.MixReport(p, BgmAnalyzer.ParseFrames(st.StdErr, "dlg"), BgmAnalyzer.ParseFrames(st.StdErr, "mus"));
}
var noDuck = plan with { Audio = plan.Audio with { Duck = false, DuckSpans = plan.Audio.DuckSpans.Select(d => d with { Db = 0 }).ToList() } };
var rep = await Measure(plan); var repNo = await Measure(noDuck);
Console.WriteLine($"ducked:    LD {rep.Ld:F1} LU (10th percentile of 400 ms blocks {rep.LdP10:F1}), music in pauses {rep.GapLu:F1} LU vs speech (target {plan.Audio.BgmGapLu}), {rep.SpeechBlocks} speech blocks");
Console.WriteLine($"no ducking: LD {repNo.Ld:F1} LU (10th percentile {repNo.LdP10:F1})");
bool ok = rep.Ld >= Math.Max(BgmPlanner.ResearchFloorLd, plan.Audio.BgmSpeechLd - 1.5);
Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] speech over music ≥ target {plan.Audio.BgmSpeechLd} − 1.5 and ≥ {BgmPlanner.ResearchFloorLd} LU");

// independent: what a recogniser hears
var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "src", "VideoCreator.App", "appsettings.json")))!["VideoCreator"]!;
var asr = new AzureFastTranscriber(new Uri(settings["Asr"]!["Endpoint"]!.GetValue<string>()), new DefaultAzureCredential(), new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
async Task<string> Hear(EditPlan p, string name)
{
    var f = Path.Combine(work, name + ".flac");
    var inp = Inputs(p);
    await ff.RunFfmpegCheckedAsync(["-hide_banner", "-loglevel", "error", "-y", .. inp.Flat, "-filter_complex", Renderer.BuildAudioOnlyGraph(p, inp, $"loudnorm=I=-14:TP=-1.5,aresample=16000,aformat=channel_layouts=mono"), "-map", "[m]", f]);
    var t = await asr.TranscribeAsync(f, body, [], 2, CancellationToken.None);
    return Regex.Replace(string.Concat(t.Sentences.Select(s => s.Text)), @"[\s、。,.!?！？「」]", "");
}
static Dictionary<string, int> Bigrams(string s) { var d = new Dictionary<string, int>(); for (int i = 0; i + 1 < s.Length; i++) { var b = s.Substring(i, 2); d[b] = d.GetValueOrDefault(b) + 1; } return d; }
static double Recall(string src, string hyp) { var a = Bigrams(src); var b = Bigrams(hyp); int tot = a.Values.Sum(); return tot == 0 ? 1 : (double)a.Sum(kv => Math.Min(kv.Value, b.GetValueOrDefault(kv.Key))) / tot; }
var dialogue = await Hear(plan with { Audio = plan.Audio with { Bgm = null } }, "dialogue");
var ducked = await Hear(plan, "ducked");
var plain = await Hear(noDuck, "noduck");
double rd = Recall(dialogue, ducked), rn = Recall(dialogue, plain);
Console.WriteLine($"ASR char-bigram recall vs dialogue alone ({dialogue.Length} chars): ducked mix {rd:P1}, un-ducked mix {rn:P1}");
Console.WriteLine($"[{(rd >= 0.9 ? "PASS" : "FAIL")}] the ducked mix keeps ≥ 90 % of what the recogniser hears in the dialogue alone");
