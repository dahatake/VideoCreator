// Spike 04a: create a Japanese 2-speaker test video with known ground truth via Azure TTS (keyless Entra ID).
// Output: spikes/media/talk.mp4 (16:9, 2 alternating "cameras"), spikes/media/talk_script.json (ground truth lines)
#:property PublishAot=false
#:package Azure.Identity@1.17.1
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var media = Path.Combine(root, "spikes", "media");
var tmp = Path.Combine(root, "spikes", "out", "tts"); Directory.CreateDirectory(tmp);
var resourceId = Environment.GetEnvironmentVariable("VC_SPEECH_RESOURCE_ID") ?? "/subscriptions/0e930937-43f5-484d-ba9e-1dd1151cf7ba/resourceGroups/rg-videocreator-jpe/providers/Microsoft.CognitiveServices/accounts/videocreator-ai-40800";
var region = "japaneast";

// A = host (Nanami), B = guest (Keita). Variety-show style: setup -> rule -> escalation -> payoff.
var lines = new (string sp, string text)[] {
    ("A", "はい、始まりました。今日の企画はこちら。"),
    ("B", "えーと、なんですか、この封筒は。"),
    ("A", "一時間で一万円を使い切るまで帰れません！"),
    ("B", "いやいや、一万円なんて五分で使えますよ。"),
    ("A", "ただし、ルールがあります。買っていいのは百円のものだけです。"),
    ("B", "百円のものだけで一万円？それって百個買うってことですか。"),
    ("A", "そうです。しかも全部違う商品じゃないとダメです。"),
    ("B", "それはきつい。百円ショップでも同じものを選んじゃいそう。"),
    ("A", "それでは、スタート！"),
    ("B", "よし、まずはお菓子コーナーから攻めます。ポテトチップス、チョコレート、ガム。"),
    ("A", "いいペースですね。今、何個ですか。"),
    ("B", "えーと、二十三個です。もう三十分たってますけど。"),
    ("A", "残り三十分で七十七個。間に合いますか。"),
    ("B", "大丈夫です。ここからは文房具とキッチン用品で一気にいきます。"),
    ("A", "おっと、カゴがもういっぱいですよ。"),
    ("B", "カゴを二つ持てばいいんです。これが僕の作戦です。"),
    ("A", "残り五分！現在九十八個！"),
    ("B", "あと二個！洗濯バサミと、えーと、これ！"),
    ("A", "それは百円じゃなくて三百円の商品です！"),
    ("B", "うそでしょ！？ちゃんと値札を見てなかった。"),
    ("A", "タイムアップ！結果は九十九個、九千九百円でした！"),
    ("B", "百円足りない！あと一個だったのに！"),
    ("A", "というわけで、罰ゲームは来週のお楽しみです。"),
    ("B", "いやー、悔しい。でも正直めちゃくちゃ楽しかったです。"),
    ("A", "皆さんもぜひ、百円ショップで挑戦してみてください。それではまた来週！"),
};
var voices = new Dictionary<string, string> { ["A"] = "ja-JP-NanamiNeural", ["B"] = "ja-JP-KeitaNeural" };

var cred = new DefaultAzureCredential();
var token = (await cred.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]))).Token;
using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"aad#{resourceId}#{token}");
http.DefaultRequestHeaders.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
http.DefaultRequestHeaders.UserAgent.ParseAdd("VideoCreator-spike");

var files = new List<string>(); var truth = new List<object>(); double t = 0;
for (int i = 0; i < lines.Length; i++)
{
    var (sp, text) = lines[i];
    var ssml = $"<speak version='1.0' xml:lang='ja-JP'><voice name='{voices[sp]}'><prosody rate='+5%'>{SecurityElement.Escape(text)}</prosody><break time='350ms'/></voice></speak>";
    var resp = await http.PostAsync($"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1", new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"));
    if (!resp.IsSuccessStatusCode) { Console.WriteLine($"TTS failed {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}"); return 1; }
    var f = Path.Combine(tmp, $"{i:D2}.wav"); await File.WriteAllBytesAsync(f, await resp.Content.ReadAsByteArrayAsync()); files.Add(f);
    var bytes = new FileInfo(f).Length - 44; var dur = bytes / 2.0 / 24000;
    truth.Add(new { index = i, speaker = sp, start = Math.Round(t, 3), end = Math.Round(t + dur, 3), text });
    t += dur;
}
File.WriteAllText(Path.Combine(media, "talk_script.json"), JsonSerializer.Serialize(truth, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
File.WriteAllLines(Path.Combine(tmp, "list.txt"), files.Select(f => $"file '{f.Replace("\\", "/")}'"));
Console.WriteLine($"TTS ok: {lines.Length} lines, {t:F1} s");

// Video: "camera A" (testsrc2) when A talks, "camera B" (smptehdbars) when B talks -> real cuts at speaker changes.
var segs = truth.Cast<dynamic>().ToList();
var vf = new StringBuilder(); var labels = new StringBuilder();
for (int i = 0; i < segs.Count; i++)
{
    double d = segs[i].end - segs[i].start;
    vf.Append($"{(segs[i].speaker == "A" ? "testsrc2" : "smptehdbars")}=s=1920x1080:r=30:d={d.ToString(System.Globalization.CultureInfo.InvariantCulture)}[v{i}];");
    labels.Append($"[v{i}]");
}
var filter = vf + labels.ToString() + $"concat=n={segs.Count}:v=1:a=0[v]";
var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe")) { RedirectStandardError = true };
foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", Path.Combine(tmp, "list.txt"),
    "-filter_complex", filter, "-map", "[v]", "-map", "0:a", "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac", "-ar", "48000", "-shortest", Path.Combine(media, "talk.mp4") }) psi.ArgumentList.Add(a);
using var p = Process.Start(psi)!; var err = p.StandardError.ReadToEnd(); p.WaitForExit();
Console.WriteLine(p.ExitCode == 0 ? "talk.mp4 written" : "ffmpeg failed: " + err);
return p.ExitCode;
