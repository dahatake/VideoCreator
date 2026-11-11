using System.Text.Json;
using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Signals;

namespace VideoCreator.Selection;

public sealed record SelectionOutcome(List<Candidate> Candidates, string Model, int InputTokens, int OutputTokens, bool UsedFallback, List<string> Warnings, int FullTextChars, int SentChars);

/// <summary>Orchestrates FR-SEL-01..09: chunked map-reduce, retries, fallback, validation, scoring, dedup.</summary>
public sealed class SelectionService(IHighlightSelector? llm, Action<string>? log = null, int singleCallCharBudget = SelectionService.DefaultCharBudget)
{
    public const int ChunkSeconds = 600, PresentCount = 3, MaxCandidates = 5;
    /// <summary>Above this many transcript characters (≈ 30 min of Japanese speech) the chunked map-reduce + energy pre-filter is used (FR-SEL-03, NFR-COST-03).</summary>
    public const int DefaultCharBudget = 12_000;
    /// <summary>"full" still sends everything in one call up to this size; beyond it every chunk is sent (nothing is dropped).</summary>
    public const int FullSingleCallChars = 60_000;

    /// <summary>FR-SEL-10: the speed/quality/cost trade-off for long inputs. keep = max share of text sent, minChunks = lower bound of chunks.</summary>
    public static (int singleBudget, double keep, int minChunks) ScopeSettings(string scope, int balancedBudget) => scope switch
    {
        "full" => (FullSingleCallChars, 1.0, int.MaxValue),
        "economy" => (Math.Min(6_000, balancedBudget), 0.25, 2),
        _ => (balancedBudget, 0.5, 3),
    };

    /// <summary>Rough share of the transcript sent to the AI (used for the pre-run estimate in the UI).</summary>
    public static (int chars, double share) EstimateSent(double durationSec, string scope, int balancedBudget)
    {
        int full = (int)(durationSec * 6);
        var (budget, keep, minChunks) = ScopeSettings(scope, balancedBudget);
        int count = (int)Math.Ceiling(durationSec / ChunkSeconds);
        if (full <= budget || count <= minChunks && scope != "full") return (full, 1.0);
        if (scope == "full") return (full, 1.0);
        int kept = Math.Min(count, Math.Max(minChunks, (int)Math.Floor(count * keep)));
        return ((int)(full * (double)kept / count), (double)kept / count);
    }

    public async Task<SelectionOutcome> SelectAsync(Transcript tr, SignalSet sig, Preset p, double duration, string? instruction, IReadOnlyList<Interval> exclude, CancellationToken ct, string scope = "balanced")
    {
        var warnings = new List<string>();
        int inTok = 0, outTok = 0; int fullChars = tr.Sentences.Sum(s => s.Text.Length), sentChars = 0;
        var normalized = new List<Candidate>();
        bool fallback = false;
        string model = llm?.Model ?? "none";
        var (budget, keepShare, minChunks) = ScopeSettings(scope, singleCallCharBudget);

        if (llm != null && tr.Sentences.Count > 0)
        {
            var chunks = Chunk(tr, duration);
            // NFR-COST-03: for long inputs keep only the most energetic chunks (share and lower bound depend on the chosen scope)
            if (fullChars > budget && scope != "full" && chunks.Count > minChunks)
            {
                var ranked = chunks.OrderByDescending(c => SignalExtractor.EnergyScore(sig, c.window)).ToList();
                var keep = new List<(Interval window, List<Sentence> sents)>(); int chars = 0;
                foreach (var c in ranked) { var n = c.sents.Sum(s => s.Text.Length); if (keep.Count >= minChunks && chars + n > fullChars * keepShare) break; keep.Add(c); chars += n; }
                chunks = keep.OrderBy(c => c.window.T0).ToList();
                warnings.Add($"長尺のため、音声の盛り上がりが大きい {chunks.Count} 区間（全体の約{100.0 * chars / fullChars:F0}%）に絞って AI に送信しました（絞り込み: {(scope == "economy" ? "節約" : "標準")}）");
            }
            else if (fullChars <= budget) chunks = [(new Interval(0, duration), tr.Sentences.ToList())];

            var system = SelectionPrompt.System(p, chunks.Count == 1 ? MaxCandidates : 3) + VideoLanguages.Prompt(tr.Language);
            var tasks = chunks.Select(async c =>
            {
                var user = SelectionPrompt.User(p, duration, c.sents, SelectionPrompt.SignalSummary(sig, c.window), instruction, exclude);
                Interlocked.Add(ref sentChars, c.sents.Sum(s => s.Text.Length));
                return await CallWithRetryAsync(system, user, ct);
            }).ToList();
            var results = await Task.WhenAll(tasks);
            int seq = 0;
            foreach (var r in results)
            {
                if (r == null) continue;
                inTok += r.Usage.InputTokens; outTok += r.Usage.OutputTokens; model = r.Model;
                foreach (var rc in r.Candidates) normalized.Add(CandidateValidator.Normalize(rc, tr, sig, duration, $"cand_{++seq:D2}"));
            }
            if (chunks.Count > 1 && normalized.Count > PresentCount) normalized = await RerankAsync(normalized, tr, p, ct, (i, o) => { inTok += i; outTok += o; });
        }
        // exclude previously shown candidates (FR-SEL-09)
        normalized = normalized.Where(c => exclude.Count == 0 || c.Segments.All(s => exclude.All(e => Math.Min(s.T1, e.T1) - Math.Max(s.T0, e.T0) < 0.5 * s.Length))).ToList();
        if (normalized.Count == 0)
        {
            fallback = true;
            warnings.Add(llm == null ? "AI が設定されていないため、音声の盛り上がりから選びました" : "AI選定に失敗したため音声の盛り上がりから選びました");
            normalized = Fallback(tr, sig, p, duration, exclude);
        }
        return new SelectionOutcome(CandidateValidator.Dedup(normalized, PresentCount), model, inTok, outTok, fallback, warnings, fullChars, sentChars);
    }

