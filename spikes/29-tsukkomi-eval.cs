// Spike 29: does the tsukkomi prompt write what a variety editor writes, where the editor put it? (docs/IMPLEMENTATION.md §25)
//  The reference videos' own transcripts (Azure fast transcription JSON) are cut into 60 s windows, turned into captions by the app's
//  TelopBuilder (P3), and sent to the real LLM twice: with the previous prompt (inlined below, same code-side filtering and cap of 4)
//  and with the current TsukkomiService. Each item is written with its caption's end time; spikes/29-tsukkomi-eval.py scores them against
//  the editor's narration telops measured by spike 28 (ground truth that is neither the app's nor ours).
// Usage: dotnet run spikes/29-tsukkomi-eval.cs <out.json> <name>=<asr.json> ...
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Text.Json;
using Azure.Identity;
using VideoCreator;
using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Selection;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var p3 = PresetStore.LoadDirectory(Path.Combine(root, "presets")).Single(p => p.Id == "P3");
var llm = new AzureOpenAISelector(new Uri("https://videocreator-mai-9709.cognitiveservices.azure.com/openai/v1/"), "gpt-chat-latest", new DefaultAzureCredential());
const double Window = 60;

// the prompt before §25 (TsukkomiService at commit 78d1cc7), kept here only for the comparison
const string OldSystem = """
あなたは日本のバラエティ番組のテロップ担当です。字幕の流れを読み、ボケや意外な発言・見せ場の直後に入る「ツッコミテロップ」を最大4個作ります。
ツッコミテロップは出演者のセリフではなく、見ている人が心の中で言いたくなる一言の代弁です。場面に合う反応を選びます:
ツッコミ（「なんでやねん！」「そこ？」「いや多すぎ！」）、称賛（「イケてる！」「天才か」）、共感（「わかる〜」）、驚き（「マジで!?」）。
規則:
- 各ツッコミは14字以内の短い口語
- caption_id は与えられた字幕のIDから選び、その字幕の直後に表示される
- 字幕の言葉をそのまま繰り返さない（発言への反応を書く）。同じ文を2回使わない
- 字幕に出てこない数字・固有名詞・商品名・人名を新しく書かない
- 人や集団を傷つける、侮辱する、差別的な内容は書かない。ツッコミの対象は「発言の内容」や「状況」にする
- 面白くなりそうな箇所が少なければ、個数を減らしてよい（0個でもよい）
""";

var results = new List<object>();
foreach (var arg in args.Skip(1))
{
    var (name, path) = (arg.Split('=')[0], arg.Split('=', 2)[1]);
    var json = File.ReadAllText(path);
    double duration = JsonDocument.Parse(json).RootElement.GetProperty("durationMilliseconds").GetDouble() / 1000;
    var tr = AzureFastTranscriber.Parse(json, duration, "azure");
    var windows = Enumerable.Range(0, (int)(duration / Window)).Select(i => i * Window).ToList();
    await Parallel.ForEachAsync(windows, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (w0, ct) =>
    {
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = w0, SrcOut = w0 + Window } };
        var caps = TelopBuilder.Build(clips, tr, p3 with { Telop = p3.Telop with { Types = ["caption"] } }, "", "", [], [])
            .Where(t => t.Type == TelopType.Caption).OrderBy(t => t.Start).ToList();
        var src = string.Concat(caps.Select(c => c.Text));
        if (caps.Count < 3) return;
        foreach (var version in new[] { "old", "new" })
        {
            List<(string Id, string Text)> items;
            try
            {
                if (version == "new") items = await TsukkomiService.GenerateAsync(llm, caps, src, ct, "ja-JP", TsukkomiService.MaxItems(Window));
                else
                {
                    items = [];
                    var user = "字幕:\n" + string.Join("\n", caps.Select(c => $"{c.Id}: {c.Text}"));
                    using var doc = JsonDocument.Parse(await llm.CompleteJsonAsync(OldSystem + VideoLanguages.Prompt("ja-JP"), user, "tsukkomi", TsukkomiService.Schema, ct));
                    var seen = new HashSet<string>();
                    foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
                    {
                        var id = it.GetProperty("caption_id").GetString() ?? ""; var text = (it.GetProperty("text").GetString() ?? "").Trim();
                        if (TsukkomiService.Validate(text, id, caps, src) == null && seen.Add(TsukkomiService.Bare(text))) items.Add((id, text));
                        if (items.Count >= 4) break;
                    }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"{name} {w0} {version}: {ex.Message}"); continue; }
            lock (results)
                foreach (var (id, text) in items)
                {
                    var cap = caps.First(c => c.Id == id);
                    results.Add(new { video = name, window = w0, version, text, cap_text = cap.Text, cap_end = Math.Round(w0 + cap.End, 3), captions = caps.Count });
                }
        }
        Console.Error.WriteLine($"{name} {w0:0}s done");
    });
}
File.WriteAllText(args[0], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"{results.Count} items → {args[0]}");
