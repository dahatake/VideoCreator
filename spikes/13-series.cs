// Spike 13: series mode (前編/後編) end to end on a real 30-minute talk, with independent checks.
//  1. Drives the running app (http://127.0.0.1:8765): project → asset → rights → job (P4, 15 min per part) → export every part.
//  2. Ground truth = the topic starts of the script that generated the video (spikes/07-make-long.cs: every new
//     segment begins with 「続いての企画は…」), located in time by the transcript. Compares the app's split with a
//     clock split (15:00) and with ablations (no AI; no AI and no topic-change words), and scores the chapters (Pk / WindowDiff).
//  3. Measures the exported files with ffprobe and the EBU R128 meter (ebur128), and re-transcribes them with
//     Azure AI Speech to check that the body speech is really in the output and the structure (cold open, recap, next) is where it should be.
// Usage (app running): dotnet run spikes/13-series.cs [path-to-video] [minutes-per-part]
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using VideoCreator;
using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Selection;
using VideoCreator.Signals;

var root = Directory.GetCurrentDirectory();
var video = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root, "spikes", "media", "long30.mp4"));
double minutes = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 15;
var outDir = Path.Combine(root, "spikes", "out", "series"); Directory.CreateDirectory(outDir);
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }
var report = new JsonObject { ["video"] = video, ["minutes_per_part"] = minutes };

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8765/"), Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
async Task<JsonNode> Api(HttpMethod m, string path, object? body = null)
{
    using var req = new HttpRequestMessage(m, path) { Content = body == null ? null : JsonContent.Create(body) };
    using var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode) throw new Exception($"{m} {path}: {(int)res.StatusCode} {text}");
    return JsonNode.Parse(text)!;
}

// ---------------- 1. run the app ----------------
var sw = Stopwatch.StartNew();
var project = await Api(HttpMethod.Post, "api/projects", new { name = $"前後編E2E {DateTime.Now:MMdd-HHmm}" });
var pid = project["id"]!.GetValue<string>();
var asset = await Api(HttpMethod.Post, $"api/projects/{pid}/assets", new { path = video });
var aid = asset["id"]!.GetValue<string>();
await Api(HttpMethod.Post, $"api/projects/{pid}/rights", new { asset_id = aid, confirmed = true });
var start = await Api(HttpMethod.Post, $"api/projects/{pid}/jobs", new { preset_ids = new[] { "P4" }, options = new { part_minutes = minutes } });
var jobId = start["job_id"]!.GetValue<string>();
Console.WriteLine($"job {jobId} (estimate ¥{start["cost"]?["estimated_jpy"]})");
JsonNode job;
string last = "";
while (true)
{
    job = await Api(HttpMethod.Get, $"api/jobs/{jobId}");
    var st = job["status"]!.GetValue<string>();
    var msg = $"{st} {job["message"]}";
    if (msg != last) { Console.WriteLine($"  [{sw.Elapsed:mm\\:ss}] {msg}"); last = msg; }
    if (st is "ready" or "failed" or "cancelled") break;
    await Task.Delay(3000);
}
Check(job["status"]!.GetValue<string>() == "ready", $"job finished: {job["message"]}");
if (job["status"]!.GetValue<string>() != "ready") return 1;
report["job"] = JsonNode.Parse(job.ToJsonString());
foreach (var w in job["warnings"]!.AsArray()) Console.WriteLine($"      warning: {w}");
var cands = (await Api(HttpMethod.Get, $"api/jobs/{jobId}/candidates")).AsArray().Select(x => x!).ToList();
var tr = Json.Deserialize<Transcript>((await Api(HttpMethod.Get, $"api/assets/{aid}/transcript")).ToJsonString());
Console.WriteLine($"transcript: {tr.Sentences.Count} sentences, {tr.Duration / 60:F1} min");

// ---------------- 2. split quality against the script's topic starts ----------------
string Norm(string s) => new(s.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray());
var truth = tr.Sentences.Where(s => s.Index == 0 || Norm(s.Text).StartsWith("続いての企画", StringComparison.Ordinal)).Select(s => s.Index).ToList();
Console.WriteLine($"ground truth: {truth.Count} segments (topic starts at {string.Join(", ", truth.Select(i => Clock(tr.Sentences[i].T0)))})");
static string Clock(double t) => $"{(int)(t / 60)}:{(int)(t % 60):D2}";
double Nearest(double t) => truth.Min(i => Math.Abs(tr.Sentences[i].T0 - t));

