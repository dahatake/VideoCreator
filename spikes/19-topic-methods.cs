// Spike 19: which way of finding the subject changes works — and does it hold for NON-technical talk?
//  eval [episodes=3] [tol=3]
//    Builds spoken-Japanese podcast episodes whose subject changes are known by construction: every subject is written separately by gpt-4.1 as a
//    stretch of casual two-person talk (non-technical subjects mostly, one technical, two deliberately adjacent ones), half of them with no announcing phrase,
//    then concatenated with ASR-like timing. The methods under test never see the truth:
//      lexical   – TextTiling on word overlap (what runs without AI / offline)
//      embedding – lexical + Azure OpenAI text-embedding-3-large depth (SeriesPlanner.FallbackOutline with semantic depth)
//      llm-index – the whole transcript with line numbers → chapters (the app's SeriesPlanner.OutlineAsync, gpt-chat-latest)
//      llm-time  – the whole transcript with mm:ss time codes only → chapters with mm:ss start times (the proposed "summarise with time codes" way)
//      llm+snap  – llm-time, each time snapped to the nearest sentence start, then to the deepest embedding valley within ±2 sentences
//    Scores precision / recall / F1 of the predicted starts (a hit = within ±tol sentences of a true start) and the mean position error.
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
using VideoCreator.Selection;

Console.OutputEncoding = Encoding.UTF8;
var root = Directory.GetCurrentDirectory();
var outDir = Path.Combine(root, "spikes", "out", "topic-eval"); Directory.CreateDirectory(outDir);
if (args.Length < 1 || args[0] != "eval") { Console.WriteLine("usage: eval [episodes=3] [tol=3]"); return; }
int episodes = args.FirstOrDefault(a => a.StartsWith("episodes=")) is { } ea ? int.Parse(ea[9..]) : 3;
int tol = args.FirstOrDefault(a => a.StartsWith("tol=")) is { } ta ? int.Parse(ta[4..]) : 3;
var endpoint = new Uri("https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1");
var cred = new AzureCliCredential();
var gen = new AzureOpenAISelector(endpoint, "gpt-4.1", cred);
var llm = new AzureOpenAISelector(endpoint, "gpt-chat-latest", cred);
var embedder = new AzureOpenAIEmbedder(endpoint, "text-embedding-3-large", cred);

string[][] plan =
[
    ["カレーの隠し味と家庭の味", "小さい子の夜泣きと寝かしつけ", "プロ野球の今季の順位", "家計の見直しと貯金", "京都への家族旅行", "睡眠不足と健康診断の結果", "最近見た映画の感想"],
    ["地元の夏祭りの準備", "婚活アプリで会った人の話", "実家の猫の老後", "転職するかどうか迷っている", "年金と老後資金の不安", "初めての冬山登山", "AzureとCopilotの料金プランの違い"],
    ["引っ越し先の部屋探し", "韓国ドラマにハマった理由", "ダイエットで始めた食事制限", "ダイエットと並行して始めたジョギング", "副業で始めたハンドメイド販売", "町内会の役員を押し付けられた話", "サッカー日本代表の試合の感想"],
];
const int LinesPer = 42;

async Task<List<(int speaker, string text)>> Segment(string topic, bool announce, int seed)
{
    const string schema = """{"type":"object","additionalProperties":false,"required":["lines"],"properties":{"lines":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["speaker","text"],"properties":{"speaker":{"type":"integer"},"text":{"type":"string"}}}}}}""";
    var sys = $"あなたは日本語のポッドキャストの書き起こしを書く脚本家です。二人（speaker 1 と 2）の自然な話し言葉の会話を、指定の話題について {LinesPer} 行書いてください。" +
        "音声認識の書き起こしのように、「えっと」「なんか」「ですよね」などの言いよどみ・相づち・言い直しを入れ、1行は1〜2文。話題に関する具体的な固有名詞・数字・エピソードを入れること。" +
        (announce ? "最初の行は「ところで」「さて、次は」など話題の切り替えを示す言い方で始める。" : "最初の行は、切り替えを示す言い方を使わず、前の話題から続いているかのように、唐突に新しい話題の内容から入る。") +
        "会話の途中で別の話題に脱線しない。最後の行は自然に話を続けたままにし、まとめや締めの挨拶は書かない。";
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            var json = JsonNode.Parse(await gen.CompleteJsonAsync(sys, $"話題: {topic}\n（乱数: {seed}）", "segment", schema, default))!;
            var lines = json["lines"]!.AsArray().Select(x => (x!["speaker"]!.GetValue<int>() is 2 ? 2 : 1, x["text"]!.GetValue<string>().Trim())).Where(x => x.Item2.Length > 0).ToList();
            if (lines.Count >= 20) return lines;
        }
        catch (Exception ex) { Console.WriteLine($"gen retry ({topic}): {ex.Message}"); }
    }
    throw new Exception("generation failed: " + topic);
}

