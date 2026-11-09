using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCreator.App;
using VideoCreator.Media;
using VideoCreator.Models;
using VideoCreator.Storage;

namespace VideoCreator.Tests;

/// <summary>PRD AT-16 through the API the 自動チェック panel uses: the per-item assessment (repair mark, fixable count, reason) matches
/// what 「この項目を修復」 then does, captions stay verbatim, an unfixable finding changes nothing, and the repair can be undone exactly.</summary>
public sealed class AcceptanceQaRepairTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "vc-at16-" + Guid.NewGuid().ToString("N")[..8]);
    WebApplicationFactory<Program> _app = null!;
    HttpClient _http = null!;
    Runtime Rt => _app.Services.GetRequiredService<Runtime>();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    const string Sha = "a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a16a";

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var data = Path.Combine(_root, "data");
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("VideoCreator:DataDir", data);
            b.UseSetting("VideoCreator:ProjectsDir", Path.Combine(_root, "projects"));
            b.UseSetting("VideoCreator:OpenBrowser", "false");
            b.UseSetting("VideoCreator:Port", "0");
            b.UseSetting("VideoCreator:Asr:Provider", "none");
            b.UseSetting("VideoCreator:Llm:Provider", "none");
        });
        _app.UseKestrel();
        _http = _app.CreateClient();
        _http.DefaultRequestHeaders.Add("X-VideoCreator", "1");
        using var r = await _http.PostAsJsonAsync("/api/projects", new { name = "at16" }, Ct);
        r.EnsureSuccessStatusCode();
        var project = (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

        // (a) one long caption with no neighbour → split in two (S-02, fixable)
        // (b) a caption spoken at 15 chars/s, boxed in by another speaker → no verbatim fix (S-01, stays with the speaking rate as reason)
        (double S, double E, string Text, int Speaker)[] ws =
            [(1, 2.2, "今日はみんなで", 1), (2.2, 3.4, "早速", 1), (3.4, 4.6, "方法を", 1), (4.6, 5.8, "話します", 1),
             (10.4, 11, "はい", 2), (11, 11.6, "こんにちはみなさん", 1), (11.6, 12.4, "どうも", 2)];
        var words = ws.Select((w, i) => new Word($"w{i}", w.S, w.E, w.Text, w.Speaker)).ToList();
        var tr = new Transcript { Provider = "fixed", Duration = 30, Words = words,
            Sentences = [new Sentence(0, 1, 5.8, "今日はみんなで早速方法を話します", 1, 0, 3), new Sentence(1, 10.4, 11, "はい", 2, 4, 4),
                         new Sentence(2, 11, 11.6, "こんにちはみなさん", 1, 5, 5), new Sentence(3, 11.6, 12.4, "どうも", 2, 6, 6)] };
        var cache = Path.Combine(data, "cache", Sha[..16]);
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "transcript_fixed.json"), Json.Serialize(tr));

        var asset = new SourceAsset { Id = "a1", ProjectId = project, Path = Path.Combine(_root, "src.mp4"), Sha256 = Sha,
            Media = new MediaInfo { Duration = 30, Width = 1080, Height = 1920, Fps = 30, AudioChannels = 1 } };
        Rt.Store.Put(Kinds.Asset, asset.Id, project, asset);
        Rt.Store.Put(Kinds.Rights, "rt1", project, new RightsConfirmation { Id = "rt1", ProjectId = project, AssetId = "a1" });
        static Telop Cap(string id, double s, double e, string text, int speaker, params string[] ids) =>
            new() { Id = id, Type = TelopType.Caption, Start = s, End = e, Text = text, SourceWordIds = [.. ids], Speaker = speaker };
        var plan = new EditPlan
        {
            EditplanId = "ep16", ProjectId = project, Preset = Fixtures.Presets["P4"].Ref, Audio = new AudioSpec { Master = "a1" },
            Timeline = [new Clip { ClipId = "c", AssetId = "a1", SrcIn = 0, SrcOut = 30 }],
            Telops = [Cap("long", 1, 5.8, "今日はみんなで早速方法を話します", 1, "w0", "w1", "w2", "w3"),
                      Cap("c0", 10.4, 11, "はい", 2, "w4"), Cap("fast", 11, 11.6, "こんにちはみなさん", 1, "w5"), Cap("c2", 11.6, 12.4, "どうも", 2, "w6")],
        };
        Rt.Store.Put(Kinds.EditPlan, plan.EditplanId, project, plan);
    }

    public async ValueTask DisposeAsync()
    {
        var store = Rt.Store;
        _http.Dispose();
        await _app.DisposeAsync();
        store.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    static JsonElement Check(JsonElement view, string id) => view.GetProperty("qa").EnumerateArray().Single(q => q.GetProperty("check_id").GetString() == id);
    static string Captions(JsonElement view) => string.Concat(view.GetProperty("plan").GetProperty("telops").EnumerateArray()
        .Where(t => t.GetProperty("type").GetString() == "caption").OrderBy(t => t.GetProperty("start").GetDouble())
        .Select(t => t.GetProperty("text").GetString())).Replace("　", "").Replace(" ", "");
    static string Ids(JsonElement view) => string.Join(",", view.GetProperty("plan").GetProperty("telops").EnumerateArray()
        .Where(t => t.GetProperty("type").GetString() == "caption").OrderBy(t => t.GetProperty("start").GetDouble())
        .SelectMany(t => t.GetProperty("source_word_ids").EnumerateArray().Select(x => x.GetString())));

    async Task<JsonElement> RepairAsync(string check)
    {
        using var r = await _http.PostAsJsonAsync("/api/editplans/ep16/qa/repair", new { check_ids = new[] { check } }, Ct);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Repair_promise_matches_result_and_can_be_undone()
    {
        var before = await _http.GetFromJsonAsync<JsonElement>("/api/editplans/ep16", Ct);
        var hash0 = before.GetProperty("plan_hash").GetString();

        // assessment shown before anything runs
        var s02 = Check(before, "S-02");
        Assert.Equal("warn", s02.GetProperty("status").GetString());
        Assert.Equal(("rule", 1), (s02.GetProperty("advice").GetProperty("repair").GetString(), s02.GetProperty("advice").GetProperty("fixable").GetInt32()));
        Assert.Equal("rule", s02.GetProperty("items").EnumerateArray().Single().GetProperty("repair").GetString());
        var s01 = Check(before, "S-01");
        Assert.Equal(("none", 0), (s01.GetProperty("advice").GetProperty("repair").GetString(), s01.GetProperty("advice").GetProperty("fixable").GetInt32()));
        Assert.Contains("警告のまま残ります", s01.GetProperty("advice").GetProperty("remaining").GetString());
        var fast = s01.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("fast", fast.GetProperty("target").GetString());
        Assert.False(fast.TryGetProperty("repair", out _));
        Assert.Contains("毎秒 15.0 字", fast.GetProperty("note").GetString());

        // the unfixable item: nothing changes, nothing to undo, the reason comes back
        var none = await RepairAsync("S-01");
        Assert.Equal(0, none.GetProperty("repair").GetProperty("fixed_count").GetInt32());
        Assert.False(none.GetProperty("repair").TryGetProperty("repair_id", out _));
        Assert.Contains(none.GetProperty("repair").GetProperty("skipped").EnumerateArray(),
            s => s.GetProperty("target").GetString() == "fast" && s.GetProperty("reason").GetString()!.Contains("話す速さ"));
        Assert.Equal(hash0, none.GetProperty("result").GetProperty("plan_hash").GetString());

        // the fixable item: fixed as promised, words and word ids unchanged, other checks not worse
        var done = await RepairAsync("S-02");
        Assert.Equal(1, done.GetProperty("repair").GetProperty("fixed_count").GetInt32());
        var after = done.GetProperty("result");
        Assert.Equal("pass", Check(after, "S-02").GetProperty("status").GetString());
        Assert.Equal(1, Check(after, "S-01").GetProperty("items").GetArrayLength());
        Assert.Equal((Captions(before), Ids(before)), (Captions(after), Ids(after)));
        Assert.Contains(after.GetProperty("plan").GetProperty("provenance").GetProperty("edits").EnumerateArray(), e => e.GetProperty("op").GetString() == "qa_repair");
        Assert.NotEqual(hash0, after.GetProperty("plan_hash").GetString());

        // undo restores the exact plan (same hash, so earlier approvals / renders are valid again)
        var id = done.GetProperty("repair").GetProperty("repair_id").GetString();
        using var u = await _http.PostAsync($"/api/editplans/ep16/qa/repair/{id}/undo", null, Ct);
        u.EnsureSuccessStatusCode();
        var undone = await u.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(hash0, undone.GetProperty("plan_hash").GetString());
        Assert.Equal("warn", Check(undone, "S-02").GetProperty("status").GetString());
    }
}
