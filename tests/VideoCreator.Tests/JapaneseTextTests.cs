using VideoCreator.Planning;

namespace VideoCreator.Tests;

public class JapaneseTextTests
{
    [Theory]
    [InlineData("1万", 10_000)]
    [InlineData("一万", 10_000)]
    [InlineData("九千九百", 9_900)]
    [InlineData("9900", 9_900)]
    [InlineData("百", 100)]
    [InlineData("二十三", 23)]
    [InlineData("1,000", 1_000)]
    [InlineData("1万5千", 15_000)]
    [InlineData("二〇二六", 2_026)]
    public void ParseNumber_canonicalises_arabic_and_kanji(string token, long expected) => Assert.Equal(expected, JapaneseText.ParseNumber(token));

    [Fact]
    public void NumberValues_finds_all_quantities() =>
        Assert.Equal([1, 10_000, 100], JapaneseText.NumberValues("一時間で1万円、百円のもの"));

    [Fact] // AT-05: a number that is not in the transcript is flagged
    public void UngroundedClaims_flags_invented_numbers()
    {
        var src = Fixtures.Talk.FullText;
        Assert.Empty(JapaneseText.UngroundedClaims("1万円を使い切る", src));
        Assert.Empty(JapaneseText.UngroundedClaims("一万円を使い切る", src));      // kanji form of a number that exists as 1万
        Assert.Equal(["500万"], JapaneseText.UngroundedClaims("500万円の賞金", src));
    }

    [Fact]
    public void UngroundedClaims_checks_katakana_and_latin_tokens_and_dictionary()
    {
        var src = Fixtures.Talk.FullText;
        Assert.Empty(JapaneseText.UngroundedClaims("ポテトチップス祭り", src));
        Assert.Equal(["ユーチューブ"], JapaneseText.UngroundedClaims("ユーチューブ限定", src));
        Assert.Empty(JapaneseText.UngroundedClaims("Contoso 特集", src + "", ["Contoso"]));
    }

    [Theory]
    [InlineData("１００万円。", "100万円")]
    [InlineData("はい、始まりました。", "はい　始まりました")]
    public void NormalizeTelop_halfwidth_digits_and_punctuation(string input, string expected) => Assert.Equal(expected, JapaneseText.NormalizeTelop(input));

    [Fact]
    public void DisplayWidth_counts_halfwidth_as_half() => Assert.Equal(3.5, JapaneseText.DisplayWidth("100万円"));

    [Theory]
    [InlineData("は", false)]
    [InlineData("ー", false)]
    [InlineData("っ", false)]
    [InlineData("、", false)]
    [InlineData("ルール", true)]
    public void CanStartLine_rejects_particles_and_small_kana(string token, bool ok) => Assert.Equal(ok, JapaneseText.CanStartLine(token));

    [Theory]
    [InlineData("それって100個買うってことですか", true)]
    [InlineData("ルールがあります", false)]
    public void Demonstrative_detection(string s, bool expected) => Assert.Equal(expected, JapaneseText.StartsWithDemonstrative(s));
}
