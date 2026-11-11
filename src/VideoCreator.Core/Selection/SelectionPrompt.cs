using System.Text;
using System.Text.Json;
using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Selection;

public sealed record RawCandidate(List<Interval> Segments, string HookText, string Headline, List<string> EmphasisWords, string Reason, int Hook, int Standalone, int Payoff, int Emotion);

public sealed record LlmUsage(int InputTokens, int OutputTokens);

public sealed record LlmResult(List<RawCandidate> Candidates, string Model, LlmUsage Usage);

public sealed class LlmSchemaException(string message) : Exception(message);

public interface IHighlightSelector
{
    string Name { get; }
    string Model { get; }
    Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct);
    Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct);
}

public static class SelectionPrompt
{
    public const string Version = "sel-1.0.0";

    /// <summary>
    /// Wire schema for strict structured outputs. Azure strict mode rejects maxLength/minimum/maximum/maxItems
    /// (Learn: structured outputs, "Unsupported type-specific keywords"), so limits live in descriptions and
    /// are enforced by <see cref="CandidateValidator"/>.
    /// </summary>
    public const string WireSchema = """
{"type":"object","additionalProperties":false,"required":["candidates"],"properties":{"candidates":{"type":"array","items":{
 "type":"object","additionalProperties":false,"required":["segments","hook_text","headline","emphasis_words","reason","scores"],
 "properties":{
  "segments":{"type":"array","description":"1-4 time ranges in seconds, chronological","items":{"type":"object","additionalProperties":false,"required":["start","end"],"properties":{"start":{"type":"number"},"end":{"type":"number"}}}},
  "hook_text":{"type":"string","description":"opening hook telop in the video language, max 14 full-width display units (Latin characters count as half)"},
  "headline":{"type":"string","description":"situation headline in the video language, max 16 full-width display units (Latin characters count as half)"},
  "emphasis_words":{"type":"array","items":{"type":"string"},"description":"max 3 keywords copied verbatim from the transcript"},
  "reason":{"type":"string","description":"one sentence why this is entertaining, max 120 characters"},
  "scores":{"type":"object","additionalProperties":false,"required":["hook","standalone","payoff","emotion"],"properties":{
    "hook":{"type":"integer","description":"0-10"},"standalone":{"type":"integer","description":"0-10"},"payoff":{"type":"integer","description":"0-10"},"emotion":{"type":"integer","description":"0-10"}}}}}}}}
""";

    public const string RerankSchema = """
{"type":"object","additionalProperties":false,"required":["ranking"],"properties":{"ranking":{"type":"array","items":{"type":"object","additionalProperties":false,
 "required":["id","hook","standalone","payoff","emotion"],"properties":{"id":{"type":"string"},"hook":{"type":"integer"},"standalone":{"type":"integer"},"payoff":{"type":"integer"},"emotion":{"type":"integer"}}}}}}
""";

    public static string System(Preset p, int maxCandidates) => $"""
あなたはYouTube Shorts / TikTok のショート動画編集者です。秒付きの書き起こしと音声の信号サマリから、縦型ショート候補を最大{maxCandidates}件選びます。
編集パターン: {p.Id} {p.Name}
パターンの方針: {(string.IsNullOrWhiteSpace(p.SelectionRules) ? "単体で意味が通り、冒頭に結果・驚き・強い主張・問いがあり、最後にオチがある場面を選ぶ。" : p.SelectionRules)}
規則:
- 合計の尺は{p.Target.Min:0}〜{p.Target.Max:0}秒。segments は1〜4個、時系列順。start/end は書き起こしの行の境界の秒を使う（発話の途中で切らない）
- 冒頭{p.Hook.MaxSec:0.#}秒以内に「結果・驚き・強い主張・問い」のいずれかが来ること
- 文脈なしで意味が通ること。「それ」「あれ」「これ」など指示語で始まる区間は避ける
- 切り出しによって発言の意図が変わる区間（皮肉・前置きの省略など）は選ばない
- 書き起こしにない事実・数字をテロップ（hook_text / headline / emphasis_words）に書かない。emphasis_words は書き起こしから原文どおり抜き出す
- hook_text は14字以内、headline は16字以内、reason は1文（120字以内）
- scores は0〜10の整数（hook=冒頭の引き, standalone=単体で通じるか, payoff=オチ, emotion=感情の動き）
- 互いに大きく重なる候補は出さない
""";

