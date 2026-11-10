using VideoCreator.Models;
using VideoCreator.Planning;
using VideoCreator.Selection;

namespace VideoCreator.Tests;

/// <summary>Posting texts: chapters on the rendered file's timeline, YouTube chapter rules, masking and grounding of every AI phrase.</summary>
public class PostCopyTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    static readonly List<string> Mask = ["田中"];

    static Clip C(string id, double masterIn, double len) => new() { ClipId = id, AssetId = "a1", SrcIn = masterIn, SrcOut = masterIn + len, Role = "body" };

    static readonly Transcript Tr = new()
    {
        Provider = "test",
        Words =
        [
            new("w1", 1, 1.5, "今日は", 0), new("w2", 2, 2.5, "釣りに", 0), new("w3", 3, 3.5, "行きます", 0),
            new("w4", 101, 101.5, "マグロが", 0), new("w5", 102, 102.5, "3匹", 0), new("w6", 103, 103.5, "釣れた", 0),
            new("w7", 126, 126.5, "田中さんと", 0), new("w8", 127, 127.5, "料理", 0),
        ],
    };

    static readonly Candidate Cand = new() { Id = "c1", Segments = [], Headline = "釣りの話", HookText = "釣れた！" };

    /// <summary>
    /// Body: 0–20 (master 0–20) | 20–45 (master 100–125, a source jump) | 45–60 (master 125–140, contiguous).
    /// Final: OP 0–5 · body 5–25 · card 25–27 · body 27–52 · tsukkomi 52–54 · body 54–69 · ED 69–81.
    /// </summary>
    static EditPlan Plan(bool sceneCard = true) => new()
    {
        EditplanId = "ep", ProjectId = "p", Preset = Fixtures.Presets["P4"].Ref, Audio = new AudioSpec { Master = "a1" },
        Timeline = [C("a", 0, 20), C("b", 100, 25), C("c", 125, 15)],
        Telops = [new Telop { Id = "h", Type = TelopType.Headline, Start = 0, End = 60, Text = "釣りの話" }],
        Inserts =
        [
            new() { Id = "opening", Kind = "opening", At = 0, Duration = 5 },
            .. sceneCard ? new List<Insert> { new() { Id = "card1", Kind = "card", At = 20, Duration = 2, Text = "さて、次！" } } : [],
            new() { Id = "tsukkomi1", Kind = "card", At = 45, Duration = 2, Text = "マジで!?" },
            new() { Id = "closing", Kind = "closing", At = 60, Duration = 12 },
        ],
    };

    sealed class FakeLlm(params string[] answers) : IHighlightSelector
    {
        public string Name => "fake";
        public string Model => "fake";
        public string? LastUser;
        public int Calls;
        public Task<LlmResult> SelectAsync(string system, string user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteJsonAsync(string system, string user, string schemaName, string wireSchema, CancellationToken ct)
        { LastUser = user; return Task.FromResult(answers[Math.Min(Calls++, answers.Length - 1)]); }
    }

    [Fact]
    public async Task Refused_parts_are_rewritten_once_with_the_reasons()
    {
        var llm = new FakeLlm(
            """{"hook":"マグロが釣れた！🎣","engage_hook":"","summary":"リアルな釣りの話。","highlights":[{"chapter":2,"text":"マグロが釣れた"}],"titles":[],"hashtags":["釣り"]}""",
            """{"hook":"別の冒頭","engage_hook":"釣りに行った日の動画です！","summary":"釣りに行き、マグロが3匹釣れた話です。","highlights":[{"chapter":1,"text":"今日は釣りに行きます"}],"titles":[],"hashtags":["マグロ"]}""");
        var kit = await PostCopy.BuildAsync(llm, Plan(sceneCard: false), Cand, Tr, Mask, [], null, Ct);
        Assert.Equal(2, llm.Calls);
        Assert.Contains("リアル", llm.LastUser);                                         // the reason is fed back
        Assert.StartsWith("マグロが釣れた！🎣", kit.Youtube);                             // what passed first is kept
        Assert.Contains("釣りに行き、マグロが3匹釣れた話です。", kit.Youtube);
        Assert.Contains("釣りに行った日の動画です！", kit.Engage);
        Assert.Contains("✅ 今日は釣りに行きます\n✅ マグロが釣れた", kit.Youtube);
        Assert.Contains("#釣り #マグロ", kit.Youtube);
        Assert.DoesNotContain(kit.Notes, n => n.Contains("リアル"));
    }

    [Fact]
    public void Outline_uses_rendered_file_times_and_skips_tsukkomi_cards()
    {
        var plan = Plan();
        var o = PostCopy.Outline(plan, Mask);
        Assert.Equal([(0.0, "オープニング"), (5.0, "釣りの話"), (25.0, "さて、次！"), (69.0, "エンディング")], o.Select(x => (x.Start, x.Title)).ToList());
        Assert.Equal(81, plan.TotalDuration, 3);
        // YouTube: the 5 s opening is shorter than 10 s, so its start goes to the next chapter; ED (12 s) stays
        Assert.Equal(["00:00 釣りの話", "00:25 さて、次！", "01:09 エンディング"], PostCopy.YoutubeLines(o, plan.TotalDuration));
        var spoken = PostCopy.SpokenText(plan, Masking.Apply(Tr, Mask).Masked, o);
        Assert.Equal(["", "今日は釣りに行きます", "マグロが3匹釣れた●●さんと料理", ""], spoken);
    }

    [Fact]
    public async Task Ai_copy_is_masked_grounded_and_timed_by_code()
    {
        var llm = new FakeLlm("""
        {"hook":"マグロが釣れた日の動画です🎣","engage_hook":"釣りに行った日の様子をまとめました！","summary":"マグロが5匹釣れた話。",
         "highlights":[{"chapter":2,"text":"マグロが3匹釣れた瞬間"},{"chapter":1,"text":"詳しくは https://example.com"}],
         "titles":[{"chapter":2,"title":"カツオ祭り"},{"chapter":1,"title":"上書き禁止"}],
         "hashtags":["釣り","#マグロ","カツオ","田中"]}
        """);
        var log = new List<string>();
        var kit = await PostCopy.BuildAsync(llm, Plan(sceneCard: false), Cand, Tr, Mask, [], log.Add, Ct);
        Assert.True(log.Count == 0, string.Join("|", log));

        Assert.DoesNotContain("田中", llm.LastUser);                                   // masked before it leaves the app
        Assert.Contains("●●さんと料理", llm.LastUser);
        Assert.Equal("ai", kit.Source);
        Assert.StartsWith("マグロが釣れた日の動画です🎣", kit.Youtube);
        Assert.DoesNotContain("5匹", kit.Youtube);                                      // ungrounded number → summary dropped
        Assert.Contains(kit.Notes, n => n.Contains("5") && n.Contains("紹介文"));
        Assert.DoesNotContain("example.com", kit.Youtube + kit.Engage);
        Assert.Contains("✅ マグロが3匹釣れた瞬間", kit.Youtube);
        Assert.Contains("#釣り #マグロ", kit.Youtube);
        Assert.DoesNotContain("#カツオ", kit.Youtube);
        Assert.DoesNotContain("田中", kit.Youtube + kit.Engage + kit.Chapters + string.Concat(kit.Notes));
        // the scene after the source jump: the AI title was ungrounded, so its first words are used; the fixed headline is not overwritten
        Assert.Equal("00:00 釣りの話\n00:25 マグロが3匹釣れた●●さんと料理\n01:07 エンディング", kit.Chapters);
        Assert.Contains(kit.Chapters, kit.Youtube);
        Assert.Contains("⏱ 目次", kit.Engage);
        Assert.Contains("#Shorts", kit.Youtube);                                        // 81 s vertical
        Assert.DoesNotContain("#Shorts", kit.Engage);
        Assert.Contains(kit.Notes, n => n.Contains("ショート"));
        Assert.DoesNotContain('<', kit.Youtube);
    }

    [Fact]
    public async Task Another_draft_is_asked_for_a_different_angle()
    {
        var llm = new FakeLlm("""{"hook":"釣りに行きます！","engage_hook":"","summary":"","highlights":[],"titles":[],"hashtags":[]}""");
        await PostCopy.BuildAsync(llm, Plan(), Cand, Tr, Mask, [], null, Ct, previous: "前回の冒頭");
        Assert.Contains("別の切り口", llm.LastUser);
        Assert.Contains("前回の冒頭", llm.LastUser);
    }

    [Fact]
    public async Task Without_ai_a_template_is_used()
    {
        var kit = await PostCopy.BuildAsync(null, Plan(), Cand, Tr, Mask, [], null, Ct);
        Assert.Equal("template", kit.Source);
        Assert.StartsWith("釣りの話！", kit.Youtube);
        Assert.Contains("🎬 釣りの話", kit.Engage);
        Assert.Equal("00:00 釣りの話\n00:25 さて、次！\n01:09 エンディング", kit.Chapters);
    }

    [Fact]
    public async Task Broken_ai_answer_falls_back_to_the_template()
    {
        var kit = await PostCopy.BuildAsync(new FakeLlm("not json"), Plan(), Cand, Tr, Mask, [], null, Ct);
        Assert.Equal("template", kit.Source);
        Assert.Contains(kit.Notes, n => n.Contains("ひな形"));
    }

    [Fact]
    public async Task Short_video_without_enough_sections_gets_no_chapters()
    {
        var plan = Plan() with { Timeline = [C("a", 0, 20)], Inserts = [] };
        var kit = await PostCopy.BuildAsync(null, plan, Cand, Tr, Mask, [], null, Ct);
        Assert.Equal("", kit.Chapters);
        Assert.DoesNotContain("▼チャプター", kit.Youtube);
        Assert.Contains(kit.Notes, n => n.Contains("条件"));
    }

    [Fact]
    public void Speech_like_series_titles_may_be_retitled_unless_shown_on_screen()
    {
        List<PostChapter> o = [new(0, "撮り始めますね", "chapter", true), new(20, "生成AIの話", "chapter", true), new(40, "はい、行きます", "chapter", true)];
        List<string> spoken = ["撮り始めますね。はい", "生成AIで外注費は", "はい行きますか"];
        var plan = Plan() with { Telops = [new Telop { Id = "h", Type = TelopType.Headline, Start = 30, End = 50, Text = "はい、行きます" }] };
        Assert.Equal([false, true, true], PostCopy.Loosen(plan, o, spoken).Select(c => c.Fixed).ToList());
    }

    [Fact]
    public void Masking_handles_emoji()
    {
        Assert.Equal("🎣●●さん🎉", Masking.MaskText("🎣田中さん🎉", Mask));
        Assert.False(Masking.Contains("楽しい🎣", Mask));
    }

    [Theory]
    [InlineData("01:23 から面白い", false)]
    [InlineData("<b>楽しい</b>", true)]
    [InlineData("www.example.com を見てね", false)]
    [InlineData("100人が集合", false)]
    public void Check_rejects_times_urls_and_ungrounded_numbers(string text, bool ok)
    {
        var r = PostCopy.Check(text, 40, "楽しい集合", [], null);
        Assert.Equal(ok, r.Ok);
        Assert.DoesNotContain('<', r.Clean);
    }
}
