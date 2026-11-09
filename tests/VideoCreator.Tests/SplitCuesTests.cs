using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Presets;
using VideoCreator.Signals;

namespace VideoCreator.Tests;

public class SplitCuesTests
{
    [Theory] // is the utterance finished? (the previous part must not stop mid-thought)
    [InlineData("ぜひどっかだと思います", 1)]
    [InlineData("それで十五年ぐらい過ごしてます。", 1)]
    [InlineData("もう1回焼けないんですか？", 0.6)]
    [InlineData("あともう一個なんですけどねこれちょっと", -1)]
    [InlineData("面白い感想かなと思ったんですけど、", -1)]
    [InlineData("それを見てて", -1)]
    [InlineData("いや、高いんですけど。", 0)]
    public void Closure_separates_finished_from_cut_off_utterances(string text, double expected) => Assert.Equal(expected, SplitCues.Closure(text));

    [Theory] // does the next sentence lean on the previous one?
    [InlineData("で、もう完全にB to Cの仕組みを作ってた", -1)]
    [InlineData("だからそこが大事なんです。", -1)]
    [InlineData("それが一番大変でした。", -1)]
    [InlineData("その時は若手でした。", -0.6)]
    [InlineData("はい、だからまあ、唯一の落とし所はあれじゃないですか。", -1)]
    [InlineData("続いての企画は激辛ラーメンです。", 0)]
    [InlineData("あともう一個なんですけどね", 0)]
    public void Continuation_flags_back_references(string text, double expected) => Assert.Equal(expected, SplitCues.Continuation(text));

    // the real failure (Hub Radio Ep.014): the recogniser ran 「…と思います」 and 「あともう一個…」 into one sentence without 「。」,
    // so the only sentence boundary near the target was in the middle of 「これちょっと｜ちょっと面白い…」
    static Transcript MergedAsr(double pause)
    {
        string[] a = ["はい", "あと", "これ", "は", "ぜひ", "どっか", "だ", "と", "思い", "ます"];
        string[] b = ["あと", "もう", "一個", "なん", "です", "けど", "ね", "これ", "ちょっと"];
        var words = new List<Word>(); double t = 500;
        foreach (var x in a) { words.Add(new Word($"w{words.Count}", t, t + 0.2, x, 1)); t += 0.2; }
        t += pause;
        foreach (var x in b) { words.Add(new Word($"w{words.Count}", t, t + 0.2, x, 1)); t += 0.2; }
        var next = new Word($"w{words.Count}", t + 0.1, t + 0.4, "ちょっと", 1); words.Add(next);
        words.Add(new Word($"w{words.Count}", t + 0.4, t + 0.5, "、", 1));
        var s0 = new Sentence(0, 500, words[a.Length + b.Length - 1].T1, string.Concat(a.Concat(b)), 1, 0, a.Length + b.Length - 1);
        var s1 = new Sentence(1, next.T0, t + 0.5, "ちょっと、", 1, a.Length + b.Length, words.Count - 1);
        return new Transcript { Provider = "test", Duration = t + 1, Words = words, Sentences = [s0, s1] };
    }

    [Fact]
    public void Resegment_restores_the_sentence_end_the_recogniser_missed()
    {
        var tr = SplitCues.Resegment(MergedAsr(0.36));
        Assert.Equal(3, tr.Sentences.Count);
        Assert.Equal("はいあとこれはぜひどっかだと思います", tr.Sentences[0].Text);
        Assert.Equal("あともう一個なんですけどねこれちょっと", tr.Sentences[1].Text);
        Assert.Equal([0, 1, 2], tr.Sentences.Select(s => s.Index));
        Assert.Equal(1, SplitCues.Closure(tr.Sentences[0].Text));
        // every word still belongs to exactly one sentence, in order
        Assert.Equal(Enumerable.Range(0, tr.Words.Count), tr.Sentences.SelectMany(s => Enumerable.Range(s.FirstWord, s.LastWord - s.FirstWord + 1)));
        Assert.Equal(tr.Words[9].T1, tr.Sentences[0].T1);
        Assert.Equal(tr.Words[10].T0, tr.Sentences[1].T0);
    }

    [Fact] // without a pause there is no evidence of a sentence end; text that is not the concatenation of the words is left alone
    public void Resegment_needs_a_pause_and_consistent_words()
    {
        var tight = MergedAsr(0.05);
        Assert.Same(tight, SplitCues.Resegment(tight));
        var odd = MergedAsr(0.36);
        odd = odd with { Sentences = [odd.Sentences[0] with { Text = "別の表記です" }, odd.Sentences[1]] };
        Assert.Same(odd, SplitCues.Resegment(odd));
    }