var report = new JsonArray();
var totals = new Dictionary<string, (int tp, int pred, int truth, double err, int matched)>();
var e2e = new Dictionary<string, (int hit, int n)>();
for (int e = 0; e < Math.Min(episodes, plan.Length); e++)
{
    var cacheFile = Path.Combine(outDir, $"corpus_{e}.json");
    List<(int topic, int speaker, string text)> lines = [];
    if (File.Exists(cacheFile))
        lines = JsonNode.Parse(File.ReadAllText(cacheFile))!.AsArray().Select(x => (x!["topic"]!.GetValue<int>(), x["speaker"]!.GetValue<int>(), x["text"]!.GetValue<string>())).ToList();
    else
    {
        var segs = await Task.WhenAll(plan[e].Select((t, k) => Segment(t, k > 0 && k % 2 == 1, e * 10 + k)));
        for (int k = 0; k < segs.Length; k++) lines.AddRange(segs[k].Select(l => (k, l.speaker, l.text)));
        File.WriteAllText(cacheFile, new JsonArray(lines.Select(l => (JsonNode)new JsonObject { ["topic"] = l.topic, ["speaker"] = l.speaker, ["text"] = l.text }).ToArray()).ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    // ASR-like transcript: duration follows the text length, short gaps, no truth inside
    var words = new List<Word>(); var sents = new List<Sentence>(); var truth = new List<int>(); double t = 1.0; int lastTopic = -1;
    foreach (var l in lines)
    {
        if (l.topic != lastTopic) { if (lastTopic >= 0) truth.Add(sents.Count); lastTopic = l.topic; }
        double dur = Math.Max(1.5, l.text.Length * 0.22); int first = words.Count;
        var chunks = Enumerable.Range(0, (l.text.Length + 1) / 2).Select(j => l.text.Substring(j * 2, Math.Min(2, l.text.Length - j * 2))).ToList();
        for (int j = 0; j < chunks.Count; j++) words.Add(new Word($"w{words.Count}", Math.Round(t + j * dur / chunks.Count, 3), Math.Round(t + (j + 1) * dur / chunks.Count - 0.02, 3), chunks[j], l.speaker));
        sents.Add(new Sentence(sents.Count, Math.Round(t, 3), Math.Round(t + dur - 0.02, 3), l.text, l.speaker, first, words.Count - 1));
        t += dur + 0.35;
    }
    var tr = new Transcript { Provider = "synthetic", Duration = Math.Round(t + 1, 3), Words = words, Sentences = sents };
    var sig = new SignalSet { Loudness100Ms = Enumerable.Repeat(-25.0, (int)(tr.Duration * 10) + 10).ToList(), MedianLoudness = -25 };
    Console.WriteLine($"\n===== episode {e}: {sents.Count} sentences, {Mmss(tr.Duration)}, true starts {string.Join(",", truth)}");

    var vecs = (await TopicEmbedding.LoadOrEmbedAsync(embedder, tr, Path.Combine(outDir, $"embed_{e}.json"), default))!;
    var sem = TopicEmbedding.Depth(tr, vecs);
    var blend = TopicEmbedding.Blend(SeriesPlanner.Depth(tr), sem);

    var results = new Dictionary<string, List<int>>
    {
        ["lexical"] = SeriesPlanner.FallbackOutline(tr, sig).Chapters.Select(c => c.Start).Where(i => i > 0).ToList(),
        ["embedding"] = SeriesPlanner.FallbackOutline(tr, sig, sem).Chapters.Select(c => c.Start).Where(i => i > 0).ToList(),
    };
    var ai = await SeriesPlanner.OutlineAsync(llm, tr, Console.WriteLine, default);
    results["llm-index"] = ai?.Chapters.Select(c => c.Start).Where(i => i > 0).ToList() ?? [];
    results["llm-major"] = ai?.Chapters.Where(c => c.Major).Select(c => c.Start).Where(i => i > 0).ToList() ?? [];

    // time-code only: no line numbers, the model writes mm:ss itself
    const string tcSchema = """{"type":"object","additionalProperties":false,"required":["chapters"],"properties":{"chapters":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["time","title"],"properties":{"time":{"type":"string","description":"mm:ss of the chapter start"},"title":{"type":"string"}}}}}}""";
    const string tcSys = "あなたは動画の構成作家です。タイムコード付きの書き起こしを、話題のまとまり（チャプター）に分けてください。話題が変わる位置の開始時刻を mm:ss で、見出しとともに時系列順に返してください。最初のチャプターは 00:00。";
    var tcUser = string.Join("\n", sents.Select(s => $"[{(int)(s.T0 / 60):D2}:{(int)(s.T0 % 60):D2}] {s.Text}"));
    var tcJson = JsonNode.Parse(await llm.CompleteJsonAsync(tcSys, tcUser, "tc_chapters", tcSchema, default))!;
    var tcTimes = tcJson["chapters"]!.AsArray().Select(c => ParseTime(c!["time"]!.GetValue<string>())).Where(x => x > 0).ToList();
    int Nearest(double sec) => sents.OrderBy(s => Math.Abs(s.T0 - sec)).First().Index;
    results["llm-time"] = tcTimes.Select(Nearest).Distinct().OrderBy(x => x).ToList();
    const string tcSys2 = "あなたは動画の構成作家です。タイムコード付きの書き起こしから、会話の主題（何について話しているか）が変わる位置だけを探してください。同じ主題の中の小見出しや展開の区切りは含めません。主題が変わる位置の開始時刻を mm:ss で、見出しとともに時系列順に返してください。最初のチャプターは 00:00。";
    var tc2 = JsonNode.Parse(await llm.CompleteJsonAsync(tcSys2, tcUser, "tc_chapters", tcSchema, default))!;
    results["llm-time-major"] = tc2["chapters"]!.AsArray().Select(c => ParseTime(c!["time"]!.GetValue<string>())).Where(x => x > 0).Select(Nearest).Distinct().OrderBy(x => x).ToList();
    double rawErr = tcTimes.Count == 0 ? 0 : tcTimes.Average(x => Math.Abs(x - sents[Nearest(x)].T0));
    results["llm+snap"] = results["llm-time"].Select(i =>
    {
        int best = i; for (int j = Math.Max(1, i - 2); j <= Math.Min(sents.Count - 1, i + 2); j++) if (blend[j] > blend[best]) best = j;
        return best;
    }).Distinct().OrderBy(x => x).ToList();

    foreach (var (name, pred) in results)
    {
        var (tp, matched, err) = Score(pred, truth, tol);
        double p = pred.Count == 0 ? 0 : (double)tp / pred.Count, r = (double)tp / truth.Count;
        Console.WriteLine($"  {name,-10} predicted {pred.Count,2}  hit {tp}/{truth.Count}  P {p:P0}  R {r:P0}  F1 {(p + r == 0 ? 0 : 2 * p * r / (p + r)):P0}  mean error {err:F1} sentences   starts {string.Join(",", pred)}");
        var cur = totals.GetValueOrDefault(name);
        totals[name] = (cur.tp + tp, cur.pred + pred.Count, cur.truth + truth.Count, cur.err + err * matched, cur.matched + matched);
        report.Add(new JsonObject { ["episode"] = e, ["method"] = name, ["predicted"] = pred.Count, ["hits"] = tp, ["truth"] = truth.Count });
    }
    Console.WriteLine($"  (llm-time: the raw time codes differ from the nearest sentence start by {rawErr:F1} s on average; llm-index returned {results["llm-index"].Count} chapters)");

    // the end task: where does the planner actually cut? a cut is right when it lands on a true subject change (±tol sentences)
    var preset = VideoCreator.Presets.PresetStore.LoadDirectory(Path.Combine(root, "presets")).First(p => p.Id == "P4");
    foreach (var parts in new[] { 2, 3, 4 })
    {
        var cuts = new Dictionary<string, List<int>>();
        List<int> Cut(ChapterOutline o, bool sceneFirst, double[]? semantic)
        {
            SeriesPlanner.SceneFirst = sceneFirst;
            try { return SeriesPlanner.Split(tr, sig, preset, tr.Duration, null, parts, o, [], null, semantic).Skip(1).Select(p => p.First).ToList(); }
            finally { SeriesPlanner.SceneFirst = true; }
        }
        cuts["clock"] = Enumerable.Range(1, parts - 1).Select(k => Nearest(tr.Duration * k / parts)).ToList();
        cuts["lexical, duration-first"] = Cut(SeriesPlanner.FallbackOutline(tr, sig), false, null);
        cuts["lexical, scene-first"] = Cut(SeriesPlanner.FallbackOutline(tr, sig), true, null);
        cuts["embedding, scene-first"] = Cut(SeriesPlanner.FallbackOutline(tr, sig, sem), true, sem);
        if (ai != null)
        {
            cuts["AI chapters only, scene-first"] = Cut(ai, true, null);
            cuts["AI chapters + embedding, scene-first"] = Cut(ai, true, sem);
        }
        foreach (var (name, c) in cuts)
        {
            int hit = c.Count(x => truth.Any(tv => Math.Abs(tv - x) <= tol));
            Console.WriteLine($"  {parts} parts  {name,-38} cuts {string.Join(",", c),-14} on a true change: {hit}/{c.Count}");
            var cur = e2e.GetValueOrDefault(name);
            e2e[name] = (cur.hit + hit, cur.n + c.Count);
        }
    }
}

Console.WriteLine($"\n===== all episodes, tolerance ±{tol} sentences");
foreach (var (name, v) in totals)
{
    double p = v.pred == 0 ? 0 : (double)v.tp / v.pred, r = (double)v.tp / v.truth;
    Console.WriteLine($"  {name,-10} P {p:P0}  R {r:P0}  F1 {(p + r == 0 ? 0 : 2 * p * r / (p + r)):P0}  mean error {(v.matched == 0 ? 0 : v.err / v.matched):F1}");
}
File.WriteAllText(Path.Combine(outDir, "report.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"\n===== end task: planner cuts on a true subject change (2/3/4 parts × {episodes} episodes)");
foreach (var (name, v) in e2e) Console.WriteLine($"  {name,-38} {v.hit}/{v.n} = {(double)v.hit / v.n:P0}");

static (int tp, int matched, double err) Score(List<int> pred, List<int> truth, int tol)
{
    var used = new HashSet<int>(); int tp = 0; double err = 0;
    foreach (var p in pred.OrderBy(x => x))
    {
        var m = truth.Where(x => !used.Contains(x) && Math.Abs(x - p) <= tol).OrderBy(x => Math.Abs(x - p)).Cast<int?>().FirstOrDefault();
        if (m is int mm) { used.Add(mm); tp++; err += Math.Abs(mm - p); }
    }
    return (tp, tp, tp == 0 ? 0 : err / tp);
}
static double ParseTime(string s)
{
    var p = s.Trim().Split(':').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
    return p.Length == 3 ? p[0] * 3600 + p[1] * 60 + p[2] : p.Length == 2 ? p[0] * 60 + p[1] : p.Length == 1 ? p[0] : 0;
}
static string Mmss(double t) => $"{(int)(t / 60)}:{t % 60:00.0}";
