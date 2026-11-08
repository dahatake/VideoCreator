using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;

namespace VideoCreator.Tests;

public sealed class AcceptanceGroundingTests
{
    static Transcript TranscriptOf(params (string Text, int Speaker)[] utterances)
    {
        var words = utterances.Select((u, i) => new Word($"w{i}", 1 + i * 3, 3 + i * 3, u.Text, u.Speaker)).ToList();
        return new Transcript
        {
            Provider = "fixed-counterexamples", Duration = 30, Words = words,
            Sentences = words.Select((w, i) => new Sentence(i, w.T0, w.T1, w.Text, w.Speaker, i, i)).ToList(),
        };
    }

    static EditPlan Plan(Telop telop) => new()
    {
        EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P4"].Ref,
        Timeline = [new Clip { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = 30 }],
        Telops = [telop], Audio = new AudioSpec { Master = "a" },
    };

    static Telop Text(string text, params string[] ids) => new()
    {
        Id = "t", Type = TelopType.Emphasis, Text = text, Start = 1, End = 3, SourceWordIds = ids.ToList(),
    };

    static List<QaResult> Check(Telop t, Transcript tr, string preset = "P4", params string[] dictionary) =>
        QaEngine.PlanChecks(new(Plan(t), Fixtures.Presets[preset], tr, ["a"], dictionary));

    [Fact]
    public void AT13_fixed_counterexamples_fail_correspondence_while_token_presence_is_insufficient()
    {
        var amounts = TranscriptOf(("A社は100万円、B社は500万円", 1));
        var wrongAmount = Check(Text("A社500万円", "w0"), amounts);
        Assert.Equal(QaStatus.Pass, wrongAmount.Single(q => q.CheckId == "H-07").Status);
        Assert.Equal(QaStatus.Fail, wrongAmount.Single(q => q.CheckId == "H-11").Status);

        var negative = TranscriptOf(("売れなかった", 1));
        var dropped = Check(Text("売れた", "w0"), negative);
        Assert.Equal(QaStatus.Pass, dropped.Single(q => q.CheckId == "H-07").Status);
        Assert.Equal(QaStatus.Warn, dropped.Single(q => q.CheckId == "S-09").Status);

        var speakerB = TranscriptOf(("こちらがおすすめです", 2));
        var wrongSpeaker = Check(Text("こちらがおすすめ", "w0") with { Speaker = 1, Type = TelopType.Caption }, speakerB, "P7");
        Assert.Equal(QaStatus.Pass, wrongSpeaker.Single(q => q.CheckId == "H-07").Status);
        Assert.Equal(QaStatus.Fail, wrongSpeaker.Single(q => q.CheckId == "H-11").Status);

        var dictionaryOnly = Check(Text("SuperWidget", "w0"), speakerB, "P4", "SuperWidget");
        Assert.Equal(QaStatus.Pass, dictionaryOnly.Single(q => q.CheckId == "H-07").Status);
        Assert.Equal(QaStatus.Fail, dictionaryOnly.Single(q => q.CheckId == "H-11").Status);
        Assert.All(wrongAmount, q => Assert.Equal(EditPlanFingerprint.Of(Plan(Text("A社500万円", "w0"))), q.PlanHash));
    }

    [Theory]
    [InlineData("A社１００万円", true)]
    [InlineData("A社百万円", true)]
    [InlineData("A社500万円", false)]
    [InlineData("A社100万個", false)]
    [InlineData("B社100万円", false)]
    public void Number_subject_unit_relationship_is_not_a_bag_of_words(string text, bool pass)
    {
        var tr = TranscriptOf(("A社は100万円、B社は500万円", 1));
        Assert.Equal(pass ? QaStatus.Pass : QaStatus.Fail, Check(Text(text, "w0"), tr).Single(q => q.CheckId == "H-11").Status);
    }

    [Theory]
    [InlineData("1.5倍", true)]
    [InlineData("15倍", false)]
    [InlineData("-1.5倍", false)]
    public void Decimal_and_sign_are_not_lost_in_normalization(string text, bool pass)
    {
        var tr = TranscriptOf(("1.5倍です", 1));
        Assert.Equal(pass ? QaStatus.Pass : QaStatus.Fail, Check(Text(text, "w0"), tr).Single(q => q.CheckId == "H-11").Status);
    }

    [Fact]
    public void Missing_unknown_duplicate_noncontiguous_and_outside_word_ids_are_rejected()
    {
        var tr = TranscriptOf(("A社", 1), ("100万円", 1), ("B社", 2), ("500万円", 2));
        foreach (var ids in new[] { Array.Empty<string>(), new[] { "missing" }, new[] { "w0", "w0" }, new[] { "w0", "w3" } })
            Assert.Equal(QaStatus.Fail, Check(Text("A社500万円", ids), tr).Single(q => q.CheckId == "H-11").Status);
        var outside = tr with { Words = tr.Words.Select(w => w with { T0 = w.T0 + 40, T1 = w.T1 + 40 }).ToList() };
        Assert.Empty(TelopGrounding.Evidence(Text("100万円", "w1"), Plan(Text("100万円", "w1")).Timeline, outside));
        Assert.Equal(QaStatus.Pass, Check(Text("A社100万円", "w0", "w1"), tr).Single(q => q.CheckId == "H-11").Status);
    }

    [Fact]
    public void Builders_bind_claims_and_preserve_speaker_boundaries()
    {
        var tr = TranscriptOf(("A社は100万円", 1), ("売れませんでした", 2));
        var telops = TelopBuilder.Build(Plan(Text("")).Timeline, tr, Fixtures.Presets["P4"], "A社100万円", "A社100万円", ["100万円"], []);
        Assert.All(telops.Where(TelopGrounding.Applies), t =>
        {
            Assert.NotEmpty(t.SourceWordIds);
            Assert.Empty(TelopGrounding.Problems(t, Plan(t).Timeline, tr));
        });
        Assert.All(telops.Where(t => t.Type == TelopType.Caption), t =>
            Assert.All(TelopGrounding.Evidence(t, Plan(t).Timeline, tr), w => Assert.Equal(t.Speaker, w.Speaker)));
        Assert.False(TelopGrounding.NegationDropped(Text("売れません", "w1"), Plan(Text("")).Timeline, tr));
        Assert.True(TelopGrounding.NegationDropped(Text("売れた", "w1"), Plan(Text("")).Timeline, tr));
    }

    [Fact]
    public void Tsukkomi_is_not_treated_as_an_utterance_summary_but_requires_approval()
    {
        var tr = TranscriptOf(("おすすめです", 1));
        var qa = Check(Text("SuperWidget") with { Type = TelopType.Tsukkomi, Approved = false }, tr, "P4", "SuperWidget");
        Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "H-11").Status);
        Assert.Equal(QaStatus.Fail, qa.Single(q => q.CheckId == "H-08").Status);
        Assert.Equal(qa.Count, qa.Select(q => q.CheckId).Distinct().Count());
    }
}
