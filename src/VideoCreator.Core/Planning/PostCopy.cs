using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoCreator.Models;
using VideoCreator.Selection;

namespace VideoCreator.Planning;

/// <summary>A section of the RENDERED file (final timeline, seconds). <see cref="Fixed"/> titles come from the plan (card text, series chapter, OP/ED); others are written from the speech.</summary>
public sealed record PostChapter(double Start, string Title, string Kind, bool Fixed);

/// <summary>Copy-and-paste posting texts for one exported video.</summary>
public sealed record PostKit
{
    public string Language { get; init; } = "ja-JP";
    public string Youtube { get; init; } = "";
    public string Engage { get; init; } = "";
    /// <summary>"00:00 タイトル" lines that satisfy YouTube's chapter rules; empty when they cannot.</summary>
    public string Chapters { get; init; } = "";
    public List<PostChapter> Outline { get; init; } = [];
    public string Source { get; init; } = "template";   // ai | template
    public List<string> Notes { get; init; } = [];
    public double Duration { get; init; }
}

/// <summary>
/// Promotional description + timecoded chapters for YouTube and Viva Engage.
/// Times are computed here from the plan (never written by the AI) on the rendered file's timeline (InsertMap);
/// every AI phrase is masked, must be grounded in the speech (no new numbers / names) and is dropped otherwise.
/// </summary>
public static partial class PostCopy
{
    public const int YoutubeMax = 5000, EngageMax = 10000, HookMax = 60, EngageHookMax = 80, SummaryMax = 200, HighlightMax = 40, TitleMax = 20;
    public const int MaxHashtags = 5, YoutubeShownHashtags = 3, EngageTopics = 3, ChapterTextMax = 600, FullTextMax = 4000;
    public const double ShortsMaxSec = 180;

    // ---------------- chapters ----------------

    /// <summary>Sections of the rendered file in playback order: OP, the body (split at scene cards and source jumps, or the series chapters), ED.</summary>
    public static List<PostChapter> Outline(EditPlan plan, IReadOnlyList<string> mask)
    {
        var active = InsertMap.Active(plan);
        var list = new List<PostChapter>();
        foreach (var op in active.Where(i => i.Kind == "opening")) list.Add(new(InsertMap.StartOf(active, op), VideoLanguages.Text(plan.Language, "オープニング", "Opening", "片头", "오프닝"), "opening", true));
        if (plan.Series != null)
            list.AddRange(Qa.QaEngine.FinalChapters(plan).Select(c => new PostChapter(c.Start, c.Title, "chapter", c.Title.Length > 0)));
        else
        {
            var title = BodyTitle(plan);
            list.Add(new(InsertMap.ToFinal(active, 0), title, "body", title.Length > 0));
            // scene-change cards start a section; tsukkomi pause cards are punchlines inside one
            var cards = active.Where(i => i.Kind == "card" && !i.Id.StartsWith("tsukkomi", StringComparison.Ordinal)).ToList();
            var ats = cards.Select(i => i.At).Concat(SourceJumps(plan.Timeline)).Where(a => a > 0.5 && a < plan.Duration - 0.5).OrderBy(a => a).ToList();
            var seen = new List<double>();
            foreach (var at in ats)
            {
                if (seen.Any(s => Math.Abs(s - at) < 1)) continue;
                seen.Add(at);
                var card = cards.FirstOrDefault(c => Math.Abs(c.At - at) < 1e-3);
                if (card != null) list.Add(new(InsertMap.StartOf(active, card), card.Text.Trim(), "card", card.Text.Trim().Length > 0));
                else list.Add(new(InsertMap.ToFinal(active, at), "", "scene", false));
            }
        }
        foreach (var ed in active.Where(i => i.Kind == "closing")) list.Add(new(InsertMap.StartOf(active, ed), VideoLanguages.Text(plan.Language, "エンディング", "Closing", "片尾", "엔딩"), "closing", true));
        return list.OrderBy(c => c.Start).Select(c => c with { Start = Math.Round(c.Start, 3), Title = Masking.MaskText(OneLine(c.Title), mask) }).ToList();
    }

