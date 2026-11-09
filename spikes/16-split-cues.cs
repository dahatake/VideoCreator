// Spike 16: where a series split (前編/後編) lands — before vs after SplitCues — on a real cached talk, plus shot/fade detection on a synthetic video.
//  split <cacheDir> [parts=2] [--llm <v1-endpoint> <deployment>]
//    Loads transcript_*.json / signals*.json / laughs_*.json from an app cache dir and runs SeriesPlanner.Split with
//    (a) the original ASR sentences and no cues (the behaviour before this change) and (b) Resegment + SplitCues, with the
//    TextTiling outline and (optionally) the AI outline. Writes the split report to spikes/out/split-cues/report.json, and ±10 s
//    audio clips around every cut (cut_<cond>_<k>.wav) so a person can listen; the part endings are checked independently
//    with a Japanese morphological analyser (janome) by spikes/16-split-cues-check.py.
//  visual
//    Builds a 60 s video from 7 coloured, moving scenes joined by known transitions (hard cut, dissolve, fade through black)
//    with FFmpeg, runs SignalExtractor.DetectShotsAsync and reports recall per transition type (±0.5 s) for scdet alone
//    and scdet + blackdetect.
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using VideoCreator;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Selection;
using VideoCreator.Signals;

Console.OutputEncoding = Encoding.UTF8;
var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var outDir = Path.Combine(root, "spikes", "out", "split-cues"); Directory.CreateDirectory(outDir);
var ff = new FFmpeg(bin, Path.Combine(root, "tools", "fonts"));
string Mmss(double t) => $"{(int)(t / 60)}:{t % 60:00.0}";

if (args.Length > 0 && args[0] == "visual") { await Visual(); return; }
if (args.Length < 2 || args[0] != "split") { Console.WriteLine("usage: split <cacheDir> [parts=N] [--llm <endpoint> <deployment>] | visual"); return; }

