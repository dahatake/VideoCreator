// Spike 05: FR-SEL-01/02/04 — Microsoft Foundry (keyless) structured outputs for highlight candidates.
// Strict mode rejects maxLength/minimum/maxItems (Learn: "Unsupported type-specific keywords"), so the wire schema omits
// them and the PRD §20.2 limits are enforced in code afterwards (AP-3).
#:property PublishAot=false
#:package Azure.Identity@1.21.0
#:package OpenAI@2.14.0
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using OpenAI;
using OpenAI.Chat;
#pragma warning disable OPENAI001

var root = Directory.GetCurrentDirectory();
var outDir = Path.Combine(root, "spikes", "out");
var endpoint = Environment.GetEnvironmentVariable("VC_OPENAI_ENDPOINT") ?? "https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1";
var model = Environment.GetEnvironmentVariable("VC_OPENAI_DEPLOYMENT") ?? "gpt-chat-latest";
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }

using var tr = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "talk_transcript.json")));
var phrases = tr.RootElement.GetProperty("phrases").EnumerateArray().ToList();
var duration = tr.RootElement.GetProperty("durationMilliseconds").GetInt64() / 1000.0;
var sb = new StringBuilder();
foreach (var p in phrases)
{
    double s = p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0, e = s + p.GetProperty("durationMilliseconds").GetInt64() / 1000.0;
    sb.AppendLine($"[{s:F2}-{e:F2}] S{(p.TryGetProperty("speaker", out var sp) ? sp.GetInt32() : 0)}: {p.GetProperty("text").GetString()}");
}

const string wireSchema = """
{"type":"object","additionalProperties":false,"required":["candidates"],"properties":{"candidates":{"type":"array","items":{
 "type":"object","additionalProperties":false,"required":["segments","hook_text","headline","emphasis_words","reason","scores"],
 "properties":{
  "segments":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["start","end"],"properties":{"start":{"type":"number"},"end":{"type":"number"}}}},
  "hook_text":{"type":"string","description":"<=14 Japanese characters"},
  "headline":{"type":"string","description":"<=16 Japanese characters"},
  "emphasis_words":{"type":"array","items":{"type":"string"},"description":"<=3 words copied verbatim from the transcript"},
  "reason":{"type":"string","description":"one sentence, <=120 characters"},
  "scores":{"type":"object","additionalProperties":false,"required":["hook","standalone","payoff","emotion"],"properties":{
    "hook":{"type":"integer"},"standalone":{"type":"integer"},"payoff":{"type":"integer"},"emotion":{"type":"integer"}}}}}}}}
""";
var system = """
あなたはYouTube Shorts/TikTokのショート動画編集者です。秒付きの書き起こしから、縦型ショート候補を最大5件選びます。
規則:
- 合計の尺は25〜35秒。区間(segments)は1〜4個。start/endは書き起こしの行の境界の秒を使う（発話の途中で切らない）
- 冒頭2秒以内に「結果・驚き・強い主張・問い」のいずれかが来る構成にする
- 文脈なしで意味が通ること。「それ」「あれ」「これ」など指示語で始まる区間は避ける
- 書き起こしにない事実・数字をテロップ(hook_text/headline/emphasis_words)に書かない。emphasis_wordsは書き起こしから原文どおり抜き出す
- hook_textは14字以内、headlineは16字以内、reasonは1文
- scoresは0〜10の整数
- 互いに大きく重なる候補は出さない
""";
var user = $"パターン: P4 切り抜き（長尺→30秒） / 動画の長さ: {duration:F1}秒\n書き起こし:\n{sb}";

var client = new ChatClient(model, new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
var opts = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("highlight_candidates", BinaryData.FromString(wireSchema), jsonSchemaIsStrict: true) };
var sw = Stopwatch.StartNew();
ChatCompletion completion = await client.CompleteChatAsync([new SystemChatMessage(system), new UserChatMessage(user)], opts);
var json = completion.Content[0].Text;
Check(true, $"Microsoft Foundry {model} responded in {sw.ElapsedMilliseconds} ms (in={completion.Usage.InputTokenCount} out={completion.Usage.OutputTokenCount} tokens)");
File.WriteAllText(Path.Combine(outDir, "candidates.json"), json);

// --- Code-side enforcement of PRD §20.2 limits + grounding
using var cd = JsonDocument.Parse(json);
var cands = cd.RootElement.GetProperty("candidates").EnumerateArray().ToList();
var transcriptText = string.Concat(phrases.Select(p => p.GetProperty("text").GetString())).Normalize(NormalizationForm.FormKC);
var boundaries = phrases.SelectMany(p => { var s = p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0; return new[] { s, s + p.GetProperty("durationMilliseconds").GetInt64() / 1000.0 }; }).ToList();
Check(cands.Count is >= 1 and <= 5, $"{cands.Count} candidates (1..5)");
int i = 0;
foreach (var c in cands)
{
    i++;
    var segs = c.GetProperty("segments").EnumerateArray().Select(s => (s: s.GetProperty("start").GetDouble(), e: s.GetProperty("end").GetDouble())).ToList();
    var total = segs.Sum(x => x.e - x.s);
    var sc = c.GetProperty("scores");
    var hook = c.GetProperty("hook_text").GetString()!; var head = c.GetProperty("headline").GetString()!;
    var emph = c.GetProperty("emphasis_words").EnumerateArray().Select(x => x.GetString()!).ToList();
    Console.WriteLine($"  #{i} {string.Join(" + ", segs.Select(x => $"{x.s:F1}-{x.e:F1}"))} ({total:F1}s) hook「{hook}」 head「{head}」 emph[{string.Join(",", emph)}] scores {sc}");
    Console.WriteLine($"      reason: {c.GetProperty("reason").GetString()}");
    Check(segs.Count is >= 1 and <= 4 && segs.All(x => x.s >= 0 && x.e > x.s && x.e <= duration + 0.5), $"#{i} segments well-formed and inside media");
    Check(segs.All(x => boundaries.Any(b => Math.Abs(b - x.s) < 1.0) && boundaries.Any(b => Math.Abs(b - x.e) < 1.0)), $"#{i} segment edges within 1 s of phrase boundaries (solver will snap exactly)");
    Check(total is >= 15 and <= 45, $"#{i} total {total:F1}s near target (solver enforces 25–35)");
    Check(hook.Length <= 14 && head.Length <= 16 && emph.Count <= 3, $"#{i} length limits (hook {hook.Length}/14, headline {head.Length}/16, emph {emph.Count}/3)");
    Check(new[] { "hook", "standalone", "payoff", "emotion" }.All(k => sc.GetProperty(k).GetInt32() is >= 0 and <= 10), $"#{i} scores within 0..10");
    Check(emph.All(w => transcriptText.Contains(w.Normalize(NormalizationForm.FormKC))), $"#{i} emphasis words grounded verbatim in transcript");
}
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