    static string BodyTitle(EditPlan plan)
    {
        var t = plan.Telops.FirstOrDefault(x => x.Enabled && x.Type == TelopType.Headline) ?? plan.Telops.FirstOrDefault(x => x.Enabled && x.Type == TelopType.Hook);
        return t?.Text.Trim() ?? "";
    }

    /// <summary>Body times where the edit jumps forward/back in the source (a new moment starts).</summary>
    static IEnumerable<double> SourceJumps(IReadOnlyList<Clip> clips)
    {
        double off = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            if (i > 0 && clips[i].Role is "body" or "reaction" or "wide" && clips[i - 1].Role is "body" or "reaction" or "wide"
                && Math.Abs(clips[i].MasterIn - clips[i - 1].MasterOut) > InsertPlanner.JumpSec) yield return off;
            off += clips[i].Length;
        }
    }

    /// <summary>What is said in each section (masked transcript, final timeline).</summary>
    public static List<string> SpokenText(EditPlan plan, Transcript masked, IReadOnlyList<PostChapter> outline)
    {
        var active = InsertMap.Active(plan);
        var words = new List<(double T, string Text)>();
        double off = 0;
        foreach (var c in plan.Timeline)
        {
            foreach (var w in masked.Words.Where(w => w.T0 >= c.MasterIn - 0.05 && w.T0 < c.MasterOut))
                words.Add((InsertMap.ToFinal(active, off + Math.Max(0, w.T0 - c.MasterIn) / c.Speed), w.Text));
            off += c.Length;
        }
        return outline.Select((ch, k) =>
        {
            double end = k + 1 < outline.Count ? outline[k + 1].Start : double.MaxValue;
            return VideoLanguages.JoinWords(words.Where(w => w.T >= ch.Start - 0.05 && w.T < end - 0.05).Select(w => w.Text), masked.Language);
        }).ToList();
    }

    /// <summary>YouTube chapter lines (Help "Video chapters": 00:00 first, ≥3, each ≥10 s), or null.</summary>
    public static List<string>? YoutubeLines(IReadOnlyList<PostChapter> outline, double duration) =>
        SeriesPlanner.YouTubeChapters(outline.Where(c => c.Title.Length > 0).Select(c => new SeriesChapter(c.Start, c.Title)).ToList(), duration);

    /// <summary>A loose table of contents for platforms without chapter rules: titled sections at least 3 s apart.</summary>
    public static List<string> IndexLines(IReadOnlyList<PostChapter> outline, double duration)
    {
        var kept = new List<PostChapter>();
        foreach (var c in outline.Where(c => c.Title.Length > 0 && c.Start < duration - 1))
        {
            if (kept.Count > 0 && c.Start - kept[^1].Start < 3) { kept[^1] = kept[^1] with { Title = c.Title }; continue; }
            kept.Add(kept.Count == 0 ? c with { Start = 0 } : c);
        }
        return kept.Count < 2 ? [] : kept.Select(c => $"{SeriesPlanner.Stamp(c.Start, duration)} {c.Title}").ToList();
    }

    // ---------------- AI copy ----------------

    public sealed record Draft(string Hook, string EngageHook, string Summary, List<(int Chapter, string Text)> Highlights, Dictionary<int, string> Titles, List<string> Hashtags);

    const string SystemPrompt = """
あなたは動画の SNS 告知担当です。社内外の人が「見たい！」と思う、明るく楽しい告知文を日本語で書きます。
守ること:
- 書き起こしに出てくる事実だけを使う。書き起こしにない数字・人名・社名・商品名・出来事を足さない
- カタカナ語・英字の語・数字は、書き起こしに出てくる表記のまま使う（英語表記やカタカナ表記に言い換えない）。書き起こしにないカタカナ語（「リアル」「ヒント」など）も使わず、ひらがな・漢字で言う
- 内容と違う煽り（「衝撃」「閲覧注意」「〇〇が号泣」など）や断定的な誇張をしない。楽しさは言い回しと絵文字（各文1つまで）で出す
- ●は伏せ字。推測して埋めず、触れない
- URL・メールアドレス・「<」「>」を書かない。時刻（00:00 など）は書かない（アプリが付けます）
出力:
- hook: 最初の1文（60字以内）。YouTube で「もっと見る」の前に見える部分。何が見られるかが一目でわかる、楽しい呼びかけ
- engage_hook: 社内 SNS（Viva Engage）向けの最初の1文（80字以内）。丁寧で親しみやすく、同僚に勧める口調
- summary: 動画の流れと見どころの紹介（200字以内、2〜3文）
- highlights: 特に見てほしい章を2〜3件。chapter は章番号、text はその章の見どころ（40字以内、その章の発話に基づく）
- titles: title が空の章にだけ付ける章タイトル（20字以内、その章の発話に基づく、名詞句か短いフレーズ）
- hashtags: 内容に直接関係するハッシュタグ（# なし、空白なし、5件以内、書き起こしに出てくる語を優先）
""";

    const string Schema = """{"type":"object","additionalProperties":false,"required":["hook","engage_hook","summary","highlights","titles","hashtags"],"properties":{"hook":{"type":"string"},"engage_hook":{"type":"string"},"summary":{"type":"string"},"highlights":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["chapter","text"],"properties":{"chapter":{"type":"integer"},"text":{"type":"string"}}}},"titles":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["chapter","title"],"properties":{"chapter":{"type":"integer"},"title":{"type":"string"}}}},"hashtags":{"type":"array","items":{"type":"string"}}}}""";

    public static string UserPrompt(string headline, double duration, IReadOnlyList<PostChapter> outline, IReadOnlyList<string> spoken, string fullText)
    {
        var chapters = outline.Select((c, i) => new Dictionary<string, object>
        {
            ["chapter"] = i, ["kind"] = c.Kind, ["title"] = c.Fixed ? c.Title : "", ["speech"] = Trunc(spoken[i], ChapterTextMax),
        });
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["video_headline"] = headline, ["duration_sec"] = Math.Round(duration), ["chapters"] = chapters, ["transcript"] = Trunc(fullText, FullTextMax),
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Parses the AI answer and keeps only the parts that pass <see cref="Check"/>; the reasons for dropped parts go to <paramref name="notes"/>.</summary>
    public static Draft Parse(string json, IReadOnlyList<PostChapter> outline, IReadOnlyList<string> spoken, string source, IReadOnlyList<string> mask, IEnumerable<string> dictionary, List<string> notes)
    {
        var dict = dictionary.ToList();
        using var d = JsonDocument.Parse(json);
        var root = d.RootElement;
        string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string Keep(string label, string text, int max, string src)
        {
            var (ok, clean, why) = Check(text, max, src, mask, dict);
            if (!ok && clean.Length > 0) notes.Add($"AI 案の{label}「{clean}」は{why}ため使いませんでした");
            return ok ? clean : "";
        }
        var highlights = new List<(int, string)>();
        if (root.TryGetProperty("highlights", out var hs) && hs.ValueKind == JsonValueKind.Array)
            foreach (var h in hs.EnumerateArray())
            {
                if (!h.TryGetProperty("chapter", out var ci) || !ci.TryGetInt32(out var k) || k < 0 || k >= outline.Count || highlights.Count >= 3) continue;
                var t = Keep("見どころ", h.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "", HighlightMax, spoken[k] + source);
                if (t.Length > 0 && highlights.All(x => x.Item1 != k)) highlights.Add((k, t));
            }
        var titles = new Dictionary<int, string>();
        if (root.TryGetProperty("titles", out var ts) && ts.ValueKind == JsonValueKind.Array)
            foreach (var x in ts.EnumerateArray())
            {
                if (!x.TryGetProperty("chapter", out var ci) || !ci.TryGetInt32(out var k) || k < 0 || k >= outline.Count || outline[k].Fixed) continue;
                var t = Keep("章タイトル", x.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "", TitleMax, spoken[k]);
                if (t.Length > 0) titles[k] = t;
            }
        var tags = new List<string>();
        if (root.TryGetProperty("hashtags", out var hts) && hts.ValueKind == JsonValueKind.Array)
            foreach (var x in hts.EnumerateArray())
            {
                var tag = Hashtag(x.GetString() ?? "");
                if (tag.Length == 0 || tags.Contains(tag, StringComparer.OrdinalIgnoreCase) || tags.Count >= MaxHashtags) continue;
                var (ok, _, why) = Check(tag, 30, source, mask, dict);
                if (ok) tags.Add(tag); else notes.Add($"AI 案のハッシュタグ「#{Masking.MaskText(tag, mask)}」は{why}ため使いませんでした");
            }
        return new Draft(Keep("冒頭文", Str("hook"), HookMax, source), Keep("社内向け冒頭文", Str("engage_hook"), EngageHookMax, source),
            Keep("紹介文", Str("summary"), SummaryMax, source), highlights, titles, tags);
    }

    /// <summary>Keeps every part of <paramref name="first"/> that passed and fills the gaps from <paramref name="second"/>.</summary>
    public static Draft Merge(Draft first, Draft second) => new(
        first.Hook.Length > 0 ? first.Hook : second.Hook,
        first.EngageHook.Length > 0 ? first.EngageHook : second.EngageHook,
        first.Summary.Length > 0 ? first.Summary : second.Summary,
        first.Highlights.Count >= 2 ? first.Highlights : first.Highlights.Concat(second.Highlights.Where(h => first.Highlights.All(f => f.Chapter != h.Chapter))).Take(3).ToList(),
        second.Titles.Concat(first.Titles).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Last().Value),
        first.Hashtags.Concat(second.Hashtags).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxHashtags).ToList());

    /// <summary>Cleans one phrase and decides whether it may be posted: within the length, no URL, nothing masked, no number or name absent from the speech.</summary>
    public static (bool Ok, string Clean, string Why) Check(string text, int max, string source, IReadOnlyList<string> mask, IEnumerable<string>? dictionary)
    {
        var t = OneLine(text).Replace("<", "").Replace(">", "").Trim();
        if (t.Length == 0) return (false, "", "空");
        if (Url().IsMatch(t)) return (false, t, "URL を含む");
        if (Timecode().IsMatch(t)) return (false, t, "時刻を含む");
        if (new StringInfo(t).LengthInTextElements > max) return (false, t, $"{max} 字を超える");
        if (Masking.Contains(t, mask)) return (false, Masking.MaskText(t, mask), "伏せる語を含む");
        var claims = JapaneseText.UngroundedClaims(t, source, dictionary);
        if (claims.Count > 0) return (false, t, $"発話にない語（{string.Join("・", claims)}）を含む");
        return (true, t, "");
    }

    static string Hashtag(string s) => new(s.Trim().TrimStart('#', '＃').Where(c => char.IsLetterOrDigit(c) || c is '_' or 'ー').ToArray());

    // ---------------- composition ----------------

    /// <summary>Builds the kit; the AI part is optional (template without it or when it fails).</summary>
    public static async Task<PostKit> BuildAsync(IHighlightSelector? llm, EditPlan plan, Candidate c, Transcript tr, IReadOnlyList<string> mask, IReadOnlyList<string> dictionary, Action<string>? log, CancellationToken ct, string? previous = null)
    {
        var masked = Masking.Apply(tr, mask).Masked;
        var outline = Outline(plan, mask);
        var spoken = SpokenText(plan, masked, outline);
        outline = Loosen(plan, outline, spoken);
        var source = string.Concat(spoken);
        var headline = Masking.MaskText(OneLine(c.Headline.Length > 0 ? c.Headline : c.HookText), mask);
        var notes = new List<string>();
        Draft? draft = null;
        if (llm != null && source.Length > 0)
        {
            try
            {
                var user = UserPrompt(headline, plan.TotalDuration, outline, spoken, source);
                if (!string.IsNullOrWhiteSpace(previous))
                    user += "\n\n次は前回の案です。同じ事実の範囲で、別の切り口（取り上げる見どころ・書き出し・言い回し）の案を作ってください。\n" + Trunc(previous, 1500);
                var json = await llm.CompleteJsonAsync(SystemPrompt + VideoLanguages.Prompt(tr.Language), user, "post_copy", Schema, ct);
                draft = Parse(json, outline, spoken, source, mask, dictionary, notes);
                if (notes.Count > 0 && (draft.Hook.Length == 0 || draft.EngageHook.Length == 0 || draft.Summary.Length == 0 || draft.Highlights.Count < 2))
                {
                    // one repair round: tell the AI which phrases were refused and why, keep what already passed
                    var retryNotes = new List<string>();
                    try
                    {
                        var fix = await llm.CompleteJsonAsync(SystemPrompt + VideoLanguages.Prompt(tr.Language), user + "\n\n前回の案の次の部分は使えませんでした。理由の語を使わずに、すべての項目を書き直してください。\n" + string.Join("\n", notes.Select(n => "- " + n)), "post_copy", Schema, ct);
                        draft = Merge(draft, Parse(fix, outline, spoken, source, mask, dictionary, retryNotes));
                        notes.Clear();
                        notes.AddRange(retryNotes);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log?.Invoke("post copy retry failed: " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log?.Invoke("post copy generation failed: " + ex.Message);
                notes.Add(VideoLanguages.Text(plan.Language, "AI で文章を作れなかったため、ひな形で作りました", "AI copy generation failed; a template was used.", "AI 文案生成失败，已使用模板。", "AI 문구 생성에 실패하여 템플릿을 사용했습니다."));
            }
        }
        return Compose(plan, headline, outline, spoken, draft, notes);
    }

    /// <summary>
    /// A series chapter whose title is just the first words said there (「撮り始めますね」) may get an AI title,
    /// unless that title is on screen as a headline (the description must match the video).
    /// </summary>
    public static List<PostChapter> Loosen(EditPlan plan, List<PostChapter> outline, IReadOnlyList<string> spoken) =>
        outline.Select((c, i) => c.Kind == "chapter" && c.Fixed && TsukkomiService.Bare(c.Title) is { Length: > 0 } t
            && TsukkomiService.Bare(spoken[i]).Contains(t, StringComparison.Ordinal)
            && !plan.Telops.Any(x => x.Enabled && x.Type == TelopType.Headline && x.Text.Trim() == c.Title)
            ? c with { Fixed = false } : c).ToList();

    public static PostKit Compose(EditPlan plan, string headline, List<PostChapter> outline, IReadOnlyList<string> spoken, Draft? draft, List<string> notes)
    {
        string T(string ja, string en, string zh, string ko) => VideoLanguages.Text(plan.Language, ja, en, zh, ko);
        double total = plan.TotalDuration;
        // sections without a fixed title: the AI title, else the current one, else the first words said there
        outline = outline.Select((ch, i) => ch.Fixed ? ch
            : ch with { Title = draft?.Titles.GetValueOrDefault(i) is { Length: > 0 } t ? t : ch.Title.Length > 0 ? ch.Title : Trunc(OneLine(spoken[i]), TitleMax) }).ToList();
        var yt = YoutubeLines(outline, total);
        var index = yt ?? IndexLines(outline, total);
        bool vertical = plan.Canvas.Height > plan.Canvas.Width;
        bool isShort = vertical && total <= ShortsMaxSec;
        if (yt == null) notes.Add(T("章が3つ未満か10秒未満になるため、YouTube のチャプター条件（00:00 から・3つ以上・各10秒以上）を満たしません。YouTube にはチャプターを付けずに投稿します",
            "YouTube chapters omitted: at least 3 chapters, starting at 00:00 and lasting at least 10 seconds each, are required.",
            "已省略 YouTube 章节：需从 00:00 开始，至少 3 章，每章至少 10 秒。",
            "YouTube 챕터 생략: 00:00부터 시작하여 각 10초 이상의 챕터가 3개 이상 필요합니다."));
        else if (isShort) notes.Add(T("3分以内の縦型動画は YouTube でショートとして扱われ、説明欄のチャプターが再生バーに表示されないことがあります",
            "YouTube may treat vertical videos up to 3 minutes as Shorts and not display chapters in the player.",
            "YouTube 可能将 3 分钟以内的竖屏视频视为 Shorts，播放器可能不显示章节。",
            "YouTube는 3분 이하 세로 영상을 Shorts로 처리하여 재생 바에 챕터를 표시하지 않을 수 있습니다."));

        var hook = draft?.Hook is { Length: > 0 } h ? h : headline.Length > 0 ? $"{headline}！" : "";
        var engageHook = draft?.EngageHook is { Length: > 0 } eh ? eh : headline.Length > 0
            ? T($"「{headline}」の動画を公開しました！", $"New video: {headline}!", $"新视频已发布：{headline}！", $"새 영상 공개: {headline}!")
            : T("動画を公開しました！", "New video released!", "新视频已发布！", "새 영상이 공개되었습니다!");
        var summary = draft?.Summary ?? "";
        var highlights = draft?.Highlights.OrderBy(x => x.Chapter).Select(x => x.Text).ToList() ?? [];
        if (highlights.Count == 0)
            highlights = outline.Where(o => o.Kind is not ("opening" or "closing") && o.Title.Length > 0).Select(o => o.Title).Distinct().Take(3).ToList();
        if (highlights.Count == 1 && highlights[0] == headline) highlights.Clear();
        var tags = draft?.Hashtags ?? [];

        var ytText = new StringBuilder();
        Line(ytText, hook);
        if (summary.Length > 0) { ytText.AppendLine(); Line(ytText, summary); }
        if (highlights.Count > 0) { ytText.AppendLine().AppendLine(T("▼見どころ", "▼Highlights", "▼精彩看点", "▼주요 장면")); foreach (var x in highlights) ytText.AppendLine("✅ " + x); }
        if (yt != null) { ytText.AppendLine().AppendLine(T("▼チャプター", "▼Chapters", "▼章节", "▼챕터")); foreach (var l in yt) ytText.AppendLine(l); }
        ytText.AppendLine().AppendLine(T("最後まで見てもらえたらうれしいです！", "Thanks for watching to the end!", "感谢观看到最后！", "끝까지 시청해 주셔서 감사합니다!"))
            .AppendLine(T("高評価・チャンネル登録・コメントで応援してもらえると励みになります🙌", "Please like, subscribe and leave a comment 🙌", "欢迎点赞、订阅和留言支持🙌", "좋아요, 구독, 댓글로 응원해 주세요🙌"));
        var ytTags = tags.Take(MaxHashtags).Select(t => "#" + t).ToList();
        if (isShort) ytTags.Add("#Shorts");
        if (ytTags.Count > 0) ytText.AppendLine().AppendLine(string.Join(" ", ytTags));

        var en = new StringBuilder();
        if (headline.Length > 0) Line(en, $"🎬 {headline}");
        Line(en, engageHook);
        if (summary.Length > 0) { en.AppendLine(); Line(en, summary); }
        if (highlights.Count > 0) { en.AppendLine().AppendLine(T("📌 見どころ", "📌 Highlights", "📌 精彩看点", "📌 주요 장면")); foreach (var x in highlights) en.AppendLine("・" + x); }
        if (index.Count > 0) { en.AppendLine().AppendLine($"⏱ {T("目次", "Contents", "目录", "목차")}（{SeriesPlanner.Stamp(total, total)}）"); foreach (var l in index) en.AppendLine(l); }
        en.AppendLine().AppendLine(T("見たら感想をコメントやリアクションで教えてください！", "Share your thoughts in a comment or reaction!", "欢迎通过留言或回应分享感想！", "댓글이나 반응으로 감상을 알려 주세요!"));
        if (tags.Count > 0) en.AppendLine().AppendLine(string.Join(" ", tags.Take(EngageTopics).Select(t => "#" + t)));

        var kit = new PostKit
        {
            Language = plan.Language,
            Youtube = ytText.ToString().ReplaceLineEndings("\n").TrimEnd(), Engage = en.ToString().ReplaceLineEndings("\n").TrimEnd(), Chapters = yt != null ? string.Join("\n", yt) : "",
            Outline = outline, Source = draft != null ? "ai" : "template", Notes = notes, Duration = Math.Round(total, 3),
        };
        if (kit.Youtube.Length > YoutubeMax) notes.Add($"YouTube の説明文が {YoutubeMax} 文字を超えています。見どころを減らしてください");
        if (kit.Engage.Length > EngageMax) notes.Add($"Viva Engage の投稿が {EngageMax} 文字を超えています");
        return kit;
    }

    /// <summary>The same texts as a Markdown section (appended to .metadata.md).</summary>
    public static string Markdown(PostKit kit) => kit.Language == "ja-JP" ? $"""

## YouTube 説明文（そのまま貼り付け）
```text
{kit.Youtube}
```

## Viva Engage 投稿文（そのまま貼り付け）
```text
{kit.Engage}
```

## チャプター（YouTube の条件: 00:00 から・3つ以上・各10秒以上）
{(kit.Chapters.Length > 0 ? $"```text\n{kit.Chapters}\n```" : "（条件を満たさないため付けません）")}
{(kit.Notes.Count > 0 ? "\n" + string.Join("\n", kit.Notes.Select(n => "- " + n)) : "")}
""" : $"""

## {VideoLanguages.Text(kit.Language, "", "YouTube description (copy and paste)", "YouTube 简介（可直接粘贴）", "YouTube 설명 (복사하여 붙여넣기)")}
```text
{kit.Youtube}
```

## {VideoLanguages.Text(kit.Language, "", "Viva Engage post (copy and paste)", "Viva Engage 帖子（可直接粘贴）", "Viva Engage 게시물 (복사하여 붙여넣기)")}
```text
{kit.Engage}
```

## {VideoLanguages.Text(kit.Language, "", "Chapters (00:00 first, at least 3, at least 10 seconds each)", "章节（从 00:00 开始，至少 3 章，每章至少 10 秒）", "챕터 (00:00부터, 3개 이상, 각 10초 이상)")}
{(kit.Chapters.Length > 0 ? $"```text\n{kit.Chapters}\n```" : VideoLanguages.Text(kit.Language, "", "(Omitted: chapter requirements not met)", "（不符合章节条件，因此省略）", "(챕터 조건을 충족하지 않아 생략)"))}
{(kit.Notes.Count > 0 ? "\n" + string.Join("\n", kit.Notes.Select(n => "- " + n)) : "")}
""";

    static void Line(StringBuilder sb, string s) { if (s.Length > 0) sb.AppendLine(s); }
    static string OneLine(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();
    static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    [GeneratedRegex(@"https?://|www\.|[\w.+-]+@[\w-]+\.", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b")]
    private static partial Regex Timecode();
}