var cache = args[1];
int parts = args.FirstOrDefault(a => a.StartsWith("parts=")) is { } pa ? int.Parse(pa[6..]) : 2;
int li = Array.IndexOf(args, "--llm");
var tr0 = Json.Deserialize<Transcript>(File.ReadAllText(Directory.GetFiles(cache, "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).First()));
var sigFile = new[] { "signals.v2.json", "signals.json" }.Select(f => Path.Combine(cache, f)).First(File.Exists);
var sig = Json.Deserialize<SignalSet>(File.ReadAllText(sigFile));
var laughs = Directory.GetFiles(cache, "laughs_*.json").Select(f => Json.Deserialize<List<Interval>>(File.ReadAllText(f))).FirstOrDefault() ?? [];
var preset = PresetStore.LoadDirectory(Path.Combine(root, "presets")).First(p => p.Id == "P4");
var tr1 = SplitCues.Resegment(tr0);
Console.WriteLine($"sentences {tr0.Sentences.Count} → {tr1.Sentences.Count} after Resegment; shots {sig.Shots.Count}, blacks {sig.Blacks.Count}, laughs {laughs.Count} ({Path.GetFileName(sigFile)})");

var outlines = new List<(string Name, ChapterOutline O0, ChapterOutline O1)> { ("tiling", SeriesPlanner.FallbackOutline(tr0, sig), SeriesPlanner.FallbackOutline(tr1, sig)) };
if (li >= 0)
{
    var llm = new AzureOpenAISelector(new Uri(args[li + 1]), args[li + 2], new AzureCliCredential());
    var o0 = await SeriesPlanner.OutlineAsync(llm, tr0, Console.WriteLine, default);
    var o1 = await SeriesPlanner.OutlineAsync(llm, tr1, Console.WriteLine, default);
    if (o0 != null && o1 != null) outlines.Add(("ai", o0, o1)); else Console.WriteLine("AI outline failed");
}

var report = new JsonArray();
var audio = Path.Combine(cache, "audio16k.flac");
foreach (var (name, o0, o1) in outlines)
    foreach (var (cond, tr, o, cues) in new[] { ("before", tr0, o0, false), ("after", tr1, o1, true) })
    {
        SeriesPlanner.CuesEnabled = cues;
        var notes = new List<string>();
        var sp = SeriesPlanner.Split(tr, sig, preset, tr.Duration, null, parts, o, notes, laughs);
        var all = SplitCues.Compute(tr, sig, laughs, sig.Blacks);
        Console.WriteLine($"\n== {name} / {cond}");
        for (int k = 0; k + 1 < sp.Count; k++)
        {
            int c = sp[k + 1].First; var prev = tr.Sentences[c - 1]; var next = tr.Sentences[c];
            var cue = all[c];
            Console.WriteLine($"  cut {k + 1} at {Mmss(sp[k].Body.T1)} (#{c})  closure {cue.Closure:+0.0;-0.0;0}  cont {cue.Continuation:+0.0;-0.0;0}  {(cue.MidThought ? "MID-THOUGHT" : "clean")}");
            Console.WriteLine($"    …{Tail(prev.Text, 40)}");
            Console.WriteLine($"    ▶ {Head(next.Text, 40)}");
            Console.WriteLine($"    {sp[k].Boundary}");
            var clip = Path.Combine(outDir, $"cut_{name}_{cond}_{k + 1}.wav");
            if (File.Exists(audio)) await ff.RunFfmpegAsync(["-y", "-hide_banner", "-loglevel", "error", "-ss", Inv(Math.Max(0, sp[k].Body.T1 - 10)), "-t", "20", "-i", audio, "-af", $"afade=t=out:st=9.8:d=0.2", clip], default);
            report.Add(new JsonObject
            {
                ["outline"] = name, ["condition"] = cond, ["cut"] = k + 1, ["time"] = Math.Round(sp[k].Body.T1, 2), ["sentence"] = c,
                ["prev_end"] = prev.Text, ["next_start"] = next.Text, ["closure"] = cue.Closure, ["continuation"] = cue.Continuation, ["mid_thought"] = cue.MidThought,
                ["part_sec"] = new JsonArray(sp.Select(x => (JsonNode)Math.Round(x.EstSec, 1)).ToArray()), ["boundary"] = sp[k].Boundary,
            });
        }
        foreach (var n in notes.Where(n => n.Contains("途中"))) Console.WriteLine($"  note: {n}");
    }
SeriesPlanner.CuesEnabled = true;
File.WriteAllText(Path.Combine(outDir, "report.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

// every sentence boundary of the resegmented transcript, for the independent POS check of the closure rule
File.WriteAllText(Path.Combine(outDir, "boundaries.json"), new JsonArray(tr1.Sentences.Skip(1).Select((s, i) => (JsonNode)new JsonObject
{
    ["i"] = i + 1, ["prev"] = tr1.Sentences[i].Text, ["next"] = s.Text, ["closure"] = SplitCues.Closure(tr1.Sentences[i].Text), ["continuation"] = SplitCues.Continuation(s.Text),
}).ToArray()).ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"\nreport: {outDir}");

static string Tail(string s, int n) => s.Length > n ? s[^n..] : s;
static string Head(string s, int n) => s.Length > n ? s[..n] + "…" : s;
static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

async Task Visual()
{
    // 7 scenes × 8 s; transitions at the boundaries: cut, dissolve(1 s), fadeblack(1 s), cut, dissolve, fadeblack
    string[] colors = ["0x3050a0", "0xa04030", "0x40a050", "0x909030", "0x8040a0", "0x30a0a0", "0xc08060"];
    string[] kinds = ["cut", "dissolve", "fadeblack", "cut", "dissolve", "fadeblack"];
    var video = Path.Combine(outDir, "transitions.mp4");
    var inputs = new List<string>(); var fc = new StringBuilder();
    for (int i = 0; i < colors.Length; i++)
    {
        inputs.AddRange(["-f", "lavfi", "-i", $"color=c={colors[i]}:s=640x360:r=30:d=8"]);
        inputs.AddRange(["-f", "lavfi", "-i", $"testsrc2=s=200x120:r=30:d=8"]);
        // a moving patch so a scene is not a still frame (scdet must not fire inside a scene)
        fc.Append($"[{2 * i}:v][{2 * i + 1}:v]overlay=x='40+{20 * i}+t*30':y='60+{15 * i}',format=yuv420p,settb=1/30[s{i}];");
    }
    string last = "s0"; double len = 8; var truth = new List<(double T, string Kind)>();
    for (int i = 1; i < colors.Length; i++)
    {
        var kind = kinds[i - 1]; var o = $"x{i}";
        if (kind == "cut") { fc.Append($"[{last}][s{i}]concat=n=2:v=1:a=0,settb=1/30[{o}];"); truth.Add((len, kind)); len += 8; }
        else { double off = len - 1; fc.Append($"[{last}][s{i}]xfade=transition={(kind == "dissolve" ? "dissolve" : "fadeblack")}:duration=1:offset={Inv(off)},settb=1/30[{o}];"); truth.Add((off + 0.5, kind)); len += 7; }
        last = o;
    }
    var fargs = new List<string> { "-y", "-hide_banner", "-loglevel", "error" }; fargs.AddRange(inputs);
    fargs.AddRange(["-filter_complex", fc.ToString().TrimEnd(';'), "-map", $"[{last}]", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", video]);
    var r = await ff.RunFfmpegAsync(fargs, default);
    if (!File.Exists(video) || new FileInfo(video).Length == 0) { Console.WriteLine(r.StdErr); return; }
    var (shots, blacks) = await new SignalExtractor(ff).DetectShotsAsync(video, default);
    Console.WriteLine($"video {Mmss(len)}; truth {string.Join(", ", truth.Select(t => $"{t.Kind}@{t.T:0.0}"))}");
    Console.WriteLine($"scdet shots: {string.Join(", ", shots.Select(s => s.ToString("0.00", CultureInfo.InvariantCulture)))}");
    Console.WriteLine($"blackdetect: {string.Join(", ", blacks.Select(b => $"{b.T0:0.00}-{b.T1:0.00}"))}");
    bool HitScdet(double t) => shots.Any(s => Math.Abs(s - t) <= 0.5);
    bool HitBlack(double t) => blacks.Any(b => b.T1 >= t - 0.5 && b.T0 <= t + 0.5);
    var rows = new JsonArray();
    foreach (var g in truth.GroupBy(t => t.Kind))
    {
        int a = g.Count(t => HitScdet(t.T)), b = g.Count(t => HitScdet(t.T) || HitBlack(t.T));
        Console.WriteLine($"  {g.Key,-9} scdet {a}/{g.Count()}   scdet+blackdetect {b}/{g.Count()}");
        rows.Add(new JsonObject { ["kind"] = g.Key, ["n"] = g.Count(), ["scdet"] = a, ["scdet_black"] = b });
    }
    int fpScdet = shots.Count(s => truth.All(t => Math.Abs(s - t.T) > 1.0)), fpBlack = blacks.Count(b => truth.All(t => !(b.T1 >= t.T - 0.5 && b.T0 <= t.T + 0.5)));
    Console.WriteLine($"  false positives: scdet {fpScdet}, blackdetect {fpBlack}");
    File.WriteAllText(Path.Combine(outDir, "visual.json"), new JsonObject { ["truth"] = new JsonArray(truth.Select(t => (JsonNode)new JsonObject { ["t"] = t.T, ["kind"] = t.Kind }).ToArray()), ["rows"] = rows, ["fp_scdet"] = fpScdet, ["fp_black"] = fpBlack }.ToJsonString());
}
