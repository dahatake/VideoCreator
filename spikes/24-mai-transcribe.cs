// Spike 24: A/B of Azure Speech fast transcription — standard model vs MAI-Transcribe-2 (enhancedMode) — on the same resource and audio.
// Ground truth: spikes/media/talk_script.json (spike 04a, 2-speaker Japanese TTS). Conditions: clean, and pink noise at 5 dB SNR.
// Metrics: CER (NFKC, letters/digits only), speaker purity (time-weighted), word-timing error vs line start/end, words per line, latency.
// Env: VC_MAI_ENDPOINT = a Speech/Foundry resource in a MAI-Transcribe region (eastus, westus, westus2, northeurope, westeurope, southeastasia, centralindia).
#:property PublishAot=false
#:package Azure.Identity@1.17.1
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

var root = Directory.GetCurrentDirectory();
var bin = Path.Combine(root, "tools", "ffmpeg", "bin");
var media = Path.Combine(root, "spikes", "media");
var outDir = Path.Combine(root, "spikes", "out"); Directory.CreateDirectory(outDir);
var endpoint = Environment.GetEnvironmentVariable("VC_MAI_ENDPOINT") ?? throw new Exception("set VC_MAI_ENDPOINT");

void Ff(params string[] args)
{
    var psi = new ProcessStartInfo(Path.Combine(bin, "ffmpeg.exe"));
    foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args)) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!; p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception("ffmpeg failed");
}
var clean = Path.Combine(outDir, "mai_clean.flac");
var noisy = Path.Combine(outDir, "mai_noisy.flac");
Ff("-i", Path.Combine(media, "talk.mp4"), "-vn", "-ac", "1", "-ar", "16000", "-c:a", "flac", clean);
// speech loudness ≈ -23 LUFS after loudnorm; pink noise at -28 LUFS ≈ 5 dB SNR
Ff("-i", clean, "-f", "lavfi", "-i", "anoisesrc=color=pink:sample_rate=16000:amplitude=1", "-filter_complex",
   "[0:a]loudnorm=I=-23[s];[1:a]loudnorm=I=-28[n];[s][n]amix=inputs=2:duration=first:normalize=0", "-ac", "1", "-ar", "16000", "-c:a", "flac", noisy);

var truth = JsonDocument.Parse(File.ReadAllText(Path.Combine(media, "talk_script.json"))).RootElement.EnumerateArray()
    .Select(t => (sp: t.GetProperty("speaker").GetString()!, text: t.GetProperty("text").GetString()!, start: t.GetProperty("start").GetDouble(), end: t.GetProperty("end").GetDouble())).ToList();
string Norm(string s) => new(s.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray());
int Lev(string a, string b) { var d = new int[b.Length + 1]; for (int j = 0; j <= b.Length; j++) d[j] = j; for (int i = 1; i <= a.Length; i++) { int prev = d[0]; d[0] = i; for (int j = 1; j <= b.Length; j++) { int tmp = d[j]; d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1)); prev = tmp; } } return d[b.Length]; }
var refText = Norm(string.Concat(truth.Select(t => t.text)));

var cred = new DefaultAzureCredential();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var rows = new List<string>();

