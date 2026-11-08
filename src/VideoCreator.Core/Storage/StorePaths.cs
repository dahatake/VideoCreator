using System.Text.Json.Nodes;

namespace VideoCreator.Storage;

/// <summary>Rewrites absolute paths stored anywhere in the document store after files have been moved on disk.</summary>
public static class StorePaths
{
    /// <summary>
    /// Every JSON string equal to <paramref name="from"/> or under it (case-insensitive, Windows) is re-rooted at <paramref name="to"/>,
    /// in every document, in one transaction. Strings that merely contain the path (e.g. recorded ffmpeg command lines) are left as history.
    /// Idempotent: once rewritten nothing matches <paramref name="from"/> any more. Returns the number of documents changed.
    /// </summary>
    public static int Rewrite(Store store, string from, string to)
    {
        from = Normalize(from); to = Normalize(to);
        var changed = new List<(string, string, string)>();
        foreach (var (kind, id, json) in store.RawAll())
        {
            if (json.IndexOf(from.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) < 0 && json.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var node = JsonNode.Parse(json);
            if (node != null && Walk(node, from, to)) changed.Add((kind, id, node.ToJsonString(Json.Options)));
        }
        return changed.Count == 0 ? 0 : store.ReplaceJson(changed);
    }

    /// <summary>The re-rooted path, or null when <paramref name="path"/> is not <paramref name="from"/> or inside it.</summary>
    public static string? Reroot(string path, string from, string to)
    {
        if (!Path.IsPathFullyQualified(path)) return null;
        if (path.Equals(from, StringComparison.OrdinalIgnoreCase)) return to;
        return path.Length > from.Length && path.StartsWith(from, StringComparison.OrdinalIgnoreCase) && path[from.Length] is '\\' or '/'
            ? to + path[from.Length..] : null;
    }

    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    static bool Walk(JsonNode node, string from, string to)
    {
        bool any = false;
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    var child = o[key];
                    if (child is JsonValue v && v.TryGetValue<string>(out var s)) { if (Reroot(s, from, to) is { } r) { o[key] = r; any = true; } }
                    else if (child != null) any |= Walk(child, from, to);
                }
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                {
                    var child = a[i];
                    if (child is JsonValue v && v.TryGetValue<string>(out var s)) { if (Reroot(s, from, to) is { } r) { a[i] = r; any = true; } }
                    else if (child != null) any |= Walk(child, from, to);
                }
                break;
        }
        return any;
    }
}
