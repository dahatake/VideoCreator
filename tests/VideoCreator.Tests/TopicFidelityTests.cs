using VideoCreator.Models;
using VideoCreator.Planning;
using Xunit;

namespace VideoCreator.Tests;

/// <summary>
/// A series cut must follow a change of SUBJECT (the scene), never the clock and never a pleasant sentence ending.
/// The talk is built so that the true topic start is known, the topics are NOT announced by a marker phrase, the two subjects share
/// vocabulary, and a tempting decoy is planted in the middle of a topic exactly where the clock midpoint is
/// (a long pause, a finished sentence, a wrap-up phrase, laughter ending) — everything a word-level / duration-level planner likes.
/// </summary>
public class TopicFidelityTests
{
    static readonly string[][] Subjects =
    [
        ["Azure", "サブスクリプション", "リソースグループ", "仮想マシン", "ストレージ", "ネットワーク", "課金", "リージョン"],
        ["Copilot", "プロンプト", "コード補完", "チャット", "拡張機能", "エージェント", "提案", "リポジトリ"],
        ["Excel", "関数", "ピボット", "セル", "グラフ", "マクロ", "シート", "集計"],
    ];
    static readonly string[] Shared = ["設定", "便利", "使い方", "料金", "ポイント", "画面"];
    static readonly string[] Forms =
    [
        "{a}の{b}は{c}が大事です。", "{a}で{b}を{c}にします。", "{b}と{c}の違いは{a}にあります。", "実は{a}の{b}が{c}なんですよね。", "{a}を使うと{b}も{c}も楽です。",
    ];

    /// <summary>topicSecs: seconds per subject. decoyAtSec: master time of a decoy inside a topic (a clean ending after a long pause, with laughter before).</summary>
    static (Transcript Tr, List<int> Starts, List<Interval> Laughs, int Decoy) Build(int[] topicSecs, double decoyAtSec, int seed = 11)
    {
        var rnd = new Random(seed);
        var words = new List<Word>(); var sents = new List<Sentence>(); var starts = new List<int>(); var laughs = new List<Interval>();
        double t = 1.0; int decoy = -1;
        for (int k = 0; k < topicSecs.Length; k++)
        {
            var v = Subjects[k % Subjects.Length];
            starts.Add(sents.Count);
            double end = t + topicSecs[k];
            for (int i = 0; t < end; i++)
            {
                string Pick() => rnd.Next(10) < 3 ? Shared[rnd.Next(Shared.Length)] : v[rnd.Next(v.Length)];
                string text = Forms[rnd.Next(Forms.Length)].Replace("{a}", Pick()).Replace("{b}", Pick()).Replace("{c}", Pick());
                double gap = 0.4;
                if (decoy < 0 && t >= decoyAtSec && i > 3 && t < end - 20)
                {
                    decoy = sents.Count; gap = 1.6;
                    text = $"{Pick()}の{Pick()}は{Pick()}ということで以上です。";
                    laughs.Add(new Interval(t - 1.4, t - 0.2));
                }
                int first = words.Count;
                var chunks = Enumerable.Range(0, (text.Length + 1) / 2).Select(j => text.Substring(j * 2, Math.Min(2, text.Length - j * 2))).ToList();
                double wd = 4.0 / chunks.Count;
                for (int j = 0; j < chunks.Count; j++) words.Add(new Word($"w{words.Count}", Math.Round(t + j * wd, 3), Math.Round(t + (j + 1) * wd - 0.02, 3), chunks[j], i % 2 + 1));
                sents.Add(new Sentence(sents.Count, Math.Round(t, 3), Math.Round(t + 3.98, 3), text, i % 2 + 1, first, words.Count - 1));
                t += 4.0 + gap;
            }
        }
        return (new Transcript { Provider = "synthetic", Duration = Math.Round(t + 1, 3), Words = words, Sentences = sents }, starts, laughs, decoy);
    }

