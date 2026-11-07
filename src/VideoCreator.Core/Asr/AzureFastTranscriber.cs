using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using VideoCreator.Models;

namespace VideoCreator.Asr;

public interface ITranscriber
{
    string Name { get; }
    Task<Transcript> TranscribeAsync(string audioFile, double duration, IReadOnlyList<string> phrases, int maxSpeakers, CancellationToken ct);
}

public sealed class AsrException(string message, bool transient) : Exception(message)
{
    public bool Transient { get; } = transient;
}

/// <summary>Azure AI Speech fast transcription, keyless (verified in spikes/04-asr.cs).</summary>
public sealed class AzureFastTranscriber(Uri endpoint, TokenCredential credential, HttpClient http, string locale = "ja-JP") : ITranscriber
{
    public string Name => "azure-fast-transcription";
    public const string ApiVersion = "2025-10-15";

    public async Task<Transcript> TranscribeAsync(string audioFile, double duration, IReadOnlyList<string> phrases, int maxSpeakers, CancellationToken ct)
    {
        var definition = new Dictionary<string, object>
        {
            ["locales"] = new[] { locale },
            ["diarization"] = new { enabled = true, maxSpeakers = Math.Clamp(maxSpeakers, 2, 35) },
            ["profanityFilterMode"] = "None",
        };
        if (phrases.Count > 0) definition["phraseList"] = new { phrases };
        var defJson = JsonSerializer.Serialize(definition);

        string body = "";
        // NFR-REL-03: 1 s, 4 s, 16 s backoff on transient failures
        foreach (var delay in new[] { 0, 1, 4, 16 })
        {
            if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct);
            using var form = new MultipartFormDataContent();
            var audio = new StreamContent(File.OpenRead(audioFile));
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
            form.Add(audio, "audio", Path.GetFileName(audioFile));
            form.Add(new StringContent(defJson, Encoding.UTF8, "application/json"), "definition");
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"speechtotext/transcriptions:transcribe?api-version={ApiVersion}")) { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            try
            {
                using var resp = await http.SendAsync(req, ct);
                body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode) return Parse(body, duration, Name);
                bool transient = resp.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
                if (!transient || delay == 16) throw new AsrException($"Speech API {(int)resp.StatusCode}: {Truncate(body)}", transient);
            }
            catch (HttpRequestException ex) when (delay < 16) { body = ex.Message; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && delay < 16) { body = "timeout"; }
        }
        throw new AsrException("Speech API failed: " + Truncate(body), true);
    }

    static string Truncate(string s) => s.Length > 300 ? s[..300] : s;

    public static Transcript Parse(string json, double duration, string provider)
    {
        using var doc = JsonDocument.Parse(json);
        var words = new List<Word>();
        var raw = new List<(double t0, double t1, string text, int spk, int first, int last)>();
        foreach (var p in doc.RootElement.GetProperty("phrases").EnumerateArray())
        {
            int spk = p.TryGetProperty("speaker", out var s) ? s.GetInt32() : 0;
            double t0 = p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0, t1 = t0 + p.GetProperty("durationMilliseconds").GetInt64() / 1000.0;
            int first = words.Count;
            if (p.TryGetProperty("words", out var ws))
                foreach (var w in ws.EnumerateArray())
                {
                    double w0 = w.GetProperty("offsetMilliseconds").GetInt64() / 1000.0;
                    words.Add(new Word($"w{words.Count}", w0, w0 + w.GetProperty("durationMilliseconds").GetInt64() / 1000.0, w.GetProperty("text").GetString() ?? "", spk));
                }
            raw.Add((t0, t1, p.GetProperty("text").GetString() ?? "", spk, first, words.Count - 1));
        }
        return new Transcript
        {
            Provider = provider,
            Duration = doc.RootElement.TryGetProperty("durationMilliseconds", out var d) ? d.GetInt64() / 1000.0 : duration,
            Words = words,
            Sentences = SentenceBuilder.Build(raw, words),
        };
    }
}

/// <summary>Split ASR phrases into sentences at 。！？ using word timing (phrases can hold several sentences).</summary>
public static class SentenceBuilder
{
    static readonly char[] Enders = ['。', '！', '？', '!', '?'];

    public static List<Sentence> Build(IEnumerable<(double t0, double t1, string text, int spk, int first, int last)> phrases, IReadOnlyList<Word> words)
    {
        var res = new List<Sentence>();
        foreach (var p in phrases)
        {
            var parts = SplitText(p.text);
            if (parts.Count <= 1 || p.last < p.first) { res.Add(new Sentence(res.Count, p.t0, p.t1, p.text.Trim(), p.spk, p.first, Math.Max(p.first, p.last))); continue; }
            // assign words to parts by cumulative character length (display text includes punctuation; lexical words don't)
            var lens = parts.Select(x => Letters(x)).ToList();
            int wi = p.first;
            for (int k = 0; k < parts.Count; k++)
            {
                int start = Math.Min(wi, p.last), end;
                if (k == parts.Count - 1) { end = p.last; wi = p.last + 1; }
                else
                {
                    int got = 0;
                    while (wi <= p.last && got < lens[k]) { got += Letters(words[wi].Text); wi++; }
                    end = Math.Max(start, wi - 1);
                }
                res.Add(new Sentence(res.Count, words[start].T0, words[end].T1, parts[k].Trim(), p.spk, start, end));
            }
        }
        return res;
    }

    static int Letters(string s) => s.Count(char.IsLetterOrDigit);

    public static List<string> SplitText(string text)
    {
        var res = new List<string>(); var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (Enders.Contains(text[i]) && (i + 1 >= text.Length || !Enders.Contains(text[i + 1]))) { if (sb.ToString().Trim().Length > 0) res.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.ToString().Trim().Length > 0) res.Add(sb.ToString());
        return res;
    }
}
