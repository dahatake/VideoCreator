using System.Net;
using VideoCreator.App;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
builder.Services.AddSingleton(options);

// NFR-SEC-01: loopback only. No authentication by design (single local user).
builder.WebHost.ConfigureKestrel(k =>
{
    k.Listen(IPAddress.Loopback, options.Port);
    k.Limits.MaxRequestBodySize = 64 * 1024 * 1024;   // the upload endpoint lifts this per request
});
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = VideoCreator.Json.Options.PropertyNamingPolicy;
    foreach (var c in VideoCreator.Json.Options.Converters) o.SerializerOptions.Converters.Add(c);
});
builder.Services.AddSingleton<Runtime>();
builder.Services.AddSingleton<JobEvents>();
builder.Services.AddSingleton<BrandKitWorker>();
builder.Services.AddSingleton<IFolderPicker, FolderPicker>();
builder.Services.AddSingleton<SceneCardService>();
builder.Services.AddSingleton<ProjectsLocation>();
builder.Services.AddSingleton<RemoveFolder>(ProjectDeletion.SendToRecycleBin);
builder.Services.AddSingleton(sp => new JobQueue(sp.GetRequiredService<Runtime>().Store, () => sp.GetRequiredService<Runtime>().CreateRunner(), sp.GetRequiredService<JobEvents>(), sp.GetRequiredService<ILogger<JobQueue>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobQueue>());

var app = builder.Build();

// Never show stack traces: bad requests and unexpected errors become the same JSON error shape as the API.
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var bad = ex is BadHttpRequestException;
    ctx.Response.StatusCode = bad ? 400 : 500;
    await ctx.Response.WriteAsJsonAsync(new { error = new { code = bad ? "E-VALIDATION" : "E-INTERNAL", message = bad ? "リクエストの形式が正しくありません" : "予期しないエラーが発生しました" } });
}));

// DNS-rebinding protection + CSRF guard for an unauthenticated localhost API:
// only loopback Host headers, and state-changing requests must carry a custom header (forces a CORS preflight cross-site).
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host is not ("127.0.0.1" or "localhost" or "[::1]")) { ctx.Response.StatusCode = 421; await ctx.Response.WriteAsync("Misdirected request"); return; }
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
        && ctx.Request.Headers["X-VideoCreator"] != "1")
    {
        ctx.Response.StatusCode = 403; await ctx.Response.WriteAsync("missing X-VideoCreator header"); return;
    }
    // while the projects folder is being moved, nothing else may write into it (the move itself already holds the lock)
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
        && !ctx.Request.Path.StartsWithSegments("/api/settings/projects-dir") && ctx.RequestServices.GetRequiredService<ProjectsLocation>().IsMoving)
    {
        ctx.Response.StatusCode = 409;
        await ctx.Response.WriteAsJsonAsync(new { error = new { code = "E-PROJECTS-MOVING", message = "プロジェクトの保存先を移動しています。完了してからもう一度お試しください" } });
        return;
    }
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; media-src 'self' blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
Api.Map(app);

_ = app.Services.GetRequiredService<Runtime>();   // fail fast on missing ffmpeg/presets
{
    var rt = app.Services.GetRequiredService<Runtime>();
    var n = RenderMaintenance.RemoveDuplicateFinals(rt.Store, rt.Options.ResolvedDataDir);
    if (n > 0) app.Logger.LogInformation("removed {Count} duplicate export records", n);
    var pending = RenderMaintenance.MarkLegacyPreviewChecksPending(rt.Store);
    if (pending > 0) app.Logger.LogInformation("marked final-only checks pending in {Count} legacy preview records", pending);
    app.Services.GetRequiredService<ProjectsLocation>().Recover();
}
app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = $"http://127.0.0.1:{options.Port}/";
    app.Logger.LogInformation("VideoCreator is running at {Url}", url);
    if (options.OpenBrowser && OperatingSystem.IsWindows())
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
});
app.Run();

public partial class Program;
