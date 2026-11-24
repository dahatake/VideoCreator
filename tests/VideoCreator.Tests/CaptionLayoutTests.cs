using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

/// <summary>字幕 (captions) in every preset: JIS X 4051 kinsoku and an automatically chosen length per caption.</summary>
public class CaptionLayoutTests
{
    /// <summary>ASR-style tokens (Azure splits words oddly: 「始｜まり｜ました」, 「パワープ｜ラットフォーム」), 0.25 s each, no pauses.</summary>
    static List<TelopBuilder.Mapped> Words(params string[] tokens) =>
        tokens.Select((t, i) => new TelopBuilder.Mapped(new Word($"w{i}", i * 0.25, i * 0.25 + 0.25, t, 1), i * 0.25, i * 0.25 + 0.25, 0)).ToList();

    static List<string> Texts(List<List<TelopBuilder.Mapped>> chunks) => chunks.Select(c => JapaneseText.NormalizeTelop(string.Concat(c.Select(x => x.Word.Text)))).ToList();

    [Theory]
    [InlineData('1', '0')]   // 数字の途中
    [InlineData('0', '円')]  // 数字と単位
    [InlineData('A', 'I')]   // 英単語の途中
    [InlineData('「', 'あ')] // 行末禁則: 始め括弧
    [InlineData('あ', '」')] // 行頭禁則: 終わり括弧
    [InlineData('か', 'っ')] // 行頭禁則: 小書き仮名
    [InlineData('ラ', 'ー')] // 行頭禁則: 長音
    [InlineData('ね', '…')] // 分離禁止
    public void BreakPenalty_forbids_jis_x4051_kinsoku(char a, char b) => Assert.Equal(JapaneseText.Forbidden, JapaneseText.BreakPenalty(a, b));

    [Fact]
    public void BreakPenalty_prefers_phrase_boundaries()
    {
        Assert.Equal(0, JapaneseText.BreakPenalty('、', '東'));
        Assert.True(JapaneseText.BreakPenalty('は', '東') < JapaneseText.BreakPenalty('東', 'は'));   // after a particle, not before 送り仮名
        Assert.True(JapaneseText.BreakPenalty('は', 'カ') < JapaneseText.BreakPenalty('カ', 'タ'));   // never inside a katakana word
    }

    [Theory]
    [InlineData("ました", false)]
    [InlineData("ですね", false)]
    [InlineData("っていうこと", false)]
    [InlineData("」と", false)]
    [InlineData("企画", true)]
    public void CanStartLine_rejects_auxiliaries_and_closing_marks(string token, bool ok) => Assert.Equal(ok, JapaneseText.CanStartLine(token));

    [Fact] // real ASR case: every split breaks some rule; JIS kinsoku (っ at the start) is worse than a caption starting with ですね
    public void Chunk_never_starts_a_caption_with_small_kana()
    {
        var caps = Texts(TelopBuilder.Chunk(Words("AI", "と", "AICOE", "が", "解決", "する", "っていうこと", "ですね", "。"), 10));
        Assert.All(caps.Skip(1), c => Assert.Empty(JapaneseText.KinsokuViolations([c])));
        Assert.DoesNotContain(caps, c => c.StartsWith('っ'));
    }

    [Fact] // a katakana word longer than a caption stays whole with its particle (the font shrinks) instead of 「パワープ｜ラットフォーム」 + 「の…」
    public void Chunk_keeps_long_words_and_their_particles_together()
    {
        var caps = Texts(TelopBuilder.Chunk(Words("なるほど", "、", "パワープ", "ラットフォーム", "の", "市民", "開発", "と", "。"), 10));
        Assert.Contains("パワープラットフォームの", caps);
        Assert.DoesNotContain(caps, c => c.StartsWith('の'));
    }

    [Fact] // 、 is shown as a full-width space inside a caption: it must count toward the width (「よし　じゃあ行きますか」 is 11, not 10)
    public void Chunk_counts_the_displayed_width_of_commas()
    {
        var caps = Texts(TelopBuilder.Chunk(Words("よし", "、", "じゃあ", "行き", "ます", "か", "。"), 10));
        Assert.True(caps.Count == 2, string.Join("|", caps));
        Assert.All(caps, c => Assert.True(JapaneseText.DisplayWidth(c) <= 10, c));
    }

    [Fact] // numbers stay with their counters; captions never split 「始｜まり」-style okurigana when a phrase boundary exists
    public void Chunk_breaks_at_phrase_boundaries()
    {
        var chunks = TelopBuilder.Chunk(Words("今日", "は", "1", "00", "円", "の", "もの", "だけ", "を", "買い", "ます", "。"), 10);
        var caps = Texts(chunks);
        Assert.True(caps.Count == 2, string.Join("|", caps));
        Assert.DoesNotContain(caps, c => c.EndsWith('1') || c.StartsWith("00") || c.StartsWith('円'));
        Assert.All(chunks.Skip(1), c => Assert.True(JapaneseText.CanStartLine(c[0].Word.Text), c[0].Word.Text));
    }

