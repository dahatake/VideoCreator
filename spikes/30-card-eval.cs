// Spike 30: scene-change card wording vs the variety editor's real cards (docs/IMPLEMENTATION.md §25).
//  At each measured card position of the reference videos (spike 28 / the white-card detector), the app's captions (TelopBuilder, P3) of the
//  surrounding 60 s are given to InsertPlanner.AiTextAsync with the pop wording (pause = false, the previous prompt) and the pause wording
//  (pause = true). Prints every proposal next to the editor's real card for the register comparison in spikes/30-card-eval.py.
// Usage: dotnet run spikes/30-card-eval.cs <out.json> <name>=<asr.json>@<t1>,<t2>,... ...
#:property PublishAot=false
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.Text.Json;
using Azure.Identity;
using VideoCreator.Asr;
using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Selection;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "run.cmd"))) root = Path.GetDirectoryName(root)!;
var p3 = PresetStore.LoadDirectory(Path.Combine(root, "presets")).Single(p => p.Id == "P3");
var llm = new AzureOpenAISelector(new Uri("https://videocreator-mai-9709.cognitiveservices.azure.com/openai/v1/"), "gpt-chat-latest", new DefaultAzureCredential());
var results = new List<object>();
foreach (var arg in args.Skip(1))
{
    var name = arg[..arg.IndexOf('=')];
    var (path, times) = (arg[(name.Length + 1)..].Split('@')[0], arg.Split('@')[1].Split(',').Select(double.Parse).ToList());
    var json = File.ReadAllText(path);
    var tr = AzureFastTranscriber.Parse(json, JsonDocument.Parse(json).RootElement.GetProperty("durationMilliseconds").GetDouble() / 1000, "azure");
    foreach (var at in times)
    {
        double w0 = Math.Max(0, at - 40);
        var clips = new List<Clip> { new() { ClipId = "c", AssetId = "a", SrcIn = w0, SrcOut = at + 20 } };
        var caps = TelopBuilder.Build(clips, tr, p3 with { Telop = p3.Telop with { Types = ["caption"] } }, "", "", [], []).Where(t => t.Type == TelopType.Caption).ToList();
        var src = string.Concat(caps.Select(c => c.Text));
        foreach (var pause in new[] { false, true })
        {
            var inserts = new List<Insert> { new() { Id = "card1", Kind = "card", At = at - w0, Duration = 2.5, Text = InsertPlanner.FixedPhrase("ja-JP", 0), TextSource = "fixed" } };
            try { await InsertPlanner.AiTextAsync(llm, inserts, caps, src, CancellationToken.None, "ja-JP", pause); }
            catch (Exception ex) { Console.Error.WriteLine($"{name} {at} {pause}: {ex.Message}"); continue; }
            results.Add(new { video = name, at, version = pause ? "pause" : "pop", text = inserts[0].TextSource == "ai" ? inserts[0].Text : null });
        }
        Console.Error.WriteLine($"{name} {at} done");
    }
}
File.WriteAllText(args[0], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"{results.Count} proposals → {args[0]}");
