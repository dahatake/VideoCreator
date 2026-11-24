using System.Text.Json;

namespace VideoCreator.App;

/// <summary>
/// One running app per data folder: the SQLite database, caches and job queue are not shared between processes.
/// A second start (e.g. double-clicking the executable again) finds the running one through <c>instance.json</c> and opens it instead.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string LockFile = "instance.lock";
    public const string InfoFile = "instance.json";

    readonly FileStream _lock;
    readonly string _info;

    SingleInstance(FileStream held, string info) { _lock = held; _info = info; }

    /// <summary>Null when another process holds the data folder.</summary>
    public static SingleInstance? TryAcquire(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        try
        {
            var held = new FileStream(Path.Combine(dataDir, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(held, Path.Combine(dataDir, InfoFile));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Publish(string url) =>
        File.WriteAllText(_info, JsonSerializer.Serialize(new { url, pid = Environment.ProcessId, version = AppVersion.InformationalVersion }));

    /// <summary>The URL the holder of <paramref name="dataDir"/> published, if it answers as VideoCreator.</summary>
    public static async Task<string?> RunningUrlAsync(string dataDir, HttpClient http, CancellationToken ct = default)
    {
        string? url;
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataDir, InfoFile), ct));
            url = doc.RootElement.GetProperty("url").GetString();
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException) { return null; }
        // only ever open a loopback page; the file is in a user-writable folder
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1") return null;
        try
        {
            using var health = await http.GetAsync(new Uri(uri, "/api/health"), ct);
            return health.IsSuccessStatusCode ? uri.GetLeftPart(UriPartial.Authority) + "/" : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return null; }
    }

    public void Dispose()
    {
        try { File.Delete(_info); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _lock.Dispose();
    }
}
