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
    /// <summary>
    /// 行頭禁則 (JIS X 4051): closing brackets cl-02, hyphens cl-03, dividing punctuation cl-04, middle dots cl-05, full stops cl-06,
    /// commas cl-07, inseparable cl-08, iteration marks cl-09, prolonged sound mark cl-10, small kana cl-11, postfixed units cl-18.
    /// </summary>
    public const string NoLineStart = "、。，．,.・･：；:;？！?!‼⁇⁈⁉）」』】〕〉》］｝〙〗〟’”)]}｠»｣‐゠–〜～ーｰ々〻ゝゞヽヾ" +
        "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶㇰㇱㇲㇳㇴㇵㇶㇷㇸㇹㇺㇻㇼㇽㇾㇿｧｨｩｪｫｯｬｭｮ…‥%％‰℃°′″";
    /// <summary>行末禁則 (JIS X 4051): opening brackets cl-01 and prefixed currency/number signs cl-19.</summary>
    public const string NoLineEnd = "（「『【〔〈《［｛〘〖〝‘“([{｟«｢￥＄¥$＃#№";
    /// <summary>Counters/units that must stay with the number before them (分離禁止: 「100｜円」).</summary>
    const string Counters = "円万億兆千百十個人回年月日時分秒歳本枚台件杯匹頭冊着足品点倍位度割番階号代周週種問曲話kKmMgGcC";
    /// <summary>Auxiliaries/suffixes that belong to the previous word; a caption starting with them reads as a broken word (「行き｜ました」).</summary>
    static readonly HashSet<string> Attached =
    [
        "た", "だ", "て", "ない", "なかった", "たい", "たかった", "れる", "られる", "せる", "させる", "ちゃう", "ちゃった", "じゃう", "てる", "てた",
        "ました", "まして", "ませ", "まし", "だった", "でしょ", "でしょう", "だろう", "ですか", "ですね", "ですよ", "ますか", "ますね", "ますよ", "ましょう",
    ];
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
    public static bool CanStartLine(string token) => token.Length > 0 && !NoLineStart.Contains(token[0]) && !IsParticle(token) && !Attached.Contains(token);
    public static bool CanEndLine(string token) => token.Length > 0 && !NoLineEnd.Contains(token[^1]);

    /// <summary>A break the layout must not make unless nothing else fits (kinsoku / inseparable).</summary>
    public const double Forbidden = 60;
    /// <summary>Starting a caption with a particle/auxiliary: avoided like kinsoku, but when every option breaks a rule this is the lesser evil.</summary>
    public const double Detached = 30;

    enum Kind { Other, Space, Hiragana, Katakana, Kanji, Digit, Latin }

    static Kind KindOf(char c) => c switch
    {
        ' ' or '　' => Kind.Space,
        >= 'ぁ' and <= 'ゖ' => Kind.Hiragana,
        >= 'ァ' and <= 'ヺ' or 'ー' or >= 'ｦ' and <= 'ﾟ' => Kind.Katakana,
        >= '0' and <= '9' or >= '０' and <= '９' => Kind.Digit,
        >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ' => Kind.Latin,
        >= '一' and <= '鿿' or '々' or '〆' or '〇' => Kind.Kanji,
        _ => Kind.Other,
    };

    static bool IsNumeral(char c) => KindOf(c) == Kind.Digit || "〇一二三四五六七八九十百千万億兆".Contains(c);

    /// <summary>
    /// Cost of a line/caption break between the characters <paramref name="a"/> and <paramref name="b"/> (0 = ideal):
    /// JIS X 4051 kinsoku and 分離禁止 are <see cref="Forbidden"/>; otherwise the character-class transition approximates a 文節 boundary
    /// (hiragana → kanji/katakana usually starts a new phrase; kanji → hiragana is usually 送り仮名 or an attached particle).
    /// </summary>
    public static double BreakPenalty(char a, char b)
    {
        if (NoLineStart.Contains(b) || NoLineEnd.Contains(a)) return Forbidden;
        var ka = KindOf(a); var kb = KindOf(b);
        if (ka == Kind.Digit && (kb == Kind.Digit || b is ',' or '.' or '，' or '．')) return Forbidden;
        if (IsNumeral(a) && Counters.Contains(b)) return Forbidden;
        if (ka == Kind.Latin && kb == Kind.Latin) return Forbidden;
        if (ka == Kind.Space || kb == Kind.Space || "、。，．！？!?…".Contains(a) || "）」』】〕〉》］｝”’".Contains(a)) return 0;
        if (NoLineEnd.Contains(b)) return 1;
        // a word followed by は/が/を/の… : the hiragana is almost always its particle (「パワープラットフォーム｜の話」)
        if (ka is Kind.Kanji or Kind.Katakana or Kind.Latin or Kind.Digit && "はがをにでとのもへや".Contains(b)) return 20;
        return (ka, kb) switch
        {
            (Kind.Hiragana, Kind.Kanji or Kind.Katakana or Kind.Digit or Kind.Latin) => 2,
            (Kind.Kanji, Kind.Katakana) or (Kind.Katakana, Kind.Kanji) or (Kind.Digit or Kind.Latin, Kind.Kanji or Kind.Katakana) => 4,
            (Kind.Hiragana, Kind.Hiragana) => 5,
            (Kind.Kanji, Kind.Kanji) => 6,
            (Kind.Kanji or Kind.Katakana or Kind.Digit or Kind.Latin, Kind.Hiragana) => 8,
            (Kind.Katakana, Kind.Katakana) => 10,
            _ => 4,
        };
    }

    /// <summary>True when the break lies inside one katakana / Latin / number word that ASR split into tokens (「パワープ｜ラットフォーム」).</summary>
    public static bool InsideWord(string prev, string next)
    {
        if (prev.Length == 0 || next.Length == 0) return false;
        var (ka, kb) = (KindOf(prev[^1]), KindOf(next[0]));
        return ka == kb && ka is Kind.Katakana or Kind.Latin or Kind.Digit;
    }

    /// <summary>Cost of starting a new caption with token <paramref name="next"/> after token <paramref name="prev"/> (ASR words): token rules + character rules.</summary>
    public static double TokenBreakCost(string prev, string next)
    {
        if (prev.Length == 0 || next.Length == 0) return 0;
        var c = BreakPenalty(prev[^1], next[0]);
        if (c >= Forbidden) return c;
        if (IsParticle(next) || Attached.Contains(next)) return Detached;
        // no Japanese word starts with ん, and a hiragana る right after a word is its inflection (「やって｜るんです」)
        if (next[0] == 'ん' || next[0] == 'る' && KindOf(prev[^1]) is Kind.Hiragana or Kind.Kanji) return Detached;
        if (IsParticle(prev) || prev.Length > 1 && IsParticle(prev[^1].ToString()) && KindOf(prev[^2]) != Kind.Hiragana) c = Math.Min(c, 1);   // 文節 ends after a particle
        if (BreakScore(prev) >= 3) c = 0;
        return c;
    }

    /// <summary>
    /// Splits caption text into at most <paramref name="maxLines"/> (1..2) lines of ≤ <paramref name="maxWidth"/>, breaking only where kinsoku allows,
    /// preferring 文節 boundaries and balanced (slightly bottom-heavy) lines. Text that fits stays on one line.
    /// </summary>
    public static List<string> WrapLines(string text, double maxWidth, int maxLines)
    {
        if (maxLines <= 1 || text.Length < 2 || DisplayWidth(text) <= maxWidth) return [text];
        int best = -1; double bestCost = double.PositiveInfinity;
        for (int k = 1; k < text.Length; k++)
        {
            if (char.IsLowSurrogate(text[k])) continue;
            var left = text[..k].TrimEnd(' ', '　'); var right = text[k..].TrimStart(' ', '　');
            if (left.Length == 0 || right.Length == 0) continue;
            double wl = DisplayWidth(left), wr = DisplayWidth(right);
            // splitting a word across lines reads worse than shrinking a slightly long line
            double cost = BreakPenalty(text[k - 1], text[k]) + (InsideWord(text[..k], text[k..]) ? 30 : 0) + Math.Abs(wl - wr) * 0.6 + (wl > wr ? 0.5 : 0)
                + (Math.Max(0, wl - maxWidth) + Math.Max(0, wr - maxWidth)) * 12;
            if (cost < bestCost) { bestCost = cost; best = k; }
        }
        return best < 0 ? [text] : [text[..best].TrimEnd(' ', '　'), text[best..].TrimStart(' ', '　')];
    }

    /// <summary>Kinsoku problems of a displayed caption (line starts/ends that JIS X 4051 forbids), for QA.</summary>
    public static IEnumerable<string> KinsokuViolations(IEnumerable<string> lines)
    {
        foreach (var l in lines.Select(l => l.Trim(' ', '　')).Where(l => l.Length > 0))
        {
            if (NoLineStart.Contains(l[0])) yield return $"行頭「{l[0]}」";
            if (NoLineEnd.Contains(l[^1])) yield return $"行末「{l[^1]}」";
        }
    }

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
