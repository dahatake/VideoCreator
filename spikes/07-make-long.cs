// Spike 07: build a ~60-minute Japanese two-speaker test video (TC-JA-60 substitute) for NFR-PERF-01 / FR-SEL-03.
// Script: Azure OpenAI writes varied variety-show episodes; audio: Azure TTS (keyless); video: 720p test pattern.
#:property PublishAot=false
#:package Azure.Identity@1.21.0
#:package OpenAI@2.14.0
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using OpenAI;
using OpenAI.Chat;
#pragma warning disable OPENAI001

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var media = Path.Combine(root, "spikes", "media");
var tmp = Path.Combine(root, "spikes", "out", "long"); Directory.CreateDirectory(tmp);
int episodes = args.Length > 0 ? int.Parse(args[0]) : 28;
var resourceId = "/subscriptions/0e930937-43f5-484d-ba9e-1dd1151cf7ba/resourceGroups/rg-videocreator-jpe/providers/Microsoft.CognitiveServices/accounts/videocreator-ai-40800";
var cred = new DefaultAzureCredential();
var chat = new ChatClient("gpt-5.4-mini", new BearerTokenPolicy(cred, "https://ai.azure.com/.default"), new OpenAIClientOptions { Endpoint = new Uri("https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1") });

const string schema = """{"type":"object","additionalProperties":false,"required":["title","lines"],"properties":{"title":{"type":"string"},"lines":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["speaker","text"],"properties":{"speaker":{"type":"string","enum":["A","B"]},"text":{"type":"string"}}}}}}""";
var themes = new[] { "激辛ラーメン完食チャレンジ", "1日1000円生活", "目隠し利き水", "早口言葉対決", "100均DIYで椅子作り", "知らない駅で途中下車", "全力で謝罪会見ごっこ", "料理初心者がカレーを作る", "クイズで負けたら罰ゲーム", "キャンプで火起こし", "スマホなしで待ち合わせ", "ガチャで出たもので晩ご飯", "30分で部屋を片付ける", "初めての釣り", "社長に企画をプレゼン", "地元の名物を当てる", "絵しりとり", "ダーツで旅先を決める", "最安値の家電を探す", "朝5時起きチャレンジ", "AIに人生相談", "苦手な虫と向き合う", "早食い対決", "昭和レトロ喫茶めぐり", "雪だるま作り", "ボウリング勝負", "書道で今年の漢字", "サウナ初体験", "手作りピザ対決", "落語を覚えて披露" };
var allLines = new List<(string sp, string text)>();
await Parallel.ForEachAsync(Enumerable.Range(0, episodes), new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (i, ct) =>
{
    var opts = new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("episode", BinaryData.FromString(schema), jsonSchemaIsStrict: true) };
    ChatCompletion c = await chat.CompleteChatAsync([new SystemChatMessage("日本のYouTubeバラエティ番組の台本作家です。司会A（女性）とゲストB（男性）の掛け合いを書きます。"),
        new UserChatMessage($"企画「{themes[i % themes.Length]}」の台本を、自然な話し言葉で30行書いてください。前半は企画とルールの説明、中盤は挑戦の実況（数字や時間を具体的に）、終盤にオチや意外な結末を入れること。1行は40字以内。")], opts, ct);
    using var doc = JsonDocument.Parse(c.Content[0].Text);
    var lines = doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("speaker").GetString()!, l.GetProperty("text").GetString()!)).ToList();
    lock (allLines) File.WriteAllText(Path.Combine(tmp, $"ep{i:D2}.json"), c.Content[0].Text);
});
foreach (var f in Directory.GetFiles(tmp, "ep*.json").OrderBy(x => x))
{
    using var d = JsonDocument.Parse(File.ReadAllText(f));
    allLines.Add(("A", $"続いての企画は、{d.RootElement.GetProperty("title").GetString()}です。"));
    allLines.AddRange(d.RootElement.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("speaker").GetString()!, l.GetProperty("text").GetString()!)));
}
Console.WriteLine($"script: {allLines.Count} lines from {episodes} episodes");

var voices = new Dictionary<string, string> { ["A"] = "ja-JP-NanamiNeural", ["B"] = "ja-JP-KeitaNeural" };
string? token = null; var tokenAt = DateTime.MinValue; var gate = new SemaphoreSlim(1);
async Task<string> Token() { await gate.WaitAsync(); try { if (token == null || DateTime.UtcNow - tokenAt > TimeSpan.FromMinutes(30)) { token = (await cred.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]))).Token; tokenAt = DateTime.UtcNow; } return token; } finally { gate.Release(); } }
using var http = new HttpClient();
var sw = Stopwatch.StartNew();
await Parallel.ForEachAsync(Enumerable.Range(0, allLines.Count), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, ct) =>
{
    var f = Path.Combine(tmp, $"l{i:D4}.wav"); if (File.Exists(f)) return;
    var (sp, text) = allLines[i];
    var ssml = $"<speak version='1.0' xml:lang='ja-JP'><voice name='{voices[sp]}'><prosody rate='+5%'>{SecurityElement.Escape(text)}</prosody><break time='{(text.StartsWith("続いての企画") ? 1500 : 300)}ms'/></voice></speak>";
    for (int attempt = 0; attempt < 5; attempt++)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://japaneast.tts.speech.microsoft.com/cognitiveservices/v1") { Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"aad#{resourceId}#{await Token()}");
        req.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm"); req.Headers.UserAgent.ParseAdd("VideoCreator-spike");
        using var resp = await http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) { await File.WriteAllBytesAsync(f, await resp.Content.ReadAsByteArrayAsync(ct), ct); return; }
        await Task.Delay(1000 * (attempt + 1), ct);
    }
    throw new Exception($"TTS failed for line {i}");
});
Console.WriteLine($"TTS {allLines.Count} lines in {sw.Elapsed.TotalSeconds:F0} s");
File.WriteAllLines(Path.Combine(tmp, "list.txt"), Enumerable.Range(0, allLines.Count).Select(i => $"file '{Path.Combine(tmp, $"l{i:D4}.wav").Replace("\\", "/")}'"));
File.WriteAllText(Path.Combine(media, "long_script.json"), JsonSerializer.Serialize(allLines.Select(l => new { speaker = l.sp, text = l.text }), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

var wav = Path.Combine(tmp, "all.wav");
void FF(params string[] a) { var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardError = true }; foreach (var x in a) psi.ArgumentList.Add(x); using var p = Process.Start(psi)!; var e = p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new Exception(e); }
FF("-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", Path.Combine(tmp, "list.txt"), "-c", "copy", wav);
sw.Restart();
FF("-hide_banner", "-loglevel", "error", "-y", "-i", wav, "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30", "-shortest", "-map", "1:v", "-map", "0:a",
   "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-c:a", "aac", "-ar", "48000", Path.Combine(media, "long.mp4"));
Console.WriteLine($"long.mp4 encoded in {sw.Elapsed.TotalSeconds:F0} s");
return 0;