    [Fact] // very fast speech: tiny captions that would flicker are merged when the width allows
    public void Chunk_avoids_flickering_captions()
    {
        var caps = TelopBuilder.Chunk(Words("あの", "、", "それ", "で", "、", "えっと", "、", "それ", "から", "、", "あれ", "です", "けど", "、", "まあ", "いい", "か", "。"), 14);
        Assert.All(caps, c => Assert.True(c[^1].End - c[0].Start >= TelopBuilder.MinCaptionSec - 1e-9, Texts([c])[0]));
    }

    [Fact] // the per-line limit follows the rendered geometry: never wider than the safe zone, the preset value otherwise
    public void Caption_line_chars_are_derived_from_font_and_safe_zone()
    {
        foreach (var p in Fixtures.Presets.Values)
            Assert.Equal(p.Telop.IsVariety ? 10 : p.Telop.IsShort ? 11 : 14, AssWriter.CaptionLineChars(p));
        var p4 = Fixtures.Presets["P4"];
        var big = p4 with { Telop = p4.Telop with { Caption = p4.Telop.Caption with { FontPx = 110 } } };
        int n = AssWriter.CaptionLineChars(big);
        Assert.True(n < 14);
        Assert.True(AssWriter.FitFontSize(new string('あ', n), 110, 7, 1080, big.Telop.Caption.MarginR) == 110);   // a full line needs no shrinking
    }

    [Fact]
    public void WrapLines_breaks_two_lines_where_kinsoku_allows()
    {
        var lines = JapaneseText.WrapLines("今日はみんなでパワーアップの方法を話します", 14, 2);
        Assert.Equal(["今日はみんなで", "パワーアップの方法を話します"], lines);
        Assert.Empty(JapaneseText.KinsokuViolations(lines));
        // a katakana word that cannot fit is not split across lines: the line runs slightly long and the font shrinks instead
        Assert.Equal(["今日はパワープラットフォームの", "話をします"], JapaneseText.WrapLines("今日はパワープラットフォームの話をします", 14, 2));
        Assert.Single(JapaneseText.WrapLines("短い字幕", 14, 2));
        Assert.Single(JapaneseText.WrapLines("今日はみんなでパワーアップの方法を話します", 14, 1));
        Assert.Equal(["（笑）すごい", "ちょっと待って"], JapaneseText.WrapLines("（笑）すごい　ちょっと待って", 8, 2));
    }

