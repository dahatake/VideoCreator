using VideoCreator.Models;
using VideoCreator.Presets;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

public class SelectionTests
{
    static readonly Transcript Tr = Fixtures.Talk;
    static RawCandidate Raw(params (double, double)[] segs) => new(segs.Select(s => new Interval(s.Item1, s.Item2)).ToList(), "1万円を使い切る", "100円だけで1万円", ["1万円", "100個"], "ok", 9, 8, 7, 6);

    [Fact] // PRD §20.2 limits enforced in code (strict mode cannot express them)
    public void Validator_enforces_limits_and_grounding()
    {
        var raw = new RawCandidate([new(1, 3), new(5, 7), new(9, 11), new(13, 15), new(17, 19), new(200, 210)],
            "とても長いフックテキストで十四文字を超えています", "見出し", ["1万円", "カゴを2つ", "存在しない語"], new string('a', 300), 12, -3, 5, 5);
        var c = CandidateValidator.Normalize(raw, Tr, Fixtures.Signals(Tr), 125, "c1");
        Assert.True(c.Segments.Count <= 4);
        Assert.All(c.Segments, s => Assert.True(s.T1 <= 125));
        Assert.True(Planning.JapaneseText.DisplayWidth(c.HookText) <= 14);
        Assert.Equal(["1万円"], c.EmphasisWords);   // non-verbatim / absent words removed
        var clause = CandidateValidator.Normalize(raw with { EmphasisWords = ["それは100円じゃなくて300円"] }, Tr, Fixtures.Signals(Tr), 125, "c2");
        Assert.Empty(clause.EmphasisWords);          // clause-length "keywords" (> 10) removed
        Assert.True(c.Reason.Length <= 120);
        Assert.InRange(c.Scores.Hook, 0, 10);
        Assert.InRange(c.Scores.Standalone, 0, 10);
        Assert.NotEmpty(c.Notes);
    }

    [Fact] // FR-SEL-06
    public void Validator_penalises_demonstrative_openings()
    {
        var s = Tr.Sentences.First(x => x.Text.StartsWith("それでは"));
        var c = CandidateValidator.Normalize(Raw((s.T0, s.T0 + 25)), Tr, Fixtures.Signals(Tr), 125, "c");
        Assert.Equal(5, c.Scores.Standalone);   // 8 - 3
    }

    [Fact] // FR-SEL-08
    public void Dedup_removes_overlapping_candidates_and_ranks()
    {
        var a = CandidateValidator.Normalize(Raw((74, 103)), Tr, Fixtures.Signals(Tr), 125, "a");
        var b = CandidateValidator.Normalize(Raw((79, 106)) with { Hook = 1 }, Tr, Fixtures.Signals(Tr), 125, "b");
        var c = CandidateValidator.Normalize(Raw((8, 35)), Tr, Fixtures.Signals(Tr), 125, "c");
        var res = CandidateValidator.Dedup([a, b, c], 3);
        Assert.Equal(2, res.Count);
        Assert.Equal([1, 2], res.Select(x => x.Rank));
        Assert.DoesNotContain(res, x => x.Id == "b");
    }

