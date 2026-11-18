using VideoCreator.Storage;

namespace VideoCreator.App;

/// <summary>
/// Settings-screen input that has not been applied yet (a connection not tested and saved, a projects folder not
/// changed). Drafts survive closing the dialog and restarting the app, but nothing at runtime reads them: only a
/// connection that passed its test, or a folder change that completed, takes effect. API keys and GitHub tokens are
/// never part of a draft — they are stored only, encrypted, by a successful "test and save".
/// </summary>
public static class SettingsDrafts
{
    public sealed record SpeechDraft(string? Endpoint, string? Auth, string? TenantId, DateTimeOffset UpdatedAt = default);
    public sealed record FoundryDraft(string? Endpoint, string? Deployment, string? EmbeddingDeployment, string? Auth, string? TenantId, DateTimeOffset UpdatedAt = default);
    public sealed record CopilotDraft(string? Auth, string? Model, string? ReasoningEffort, string? ContextTier, DateTimeOffset UpdatedAt = default);
    public sealed record ProjectsDirDraft(string? Path, string? Move, DateTimeOffset UpdatedAt = default);

    public const string Speech = "speech", Foundry = "foundry", Copilot = "copilot", ProjectsDir = "projects_dir";
    const int MaxLength = 2048;

    static string Id(string section) => "draft_" + section;

    public static void Clear(Store store, string section) => store.Delete([(Kinds.Setting, Id(section))]);

    public static object View(Store store) => new
    {
        Speech = store.Get<SpeechDraft>(Kinds.Setting, Id(Speech)),
        Foundry = store.Get<FoundryDraft>(Kinds.Setting, Id(Foundry)),
        Copilot = store.Get<CopilotDraft>(Kinds.Setting, Id(Copilot)),
        ProjectsDir = store.Get<ProjectsDirDraft>(Kinds.Setting, Id(ProjectsDir)),
    };

    // Free text is only trimmed and length-capped (a draft may be incomplete); choices outside their set are dropped.
    static string? Text(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim() is var t && t.Length <= MaxLength ? t : throw new ArgumentException("入力が長すぎます");
    static string? OneOf(string? v, params string[] allowed) => v != null && allowed.Contains(v) ? v : null;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/settings/drafts", (Runtime rt) => Results.Json(View(rt.Store), Json.Options))
            .WithSummary("Unsaved settings-screen input (no keys or tokens); restored when the dialog opens");
        api.MapPut("/settings/drafts/speech", (Runtime rt, SpeechDraft d) =>
            Put(rt, Speech, () => new SpeechDraft(Text(d.Endpoint), OneOf(d.Auth, "entra", "key"), Text(d.TenantId), DateTimeOffset.Now)));
        api.MapPut("/settings/drafts/foundry", (Runtime rt, FoundryDraft d) =>
            Put(rt, Foundry, () => new FoundryDraft(Text(d.Endpoint), Text(d.Deployment), Text(d.EmbeddingDeployment), OneOf(d.Auth, "entra", "key"), Text(d.TenantId), DateTimeOffset.Now)));
        api.MapPut("/settings/drafts/copilot", (Runtime rt, CopilotDraft d) =>
            Put(rt, Copilot, () => new CopilotDraft(OneOf(d.Auth, "user", "token"), Text(d.Model), Text(d.ReasoningEffort), OneOf(d.ContextTier, "default", "long_context"), DateTimeOffset.Now)));
        api.MapPut("/settings/drafts/projects_dir", (Runtime rt, ProjectsDirDraft d) =>
            Put(rt, ProjectsDir, () => new ProjectsDirDraft(Text(d.Path), OneOf(d.Move, "move", "keep"), DateTimeOffset.Now)));
        api.MapDelete("/settings/drafts/{section}", (Runtime rt, string section) =>
        {
            if (section is not (Speech or Foundry or Copilot or ProjectsDir)) return Results.NotFound();
            Clear(rt.Store, section);
            return Results.NoContent();
        }).WithSummary("Discard the unsaved input of one settings section");
    }

    static IResult Put<T>(Runtime rt, string section, Func<T> build)
    {
        T draft;
        try { draft = build(); }
        catch (ArgumentException e) { return Results.Json(new { error = new { code = "E-VALIDATION", message = e.Message } }, Json.Options, statusCode: 422); }
        rt.Store.Put(Kinds.Setting, Id(section), null, draft);
        return Results.Json(draft, Json.Options);
    }
}