    [Fact] // max_lines 2: video (ASS \N), SRT and QA bands use the same kinsoku-safe line break
    public void Two_line_captions_are_rendered_consistently()
    {
        var p4 = Fixtures.Presets["P4"];
        var two = p4 with { Telop = p4.Telop with { Caption = p4.Telop.Caption with { MaxLines = 2 } } };
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = two.Ref, Audio = new AudioSpec { Master = "a1" },
            Telops = [new Telop { Id = "t1", Type = TelopType.Caption, Start = 0, End = 4, Text = "今日はみんなでパワーアップの方法を話します", Style = "Caption" }] };
        var ass = AssWriter.Write(plan, two);
        Assert.Contains(@"今日はみんなで\Nパワーアップの方法を話します", ass);
        Assert.DoesNotContain(@"\fs", ass);                                         // fits on two lines: no shrinking
        Assert.Contains("今日はみんなで\nパワーアップの方法を話します", AssWriter.Srt(plan, two).ReplaceLineEndings("\n"));
        var one = AssWriter.Band(TelopType.Caption, p4, 1920)!.Value; var dbl = AssWriter.Band(TelopType.Caption, two, 1920)!.Value;
        Assert.Equal(one.Bottom, dbl.Bottom);
        Assert.True(dbl.Top < one.Top);
        var p3 = Fixtures.Presets["P3"];
        var varietyTwo = p3 with { Telop = p3.Telop with { Caption = p3.Telop.Caption with { MaxLines = 2 } } };
        Assert.Contains(@"\N", AssWriter.Write(plan with { Preset = varietyTwo.Ref }, varietyTwo));
    }

    // Independent oracle: W3C JLReq / UAX #14 classes (CL, CP, EX, IS, NS, OP), written out here rather than read from JapaneseText.
    const string NoStartOracle = "、。，．）」』】〕〉》］｝！？‼⁁⁈⁉：；・ゝゞヽヾー々ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ…‥)]}!?:;,.";
    const string NoEndOracle = "（「『【〔〈《［｛([{";

    static IEnumerable<string> Violations(string line)
    {
        var l = line.Trim(' ', '　');
        if (l.Length == 0) yield break;
        if (NoStartOracle.Contains(l[0])) yield return $"head {l[0]}";
        if (NoEndOracle.Contains(l[^1])) yield return $"tail {l[^1]}";
    }

    [Fact] // kinsoku outranks fit and balance: no break position the rules forbid is ever used, whatever the width
    public void WrapLines_never_breaks_a_rule_for_any_width()
    {
        string[] texts =
        [
            "今日は、みんなで（笑）パワーアップの方法を話します！", "えっ、そうなんですか？　すごいですね。", "「はい」と言って、『ありがとう』と答えた",
            "ちょっと待って、っていうかそれって本当に大丈夫なの", "（笑）（笑）（笑）（笑）（笑）（笑）（笑）", "あああああああああああ、、、、、、ああああ",
            "ええっ！？　まじで！？　やっぱりすごいー", "ここは「（重要）」の部分ですよね、ほんとに。",
        ];
        foreach (var text in texts)
            for (double width = 3; width <= 20; width++)
                foreach (var maxLines in new[] { 1, 2 })
                {
                    var lines = JapaneseText.WrapLines(text, width, maxLines);
                    Assert.True(lines.Count <= maxLines, $"{text} @ {width}");
                    Assert.All(lines, l => Assert.Empty(Violations(l)));
                    Assert.Equal(Compact(JapaneseText.KinsokuSafe(text)), Compact(string.Concat(lines)));   // only edge marks may disappear, never a word
                }
    }

    static string Compact(string s) => s.Replace(" ", "").Replace("　", "");

    [Fact] // every writer, every telop type and the cards: not one displayed line starts or ends against the rules
    public void Ass_output_has_no_kinsoku_violation_in_any_item()
    {
        foreach (var id in new[] { "P1", "P3", "P4", "P10" })
        {
            var p = Fixtures.Presets[id];
            var two = p with { Telop = p.Telop with { Caption = p.Telop.Caption with { MaxLines = 2 } } };
            var types = new[] { TelopType.Caption, TelopType.Emphasis, TelopType.Headline, TelopType.Hook, TelopType.Counter, TelopType.Sfx, TelopType.Tsukkomi };
            var telops = types.SelectMany((ty, i) => new[]
            {
                new Telop { Id = $"a{i}", Type = ty, Start = i * 5, End = i * 5 + 4, Text = "、ここは（重要）な話です。ちょっと待って（", Style = ty == TelopType.Caption ? "Caption" : ty.ToString() },
                new Telop { Id = $"b{i}", Type = ty, Start = i * 5, End = i * 5 + 4, Text = "）すごい！", Style = ty == TelopType.Caption ? "Caption" : ty.ToString() },
            }).ToList();
            var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = two.Ref, Audio = new AudioSpec { Master = "a1" }, Telops = telops };
            var ass = AssWriter.Write(plan, two);
            var shown = ass.Split('\n').Where(l => l.StartsWith("Dialogue:", StringComparison.Ordinal))
                .Select(l => System.Text.RegularExpressions.Regex.Replace(string.Join(",", l.Split(',').Skip(9)), @"\{[^}]*\}", "")).ToList();
            Assert.NotEmpty(shown);
            foreach (var d in shown)
                foreach (var line in d.Split(@"\N"))
                    Assert.True(!Violations(line).Any(), $"{id}: 「{line}」 in {d}");
        }
    }

    [Fact] // the display-edge cleaner drops only marks, never letters, ー, prefixed symbols or Latin dots
    public void KinsokuSafe_drops_only_edge_marks()
    {
        Assert.Equal("すごい", JapaneseText.KinsokuSafe("）すごい（"));
        Assert.Equal("C#", JapaneseText.KinsokuSafe("C#"));
        Assert.Equal(".NET", JapaneseText.KinsokuSafe(".NET"));
        Assert.Equal("ーすごい", JapaneseText.KinsokuSafe("ーすごい"));
    }

    [Fact] // S-08: generated captions pass; a hand edit that starts a caption with ー or ends it with 「 is flagged
    public void Qa_flags_kinsoku_in_captions()
    {
        var tr = Fixtures.Talk;
        foreach (var id in new[] { "P1", "P2", "P3", "P4", "P6", "P7", "P10" })
        {
            var p = Fixtures.Presets[id];
            var res = Solver.Plan([new Interval(0, 60)], tr, Fixtures.Signals(tr), p, Fixtures.Landscape(), "a1");
            var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips, Audio = new AudioSpec { Master = "a1" },
                Telops = TelopBuilder.Build(res.Clips, tr, p, "", "", [], []) };
            var qa = QaEngine.PlanChecks(new QaContext(plan, p, tr, ["a1"], []));
            Assert.True(QaStatus.Pass == qa.Single(q => q.CheckId == "S-08").Status, id + ": " + qa.Single(q => q.CheckId == "S-08").Measured);
            Assert.True(QaStatus.Pass == qa.Single(q => q.CheckId == "S-02").Status, id + ": " + qa.Single(q => q.CheckId == "S-02").Measured);
            var target = plan.Telops.First(t => t.Type == TelopType.Caption).Id;
            var edited = PlanEditor.Apply(plan, [new PlanOp("replace_text", target, "ーすごい「")]);
            Assert.Equal(QaStatus.Warn, QaEngine.PlanChecks(new QaContext(edited, p, tr, ["a1"], [])).Single(q => q.CheckId == "S-08").Status);
        }
    }
}