var parts = cands.Select(c => c["candidate"]!["series"]!).ToList();
Check(parts.Count == (int)Math.Round(tr.Duration / 60 / minutes), $"{parts.Count} parts for {tr.Duration / 60:F1} min at {minutes} min per part ({string.Join("・", parts.Select(p => p["label"]))})");
var appCuts = parts.Skip(1).Select(p => p["body"]!["t0"]!.GetValue<double>()).ToList();
foreach (var (cut, k) in appCuts.Select((c, k) => (c, k)))
{
    var firstSentence = tr.Sentences.First(s => s.T0 >= cut - 0.01);
    bool onTopic = truth.Contains(firstSentence.Index);
    Check(onTopic, $"app split {k + 1} at {Clock(cut)} starts a new topic (「{firstSentence.Text[..Math.Min(20, firstSentence.Text.Length)]}」, nearest true topic start {Nearest(firstSentence.T0):F1}s away)");
}
// clock split for comparison: what an exact 0:00–15:00 / 15:00–30:00 split does
var clockCuts = Enumerable.Range(1, parts.Count - 1).Select(k => tr.Duration * k / parts.Count).ToList();
foreach (var c in clockCuts)
{
    var inWord = tr.Words.FirstOrDefault(w => w.T0 < c && w.T1 > c);
    var sentence = tr.Sentences.FirstOrDefault(s => s.T0 < c && s.T1 > c);
    Console.WriteLine($"      clock split at {Clock(c)}: nearest topic start {Nearest(c):F1}s away; {(inWord != null ? $"cuts inside the word 「{inWord.Text}」" : sentence != null ? "cuts inside a sentence" : "between sentences")}");
    report["clock_split_offset_sec"] = Math.Round(Nearest(c), 1);
}

var preset = PresetStore.LoadDirectory(Path.Combine(root, "presets")).Single(p => p.Id == "P4");
var sig = new SignalSet { Loudness100Ms = Enumerable.Repeat(-25.0, (int)(tr.Duration * 10) + 10).ToList(), MedianLoudness = -25 };
var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "src", "VideoCreator.App", "appsettings.json")))!["VideoCreator"]!;
var llm = new AzureOpenAISelector(new Uri(settings["Llm"]!["Endpoint"]!.GetValue<string>()), settings["Llm"]!["Deployment"]!.GetValue<string>(), new DefaultAzureCredential());
var stripped = tr with { Sentences = tr.Sentences.Select(s => s with { Text = System.Text.RegularExpressions.Regex.Replace(s.Text, "^続いての企画は、?", "") }).ToList() };
var variants = new List<(string name, Transcript t, ChapterOutline o)>
{
    ("AI の章分け（アプリと同じ）", tr, await SeriesPlanner.OutlineAsync(llm, tr, Console.WriteLine, CancellationToken.None) ?? throw new Exception("outline failed")),
    ("AI なし（語彙の変化＋間＋話題転換語）", tr, SeriesPlanner.FallbackOutline(tr, sig)),
    ("AI なし・話題転換語も除去（語彙の変化＋間のみ）", stripped, SeriesPlanner.FallbackOutline(stripped, sig)),
};
// Pk / WindowDiff (Beeferman 1999; Pevzner & Hearst 2002) of the chapters against the true segments, over sentence indices
var n = tr.Sentences.Count;
int[] Seg(IEnumerable<int> starts) { var st = starts.Where(x => x > 0).ToHashSet(); var a = new int[n]; int id = 0; for (int i = 0; i < n; i++) { if (st.Contains(i)) id++; a[i] = id; } return a; }
var refSeg = Seg(truth); int kw = Math.Max(2, (int)Math.Round(n / (double)truth.Count / 2));
(double pk, double wd) Score(int[] hyp)
{
    int pkErr = 0, wdErr = 0, cnt = n - kw;
    for (int i = 0; i < cnt; i++)
    {
        if ((refSeg[i] == refSeg[i + kw]) != (hyp[i] == hyp[i + kw])) pkErr++;
        if (refSeg[i + kw] - refSeg[i] != hyp[i + kw] - hyp[i]) wdErr++;
    }
    return ((double)pkErr / cnt, (double)wdErr / cnt);
}
var variantsReport = new JsonArray();
foreach (var (name, t, o) in variants)
{
    var cuts = SeriesPlanner.Split(t, sig, preset, tr.Duration, minutes * 60, o, []).Skip(1).Select(p => p.First).ToList();
    var (pk, wd) = Score(Seg(o.Chapters.Select(c => c.Start)));
    int hits = cuts.Count(truth.Contains);
    Console.WriteLine($"      {name}: split at {string.Join(", ", cuts.Select(c => Clock(tr.Sentences[c].T0)))} → on a true topic start {hits}/{cuts.Count}; chapters {o.Chapters.Count} (truth {truth.Count}) Pk={pk:F3} WindowDiff={wd:F3}");
    variantsReport.Add(new JsonObject { ["name"] = name, ["cuts"] = new JsonArray(cuts.Select(c => (JsonNode)Clock(tr.Sentences[c].T0)).ToArray()), ["on_topic"] = hits, ["chapters"] = o.Chapters.Count, ["pk"] = Math.Round(pk, 3), ["windowdiff"] = Math.Round(wd, 3) });
    if (name.StartsWith("AI の")) Check(pk < 0.25, $"AI chapters agree with the script's segments (Pk {pk:F3} < 0.25; random ≈ 0.5)");
}
report["variants"] = variantsReport;
// coverage: every word in exactly one body
var bodies = parts.Select(p => (t0: p["body"]!["t0"]!.GetValue<double>(), t1: p["body"]!["t1"]!.GetValue<double>())).ToList();
var multi = tr.Words.Count(w => bodies.Count(b => w.T0 >= b.t0 - 1e-6 && w.T1 <= b.t1 + 1e-6) != 1);
Check(multi == 0, $"every word ({tr.Words.Count}) is in exactly one part body (violations: {multi})");

