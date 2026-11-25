using System.Text.Json;
using VideoCreator.Models;

namespace VideoCreator.Selection;

/// <summary>A page the search tool actually returned or opened (not a URL the model wrote).</summary>
public sealed record WebSource(string Url, string Title = "");

/// <summary>The model's answer plus every source the web-search tool surfaced in that turn.</summary>
public sealed record WebSearchReply(string Text, List<WebSource> Sources);

/// <summary>An AI connection that can search the web (Foundry Responses <c>web_search</c>, or the Copilot CLI's <c>web_search</c>/<c>web_fetch</c>).</summary>
public interface IWebSearch
{
    string Name { get; }
    Task<WebSearchReply> SearchAsync(string instructions, string prompt, CancellationToken ct);
}

/// <summary>
/// The 速報帯 of the news pattern: topics come from what is said in the edit, the items from a live web search through the configured AI connection.
/// An item is kept only when its link is one the search tool itself returned (the model cannot cite a page it did not retrieve), its date is not in the future
/// and its text fits the band; every item starts unapproved, so QA H-08 blocks the export until a person has checked the source.
/// </summary>
public static class NewsTickerService
{
    public const string Version = "ticker-1.0.0";
    public const int MaxItems = 5;
    public const int MaxPromptChars = 4000;

    public const string WireSchema = """
{"type":"object","additionalProperties":false,"required":["items"],"properties":{
 "topics":{"type":"array","items":{"type":"string"},"description":"1-3 topics (products, organisations, events) the speech is about"},
 "queries":{"type":"array","items":{"type":"string"},"description":"the web search queries you ran"},
 "items":{"type":"array","description":"0-5 recent news facts about the topics, newest first","items":{"type":"object","additionalProperties":false,"required":["text","source_url","source_title","published"],"properties":{
  "text":{"type":"string","description":"one factual headline in the video language, max 40 full-width characters, stated by the source"},
  "source_url":{"type":"string","description":"the exact URL of the search result that states it"},
  "source_title":{"type":"string"},
  "published":{"type":"string","description":"publication date yyyy-MM-dd as stated by the source, or empty"}}}}}}
""";

    static string LanguageName(string language) => language switch { "en-US" => "English", "ko-KR" => "Korean", "zh-CN" => "Simplified Chinese", _ => "Japanese" };

    public static string Instructions(string language, DateTimeOffset now) =>
        $"You are a news-desk researcher preparing the breaking-news ticker of a short news video. Today is {now:yyyy-MM-dd}.\n" +
        "1. Read the speech transcript and name the 1-3 concrete topics it is about (products, organisations, events). Ignore greetings and small talk.\n" +
        "2. Use the web search tool to find the most recent factual news about those topics, preferring official announcements and established news outlets.\n" +
        $"3. Return at most {MaxItems} items, newest first. Each text is ONE fact the cited page states, written in {LanguageName(language)}, at most 40 full-width characters, without opinions or speculation.\n" +
        "4. source_url must be the exact URL of a page your search returned. Never write a URL you did not get from the search tool. If nothing reliable is found, return an empty items array.\n" +
        "Output JSON only, matching this schema:\n" + WireSchema;

    /// <summary>The words spoken inside the edit (master time ranges), capped to keep the prompt small.</summary>
    public static string Prompt(Transcript tr, IReadOnlyList<Interval> segments)
    {
        var text = string.Concat(tr.Sentences.Where(s => segments.Count == 0 || segments.Any(g => s.T1 > g.T0 && s.T0 < g.T1)).Select(s => s.Text + "\n"));
        if (text.Length > MaxPromptChars) text = text[..MaxPromptChars];
        return "Transcript of the video:\n" + text;
    }

    /// <summary>Scheme-less, lower-case host, no www., no query/fragment, no trailing slash: the form two links are compared in.</summary>
    public static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return null;
        var host = u.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host + Uri.UnescapeDataString(u.AbsolutePath).TrimEnd('/');
    }

    public static async Task<(NewsTicker? Ticker, List<string> Notes)> FetchAsync(IWebSearch search, Transcript tr, IReadOnlyList<Interval> segments, string language, DateTimeOffset now, CancellationToken ct)
    {
        WebSearchReply reply;
        try { reply = await search.SearchAsync(Instructions(language, now), Prompt(tr, segments), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return (null, [$"速報帯: Web 検索に失敗したため、速報帯は付けません（{ex.Message}）"]); }
        return Build(reply, search.Name, language, now);
    }

    public static (NewsTicker? Ticker, List<string> Notes) Build(WebSearchReply reply, string provider, string language, DateTimeOffset now)
    {
        var notes = new List<string>();
        var json = SelectionPrompt.ExtractJsonObject([reply.Text], "items");
        if (json == null) return (null, ["速報帯: Web 検索の結果を読み取れなかったため、速報帯は付けません"]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        var queries = root.TryGetProperty("queries", out var q) && q.ValueKind == JsonValueKind.Array
            ? q.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Take(10).ToList() : [];
        var returned = reply.Sources.Select(s => (key: NormalizeUrl(s.Url), s)).Where(x => x.key != null).GroupBy(x => x.key!).ToDictionary(g => g.Key, g => g.First().s);
        var items = new List<NewsTickerItem>();
        int unverified = 0, future = 0, tooLong = 0;
        foreach (var e in root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray() : Enumerable.Empty<JsonElement>())
        {
            string text = Str(e, "text").ReplaceLineEndings(" "), url = Str(e, "source_url");
            if (text.Length == 0) continue;
            if (NormalizeUrl(url) is not { } key || !returned.TryGetValue(key, out var src)) { unverified++; continue; }
            if (text.Length > Planning.PlanEditor.MaxTickerLength) { tooLong++; continue; }
            string? published = DateOnly.TryParseExact(Str(e, "published"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd") : null;
            if (published != null && d > DateOnly.FromDateTime(now.Date.AddDays(1))) { future++; continue; }
            if (items.Any(i => i.Text == text || NormalizeUrl(i.SourceUrl) == key && i.Text.Length > 0 && i.Text[..Math.Min(8, i.Text.Length)] == text[..Math.Min(8, text.Length)])) continue;
            var title = Str(e, "source_title");
            items.Add(new NewsTickerItem
            {
                Id = $"tk{items.Count + 1}", Text = text, SourceUrl = src.Url, SourceTitle = title.Length > 0 ? title : src.Title,
                Published = published, RetrievedAt = now,
            });
            if (items.Count == MaxItems) break;
        }
        if (unverified > 0) notes.Add($"速報帯: 検索結果に無い出典を挙げた {unverified} 件を除きました");
        if (future > 0) notes.Add($"速報帯: 公開日が未来の {future} 件を除きました");
        if (tooLong > 0) notes.Add($"速報帯: {Planning.PlanEditor.MaxTickerLength} 文字を超える {tooLong} 件を除きました");
        if (items.Count == 0)
        {
            notes.Add("速報帯: 出典を確認できる最新情報が見つからなかったため、速報帯は付けません");
            return (null, notes);
        }
        notes.Add($"速報帯: Web 検索（{provider}）で {items.Count} 件を取得しました。出典を確認して承認してください（未承認のままでは書き出せません）");
        return (new NewsTicker { Items = items, Provider = provider, Queries = queries, RetrievedAt = now }, notes);
    }
}
