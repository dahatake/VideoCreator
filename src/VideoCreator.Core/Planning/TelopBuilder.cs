using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Planning;

/// <summary>FR-TEL-01..05/09/10: builds telops on the OUTPUT timeline from words inside the clips.</summary>
public static class TelopBuilder
{
    public const double HookSeconds = 2.5, EmphasisSeconds = 1.6, CaptionHoldMax = 0.5;
    public const double EmphasisInterval = 4;

    public sealed record Mapped(Word Word, double Start, double End, int ClipIndex);

    /// <summary>Map words whose centre lies inside a clip to output time.</summary>
    public static List<Mapped> MapWords(IReadOnlyList<Clip> clips, Transcript tr)
    {
        var res = new List<Mapped>(); double offset = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            var c = clips[i];
            foreach (var w in tr.Words)
            {
                var mid = (w.T0 + w.T1) / 2;
                if (mid < c.MasterIn || mid > c.MasterOut) continue;
                res.Add(new Mapped(w, offset + Math.Max(0, w.T0 - c.MasterIn) / c.Speed, offset + Math.Min(c.Length, (w.T1 - c.MasterIn) / c.Speed), i));
            }
            offset += c.Length;
        }
        return res;
    }

    public static List<Telop> Build(IReadOnlyList<Clip> clips, Transcript tr, Preset p, string hook, string headline, IReadOnlyList<string> emphasis, List<string> notes)
    {
        var telops = new List<Telop>();
        double total = clips.Sum(c => c.Length);
        var mapped = MapWords(clips, tr);
        var cap = p.Telop.Caption;
        var captionClipEnds = new Dictionary<string, double>();
        int n = 0;

        if (p.HasTelop("caption"))
        {
            foreach (var group in mapped.GroupBy(m => m.ClipIndex))
            {
                double clipEnd = clips.Take(group.Key + 1).Sum(c => c.Length);
                foreach (var chunk in Chunk(group.ToList(), Rendering.AssWriter.CaptionLineChars(p) * cap.MaxLines))
                {
                    var text = JapaneseText.NormalizeTelop(string.Concat(chunk.Select(c => c.Word.Text)));
                    if (text.Length == 0) continue;
                    telops.Add(new Telop
                    {
                        Id = $"t{++n}", Type = TelopType.Caption, Start = R(chunk[0].Start), End = R(chunk[^1].End), Text = text, Style = "Caption",
                        SourceWordIds = chunk.Select(c => c.Word.Id).ToList(), Speaker = chunk[0].Word.Speaker,
                    });
                    captionClipEnds[$"t{n}"] = clipEnd;
                }
            }
            // hold captions into following gaps (less flicker) and relieve CPS (FR-TEL-02 step 2)
            telops = telops.OrderBy(t => t.Start).ToList();
            for (int i = 0; i < telops.Count; i++)
            {
                double next = Math.Min(i + 1 < telops.Count ? telops[i + 1].Start : total, captionClipEnds[telops[i].Id]);
                double need = JapaneseText.DisplayWidth(telops[i].Text) / cap.MaxCps;
                double end = Math.Min(Math.Min(next, telops[i].End + CaptionHoldMax), Math.Max(telops[i].End + 0.2, telops[i].Start + need));
                telops[i] = telops[i] with { End = R(Math.Max(Math.Min(telops[i].End, next), end)) };
            }
        }

        if (p.HasTelop("emphasis"))
        {
            // in time order; emphasis telops share one screen row, so they must never overlap in time
            var hits = emphasis.Distinct().SelectMany(w => FindPhrases(mapped, w).Select(hit => (w, hit))).ToList();
            foreach (var w in emphasis.Where(w => !hits.Any(h => h.w == w))) notes.Add($"強調語「{w}」が切り出し範囲にないため表示しません");
            foreach (var (w, hit) in hits.OrderBy(h => h.hit.start))
            {
                var start = hit.start;
                var end = Math.Min(total, Math.Max(hit.end, start + EmphasisSeconds));
                var text = JapaneseText.NormalizeTelop(w);
                // Keep verbatim captions (including SRT); omit only the redundant presentation telop.
                if (telops.Any(t => t.Type == TelopType.Caption && t.Start < end && t.End > start && t.Text == text)) continue;
                var prevIdx = telops.FindLastIndex(t => t.Type == TelopType.Emphasis);
                if (prevIdx >= 0 && telops[prevIdx].End > start)
                {
                    if (start - telops[prevIdx].Start >= 0.8) telops[prevIdx] = telops[prevIdx] with { End = R(start) };
                    else { notes.Add($"強調語「{w}」は直前の強調と重なるため表示しません"); continue; }
                }
                telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Emphasis, Start = R(start), End = R(end), Text = text, Style = "Emph", Animation = "pop", SourceWordIds = hit.ids });
            }
            // Fill sparse stretches with short, verbatim phrases; never invent text or change captions.
            foreach (var group in mapped.GroupBy(m => m.ClipIndex))
            foreach (var chunk in Chunk(group.ToList(), 8))
            {
                var text = JapaneseText.NormalizeTelop(string.Concat(chunk.Select(m => m.Word.Text)));
                double start = chunk[0].Start, end = Math.Min(clips.Take(group.Key + 1).Sum(c => c.Length), Math.Max(chunk[^1].End, start + EmphasisSeconds));
                if (JapaneseText.DisplayWidth(text) is < 3 or > 10 || end - start < MinCaptionSec) continue;
                if (telops.Any(t => t.Type == TelopType.Emphasis && (Math.Abs(t.Start - start) < EmphasisInterval || t.Start < end && t.End > start))) continue;
                if (telops.Any(t => t.Type == TelopType.Caption && t.Start < end && t.End > start && t.Text == text)) continue;
                telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Emphasis, Start = R(start), End = R(end), Text = text, Style = "Emph", Animation = "pop", SourceWordIds = chunk.Select(m => m.Word.Id).ToList() });
            }
        }

        if (p.HasTelop("headline"))
        {
            double hs = 0;
            if (!string.IsNullOrWhiteSpace(hook)) { telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Hook, Start = 0, End = R(Math.Min(total, HookSeconds)), Text = hook, Style = "Hook", Animation = "pop" }); hs = HookSeconds; }
            if (!string.IsNullOrWhiteSpace(headline) && hs < total) telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Headline, Start = R(hs), End = R(total), Text = headline, Style = "Headline" });
            else if (string.IsNullOrWhiteSpace(headline) && !string.IsNullOrWhiteSpace(hook) && total > HookSeconds) { /* hook only */ }
        }
        return telops.Select(t => TelopGrounding.Bind(t, clips, tr)).OrderBy(t => t.Start).ThenBy(t => t.Type).ToList();
    }

    static double R(double x) => Math.Round(x, 3);

    /// <summary>Captions shorter than this flicker (Netflix ja: ≥ 5/6 s); the chunker prefers longer captions when the phrase allows.</summary>
    public const double MinCaptionSec = 0.8;

    /// <summary>
    /// Caption chunking: split at sentence ends / pauses, then break each phrase into balanced captions with a small
    /// dynamic program. The length of each caption is chosen automatically: ≤ maxChars (the on-screen limit), never breaking where
    /// JIS X 4051 kinsoku forbids (line-start/line-end characters, number+counter, words) nor before a particle/auxiliary,
    /// preferring 文節 boundaries and breath pauses, and avoiding flickering captions shorter than <see cref="MinCaptionSec"/>
    /// and greedy orphans like 「…いっぱい」+「ですよ」.
    /// </summary>
    public static List<List<Mapped>> Chunk(List<Mapped> words, int maxChars)
    {
        var res = new List<List<Mapped>>();
        var phrase = new List<Mapped>();
        void Flush() { if (phrase.Count > 0) res.AddRange(Balance(phrase, maxChars)); phrase = []; }
        for (int i = 0; i < words.Count; i++)
        {
            if (phrase.Count > 0 && (words[i].Start - phrase[^1].End > 0.6 || words[i].Word.Speaker != phrase[^1].Word.Speaker)) Flush();
            phrase.Add(words[i]);
            if (JapaneseText.BreakScore(words[i].Word.Text) == 4) Flush();
        }
        Flush();
        return res.Where(c => c.Any(x => !JapaneseText.IsPunct(x.Word.Text))).ToList();
    }

    static List<List<Mapped>> Balance(List<Mapped> ws, int maxChars)
    {
        int n = ws.Count;
        // width as displayed (、→ full-width space inside a caption, trimmed at its edges), not a sum of per-word widths
        var raw = ws.Select(m => m.Word.Text).ToArray();
        double SegWidth(int i, int j) => JapaneseText.DisplayWidth(JapaneseText.NormalizeTelop(string.Concat(raw[i..j])));
        double total = SegWidth(0, n);
        if (total <= maxChars) return [ws];
        int lines = (int)Math.Ceiling(total / maxChars);
        double target = total / lines;
        // cost of a break after word k (between k and k+1): kinsoku/文節 quality, relieved by a breath pause
        var breakCost = new double[n];
        for (int k = 0; k + 1 < n; k++)
        {
            var c = JapaneseText.TokenBreakCost(ws[k].Word.Text, ws[k + 1].Word.Text);
            if (c < JapaneseText.Forbidden && ws[k + 1].Start - ws[k].End >= 0.25) c = Math.Max(0, c - 3);
            breakCost[k] = c;
        }
        var best = new double[n + 1]; var from = new int[n + 1];
        Array.Fill(best, double.PositiveInfinity); best[0] = 0;
        // a katakana/Latin/number word split by ASR into several tokens is one unit: never cut, shrunk to fit if longer than a caption
        var unitStart = Enumerable.Range(0, n + 1).Select(k => k == 0 || k == n || !JapaneseText.InsideWord(raw[k - 1], raw[k])).ToArray();
        for (int j = 1; j <= n; j++)
        {
            if (!unitStart[j]) continue;
            // over-long only when unavoidable: one word plus the particles/auxiliaries glued to it (「パワープラットフォームの」), shrunk to fit
            bool glued = true; int prev = -1;
            for (int i = j - 1; i >= 0; i--)
            {
                if (!unitStart[i]) continue;
                if (prev >= 0 && breakCost[prev - 1] < JapaneseText.Detached) glued = false;
                prev = i;
                double width = SegWidth(i, j);
                if (width > maxChars && !glued) break;
                if (double.IsInfinity(best[i])) continue;
                double cost = (width - target) * (width - target) / Math.Max(1, target) + 2;   // +2 per caption favours fewer captions
                if (j < n) cost += breakCost[j - 1] * 1.5;
                double dur = ws[j - 1].End - ws[i].Start;
                if (dur < MinCaptionSec) cost += (MinCaptionSec - dur) * 15;
                if (width < 4) cost += (4 - width) * 4;                         // a 1–3 character caption is an orphan
                if (width > maxChars) cost += (width - maxChars) * 6;           // shrinking the font is the last resort
                if (best[i] + cost < best[j]) { best[j] = best[i] + cost; from[j] = i; }
            }
        }
        var res = new List<List<Mapped>>();
        for (int j = n; j > 0; j = from[j]) res.Insert(0, ws.GetRange(from[j], j - from[j]));
        return res;
    }

    static IEnumerable<(double start, double end, List<string> ids)> FindPhrases(List<Mapped> mapped, string phrase)
    {
        var target = Norm(phrase);
        if (target.Length == 0) yield break;
        for (int i = 0; i < mapped.Count; i++)
        {
            var acc = ""; var ids = new List<string>();
            for (int j = i; j < mapped.Count && acc.Length < target.Length + 4; j++)
            {
                if (mapped[j].ClipIndex != mapped[i].ClipIndex || mapped[j].Word.Speaker != mapped[i].Word.Speaker
                    || j > i && (mapped[j].Start - mapped[j - 1].End > 0.6 || JapaneseText.BreakScore(mapped[j - 1].Word.Text) == 4)) break;
                acc += Norm(mapped[j].Word.Text); ids.Add(mapped[j].Word.Id);
                if (acc.Contains(target))
                {
                    int first = i;
                    while (first < j && Norm(string.Concat(mapped.Skip(first + 1).Take(j - first).Select(m => m.Word.Text))).Contains(target)) first++;
                    yield return (mapped[first].Start, mapped[j].End, ids.Skip(first - i).ToList());
                    i = j;
                    break;
                }
            }
        }
    }

    static string Norm(string s) => new(s.Normalize(System.Text.NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray());
}
