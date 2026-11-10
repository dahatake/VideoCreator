using System.Text;
using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>
/// 伏せ字: words the user wants hidden (a name, a shop…) are replaced by ● in every telop/SRT/card and the dialogue under them is replaced by a tone.
/// Matching is per character after NFKC (half/full width and case insensitive), across ASR word boundaries, so a name split into two words or two captions is still hidden.
/// </summary>
public static class Masking
{
    public const char Dot = '●';
    public const int MaxWords = 20, MaxWordLength = 30;

    public static List<string> Clean(IEnumerable<string>? words) =>
        (words ?? []).Select(w => w.Trim()).Where(w => w.Length > 0).Select(w => w.Length > MaxWordLength ? w[..MaxWordLength] : w)
            .DistinctBy(Key).Take(MaxWords).ToList();

    static string Key(string s) => new(s.Select(Fold).ToArray());

    /// <summary>One character folded so that the text keeps its length (NFKC when it maps to a single char, then lower case).</summary>
    static char Fold(char c)
    {
        if (char.IsSurrogate(c)) return c;   // half of an emoji etc.: cannot be normalized alone
        var n = c.ToString().Normalize(NormalizationForm.FormKC);
        return char.ToLowerInvariant(n.Length == 1 ? n[0] : c);
    }

    /// <summary>Which characters of <paramref name="text"/> belong to a masked word.</summary>
    static bool[] Marks(string text, IReadOnlyList<string> words)
    {
        var marks = new bool[text.Length];
        var folded = Key(text);
        foreach (var w in words.Select(Key).Where(k => k.Length > 0))
            for (int i = folded.IndexOf(w, StringComparison.Ordinal); i >= 0; i = folded.IndexOf(w, i + 1, StringComparison.Ordinal))
                Array.Fill(marks, true, i, w.Length);
        return marks;
    }

    public static string MaskText(string text, IReadOnlyList<string> words)
    {
        if (words.Count == 0 || text.Length == 0) return text;
        var m = Marks(text, words);
        if (!m.Contains(true)) return text;
        var a = text.ToCharArray();
        for (int i = 0; i < a.Length; i++) if (m[i]) a[i] = Dot;
        return new string(a);
    }

    public static bool Contains(string text, IReadOnlyList<string> words) => words.Count > 0 && Marks(text, words).Contains(true);

    /// <summary>The transcript with masked characters replaced (word timing unchanged) and the source-time spans of every word that held a masked character.</summary>
    public static (Transcript Masked, List<Interval> Spans) Apply(Transcript tr, IReadOnlyList<string> words)
    {
        if (words.Count == 0 || tr.Words.Count == 0) return (tr, []);
        var all = string.Concat(tr.Words.Select(w => w.Text));
        var marks = Marks(all, words);
        if (!marks.Contains(true)) return (tr, []);
        var newWords = new List<Word>(tr.Words.Count); var spans = new List<Interval>(); int pos = 0;
        foreach (var w in tr.Words)
        {
            var a = w.Text.ToCharArray(); bool hit = false;
            for (int i = 0; i < a.Length; i++) if (marks[pos + i]) { a[i] = Dot; hit = true; }
            pos += a.Length;
            newWords.Add(hit ? w with { Text = new string(a) } : w);
            if (!hit) continue;
            if (spans.Count > 0 && w.T0 - spans[^1].T1 < 0.05) spans[^1] = spans[^1] with { T1 = Math.Max(spans[^1].T1, w.T1) };
            else spans.Add(new Interval(w.T0, w.T1));
        }
        var sentences = tr.Sentences.Select(s => s with
        {
            Text = s.FirstWord >= 0 && s.LastWord < newWords.Count && s.LastWord >= s.FirstWord
                ? string.Concat(newWords.Skip(s.FirstWord).Take(s.LastWord - s.FirstWord + 1).Select(w => w.Text)) : MaskText(s.Text, words),
        }).ToList();
        return (tr with { Words = newWords, Sentences = sentences }, spans);
    }

    /// <summary>Source-time spans → output (body) timeline, split at clip boundaries; spans outside the clips are dropped.</summary>
    public static List<Interval> ToOutput(IReadOnlyList<Clip> clips, IReadOnlyList<Interval> spans)
    {
        var res = new List<Interval>(); double off = 0;
        foreach (var c in clips)
        {
            foreach (var s in spans)
            {
                double a = Math.Max(s.T0, c.MasterIn), b = Math.Min(s.T1, c.MasterOut);
                if (b - a > 0.005) res.Add(new Interval(Math.Round(off + (a - c.MasterIn) / c.Speed, 3), Math.Round(off + (b - c.MasterIn) / c.Speed, 3)));
            }
            off += c.Length;
        }
        return res.OrderBy(i => i.T0).ToList();
    }
}
