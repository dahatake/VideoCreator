// Spike 22: raw data behind the AI connection settings — what the Copilot runtime reports (auth status, models,
// reasoning efforts, context tiers) and that a session honours ReasoningEffort + ContextTier. Usage:
//   dotnet run spikes/22-ai-connection.cs [model-id]
#:property PublishAot=false
#:property CopilotSkipCliDownload=true
#:package GitHub.Copilot.SDK@1.0.29
using GitHub.Copilot;

var cli = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH") ?? FindCli() ?? throw new Exception("copilot CLI not found");
Console.WriteLine($"cli: {cli}");
await using var client = new CopilotClient(new CopilotClientOptions { Connection = RuntimeConnection.ForStdio(cli), WorkingDirectory = Path.GetTempPath() });
await client.StartAsync();
var auth = await client.GetAuthStatusAsync();
Console.WriteLine($"auth: ok={auth.IsAuthenticated} type={auth.AuthType} host={auth.Host} login={auth.Login} msg={auth.StatusMessage}");
var models = await client.ListModelsAsync();
foreach (var m in models)
{
    var p = m.Billing?.TokenPrices;
    Console.WriteLine($"{m.Id,-28} {m.Name,-24} policy={m.Policy?.State,-9} ctx={m.Capabilities?.Limits?.MaxContextWindowTokens,8} prompt={m.Capabilities?.Limits?.MaxPromptTokens,8} " +
        $"efforts=[{string.Join(',', m.SupportedReasoningEfforts ?? [])}] def={m.DefaultReasoningEffort} mult={m.Billing?.Multiplier} " +
        $"in={p?.InputPrice} out={p?.OutputPrice} ctxMax={p?.ContextMax}/{p?.MaxPromptTokens} long={(p?.LongContext is { } l ? $"ctxMax={l.ContextMax}/{l.MaxPromptTokens} in={l.InputPrice}" : "-")}");
}
if (args.Length > 0)
{
    var m = models.First(x => x.Id == args[0]);
    var effort = m.SupportedReasoningEfforts?.LastOrDefault();
    var tier = m.Billing?.TokenPrices?.LongContext != null ? ContextTier.LongContext : (ContextTier?)null;
    await using var s = await client.CreateSessionAsync(new SessionConfig { Model = m.Id, ReasoningEffort = effort, ContextTier = tier, OnPermissionRequest = PermissionHandler.ApproveAll, AvailableTools = [] });
    var done = new TaskCompletionSource<string>();
    s.On<SessionEvent>(e => { if (e is AssistantMessageEvent a) done.TrySetResult(a.Data.Content ?? ""); else if (e is SessionErrorEvent er) done.TrySetException(new Exception(er.Data.Message)); });
    await s.SendAsync(new MessageOptions { Prompt = "Reply with exactly: OK" });
    Console.WriteLine($"session model={m.Id} effort={effort} tier={tier?.Value ?? "default"} reply={await done.Task.WaitAsync(TimeSpan.FromMinutes(2))}");
}

static string? FindCli()
{
    var exe = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
    var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
    if (OperatingSystem.IsWindows() && Directory.Exists(winget))
        foreach (var d in Directory.GetDirectories(winget, "GitHub.Copilot*"))
            if (Directory.GetFiles(d, exe, SearchOption.AllDirectories).FirstOrDefault() is { } f) return f;
    return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, exe)).FirstOrDefault(File.Exists);
}

