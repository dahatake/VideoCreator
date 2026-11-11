using VideoCreator.Models;

namespace VideoCreator.Planning;

public static class CaptionText
{
    public static string Normalize(string text, string language) =>
        language == "ja-JP" ? JapaneseText.NormalizeTelop(text) : text.Trim();

    public static string Join(IEnumerable<string> words, string language) => Normalize(VideoLanguages.JoinWords(words, language), language);

    public static double BreakCost(string previous, string next, string language) =>
        language == "ja-JP" ? JapaneseText.TokenBreakCost(previous, next)
        : previous.Length > 0 && next.Length > 0 && (JapaneseText.NoLineStart.Contains(next[0]) || JapaneseText.NoLineEnd.Contains(previous[^1]))
            ? JapaneseText.Forbidden : 0;

    public static List<string> WrapLines(string text, double maxWidth, int maxLines, string language)
    {
        if (language is "ja-JP" or "zh-CN") return JapaneseText.WrapLines(text, maxWidth, maxLines);
        if (maxLines <= 1 || JapaneseText.DisplayWidth(text) <= maxWidth) return [text];
        var choices = Enumerable.Range(1, Math.Max(0, text.Length - 1))
            .Where(i => char.IsWhiteSpace(text[i]) && !char.IsWhiteSpace(text[i - 1]))
            .Select(i => (Left: text[..i].TrimEnd(), Right: text[i..].TrimStart()))
            .Where(x => x.Left.Length > 0 && x.Right.Length > 0 && !JapaneseText.NoLineStart.Contains(x.Right[0]))
            .Select(x => (x.Left, x.Right, Wl: JapaneseText.DisplayWidth(x.Left), Wr: JapaneseText.DisplayWidth(x.Right)))
            .OrderBy(x => Math.Max(0, x.Wl - maxWidth) + Math.Max(0, x.Wr - maxWidth))
            .ThenBy(x => Math.Abs(x.Wl - x.Wr)).ToList();
        return choices.Count == 0 ? [text] : [choices[0].Left, choices[0].Right];
    }
}
