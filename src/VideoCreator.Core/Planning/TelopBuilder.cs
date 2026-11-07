using VideoCreator.Models;
using VideoCreator.Presets;

namespace VideoCreator.Planning;

/// <summary>FR-TEL-01..05/09/10: builds telops on the OUTPUT timeline from words inside the clips.</summary>
public static class TelopBuilder
{
    public const double HookSeconds = 2.5, EmphasisSeconds = 1.6, CaptionHoldMax = 0.6;

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
        int n = 0;

        if (p.HasTelop("caption"))
        {
            foreach (var group in mapped.GroupBy(m => m.ClipIndex))
            {
                foreach (var chunk in Chunk(group.ToList(), cap.MaxCharsPerLine))
                {
                    var text = JapaneseText.NormalizeTelop(string.Concat(chunk.Select(c => c.Word.Text)));
                    if (text.Length == 0) continue;
                    telops.Add(new Telop
                    {
                        Id = $"t{++n}", Type = TelopType.Caption, Start = R(chunk[0].Start), End = R(chunk[^1].End), Text = text, Style = "Caption",
                        SourceWordIds = chunk.Select(c => c.Word.Id).ToList(), Speaker = chunk[0].Word.Speaker,
                    });
                }
            }
            // hold captions into following gaps (less flicker) and relieve CPS (FR-TEL-02 step 2)
            telops = telops.OrderBy(t => t.Start).ToList();
            for (int i = 0; i < telops.Count; i++)
            {
                double next = i + 1 < telops.Count ? telops[i + 1].Start : total;
                double need = JapaneseText.DisplayWidth(telops[i].Text) / cap.MaxCps;
                double end = Math.Min(next, Math.Max(telops[i].End + 0.2, telops[i].Start + need));
                telops[i] = telops[i] with { End = R(Math.Max(Math.Min(telops[i].End, next), end)) };
            }
        }

        if (p.HasTelop("emphasis"))
        {
            // in time order; emphasis telops share one screen row, so they must never overlap in time
            var hits = emphasis.Select(w => (w, hit: FindPhrase(mapped, w))).ToList();
            foreach (var (w, _) in hits.Where(h => h.hit == null)) notes.Add($"強調語「{w}」が切り出し範囲にないため表示しません");
            foreach (var (w, hit) in hits.Where(h => h.hit != null).OrderBy(h => h.hit!.Value.start))
            {
                var start = hit!.Value.start;
                var end = Math.Min(total, Math.Max(hit.Value.end, start + EmphasisSeconds));
                var text = JapaneseText.NormalizeTelop(w);
                var prevIdx = telops.FindLastIndex(t => t.Type == TelopType.Emphasis);
                if (prevIdx >= 0 && telops[prevIdx].End > start)
                {
                    if (start - telops[prevIdx].Start >= 0.8) telops[prevIdx] = telops[prevIdx] with { End = R(start) };
                    else { notes.Add($"強調語「{w}」は直前の強調と重なるため表示しません"); continue; }
                }
                // an emphasis identical to the caption on screen replaces that caption (no duplicated text)
                telops.RemoveAll(t => t.Type == TelopType.Caption && t.Start < end && t.End > start && t.Text == text);
                telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Emphasis, Start = R(start), End = R(end), Text = text, Style = "Emph", Animation = "pop", SourceWordIds = hit.Value.ids });
            }
        }

        if (p.HasTelop("headline"))
        {
            double hs = 0;
            if (!string.IsNullOrWhiteSpace(hook)) { telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Hook, Start = 0, End = R(Math.Min(total, HookSeconds)), Text = hook, Style = "Hook", Animation = "pop" }); hs = HookSeconds; }
            if (!string.IsNullOrWhiteSpace(headline) && hs < total) telops.Add(new Telop { Id = $"t{++n}", Type = TelopType.Headline, Start = R(hs), End = R(total), Text = headline, Style = "Headline" });
            else if (string.IsNullOrWhiteSpace(headline) && !string.IsNullOrWhiteSpace(hook) && total > HookSeconds) { /* hook only */ }
        }
        return telops.OrderBy(t => t.Start).ThenBy(t => t.Type).ToList();
    }

    static double R(double x) => Math.Round(x, 3);

    /// <summary>
    /// Caption chunking: split at sentence ends / pauses, then break each phrase into balanced lines with a small
    /// dynamic program (≤ maxChars per line, never starting a line with a particle/small kana/punctuation,
    /// preferring breaks after punctuation and particles). Avoids greedy orphans like 「…いっぱい」+「ですよ」.
    /// </summary>
    public static List<List<Mapped>> Chunk(List<Mapped> words, int maxChars)
    {
        var res = new List<List<Mapped>>();
        var phrase = new List<Mapped>();
        void Flush() { if (phrase.Count > 0) res.AddRange(Balance(phrase, maxChars)); phrase = []; }
        for (int i = 0; i < words.Count; i++)
        {
            if (phrase.Count > 0 && words[i].Start - phrase[^1].End > 0.6) Flush();
            phrase.Add(words[i]);
            if (JapaneseText.BreakScore(words[i].Word.Text) == 4) Flush();
        }
        Flush();
        return res.Where(c => c.Any(x => !JapaneseText.IsPunct(x.Word.Text))).ToList();
    }

    static double W(Mapped m) => JapaneseText.DisplayWidth(JapaneseText.NormalizeTelop(m.Word.Text));

    static List<List<Mapped>> Balance(List<Mapped> ws, int maxChars)
    {
        int n = ws.Count;
        var widths = ws.Select(W).ToArray();
        double total = widths.Sum();
        if (total <= maxChars) return [ws];
        int lines = (int)Math.Ceiling(total / maxChars);
        double target = total / lines;
        var best = new double[n + 1]; var from = new int[n + 1];
        Array.Fill(best, double.PositiveInfinity); best[0] = 0;
        for (int j = 1; j <= n; j++)
        {
            double width = 0;
            for (int i = j - 1; i >= 0; i--)
            {
                width += widths[i];
                if (width > maxChars && j - i > 1) break;                       // a single over-long token is allowed alone
                if (double.IsInfinity(best[i])) continue;
                bool okStart = i == 0 || JapaneseText.CanStartLine(ws[i].Word.Text);
                double cost = (width - target) * (width - target) / Math.Max(1, target) + 2;   // +2 per line favours fewer lines
                if (!okStart) cost += 50;                                       // forbidden unless nothing else fits
                if (j < n) cost += (4 - JapaneseText.BreakScore(ws[j - 1].Word.Text)) * 3;   // prefer breaks after 、/particles
                if (best[i] + cost < best[j]) { best[j] = best[i] + cost; from[j] = i; }
            }
        }
        var res = new List<List<Mapped>>();
        for (int j = n; j > 0; j = from[j]) res.Insert(0, ws.GetRange(from[j], j - from[j]));
        return res;
    }

    static (double start, double end, List<string> ids)? FindPhrase(List<Mapped> mapped, string phrase)
    {
        var target = Norm(phrase);
        if (target.Length == 0) return null;
        for (int i = 0; i < mapped.Count; i++)
        {
            var acc = ""; var ids = new List<string>();
            for (int j = i; j < mapped.Count && acc.Length < target.Length + 4; j++)
            {
                acc += Norm(mapped[j].Word.Text); ids.Add(mapped[j].Word.Id);
                if (acc.Contains(target)) return (mapped[i].Start, mapped[j].End, ids);
            }
        }
        return null;
    }

    static string Norm(string s) => new(s.Normalize(System.Text.NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray());
}
