using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Qa;
using VideoCreator.Rendering;

namespace VideoCreator.Tests;

public class PlanningTests
{
    static readonly Transcript Tr = Fixtures.Talk;

    [Fact]
    public void Transcript_fixture_is_parsed_into_sentences_with_word_ranges()
    {
        Assert.True(Tr.Sentences.Count >= 25);
        Assert.All(Tr.Sentences, s => Assert.True(s.T1 > s.T0 && s.LastWord >= s.FirstWord));
        Assert.Contains(Tr.Sentences, s => s.Text.StartsWith("買っていいのは100円"));   // phrase split at 。
        Assert.Equal(2, Tr.Words.Select(w => w.Speaker).Distinct().Count());
    }

    [Fact] // AT-04 / FR-SOL-01/02: edges inside sentences are moved to sentence boundaries; no mid-word cut
    public void Solver_snaps_mid_sentence_edges_and_never_cuts_words()
    {
        var p = Fixtures.Presets["P4"];
        var s5 = Tr.Sentences[5];
        var segs = new List<Interval> { new(s5.T0 + (s5.T1 - s5.T0) / 2, Tr.Sentences[10].T0 + 0.7) };   // both edges mid-sentence
        var res = Solver.Plan(segs, Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        Assert.True(res.Feasible, string.Join(";", res.Notes));
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips, Audio = new AudioSpec { Master = "a1" } };
        var qa = QaEngine.PlanChecks(new QaContext(plan, p, Tr, ["a1"], []));
        Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "H-06").Status);
        Assert.Equal(QaStatus.Pass, qa.Single(q => q.CheckId == "H-01").Status);
        Assert.InRange(res.Duration, p.Target.Min, p.Target.Max);
    }

    [Fact] // FR-SOL-07 determinism
    public void Solver_is_deterministic()
    {
        var p = Fixtures.Presets["P4"];
        var segs = new List<Interval> { new(8.8, 23.9), new(29.4, 35) };
        var a = Solver.Plan(segs, Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        var b = Solver.Plan(segs, Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        Assert.Equal(Json.Serialize(a.Clips), Json.Serialize(b.Clips));
    }

    [Fact] // FR-SOL-03: too-short selections grow toward the payoff, too-long ones shrink
    public void Solver_fits_duration_both_ways()
    {
        var p = Fixtures.Presets["P4"];
        var shortRes = Solver.Plan([new Interval(Tr.Sentences[3].T0, Tr.Sentences[3].T1)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        Assert.InRange(shortRes.Duration, p.Target.Min, p.Target.Max);
        var longRes = Solver.Plan([new Interval(0, 120)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        Assert.InRange(longRes.Duration, p.Target.Min, p.Target.Max);
    }

    [Fact] // FR-SOL-05: pauses above the threshold are cut, with pads kept around words
    public void JumpCut_removes_long_pauses_only_between_words()
    {
        var p = Fixtures.Presets["P4"];
        var range = new Interval(Tr.Sentences[0].T0, Tr.Sentences[6].T1);
        var pieces = Solver.JumpCut(range, Tr, Fixtures.Signals(Tr), p);
        Assert.True(pieces.Count >= 2);
        Assert.True(pieces.Sum(x => x.Length) < range.Length);
        foreach (var piece in pieces)
            foreach (var edge in new[] { piece.T0, piece.T1 })
                Assert.DoesNotContain(Tr.Words, w => edge - w.T0 > 0.15 && w.T1 - edge > 0.15);
        Assert.All(pieces, x => Assert.True(x.Length >= p.Cut.MinShotSec));
    }

    [Fact] // AT-11 (planning side): vertical sources are not blurred
    public void Vertical_source_uses_fit_mode()
    {
        var vert = Fixtures.Landscape() with { Width = 1080, Height = 1920 };
        var res = Solver.Plan([new Interval(8.8, 40)], Tr, Fixtures.Signals(Tr), Fixtures.Presets["P4"], vert, "a1");
        Assert.All(res.Clips, c => Assert.Equal("fit", c.Reframe.Mode));
    }

    [Fact] // AT-06 / FR-TEL-01/03: ≤ 14 width per caption, never starting with a particle or punctuation
    public void Captions_respect_width_and_japanese_line_start_rules()
    {
        var p = Fixtures.Presets["P4"];
        var res = Solver.Plan([new Interval(0, 60)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        var notes = new List<string>();
        var telops = TelopBuilder.Build(res.Clips, Tr, p, "1万円を使い切る", "100円だけで1万円", ["1万円"], notes);
        var caps = telops.Where(t => t.Type == TelopType.Caption).ToList();
        Assert.NotEmpty(caps);
        Assert.All(caps, t => Assert.True(JapaneseText.DisplayWidth(t.Text) <= p.Telop.Caption.MaxCharsPerLine, t.Text));
        var wordsById = Tr.Words.ToDictionary(w => w.Id);
        Assert.All(caps, t => Assert.True(JapaneseText.CanStartLine(wordsById[t.SourceWordIds[0]].Text), $"starts with particle: {t.Text}"));
        Assert.All(caps, t => Assert.DoesNotContain('。', t.Text));
        // captions never overlap each other and lie within the timeline
        for (int i = 1; i < caps.Count; i++) Assert.True(caps[i].Start >= caps[i - 1].End - 0.001);
        Assert.Contains(telops, t => t.Type == TelopType.Emphasis && t.Text == "1万円");
        Assert.Contains(telops, t => t.Type == TelopType.Hook && t.Start == 0);
        Assert.Contains(telops, t => t.Type == TelopType.Headline);
        // telops that share a screen row never overlap in time (captions / emphasis / top band)
        foreach (var row in new[] { new[] { TelopType.Caption }, [TelopType.Emphasis], [TelopType.Hook, TelopType.Headline] })
        {
            var r = telops.Where(t => row.Contains(t.Type)).OrderBy(t => t.Start).ToList();
            for (int i = 1; i < r.Count; i++) Assert.True(r[i].Start >= r[i - 1].End - 0.001, $"overlap {r[i - 1].Text} / {r[i].Text}");
        }
        // emphasis words close together in time: the later one must not overlap the earlier one
        var dense = TelopBuilder.Build(res.Clips, Tr, p, "", "", ["100円のものだけ", "1万円"], []);
        var em = dense.Where(t => t.Type == TelopType.Emphasis).OrderBy(t => t.Start).ToList();
        for (int i = 1; i < em.Count; i++) Assert.True(em[i].Start >= em[i - 1].End - 0.001);
    }

    [Fact] // regression from E2E: greedy breaking produced 「おっと　かごがもういっぱい」 + orphan 「ですよ」
    public void Caption_lines_are_balanced_without_orphans()
    {
        var clip = new Clip { ClipId = "c", AssetId = "a", SrcIn = 0, SrcOut = Tr.Duration };
        var mapped = TelopBuilder.MapWords([clip], Tr);
        var chunks = TelopBuilder.Chunk(mapped, 14);
        double Wd(IEnumerable<TelopBuilder.Mapped> c) => JapaneseText.DisplayWidth(JapaneseText.NormalizeTelop(string.Concat(c.Select(x => x.Word.Text))));
        Assert.All(chunks, c => Assert.True(Wd(c) <= 14, string.Concat(c.Select(x => x.Word.Text))));
        // a split line is never a tiny tail: lines narrower than 4 only exist when the whole phrase was that short
        var sentenceOf = Tr.Sentences.SelectMany(s => Enumerable.Range(s.FirstWord, s.LastWord - s.FirstWord + 1).Select(i => (i, s))).ToDictionary(x => Tr.Words[x.i].Id, x => x.s);
        var bySentence = chunks.GroupBy(c => sentenceOf[c[0].Word.Id].Index).Where(g => g.Count() > 1);
        Assert.All(bySentence.SelectMany(g => g), c => Assert.True(Wd(c) >= 4, $"orphan: {string.Concat(c.Select(x => x.Word.Text))}"));
        Assert.Contains(chunks, c => string.Concat(c.Select(x => x.Word.Text)).EndsWith("ですよ。") && Wd(c) >= 4);
    }

    [Fact] // NFR-SEC-06: ASS override tags in user/LLM text cannot inject styling or positioning
    public void Ass_escapes_override_tags()
    {
        Assert.Equal("｛＼pos(0,0)｝悪い", AssWriter.Escape("{\\pos(0,0)}悪い"));
        Assert.Equal("0:01:02.35", AssWriter.Time(62.346));
    }

    [Fact] // AT-09: an edit is re-evaluated by QA immediately (< 1 s) and a 22-char caption raises S-02
    public void Edit_then_qa_flags_long_caption_quickly()
    {
        var p = Fixtures.Presets["P4"];
        var res = Solver.Plan([new Interval(8.8, 40)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips, Audio = new AudioSpec { Master = "a1" },
            Telops = TelopBuilder.Build(res.Clips, Tr, p, "", "", [], []) };
        var target = plan.Telops.First(t => t.Type == TelopType.Caption).Id;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var edited = PlanEditor.Apply(plan, [new PlanOp("replace_text", target, new string('あ', 22))]);
        var qa = QaEngine.PlanChecks(new QaContext(edited, p, Tr, ["a1"], []));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000);
        Assert.Equal(QaStatus.Warn, qa.Single(q => q.CheckId == "S-02").Status);
        Assert.Single(edited.Provenance.Edits);
    }

    [Fact] // AT-05 (QA side) + H-08
    public void Qa_fails_ungrounded_and_unapproved_telops()
    {
        var p = Fixtures.Presets["P4"];
        var res = Solver.Plan([new Interval(8.8, 40)], Tr, Fixtures.Signals(Tr), p, Fixtures.Landscape(), "a1");
        var plan = new EditPlan { EditplanId = "e", ProjectId = "p", Preset = p.Ref, Timeline = res.Clips, Audio = new AudioSpec { Master = "a1" },
            Telops = [new Telop { Id = "x", Type = TelopType.Emphasis, Start = 1, End = 2, Text = "500万円" }, new Telop { Id = "y", Type = TelopType.Tsukkomi, Start = 3, End = 4, Text = "いや無理", Approved = false }] };
        var qa = QaEngine.PlanChecks(new QaContext(plan, p, Tr, [], []));
        Assert.Equal(QaStatus.Fail, qa.Single(q => q.CheckId == "H-07").Status);
        Assert.Equal(QaStatus.Fail, qa.Single(q => q.CheckId == "H-08").Status);
        Assert.Equal(QaStatus.Fail, qa.Single(q => q.CheckId == "H-09").Status);   // no rights confirmation
    }
}
