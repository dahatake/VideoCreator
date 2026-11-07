// Spike 04: FR-ASR-01/02/04/05 — Azure AI Speech fast transcription (keyless), word timestamps, diarization, phrase list.
// Verified against spikes/media/talk_script.json (TTS ground truth).
#:property PublishAot=false
#:package Azure.Identity@1.17.1
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var media = Path.Combine(root, "spikes", "media");
var outDir = Path.Combine(root, "spikes", "out"); Directory.CreateDirectory(outDir);
var endpoint = Environment.GetEnvironmentVariable("VC_SPEECH_ENDPOINT") ?? "https://videocreator-ai-40800.cognitiveservices.azure.com/";
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }

// 16 kHz mono FLAC keeps the upload small (60 min ≈ 60 MB, well under the 500 MB limit)
var flac = Path.Combine(outDir, "talk.flac");
var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe"));
foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", Path.Combine(media, "talk.mp4"), "-vn", "-ac", "1", "-ar", "16000", "-c:a", "flac", flac }) psi.ArgumentList.Add(a);
Process.Start(psi)!.WaitForExit();

var token = (await new DefaultAzureCredential().GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]))).Token;
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
var definition = JsonSerializer.Serialize(new
{
    locales = new[] { "ja-JP" },
    diarization = new { enabled = true, maxSpeakers = 2 },
    phraseList = new { phrases = new[] { "百円ショップ", "罰ゲーム" } },
    profanityFilterMode = "None"
});
using var form = new MultipartFormDataContent();
var audio = new ByteArrayContent(await File.ReadAllBytesAsync(flac)); audio.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
form.Add(audio, "audio", "talk.flac");
form.Add(new StringContent(definition, Encoding.UTF8, "application/json"), "definition");
var sw = Stopwatch.StartNew();
var resp = await http.PostAsync(new Uri(new Uri(endpoint), "speechtotext/transcriptions:transcribe?api-version=2025-10-15"), form);
var body = await resp.Content.ReadAsStringAsync();
Check(resp.IsSuccessStatusCode, $"fast transcription HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds} ms (keyless Entra ID)");
if (!resp.IsSuccessStatusCode) { Console.WriteLine(body); return 1; }
File.WriteAllText(Path.Combine(outDir, "talk_transcript.json"), body);

using var doc = JsonDocument.Parse(body);
var phrases = doc.RootElement.GetProperty("phrases").EnumerateArray().ToList();
var words = phrases.SelectMany(p => p.TryGetProperty("words", out var w) ? w.EnumerateArray() : Enumerable.Empty<JsonElement>()).ToList();
var speakers = phrases.Where(p => p.TryGetProperty("speaker", out _)).Select(p => p.GetProperty("speaker").GetInt32()).Distinct().ToList();
Console.WriteLine($"      phrases={phrases.Count} words={words.Count} speakers=[{string.Join(",", speakers)}]");
foreach (var p in phrases.Take(6)) Console.WriteLine($"      [{p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0,6:F2}] spk{(p.TryGetProperty("speaker", out var s) ? s.GetInt32() : -1)} {p.GetProperty("text").GetString()}");
Check(words.Count > 50 && words.All(w => w.TryGetProperty("offsetMilliseconds", out _) && w.TryGetProperty("durationMilliseconds", out _)), "FR-ASR-01 every word has offset + duration");
Check(speakers.Count == 2, "FR-ASR-02 diarization found 2 speakers");

// CER against ground truth (NFKC, punctuation removed)
var truth = JsonDocument.Parse(File.ReadAllText(Path.Combine(media, "talk_script.json"))).RootElement.EnumerateArray().ToList();
string Norm(string s) => new string(s.Normalize(NormalizationForm.FormKC).Where(c => char.IsLetterOrDigit(c)).ToArray());
var refText = Norm(string.Concat(truth.Select(t => t.GetProperty("text").GetString())));
var hypText = Norm(string.Concat(phrases.Select(p => p.GetProperty("text").GetString())));
int Lev(string a, string b) { var d = new int[b.Length + 1]; for (int j = 0; j <= b.Length; j++) d[j] = j; for (int i = 1; i <= a.Length; i++) { int prev = d[0]; d[0] = i; for (int j = 1; j <= b.Length; j++) { int tmp = d[j]; d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1)); prev = tmp; } } return d[b.Length]; }
var cer = (double)Lev(refText, hypText) / refText.Length;
Console.WriteLine($"      ref: {refText[..60]}…\n      hyp: {hypText[..60]}…");
Check(cer < 0.15, $"CER {cer:P1} vs TTS ground truth (< 15%; numerals/kanji spelling differences count as errors)");

// Speaker attribution: majority speaker per ground-truth line must match A/B alternation consistently
var map = new Dictionary<string, Dictionary<int, double>> { ["A"] = new(), ["B"] = new() };
foreach (var p in phrases.Where(p => p.TryGetProperty("speaker", out _)))
{
    double ps = p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0, pe = ps + p.GetProperty("durationMilliseconds").GetInt64() / 1000.0;
    foreach (var t in truth)
    {
        double ov = Math.Min(pe, t.GetProperty("end").GetDouble()) - Math.Max(ps, t.GetProperty("start").GetDouble());
        if (ov > 0) { var m = map[t.GetProperty("speaker").GetString()!]; var k = p.GetProperty("speaker").GetInt32(); m[k] = m.GetValueOrDefault(k) + ov; }
    }
}
double Purity(Dictionary<int, double> m) => m.Values.Max() / m.Values.Sum();
var aSpk = map["A"].MaxBy(kv => kv.Value).Key; var bSpk = map["B"].MaxBy(kv => kv.Value).Key;
Check(aSpk != bSpk && Purity(map["A"]) > 0.85 && Purity(map["B"]) > 0.85, $"speaker purity A={Purity(map["A"]):P0} B={Purity(map["B"]):P0} (time-weighted, >85%)");

// Word timing sanity: words monotonic and inside the media duration; first word near first TTS line start
var offs = words.Select(w => w.GetProperty("offsetMilliseconds").GetInt64()).ToList();
Check(offs.Zip(offs.Skip(1)).All(z => z.Second >= z.First) && offs.Last() / 1000.0 <= truth.Last().GetProperty("end").GetDouble() + 1, "word offsets monotonic and within media duration");
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
