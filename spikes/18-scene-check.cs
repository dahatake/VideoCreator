// Spike 18: is the series cut a change of SUBJECT? — duration-first (previous) vs scene-first (embeddings) on a real cached talk, judged by independent evidence.
//  scene <cacheDir> [parts=2,3,4]
//    Cuts the real transcript with (a) SeriesPlanner.SceneFirst=false and no embeddings (the previous planner), (b) scene-first + Microsoft Foundry embeddings (+ AI chapters).
//    For every cut it reports, from sources the planner does not use for the decision:
//      - the cosine between the 90 s of speech before and after, as a percentile among ALL sentence boundaries (raw text-embedding-3-large vectors),
//      - a blind judgement by a different chat model (gpt-4.1; the outline uses the app's chat deployment): the subject of each side and a 0–10 "same subject" score.
//    Writes spikes/out/scene-check/report.json.
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using VideoCreator;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Selection;
using VideoCreator.Signals;

Console.OutputEncoding = Encoding.UTF8;
var root = Directory.GetCurrentDirectory();
var outDir = Path.Combine(root, "spikes", "out", "scene-check"); Directory.CreateDirectory(outDir);
if (args.Length < 2 || args[0] != "scene") { Console.WriteLine("usage: scene <cacheDir> [parts=2,3,4]"); return; }
var cache = args[1];
var partsList = (args.FirstOrDefault(a => a.StartsWith("parts="))?[6..] ?? "2,3,4").Split(',').Select(int.Parse).ToArray();
var endpoint = new Uri("https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1");
var cred = new AzureCliCredential();
var embedder = new AzureOpenAIEmbedder(endpoint, "text-embedding-3-large", cred);
var judge = new AzureOpenAISelector(endpoint, "gpt-4.1", cred);
var outline = new AzureOpenAISelector(endpoint, "gpt-chat-latest", cred);

var tr0 = Json.Deserialize<Transcript>(File.ReadAllText(Directory.GetFiles(cache, "transcript_*.json").OrderByDescending(File.GetLastWriteTimeUtc).First()));
var tr = SplitCues.Resegment(tr0);
var sigFile = new[] { "signals.v2.json", "signals.json" }.Select(f => Path.Combine(cache, f)).First(File.Exists);
var sig = Json.Deserialize<SignalSet>(File.ReadAllText(sigFile));
var laughs = Directory.GetFiles(cache, "laughs_*.json").Select(f => Json.Deserialize<List<Interval>>(File.ReadAllText(f))).FirstOrDefault() ?? [];
var preset = PresetStore.LoadDirectory(Path.Combine(root, "presets")).First(p => p.Id == "P4");

var vecs = (await TopicEmbedding.LoadOrEmbedAsync(embedder, tr, Path.Combine(outDir, "embed.json"), default))!;
var sem = TopicEmbedding.Depth(tr, vecs);
ChapterOutline? ai = null;
try { ai = await SeriesPlanner.OutlineAsync(outline, tr, Console.WriteLine, default); } catch (Exception ex) { Console.WriteLine("AI outline failed: " + ex.Message); }
Console.WriteLine($"sentences {tr.Sentences.Count}, duration {Mmss(tr.Duration)}, AI chapters {(ai?.Chapters.Count.ToString() ?? "none")}");

// cosine of the 90 s before vs after every boundary (raw embedding vectors), for the percentile
var raw = new double[tr.Sentences.Count + 1];
for (int i = 1; i < tr.Sentences.Count; i++) raw[i] = WindowCos(i, 90);