    static SignalSet Quiet(Transcript tr) => new()
    {
        Loudness100Ms = Enumerable.Repeat(-25.0, (int)(tr.Duration * 10) + 10).ToList(),
        MedianLoudness = -25,
    };

    /// <summary>Planned cut vs the true topic start, in sentences (0 = exactly on it).</summary>
    static int Miss(List<SeriesPart> parts, List<int> truth) =>
        parts.Skip(1).Max(p => truth.Skip(1).Min(x => Math.Abs(x - p.First)));

    /// <summary>
    /// Stand-in for the embedding model: a sentence about subject k points to direction k (plus noise), whatever its words — what a real model does for synonyms.
    /// The real model on real speech is checked in spikes/17-topic-embed.py and spikes/18-scene-check.cs.
    /// </summary>
    static double[] FakeSemanticDepth(Transcript tr, List<int> truth)
    {
        var rnd = new Random(3);
        var vecs = tr.Sentences.Select(s =>
        {
            int subject = truth.Count(t => t <= s.Index) - 1;
            var v = new float[8];
            for (int d = 0; d < v.Length; d++) v[d] = (float)(rnd.NextDouble() * 0.6 + (d == subject % 8 ? 1.0 : 0));
            return v;
        }).ToArray();
        return TopicEmbedding.Depth(tr, vecs);
    }

    public static TheoryData<int[], int> Cases => new()
    {
        { new[] { 300, 300 }, 2 },        // balanced
        { new[] { 360, 240 }, 2 },        // 6 min Azure + 4 min Copilot (the user's example)
        { new[] { 240, 360 }, 2 },
        { new[] { 420, 180 }, 2 },        // beyond the ±25 % duration tier: the topic must still win
        { new[] { 180, 420 }, 2 },
        { new[] { 200, 260, 140 }, 3 },
        { new[] { 150, 300, 150 }, 3 },
    };

