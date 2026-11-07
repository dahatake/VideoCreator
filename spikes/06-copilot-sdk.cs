// Spike 06: alternative LLM provider — GitHub Copilot SDK driving the OS-installed Copilot CLI (no bundled runtime).
// Same selection task as spike 05; output JSON validated in code (Copilot SDK has no strict schema mode).
#:property PublishAot=false
#:property CopilotSkipCliDownload=true
#:package GitHub.Copilot.SDK@1.0.29
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;

var root = Directory.GetCurrentDirectory();
var outDir = Path.Combine(root, "spikes", "out");
int fails = 0;
void Check(bool ok, string msg) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {msg}"); if (!ok) fails++; }

// Resolve the installed CLI: COPILOT_CLI_PATH, then PATH entries (WinGet install preferred over editor shims).
string? cli = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
if (cli is null)
{
    var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(d => Path.Combine(d.Trim(), OperatingSystem.IsWindows() ? "copilot.exe" : "copilot")).Where(File.Exists).ToList();
    var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
    if (Directory.Exists(winget)) candidates.InsertRange(0, Directory.GetFiles(winget, "copilot.exe", SearchOption.AllDirectories));
    cli = candidates.FirstOrDefault();
}
Check(cli != null, $"installed Copilot CLI found: {cli}");
if (cli is null) return 1;

using var tr = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "talk_transcript.json")));
var phrases = tr.RootElement.GetProperty("phrases").EnumerateArray().ToList();
var sb = new StringBuilder();
foreach (var p in phrases)
{
    double s = p.GetProperty("offsetMilliseconds").GetInt64() / 1000.0, e = s + p.GetProperty("durationMilliseconds").GetInt64() / 1000.0;
    sb.AppendLine($"[{s:F2}-{e:F2}] {p.GetProperty("text").GetString()}");
}
var prompt = $$$"""
あなたはショート動画編集者です。次の書き起こしから、合計25〜35秒の縦型ショート候補を3件選び、JSONだけを出力してください（説明文やコードフェンスは不要）。
形式: {"candidates":[{"segments":[{"start":秒,"end":秒}],"hook_text":"14字以内","headline":"16字以内","emphasis_words":["書き起こしの原文"],"reason":"1文","scores":{"hook":0-10,"standalone":0-10,"payoff":0-10,"emotion":0-10}}]}
書き起こし:
{{{sb}}}
""";

var sw = Stopwatch.StartNew();
await using var client = new CopilotClient(new CopilotClientOptions { Connection = RuntimeConnection.ForStdio(cli), WorkingDirectory = outDir });
await client.StartAsync();
await using var session = await client.CreateSessionAsync(new SessionConfig
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    AvailableTools = [],   // pure text generation: no shell/file tools for the agent
});
var messages = new List<string>(); var done = new TaskCompletionSource();
session.On<SessionEvent>(evt =>
{
    if (evt is AssistantMessageEvent m) messages.Add(m.Data.Content ?? "");
    else if (evt is SessionIdleEvent) done.TrySetResult();
    else if (evt is SessionErrorEvent err) done.TrySetException(new Exception(err.Data.Message));
});
await session.SendAsync(new MessageOptions { Prompt = prompt });
await done.Task.WaitAsync(TimeSpan.FromMinutes(3));
Check(messages.Count > 0, $"Copilot responded in {sw.ElapsedMilliseconds} ms ({messages.Count} assistant message(s))");
// The agent may send several messages (e.g. a truncated draft then a correction). Scan every balanced {...} block,
// newest first, and keep the first one that parses and has "candidates".
JsonDocument? doc = null;
foreach (var msg in Enumerable.Reverse(messages))
{
    var blocks = new List<string>(); int depth = 0, st = -1; bool inStr = false, esc = false;
    for (int k = 0; k < msg.Length; k++)
    {
        char ch = msg[k];
        if (inStr) { if (esc) esc = false; else if (ch == '\\') esc = true; else if (ch == '"') inStr = false; continue; }
        if (ch == '"') inStr = true; else if (ch == '{') { if (depth++ == 0) st = k; } else if (ch == '}' && depth > 0 && --depth == 0) blocks.Add(msg[st..(k + 1)]);
    }
    foreach (var blk in Enumerable.Reverse(blocks))
        try { var d = JsonDocument.Parse(blk); if (d.RootElement.TryGetProperty("candidates", out _)) { doc = d; break; } } catch { }
    if (doc != null) break;
}
Check(doc != null, "found a parseable candidates JSON among the assistant messages");
if (doc != null)
{
    var cands = doc.RootElement.GetProperty("candidates").EnumerateArray().ToList();
    Check(cands.Count >= 1, $"{cands.Count} candidates");
    foreach (var c in cands)
    {
        var segs = c.GetProperty("segments").EnumerateArray().Select(s => (s: s.GetProperty("start").GetDouble(), e: s.GetProperty("end").GetDouble())).ToList();
        Console.WriteLine($"  {string.Join(" + ", segs.Select(x => $"{x.s:F1}-{x.e:F1}"))} ({segs.Sum(x => x.e - x.s):F1}s) 「{c.GetProperty("headline").GetString()}」 {c.GetProperty("reason").GetString()}");
        Check(segs.All(x => x.e > x.s && x.s >= 0 && x.e <= 130), "segments well-formed");
    }
}
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;
