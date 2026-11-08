using System.Text;
using System.Text.RegularExpressions;
using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>Conservative lexical evidence, not a semantic truth classifier (FR-TEL-12).</summary>
public static partial class TelopGrounding
{
    public static bool Applies(Telop t) => t.Type is TelopType.Emphasis or TelopType.Hook or TelopType.Headline or TelopType.Counter;

    static string ClaimText(Telop t, SeriesInfo? series)
    {
        var text = t.Text;
        if (series != null)
            foreach (var label in series.Labels.OrderByDescending(x => x.Length))
                text = text.Replace(label, "", StringComparison.Ordinal);
        return text;
    }

    public static bool RequiresWords(Telop t, IEnumerable<string>? dictionary = null, SeriesInfo? series = null)
    {
        var text = ClaimText(t, series);
        return Applies(t) && (Quantity().IsMatch(text.Normalize(NormalizationForm.FormKC))
            || Names(text).Count > 0 || (dictionary ?? []).Any(d => d.Length > 0 && text.Contains(d, StringComparison.Ordinal)));
    }

    public static List<Word> Evidence(Telop t, IReadOnlyList<Clip> clips, Transcript tr)
    {
        if (t.SourceWordIds.Count == 0 || t.SourceWordIds.Distinct().Count() != t.SourceWordIds.Count) return [];
        var first = tr.Words.FindIndex(w => w.Id == t.SourceWordIds[0]);
        if (first < 0 || first + t.SourceWordIds.Count > tr.Words.Count) return [];
        var words = tr.Words.Skip(first).Take(t.SourceWordIds.Count).ToList();
        if (!words.Select(w => w.Id).SequenceEqual(t.SourceWordIds)) return [];
        return words.All(w => clips.Any(c => w.T0 >= c.MasterIn - 0.15 && w.T1 <= c.MasterOut + 0.15)) ? words : [];
    }

    public static List<string> Problems(Telop t, IReadOnlyList<Clip> clips, Transcript tr, IEnumerable<string>? dictionary = null, SeriesInfo? series = null)
    {
        var words = Evidence(t, clips, tr);
        var problems = new List<string>();
        bool claim = RequiresWords(t, dictionary, series);
        if ((claim || t.Speaker != null || t.SourceWordIds.Count > 0) && words.Count == 0)
            problems.Add("根拠発話は区間内の連続した単語IDで指定してください");
        if (words.Count == 0) return problems;
        if (t.Speaker != null && words.Any(w => w.Speaker != t.Speaker))
            problems.Add("テロップと根拠発話の話者が異なります");
        if (!Applies(t)) return problems;
        var text = ClaimText(t, series);
        var source = string.Concat(words.Select(w => w.Text));
        foreach (var name in Names(text).Concat((dictionary ?? []).Where(d => d.Length > 0 && text.Contains(d, StringComparison.Ordinal))).Distinct())
            if (!source.Normalize(NormalizationForm.FormKC).Contains(name.Normalize(NormalizationForm.FormKC), StringComparison.OrdinalIgnoreCase))
                problems.Add($"「{name}」は根拠発話にありません");
        // A bag of numbers cannot prove their subject/unit relationship. Require an uninterrupted normalized phrase.
        if (Quantity().IsMatch(text.Normalize(NormalizationForm.FormKC)) && !Canonical(source).Contains(Canonical(text), StringComparison.Ordinal))
            problems.Add("数字・単位・対象の組み合わせを根拠発話で確認できません");
        return problems;
    }

    public static Telop Bind(Telop t, IReadOnlyList<Clip> clips, Transcript tr)
    {
        if (!Applies(t) || t.SourceWordIds.Count > 0) return t;
        foreach (var sentence in tr.Sentences)
        {
            var words = tr.Words.Skip(sentence.FirstWord).Take(sentence.LastWord - sentence.FirstWord + 1)
                .Where(w => clips.Any(c => w.T0 >= c.MasterIn - 0.15 && w.T1 <= c.MasterOut + 0.15)).ToList();
            if (words.Count == 0) continue;
            var bound = t with { SourceWordIds = words.Select(w => w.Id).ToList() };
            if (Problems(bound, clips, tr).Count == 0 && Canonical(string.Concat(words.Select(w => w.Text))).Contains(Canonical(t.Text), StringComparison.Ordinal))
                return bound;
        }
        return t;
    }

    public static bool NegationDropped(Telop t, IReadOnlyList<Clip> clips, Transcript tr) =>
        Applies(t) && Negation().IsMatch(string.Concat(Evidence(t, clips, tr).Select(w => w.Text)))
        && !Negation().IsMatch(t.Text);

    static List<string> Names(string text) => JapaneseText.ProperTokens(text)
        .Concat(LatinName().Matches(text.Normalize(NormalizationForm.FormKC)).Select(m => m.Value))
        .Where(n => n is not ("DAY" or "day")).Distinct().ToList();

    static string Canonical(string text)
    {
        text = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        text = Regex.Replace(text, @"(?:¥|￥)\s*([0-9,]+)", "$1円");
        text = Regex.Replace(text, @"day\s*([0-9]+)", "$1日", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"-(?=[0-9])", "minus");
        text = Quantity().Replace(text, m =>
        {
            var token = m.Value.Replace(",", "");
            if (decimal.TryParse(token, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var number))
                return number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "decimal", StringComparison.Ordinal);
            return JapaneseText.ParseNumber(token)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? token;
        });
        text = Regex.Replace(text, @"[はがをにでとの]", "");
        return string.Concat(text.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)));
    }

    [GeneratedRegex(@"[0-9][0-9,.]*[万億兆千百十]?(?:[0-9]+[万億兆千百十]?)*|[〇一二三四五六七八九十百千万億兆]+")]
    private static partial Regex Quantity();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9\-']*")]
    private static partial Regex LatinName();

    [GeneratedRegex(@"ない|なかった|なかっ|ません|じゃない|ではない|(?:せ|れ|ら|か|き|し|でき)ず")]
    private static partial Regex Negation();
}
