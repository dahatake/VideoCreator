using System.Text;
using System.Text.RegularExpressions;

namespace VideoCreator.Planning;

/// <summary>Japanese text rules used by telops (C-3, FR-TEL-03) and the faithfulness check (FR-TEL-09 / H-07).</summary>
public static partial class JapaneseText
{
    static readonly HashSet<string> Particles =
    [
        "は", "が", "を", "に", "で", "と", "の", "も", "へ", "や", "か", "ね", "よ", "な", "わ", "さ", "ぞ", "ぜ",
        "から", "まで", "より", "けど", "けれど", "ので", "のに", "って", "には", "では", "とは", "でも", "のは", "のが", "のを", "だけ", "だけで", "しか", "など", "ても", "でした", "です", "ます", "ません",
    ];
    const string NoLineStart = "、。，．・：；？！?!）」』】〕ー～ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ々…";
    const string Punct = "、。，．！？!?…";

    /// <summary>Netflix-style counting: full-width = 1, half-width = 0.5 (S-02).</summary>
    public static double DisplayWidth(string s)
    {
        double w = 0;
        foreach (var c in s) w += c < 0x2E80 || (c >= 0xFF61 && c <= 0xFFDC) ? 0.5 : 1;
        return w;
    }

    /// <summary>Telop display text: half-width digits/latin (look tighter on screen), drop 。, 、 -> full-width space.</summary>
    public static string NormalizeTelop(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is >= '０' and <= '９' or >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') sb.Append((char)(c - 0xFEE0));
            else if (c == '。') continue;
            else if (c == '、' || c == ',') sb.Append('　');
            else sb.Append(c);
        }
        return MultiSpace().Replace(sb.ToString(), "　").Trim('　', ' ');
    }

    public static bool IsPunct(string token) => token.Length > 0 && token.All(c => Punct.Contains(c));
    public static bool IsParticle(string token) => Particles.Contains(token);
    public static bool CanStartLine(string token) => token.Length > 0 && !NoLineStart.Contains(token[0]) && !IsParticle(token);

    /// <summary>Score for breaking AFTER a token (higher is better).</summary>
    public static int BreakScore(string token)
    {
        if (token.Length == 0) return 0;
        if ("。！？!?".Contains(token[^1])) return 4;
        if ("、,…".Contains(token[^1])) return 3;
        if (IsParticle(token)) return 2;
        return 1;
    }

    static readonly string[] Demonstratives = ["それ", "あれ", "これ", "その", "あの", "この", "そこ", "あそこ", "そういう", "ああいう", "で、", "でも", "だから"];
    public static bool StartsWithDemonstrative(string sentence) => Demonstratives.Any(d => sentence.TrimStart('　', ' ').StartsWith(d, StringComparison.Ordinal));

    // ---- numbers: canonical integer values for "1万", "一万", "九千九百", "100", "1,000" ----
    const string KanjiDigits = "〇一二三四五六七八九";
    static readonly Dictionary<char, long> SmallUnits = new() { ['十'] = 10, ['百'] = 100, ['千'] = 1000 };
    static readonly Dictionary<char, long> BigUnits = new() { ['万'] = 10_000, ['億'] = 100_000_000, ['兆'] = 1_000_000_000_000 };

    public static List<long> NumberValues(string text)
    {
        var res = new List<long>();
        foreach (Match m in NumberToken().Matches(Fold(text)))
        {
            var v = ParseNumber(m.Value);
            if (v is long x) res.Add(x);
        }
        return res;
    }

    static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c is >= '０' and <= '９' ? (char)(c - 0xFEE0) : c);
        return sb.ToString();
    }

    public static long? ParseNumber(string token)
    {
        token = token.Replace(",", "");
        if (token.Length == 0) return null;
        long total = 0, section = 0, current = 0; bool any = false; bool digitRun = false;
        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (char.IsAsciiDigit(c))
            {
                int j = i; while (j < token.Length && (char.IsAsciiDigit(token[j]) || token[j] == '.')) j++;
                if (!double.TryParse(token[i..j], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return null;
                current = (long)d; any = true; digitRun = true; i = j - 1;
            }
            else if (KanjiDigits.IndexOf(c) is var k and >= 0) { current = digitRun ? current * 10 + k : k; digitRun = true; any = true; }
            else if (SmallUnits.TryGetValue(c, out var su)) { section += (current == 0 ? 1 : current) * su; current = 0; digitRun = false; any = true; }
            else if (BigUnits.TryGetValue(c, out var bu)) { total += (section + current == 0 ? 1 : section + current) * bu; section = 0; current = 0; digitRun = false; any = true; }
            else return null;
        }
        return any ? total + section + current : null;
    }

    /// <summary>Katakana runs and Latin words (proxy for proper nouns / product names) — H-07 checks these verbatim.</summary>
    public static List<string> ProperTokens(string text) =>
        ProperToken().Matches(text.Normalize(NormalizationForm.FormKC)).Select(m => m.Value).Where(v => v.Length >= 2 && v.Any(c => c != 'ー')).Distinct().ToList();

    /// <summary>Returns the claims in <paramref name="telop"/> that are not grounded in <paramref name="source"/> or the user dictionary.</summary>
    public static List<string> UngroundedClaims(string telop, string source, IEnumerable<string>? dictionary = null)
    {
        var problems = new List<string>();
        var srcNumbers = NumberValues(source).ToHashSet();
        foreach (Match m in NumberToken().Matches(Fold(telop)))
            if (ParseNumber(m.Value) is long v && !srcNumbers.Contains(v)) problems.Add(m.Value);
        var normSrc = source.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var dict = (dictionary ?? []).Select(d => d.Normalize(NormalizationForm.FormKC).ToLowerInvariant()).ToList();
        foreach (var tok in ProperTokens(telop))
        {
            var t = tok.ToLowerInvariant();
            if (!normSrc.Contains(t) && !dict.Any(d => d.Contains(t))) problems.Add(tok);
        }
        return problems;
    }

    [GeneratedRegex(@"[0-9][0-9,\.]*[万億兆千百十]?(?:[0-9]+[万億兆千百十]?)*|[〇一二三四五六七八九十百千万億兆]+")]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"[ァ-ヴー]+|[A-Za-z][A-Za-z0-9\-']+")]
    private static partial Regex ProperToken();

    [GeneratedRegex("[　 ]{2,}")]
    private static partial Regex MultiSpace();
}