async Task Run(string label, string file, object definition)
{
    var token = (await cred.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]))).Token;
    using var form = new MultipartFormDataContent();
    var audio = new ByteArrayContent(await File.ReadAllBytesAsync(file)); audio.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
    form.Add(audio, "audio", Path.GetFileName(file));
    form.Add(new StringContent(JsonSerializer.Serialize(definition), Encoding.UTF8, "application/json"), "definition");
    using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), "speechtotext/transcriptions:transcribe?api-version=2025-10-15")) { Content = form };
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    var sw = Stopwatch.StartNew();
    using var resp = await http.SendAsync(req);
    var body = await resp.Content.ReadAsStringAsync();
    sw.Stop();
    File.WriteAllText(Path.Combine(outDir, $"mai_{label}.json"), body);
    if (!resp.IsSuccessStatusCode) { rows.Add($"{label,-22} HTTP {(int)resp.StatusCode}: {body[..Math.Min(200, body.Length)]}"); return; }

    using var doc = JsonDocument.Parse(body);
    var phrases = doc.RootElement.GetProperty("phrases").EnumerateArray().ToList();
    var words = phrases.SelectMany(p => (p.TryGetProperty("words", out var w) ? w.EnumerateArray() : Enumerable.Empty<JsonElement>())
        .Select(w => (text: w.GetProperty("text").GetString() ?? "", t0: w.GetProperty("offsetMilliseconds").GetInt64() / 1000.0, t1: (w.GetProperty("offsetMilliseconds").GetInt64() + w.GetProperty("durationMilliseconds").GetInt64()) / 1000.0))).ToList();
    var hyp = Norm(string.Concat(phrases.Select(p => p.GetProperty("text").GetString())));
    double cer = (double)Lev(refText, hyp) / refText.Length;

    var map = new Dictionary<string, Dictionary<int, double>> { ["A"] = new(), ["B"] = new() };
    foreach (var p in phrases.Where(p => p.TryGetProperty("speaker", out _)))
    {
        double ps = p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0, pe = ps + p.GetProperty("durationMilliseconds").GetInt64() / 1000.0;
        foreach (var t in truth)
        {
            double ov = Math.Min(pe, t.end) - Math.Max(ps, t.start);
            if (ov > 0) { var m = map[t.sp]; var k = p.GetProperty("speaker").GetInt32(); m[k] = m.GetValueOrDefault(k) + ov; }
        }
    }
    double Purity(Dictionary<int, double> m) => m.Count == 0 ? 0 : m.Values.Max() / m.Values.Sum();
    bool split = map["A"].Count > 0 && map["B"].Count > 0 && map["A"].MaxBy(kv => kv.Value).Key != map["B"].MaxBy(kv => kv.Value).Key;

    // word timing: for each line, words whose midpoint falls inside [start-0.3, end+0.3]; |first word start - line start| and |last word end - line end|
    var errs = new List<double>();
    foreach (var t in truth)
    {
        var ws = words.Where(w => (w.t0 + w.t1) / 2 >= t.start - 0.3 && (w.t0 + w.t1) / 2 <= t.end + 0.3).ToList();
        if (ws.Count == 0) { errs.Add(double.NaN); continue; }
        errs.Add(Math.Abs(ws[0].t0 - t.start)); errs.Add(Math.Abs(ws[^1].t1 - t.end));
    }
    var valid = errs.Where(e => !double.IsNaN(e)).OrderBy(e => e).ToList();
    bool mono = words.Zip(words.Skip(1)).All(z => z.Second.t0 >= z.First.t0 - 1e-3);
    double charsPerWord = words.Count == 0 ? 0 : words.Average(w => Norm(w.text).Length);
    rows.Add($"{label,-22} CER {cer,6:P1}  spk {(split ? "ok" : "NG")} A={Purity(map["A"]),4:P0} B={Purity(map["B"]),4:P0}  words {words.Count,4} ({charsPerWord:F1} ch/word, mono={mono})  " +
             $"timing |err| median {(valid.Count > 0 ? valid[valid.Count / 2] : double.NaN):F2}s p90 {(valid.Count > 0 ? valid[(int)(valid.Count * 0.9)] : double.NaN):F2}s missing {errs.Count(double.IsNaN)}  {sw.ElapsedMilliseconds,6} ms");
}

var phraseList = new { phrases = new[] { "百円ショップ", "罰ゲーム" } };
object Standard(string locale) => new { locales = new[] { locale }, diarization = new { enabled = true, maxSpeakers = 2 }, phraseList, profanityFilterMode = "None" };
object Mai(string? locale) => locale == null
    ? new { enhancedMode = new { enabled = true, model = "MAI-Transcribe-2", modelOptions = new { timestamps = "word", transcribeStyle = "verbatim" } }, diarization = new { enabled = true }, phraseList }
    : new { locales = new[] { locale }, enhancedMode = new { enabled = true, model = "MAI-Transcribe-2", modelOptions = new { timestamps = "word", transcribeStyle = "verbatim" } }, diarization = new { enabled = true }, phraseList };

foreach (var (cond, file) in new[] { ("clean", clean), ("noisy5dB", noisy) })
{
    await Run($"{cond}-standard", file, Standard("ja-JP"));
    await Run($"{cond}-mai2-ja", file, Mai("ja"));
}
await Run("clean-mai2-ja-JP", clean, Mai("ja-JP"));
await Run("clean-mai2-auto", clean, Mai(null));
foreach (var r in rows) Console.WriteLine(r);
return 0;