// ---------------- 3. export every part and measure the files independently ----------------
var exports = new JsonArray();
var asr = new AzureFastTranscriber(new Uri(settings["Asr"]!["Endpoint"]!.GetValue<string>()), new DefaultAzureCredential(), new HttpClient { Timeout = TimeSpan.FromMinutes(15) });
string? RunTool(string exe, params string[] a)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, exe)) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var x in a) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!; var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEnd(); p.WaitForExit();
    return o.Result + e;
}
static Dictionary<string, int> Bigrams(string s) { var d = new Dictionary<string, int>(); for (int i = 0; i + 1 < s.Length; i++) { var b = s.Substring(i, 2); d[b] = d.GetValueOrDefault(b) + 1; } return d; }
static double Recall(string src, string hyp) { var a = Bigrams(src); var b = Bigrams(hyp); int tot = a.Values.Sum(); return tot == 0 ? 1 : (double)a.Sum(kv => Math.Min(kv.Value, b.GetValueOrDefault(kv.Key))) / tot; }
var outputs = new List<Transcript>();
foreach (var c in cands)
{
    var cand = c["candidate"]!; var se = cand["series"]!; var label = se["label"]!.GetValue<string>();
    var ep = cand["edit_plan_id"]!.GetValue<string>();
    var plan = await Api(HttpMethod.Get, $"api/editplans/{ep}");
    var hardFails = plan["qa"]!.AsArray().Where(q => q!["check_id"]!.GetValue<string>().StartsWith("H-") && q!["status"]!.GetValue<string>() == "fail").Select(q => q!["check_id"]!.ToString()).ToList();
    Check(hardFails.Count == 0, $"{label}: plan QA has no hard failures {string.Join(",", hardFails)}");
    var t0 = sw.Elapsed;
    var rid = (await Api(HttpMethod.Post, $"api/editplans/{ep}/render"))["render_id"]!.GetValue<string>();
    JsonNode r;
    while (true) { r = await Api(HttpMethod.Get, $"api/renders/{rid}"); var st = r["state"]!.GetValue<string>(); if (st is "done" or "failed") break; await Task.Delay(5000); }
    Check(r["state"]!.GetValue<string>() == "done", $"{label}: export {r["state"]} in {(sw.Elapsed - t0).TotalSeconds:F0}s {r["message"]}");
    if (r["state"]!.GetValue<string>() != "done") { fails++; continue; }
    var render = r["render"]!;
    var mp4 = render["path"]!.GetValue<string>();
    foreach (var q in render["qa"]!.AsArray()) Console.WriteLine($"      {q!["check_id"]} {q["status"],-4} {q["measured"]} ({q["threshold"]})");
    // ffprobe + EBU R128 (ebur128 filter, a different meter from the loudnorm used to produce the file)
    var probe = RunTool("ffprobe.exe", "-v", "error", "-show_entries", "stream=width,height:format=duration", "-of", "json", mp4)!;
    var pj = JsonNode.Parse(probe[..(probe.LastIndexOf('}') + 1)])!;
    double dur = double.Parse(pj["format"]!["duration"]!.GetValue<string>(), CultureInfo.InvariantCulture);
    var vs = pj["streams"]!.AsArray().First(s => s!["width"] != null)!;
    var eb = RunTool("ffmpeg.exe", "-hide_banner", "-nostats", "-i", mp4, "-vn", "-af", "ebur128=peak=true", "-f", "null", "-")!;
    var I = double.Parse(System.Text.RegularExpressions.Regex.Matches(eb, @"I:\s+(-?[\d.]+) LUFS").Last().Groups[1].Value, CultureInfo.InvariantCulture);
    var tp = double.Parse(System.Text.RegularExpressions.Regex.Matches(eb, @"Peak:\s+(-?[\d.]+) dBFS").Last().Groups[1].Value, CultureInfo.InvariantCulture);
    double min = se["min_sec"]!.GetValue<double>(), max = se["max_sec"]!.GetValue<double>();
    Check($"{vs["width"]}x{vs["height"]}" == "1080x1920", $"{label}: ffprobe {vs["width"]}x{vs["height"]}");
    Check(dur >= min && dur <= max, $"{label}: ffprobe duration {Clock(dur)} within {Clock(min)}–{Clock(max)} (requested {minutes} min)");
    Check(Math.Abs(I + 14) <= 1 && tp <= -1.0, $"{label}: ebur128 integrated {I:F1} LUFS, true peak {tp:F1} dBTP");
    // re-transcribe the output
    var flac = Path.Combine(outDir, $"{label}.flac");
    RunTool("ffmpeg.exe", "-hide_banner", "-loglevel", "error", "-y", "-i", mp4, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "flac", flac);
    var otr = await asr.TranscribeAsync(flac, dur, [], 2, CancellationToken.None);
    outputs.Add(otr);
    File.WriteAllText(Path.Combine(outDir, $"{label}_retranscript.json"), Json.Serialize(otr));
    double b0 = se["body"]!["t0"]!.GetValue<double>(), b1 = se["body"]!["t1"]!.GetValue<double>();
    var bodyText = Norm(string.Concat(tr.Sentences.Where(s => s.T0 >= b0 - 0.01 && s.T1 <= b1 + 0.01).Select(s => s.Text)));
    var outText = Norm(otr.FullText);
    double recall = Recall(bodyText, outText);
    Check(recall >= 0.9, $"{label}: re-transcribed output contains the body speech (character-bigram recall {recall:P1}; ASR noise included)");
    // structure: what is heard first / last
    var planNode = plan["plan"]!;
    var tl = planNode["timeline"]!.AsArray();
    string SrcText(JsonNode clip) { double a = clip["audio_in"]?.GetValue<double>() ?? clip["src_in"]!.GetValue<double>(); double len = clip["src_out"]!.GetValue<double>() - clip["src_in"]!.GetValue<double>(); return Norm(string.Concat(tr.Sentences.Where(s => s.T0 >= a - 0.3 && s.T1 <= a + len + 0.3).Select(s => s.Text))); }
    string OutText(double from, double to) => Norm(string.Concat(otr.Sentences.Where(s => (s.T0 + s.T1) / 2 >= from && (s.T0 + s.T1) / 2 <= to).Select(s => s.Text)));
    double off = 0;
    foreach (var clip in tl)
    {
        var role = clip!["role"]!.GetValue<string>(); double len = (clip["src_out"]!.GetValue<double>() - clip["src_in"]!.GetValue<double>()) / (clip["speed"]?.GetValue<double>() ?? 1);
        if (role is "teaser" or "recap" or "next")
        {
            var heard = OutText(off - 0.3, off + len + 0.3); var expected = SrcText(clip);
            double rr = Recall(expected, heard);
            Check(rr >= 0.6, $"{label}: {role} at {Clock(off)} is heard in the output (recall {rr:P0}: 「{expected[..Math.Min(18, expected.Length)]}」)");
        }
        off += len;
    }
    exports.Add(new JsonObject { ["label"] = label, ["file"] = mp4, ["duration"] = Math.Round(dur, 2), ["lufs"] = I, ["true_peak"] = tp, ["body_recall"] = Math.Round(recall, 3), ["render_seconds"] = Math.Round((sw.Elapsed - t0).TotalSeconds) });
    var md = render["outputs"]?["metadata"]?.GetValue<string>();
    if (md != null) { var text = File.ReadAllText(md); Check(!text.Contains("#Shorts") && text.Contains("00:00 "), $"{label}: metadata has chapters from 00:00 and no #Shorts ({Path.GetFileName(md)})"); }
}
report["exports"] = exports;
File.WriteAllText(Path.Combine(outDir, "series_e2e.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"total {sw.Elapsed:mm\\:ss}, {fails} failure(s) → spikes/out/series/series_e2e.json");
return fails == 0 ? 0 : 1;