    [Theory, MemberData(nameof(Cases))]
    public void The_cut_follows_the_change_of_subject_not_the_clock_midpoint(int[] topics, int parts)
    {
        double total = topics.Sum();
        // the decoy sits at the clock-balanced position, inside a topic
        var (tr, truth, laughs, decoy) = Build(topics, total / parts + 0.0);
        Assert.True(decoy > 0);
        Assert.DoesNotContain(truth, x => x == decoy);
        var sig = Quiet(tr);
        var sem = FakeSemanticDepth(tr, truth);
        var notes = new List<string>();
        var plan = SeriesPlanner.Split(tr, sig, Fixtures.Presets["P4"], tr.Duration, null, parts, SeriesPlanner.FallbackOutline(tr, sig, sem), notes, laughs, sem);
        Assert.Equal(parts, plan.Count);
        var cuts = string.Join(", ", plan.Skip(1).Select(p => $"#{p.First}"));
        Assert.True(Miss(plan, truth) <= 1, $"cuts {cuts} vs true topic starts {string.Join(", ", truth.Skip(1).Select(x => "#" + x))} (decoy #{decoy}); notes: {string.Join(" / ", notes)}");
        Assert.DoesNotContain(notes, n => n.Contains("話題の途中"));
    }

    [Fact] // old behaviour, shown by switching the scene rule off: the clock / the decoy wins on the same material
    public void Without_the_scene_rule_the_same_material_is_cut_at_the_decoy()
    {
        var (tr, truth, laughs, decoy) = Build([180, 420], 300);
        var sig = Quiet(tr);
        var sem = FakeSemanticDepth(tr, truth);
        SeriesPlanner.SceneFirst = false;
        try
        {
            var plan = SeriesPlanner.Split(tr, sig, Fixtures.Presets["P4"], tr.Duration, null, 2, SeriesPlanner.FallbackOutline(tr, sig, sem), [], laughs, sem);
            Assert.True(Miss(plan, truth) > 1, $"cut #{plan[1].First} unexpectedly on the true start #{truth[1]}");
        }
        finally { SeriesPlanner.SceneFirst = true; }
    }

    [Fact] // the AI marks sub-sections (major=false); they must not become scene boundaries, while a missing flag (older outlines) means major
    public void Ai_sub_sections_are_not_scenes_but_subject_changes_are()
    {
        var (tr, truth, _, _) = Build([200, 200], 100);
        int sub = truth[1] - 10, main = truth[1];
        var json = $$"""
        {"series_title":"テスト","chapters":[
          {"start":0,"title":"最初","major":true,"split_score":3,"highlight":1,"emphasis_words":[]},
          {"start":{{sub}},"title":"小見出し","major":false,"split_score":9,"highlight":{{sub + 1}},"emphasis_words":[]},
          {"start":{{main}},"title":"話題が変わる","major":true,"split_score":5,"highlight":{{main + 1}},"emphasis_words":[]},
          {"start":{{main + 8}},"title":"旧形式","split_score":5,"highlight":{{main + 9}},"emphasis_words":[]}]}
        """;
        var o = SeriesPlanner.ParseOutline(json, tr, 0, tr.Sentences.Count - 1);
        Assert.False(o.Chapters.Single(c => c.Start == sub).Major);
        Assert.True(o.Chapters.Single(c => c.Start == main).Major);
        Assert.True(o.Chapters.Single(c => c.Start == main + 8).Major);
        o = o with { Source = "llm" };
        var strength = SeriesPlanner.SceneStrength(tr, new double[tr.Sentences.Count + 1], o);
        Assert.True(strength[main] >= SeriesPlanner.SceneThreshold);
        Assert.True(strength[sub] < SeriesPlanner.SceneThreshold);
    }

    [Fact] // not only technical talk: a plain-language subject change (cooking → baseball) with no shared vocabulary and no announcing phrase is a scene boundary
    public void Non_technical_subjects_are_detected_the_same_way()
    {
        string[] cook = ["カレーに隠し味を入れるとコクが出ます。", "玉ねぎは飴色になるまで炒めるのがコツですね。", "うちはヨーグルトとはちみつを入れますよ。", "ルーは二種類を混ぜると深みが出ます。"];
        string[] ball = ["昨日の阪神は逆転勝ちでしたね。", "先発ピッチャーの立ち上がりが良かったです。", "四番のホームランで流れが変わりました。", "このまま優勝争いに残ってほしいです。"];
        var s = new List<Sentence>(); var w = new List<Word>(); double t = 1;
        for (int i = 0; i < 40; i++)
        {
            var text = (i < 20 ? cook : ball)[i % 4];
            int f = w.Count; w.Add(new Word($"w{f}", t, t + 3.9, text, 1));
            s.Add(new Sentence(i, t, t + 3.9, text, 1, f, f)); t += 4.4;
        }
        var tr = new Transcript { Provider = "synthetic", Duration = t + 1, Words = w, Sentences = s };
        var depth = SeriesPlanner.Depth(tr);
        var strength = SeriesPlanner.SceneStrength(tr, depth, new ChapterOutline("", [], "text"));
        Assert.Equal(20, Enumerable.Range(1, 39).OrderByDescending(i => strength[i]).First());
    }

    [Fact] // too few real subject changes for the requested count: say so instead of pretending
    public void A_single_subject_talk_is_reported_as_cut_mid_topic()
    {
        var (tr, truth, laughs, _) = Build([600], 300);
        var sig = Quiet(tr);
        var sem = FakeSemanticDepth(tr, truth);
        var notes = new List<string>();
        var plan = SeriesPlanner.Split(tr, sig, Fixtures.Presets["P4"], tr.Duration, null, 2, SeriesPlanner.FallbackOutline(tr, sig, sem), notes, laughs, sem);
        Assert.Equal(2, plan.Count);
        Assert.Contains(notes, n => n.Contains("話題の途中"));
    }
}