    async Task<LlmResult?> CallWithRetryAsync(string system, string user, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)   // 1 try + 2 retries (FR-SEL-02)
        {
            try
            {
                var r = await llm!.SelectAsync(system, user, ct);
                if (r.Candidates.Count == 0) throw new LlmSchemaException("no candidates");
                return r;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                log?.Invoke($"LLM attempt {attempt + 1} failed: {ex.GetType().Name}: {ex.Message}");
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 1 : 4), ct);
            }
        }
        return null;
    }

    async Task<List<Candidate>> RerankAsync(List<Candidate> cands, Transcript tr, Preset p, CancellationToken ct, Action<int, int> usage)
    {
        try
        {
            var lines = cands.Select(c => $"id={c.Id} [{string.Join(" + ", c.Segments.Select(s => $"{s.T0:F0}-{s.T1:F0}s"))}] 見出し「{c.Headline}」 本文: {Trunc(CandidateValidator.TextOf(tr, c.Segments), 220)}");
            var json = await llm!.CompleteJsonAsync($"あなたはショート動画編集者です。動画全体から集めた候補を同じ基準で採点し直してください。パターン: {p.Name}。scores は0〜10の整数。",
                string.Join("\n", lines), "rerank", SelectionPrompt.RerankSchema, ct);
            using var doc = JsonDocument.Parse(json);
            var map = doc.RootElement.GetProperty("ranking").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!, e => e);
            usage(lines.Sum(l => l.Length) / 2, json.Length / 2);
            return cands.Select(c =>
            {
                if (!map.TryGetValue(c.Id, out var e)) return c;
                var s = c.Scores with { Hook = Clamp(e, "hook"), Standalone = Math.Min(c.Scores.Standalone, Clamp(e, "standalone")), Payoff = Clamp(e, "payoff"), Emotion = Clamp(e, "emotion") };
                return c with { Scores = s with { Final = Math.Round(CandidateValidator.LlmWeight * s.LlmAverage + CandidateValidator.SignalWeight * s.Signal, 2) } };
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke("rerank failed, keeping chunk scores: " + ex.Message);
            return cands;
        }
    }

    static int Clamp(JsonElement e, string k) => Math.Clamp(e.GetProperty(k).GetInt32(), 0, 10);
    static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    static List<(Interval window, List<Sentence> sents)> Chunk(Transcript tr, double duration)
    {
        var res = new List<(Interval, List<Sentence>)>();
        for (double t = 0; t < duration; t += ChunkSeconds)
        {
            var w = new Interval(t, Math.Min(duration, t + ChunkSeconds));
            var s = tr.Sentences.Where(x => x.T0 >= w.T0 && x.T0 < w.T1).ToList();
            if (s.Count > 0) res.Add((w, s));
        }
        return res;
    }

    /// <summary>FR-SEL-02 fallback: windows around the strongest loudness peaks (or the most voiced windows).</summary>
    public static List<Candidate> Fallback(Transcript tr, SignalSet sig, Preset p, double duration, IReadOnlyList<Interval> exclude)
    {
        double half = (p.Target.Min + p.Target.Max) / 4;
        var anchors = sig.Events.OrderByDescending(e => e.Strength).Select(e => e.T).ToList();
        if (anchors.Count < PresentCount)
            for (double t = half; t < duration - half; t += half) anchors.Add(t);   // evenly spaced as last resort
        if (anchors.Count == 0 && duration >= p.Target.Min) anchors.Add(duration / 2 + 2);
        var list = new List<Candidate>(); int n = 0;
        foreach (var a in anchors)
        {
            var iv = new Interval(Math.Max(0, a - half - 2), Math.Min(duration, a + half - 2));   // peak slightly after the middle (payoff)
            if (exclude.Any(e => Math.Min(iv.T1, e.T1) - Math.Max(iv.T0, e.T0) > 0.5 * iv.Length)) continue;
            var raw = new RawCandidate([iv], "", "", [], "AI選定に失敗したため音声の盛り上がりから選びました", 5, 5, 5, 5);
            list.Add(CandidateValidator.Normalize(raw, tr, sig, duration, $"fb_{++n:D2}", "fallback"));
            if (list.Count >= MaxCandidates * 2) break;
        }
        return list;
    }
}