    [Fact] // fade to black, a laugh that has just ended, and a laugh running over the cut
    public void Compute_reads_visual_and_laughter_cues()
    {
        var tr = SyntheticTalk.Build([30, 30]).Tr;
        var sig = new SignalSet { Blacks = [new Interval(tr.Sentences[3].T1 + 0.05, tr.Sentences[3].T1 + 0.3)] };
        var laughs = new List<Interval> { new(tr.Sentences[5].T1 - 0.8, tr.Sentences[5].T1 + 0.1), new(tr.Sentences[7].T1 - 1, tr.Sentences[8].T0 + 1) };
        var cues = SplitCues.Compute(tr, sig, laughs, sig.Blacks);
        Assert.True(cues[4].Fade);
        Assert.True(cues[6].LaughEnded);
        Assert.True(cues[8].LaughSpans);
        Assert.True(cues[4].Score > cues[2].Score && cues[6].Score > cues[2].Score && cues[8].Score < cues[2].Score);
        Assert.Contains("映像の暗転", SplitCues.Describe(cues[4]));
    }

    [Fact]
    public void Blackdetect_output_is_parsed()
    {
        const string err = "[blackdetect @ 0x1] black_start:22.1667 black_end:22.4333 black_duration:0.266667\n[blackdetect @ 0x1] black_start:44.17 black_end:44.43 black_duration:0.26";
        Assert.Equal([new Interval(22.1667, 22.4333), new Interval(44.17, 44.43)], SignalExtractor.ParseBlacks(err));
    }

    [Fact] // a topic start whose previous sentence trails off (「…けど、これちょっと」) is not used; the cut moves to a finished utterance
    public void Split_avoids_ending_a_part_mid_thought()
    {
        int[] topics = [100, 140, 90, 160, 120, 110, 150, 90];
        var (tr, truth) = SyntheticTalk.Build(topics);
        var s = tr.Sentences.ToList();
        int c = truth[4];
        s[c - 1] = s[c - 1] with { Text = "今日の映画は監督が大事なんですけど、これちょっと" };
        tr = tr with { Sentences = s };
        var sig = Fixtures.Signals(tr);
        var notes = new List<string>();
        var parts = SeriesPlanner.Split(tr, sig, Fixtures.Presets["P4"], tr.Duration, 8 * 60, SeriesPlanner.FallbackOutline(tr, sig), notes);
        Assert.Equal(2, parts.Count);
        Assert.NotEqual(c, parts[1].First);
        Assert.True(SplitCues.Closure(tr.Sentences[parts[1].First - 1].Text) > 0);
        Assert.DoesNotContain(notes, n => n.Contains("話の途中で終わっています"));
        Assert.All(parts, p => Assert.InRange(p.EstSec, p.MinSec, p.MaxSec));
    }

    [Fact] // the real failure mode: the AI rated a mid-thought line as the best split (9–10/10); the cut must not follow it there
    public void A_high_ai_split_score_does_not_override_a_cut_off_utterance()
    {
        int[] topics = [100, 140, 150, 100, 120, 110, 150, 90];   // starts ≈ 0,100,240,390,490,610,720,870 s
        var (tr, truth) = SyntheticTalk.Build(topics);
        var s = tr.Sentences.ToList();
        int x = truth[4] - 2;                                     // inside a topic, next to the balanced target: the AI says "split here" (10/10)
        s[x - 1] = s[x - 1] with { Text = "あともう一個なんですけどねこれちょっと" };
        s[x] = s[x] with { Text = "ちょっと面白い感想かなと思ったんですけど、" };
        tr = tr with { Sentences = s };
        var sig = Fixtures.Signals(tr);
        var outline = new ChapterOutline("テスト", truth.Select((t, k) => new ChapterMark(t, $"章{k}", 1, t + 1, [])).Append(new ChapterMark(x, "途中", 10, x, [])).OrderBy(m => m.Start).ToList(), "llm");
        var parts = SeriesPlanner.Split(tr, sig, Fixtures.Presets["P4"], tr.Duration, 8 * 60, outline, []);
        Assert.NotEqual(x, parts[1].First);
        Assert.True(SplitCues.Closure(tr.Sentences[parts[1].First - 1].Text) > 0);
    }
}