var report = new JsonArray();
// the judge's view of the whole talk: every 5th sentence boundary, scored blind — the base rate a cut has to beat (a random cut is a scene change this often)
var sweep = new Dictionary<int, int>();
var gate = new SemaphoreSlim(6);
await Task.WhenAll(Enumerable.Range(1, tr.Sentences.Count - 1).Where(i => i % 5 == 0 && tr.Sentences[i].T0 > 60 && tr.Duration - tr.Sentences[i].T0 > 60).Select(async i =>
{
    await gate.WaitAsync();
    try { if ((await Judge(Excerpt(i - 1, -90), Excerpt(i, 90)))?["same_subject"]?.GetValue<int>() is int v) lock (sweep) sweep[i] = v; }
    finally { gate.Release(); }
}));
Console.WriteLine($"judge sweep: {sweep.Count} boundaries, mean same_subject {sweep.Values.Average():F1}, rated ≤2 (clear subject change): {100.0 * sweep.Values.Count(v => v <= 2) / sweep.Count:F0}%, rated ≥7 (same subject): {100.0 * sweep.Values.Count(v => v >= 7) / sweep.Count:F0}%");
foreach (var parts in partsList)
    foreach (var (cond, sceneFirst, semantic, o) in new (string, bool, double[]?, ChapterOutline?)[] { ("previous", false, null, SeriesPlanner.FallbackOutline(tr, sig)), ("scene-first", true, sem, ai ?? SeriesPlanner.FallbackOutline(tr, sig, sem)) })
    {
        SeriesPlanner.SceneFirst = sceneFirst;
        var notes = new List<string>();
        var plan = SeriesPlanner.Split(tr, sig, preset, tr.Duration, null, parts, o!, notes, laughs, semantic);
        SeriesPlanner.SceneFirst = true;
        Console.WriteLine($"\n== {parts} parts / {cond}   part lengths {string.Join(" / ", plan.Select(p => Mmss(p.EstSec)))}");
        for (int k = 1; k < plan.Count; k++)
        {
            int c = plan[k].First;
            double cos = raw[c];
            var j = await Judge(Excerpt(c - 1, -90), Excerpt(c, 90));
            Console.WriteLine($"  cut {k} at {Mmss(tr.Sentences[c].T0)} (#{c})  judge same_subject={j?["same_subject"]} {j?["verdict"]}   (embedding cos90 {cos:F3})");
            Console.WriteLine($"     …{Tail(tr.Sentences[c - 1].Text, 36)} ▶ {Head(tr.Sentences[c].Text, 36)}");
            Console.WriteLine($"     before: {j?["subject_before"]}  |  after: {j?["subject_after"]}");
            report.Add(new JsonObject
            {
                ["parts"] = parts, ["condition"] = cond, ["cut"] = k, ["time"] = Math.Round(tr.Sentences[c].T0, 1), ["sentence"] = c, ["cos90"] = Math.Round(cos, 4),
                ["judge"] = j?.DeepClone(), ["part_sec"] = new JsonArray(plan.Select(x => (JsonNode)Math.Round(x.EstSec)).ToArray()),
            });
        }
        foreach (var n in notes.Where(n => n.Contains("途中") || n.Contains("差が目安"))) Console.WriteLine("  note: " + n);
    }
File.WriteAllText(Path.Combine(outDir, "report.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"\nreport: {outDir}");

double WindowCos(int i, double w)
{
    int a = i - 1; while (a > 0 && tr.Sentences[i].T0 - tr.Sentences[a - 1].T0 <= w) a--;
    int b = i; while (b < tr.Sentences.Count - 1 && tr.Sentences[b + 1].T1 - tr.Sentences[i].T0 <= w) b++;
    double[] Mean(int x, int y) { var m = new double[vecs[0].Length]; for (int s = x; s <= y; s++) for (int d = 0; d < m.Length; d++) m[d] += vecs[s][d]; return m; }
    var l = Mean(a, i - 1); var r = Mean(i, b);
    double dot = 0, nl = 0, nr = 0; for (int d = 0; d < l.Length; d++) { dot += l[d] * r[d]; nl += l[d] * l[d]; nr += r[d] * r[d]; }
    return dot / Math.Sqrt(nl * nr);
}

string Excerpt(int from, double sec)
{
    var sb = new StringBuilder(); var s = tr.Sentences;
    if (sec > 0) for (int i = from; i < s.Count && s[i].T0 - s[from].T0 <= sec; i++) sb.Append(s[i].Text);
    else for (int i = from; i >= 0 && s[from].T1 - s[i].T1 <= -sec; i--) sb.Insert(0, s[i].Text);
    return sb.ToString();
}

async Task<JsonNode?> Judge(string a, string b)
{
    const string schema = """{"type":"object","additionalProperties":false,"required":["subject_before","subject_after","same_subject","verdict"],"properties":{"subject_before":{"type":"string"},"subject_after":{"type":"string"},"same_subject":{"type":"integer"},"verdict":{"type":"string","enum":["scene_change","same_scene","unclear"]}}}""";
    const string sys = "あなたは動画編集者です。音声認識の書き起こし2つ（Aは分割点の直前、Bは直後）を読み、それぞれの話題（何について話しているか）を1文で述べ、同じ話題が続いているかを判定してください。言葉の切れ目の良さや長さは判断に含めず、話題だけで判断すること。same_subject は 0（全く別の話題）〜10（同じ話題の続き）。";
    try { return JsonNode.Parse(await judge.CompleteJsonAsync(sys, $"A:\n{a}\n\nB:\n{b}", "scene_judgement", schema, default)); }
    catch (Exception ex) { Console.WriteLine("judge failed: " + ex.Message); return null; }
}

static string Mmss(double t) => $"{(int)(t / 60)}:{t % 60:00.0}";
static string Tail(string s, int n) => s.Length > n ? s[^n..] : s;
static string Head(string s, int n) => s.Length > n ? s[..n] + "…" : s;
