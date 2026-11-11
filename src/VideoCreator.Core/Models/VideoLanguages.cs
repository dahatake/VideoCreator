namespace VideoCreator.Models;

public static class VideoLanguages
{
    public static bool IsSupported(string? language) => language is "ja-JP" or "en-US" or "zh-CN" or "ko-KR";

    public static string Name(string language) => language switch
    {
        "ja-JP" => "Japanese",
        "en-US" => "English",
        "zh-CN" => "Simplified Chinese",
        "ko-KR" => "Korean",
        _ => throw new ArgumentException("Unsupported video language: " + language),
    };

    public static string Prompt(string language) =>
        $"\nOUTPUT LANGUAGE REQUIREMENT: The video's language is {Name(language)} ({language}). " +
        "Write all viewer-visible text (hooks, headlines, chapter titles, reactions, cards and post copy) in that language, " +
        "not in the language of these instructions or examples. This overrides earlier language-specific wording. " +
        "Keep quoted speech and emphasis words verbatim in the source language; never translate or summarize spoken captions. " +
        "JSON property names and identifiers must remain unchanged.";

    public static string JoinWords(IEnumerable<string> words, string language)
    {
        if (language is not ("en-US" or "ko-KR")) return string.Concat(words);
        var result = new System.Text.StringBuilder();
        foreach (var word in words.Where(w => w.Length > 0))
        {
            bool closingPunctuation = char.IsPunctuation(word[0]) && !"([{“‘".Contains(word[0])
                && !(word[0] == '"' && word.Length > 1);
            if (result.Length > 0 && !char.IsWhiteSpace(result[^1]) && !char.IsWhiteSpace(word[0])
                && !closingPunctuation && !"([{“‘".Contains(result[^1]))
                result.Append(' ');
            result.Append(word);
        }
        return result.ToString();
    }

    public static string Text(string language, string ja, string en, string zh, string ko) => language switch
    {
        "ja-JP" => ja, "en-US" => en, "zh-CN" => zh, "ko-KR" => ko,
        _ => throw new ArgumentException("Unsupported video language: " + language),
    };
}