    public static string User(Preset p, double duration, IEnumerable<Sentence> sentences, string signalSummary, string? instruction, IEnumerable<Interval> exclude)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"動画の長さ: {duration:F1}秒");
        if (!string.IsNullOrWhiteSpace(instruction)) sb.AppendLine($"ユーザーの追加指示: {instruction}");
        var ex = exclude.ToList();
        if (ex.Count > 0) sb.AppendLine("次の時間帯は既に提示済みのため避ける: " + string.Join(", ", ex.Select(e => $"{e.T0:F1}-{e.T1:F1}")));
        sb.AppendLine($"信号サマリ: {signalSummary}");
        sb.AppendLine("書き起こし（[開始-終了] 話者: 本文）:");
        foreach (var s in sentences) sb.AppendLine($"[{s.T0:F2}-{s.T1:F2}] S{s.Speaker}: {s.Text}");
        return sb.ToString();
    }

    public static string SignalSummary(SignalSet s, Interval window)
    {
        var peaks = s.Events.Where(e => e.T >= window.T0 && e.T <= window.T1).OrderByDescending(e => e.Strength).Take(8).Select(e => new { t = Math.Round(e.T, 1), kind = e.Kind, db = e.Strength });
        var sil = s.Silences.Count(x => x.T0 >= window.T0 && x.T1 <= window.T1);
        var shots = s.Shots.Count(t => t >= window.T0 && t <= window.T1);
        return JsonSerializer.Serialize(new { loudness_peaks = peaks, silences = sil, shot_changes = shots });
    }

    public static List<RawCandidate> ParseCandidates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var arr) || arr.ValueKind != JsonValueKind.Array) throw new LlmSchemaException("missing candidates array");
        var list = new List<RawCandidate>();
        foreach (var c in arr.EnumerateArray())
        {
            try
            {
                var segs = c.GetProperty("segments").EnumerateArray().Select(s => new Interval(s.GetProperty("start").GetDouble(), s.GetProperty("end").GetDouble())).ToList();
                var sc = c.GetProperty("scores");
                list.Add(new RawCandidate(segs,
                    c.TryGetProperty("hook_text", out var h) ? h.GetString() ?? "" : "",
                    c.TryGetProperty("headline", out var hd) ? hd.GetString() ?? "" : "",
                    c.TryGetProperty("emphasis_words", out var ew) ? ew.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [],
                    c.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                    Int(sc, "hook"), Int(sc, "standalone"), Int(sc, "payoff"), Int(sc, "emotion")));
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new LlmSchemaException("candidate does not match schema: " + ex.Message);
            }
        }
        return list;
    }

    static int Int(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.TryGetDouble(out var d) ? (int)Math.Round(d) : 0;

    /// <summary>Copilot (agent) replies may contain several messages and prose; scan balanced {...} blocks newest-first.</summary>
    public static string? ExtractJsonObject(IReadOnlyList<string> messages, string requiredProperty)
    {
        foreach (var msg in Enumerable.Reverse(messages))
        {
            var blocks = new List<string>(); int depth = 0, st = -1; bool inStr = false, esc = false;
            for (int k = 0; k < msg.Length; k++)
            {
                char ch = msg[k];
                if (inStr) { if (esc) esc = false; else if (ch == '\\') esc = true; else if (ch == '"') inStr = false; continue; }
                if (ch == '"') inStr = true;
                else if (ch == '{') { if (depth++ == 0) st = k; }
                else if (ch == '}' && depth > 0 && --depth == 0) blocks.Add(msg[st..(k + 1)]);
            }
            foreach (var blk in Enumerable.Reverse(blocks))
            {
                try { using var d = JsonDocument.Parse(blk); if (d.RootElement.TryGetProperty(requiredProperty, out _)) return blk; } catch (JsonException) { }
            }
        }
        return null;
    }
}