    [Fact] // FR-SEL-03 + NFR-COST-03: long input -> energy pre-filter (≤ 50 % of text) + parallel per-chunk calls
    public async Task Long_transcripts_are_prefiltered_and_chunked()
    {
        var words = new List<Word>(); var sents = new List<Sentence>();
        for (int i = 0; i < 900; i++)   // 60 min, one 25-char sentence every 4 s
        {
            double t = i * 4;
            words.Add(new Word($"w{i}", t, t + 3.5, "これはテストの文章です。ここで何かが起きています。", 0));
            sents.Add(new Sentence(i, t, t + 3.5, "これはテストの文章です。ここで何かが起きています。", 0, i, i));
        }
        var tr = new Transcript { Provider = "t", Duration = 3600, Words = words, Sentences = sents };
        // chunks 1, 3 and 5 (10–20, 30–40, 50–60 min) are louder
        var loud = Enumerable.Range(0, 36000).Select(k => (k / 6000) % 2 == 1 ? -18.0 : -30.0).ToList();
        var sig = new SignalSet { Loudness100Ms = loud, MedianLoudness = -24 };
        var llm = new FakeSelector((n, user) =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(user, @"\[(\d+\.\d+)-");
            var s = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return new LlmResult([new RawCandidate([new Interval(s + 60, s + 90)], "", "", [], "r", 7, 7, 7, 7)], "fake", new LlmUsage(1000, 100));
        });
        var res = await new SelectionService(llm).SelectAsync(tr, sig, Fixtures.Presets["P4"], 3600, null, [], TestContext.Current.CancellationToken);
        Assert.Equal(3, llm.Calls);                                     // 6 chunks -> 3 kept
        Assert.True(res.SentChars <= res.FullTextChars / 2, $"{res.SentChars}/{res.FullTextChars}");
        Assert.Contains(res.Warnings, w => w.Contains("絞って AI に送信"));
        Assert.All(res.Candidates, c => Assert.Equal(1, (int)(c.Segments[0].T0 / 600) % 2));   // only loud chunks
    }

    [Fact] // AT-07 / FR-SEL-02: two schema failures + one more -> signal fallback with the specified message
    public async Task Selection_falls_back_to_signals_after_retries()
    {
        var llm = new FakeSelector(_ => throw new LlmSchemaException("bad json"));
        var sig = Fixtures.Signals(Tr) with { Events = [new SignalEvent(95, "loudness_peak", 9), new SignalEvent(20, "loudness_peak", 7), new SignalEvent(60, "loudness_peak", 5)] };
        var res = await new SelectionService(llm).SelectAsync(Tr, sig, Fixtures.Presets["P4"], 125, null, [], CancellationToken.None);
        Assert.Equal(3, llm.Calls);                       // 1 try + 2 retries
        Assert.True(res.UsedFallback);
        Assert.Contains("AI選定に失敗したため音声の盛り上がりから選びました", res.Warnings);
        Assert.NotEmpty(res.Candidates);
        Assert.All(res.Candidates, c => Assert.Equal("fallback", c.Source));
    }

    [Fact]
    public async Task Selection_uses_llm_candidates_and_excludes_previous_ones()
    {
        var llm = new FakeSelector(_ => new LlmResult([Raw((8.8, 35)), Raw((74, 103))], "fake", new LlmUsage(1000, 200)));
        var res = await new SelectionService(llm).SelectAsync(Tr, Fixtures.Signals(Tr), Fixtures.Presets["P4"], 125, null, [new Interval(8, 36)], CancellationToken.None);
        Assert.False(res.UsedFallback);
        Assert.Single(res.Candidates);
        Assert.True(res.Candidates[0].Segments[0].T0 >= 70);
    }

    [Fact] // Copilot replies: several messages, prose, truncated drafts
    public void ExtractJson_takes_the_newest_complete_object()
    {
        var msgs = new List<string> { """{"candidates":[{"segments":[""", """訂正します: {"candidates":[]} 以上""" };
        Assert.Equal("""{"candidates":[]}""", SelectionPrompt.ExtractJsonObject(msgs, "candidates"));
        Assert.Null(SelectionPrompt.ExtractJsonObject(["no json here"], "candidates"));
    }

    [Fact]
    public void All_presets_load_and_mvp_presets_are_available()
    {
        var ps = Fixtures.Presets;
        Assert.Equal(10, ps.Count);
        Assert.True(ps.Values.All(p => p.Available));
        Assert.Equal("switch", ps["P7"].Planner);
        Assert.Equal("speed", ps["P8"].Planner);
        Assert.Equal("beat", ps["P5"].Planner);
        Assert.Equal("screen", ps["P10"].Planner);
        Assert.Contains("P3", ps["P9"].IncompatibleWith);
    }

    [Fact]
    public void Preset_validation_rejects_unsafe_margins()
    {
        var bad = Fixtures.Presets["P4"] with { Telop = new PresetTelop { Caption = new CaptionStyle { MarginV = 300 } } };
        Assert.Throws<PresetValidationException>(() => PresetStore.Validate(bad));
    }
}
