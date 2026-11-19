using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using VideoCreator.Asr;
using VideoCreator.Selection;

namespace VideoCreator.App;

/// <summary>Encrypts secrets (API keys, GitHub tokens) at rest. <c>Kind</c> is shown in the settings screen so the protection level is never overstated.</summary>
public interface ISecretProtector
{
    /// <summary>dpapi = Windows Data Protection API bound to the signed-in Windows user; unavailable = secrets are refused, never stored.</summary>
    string Kind { get; }
    bool StoresSecrets => Kind != "unavailable";
    string Protect(string plain);
    string Unprotect(string blob);
}

/// <summary>Windows DPAPI (CurrentUser scope) with an app-specific entropy: another Windows user, another PC, or a copied file cannot decrypt it.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VideoCreator.ai-settings.v1");
    public string Kind => "dpapi";

    public string Protect(string plain) => Convert.ToBase64String(Crypt(Encoding.UTF8.GetBytes(plain), protect: true));
    public string Unprotect(string blob) => Encoding.UTF8.GetString(Crypt(Convert.FromBase64String(blob), protect: false));

    static byte[] Crypt(byte[] data, bool protect)
    {
        var input = new DataBlob(); var entropy = new DataBlob(); var output = new DataBlob();
        var hData = GCHandle.Alloc(data, GCHandleType.Pinned); var hEntropy = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            input = new DataBlob { cbData = data.Length, pbData = hData.AddrOfPinnedObject() };
            entropy = new DataBlob { cbData = Entropy.Length, pbData = hEntropy.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            hData.Free(); hEntropy.Free();
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    const int UiForbidden = 0x1;
    [StructLayout(LayoutKind.Sequential)] struct DataBlob { public int cbData; public IntPtr pbData; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, ref DataBlob pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr hMem);
}

/// <summary>macOS / Linux: no OS-backed encryption is wired up, so secrets are never written (PRD NFR-SEC-02). Keyless sign-in (Entra ID / the Copilot CLI user) still saves.</summary>
public sealed class NoSecretStorage : ISecretProtector
{
    public string Kind => "unavailable";
    public string Protect(string plain) => throw new NotSupportedException("secrets cannot be stored on this OS");
    public string Unprotect(string blob) => throw new NotSupportedException("secrets cannot be stored on this OS");
}

public sealed record SavedFoundry(string Endpoint, string Deployment, string? EmbeddingDeployment, string Auth, string? TenantId, string? ApiKeyProtected, DateTimeOffset TestedAt, string? ImageDeployment = null);
public sealed record SavedCopilot(string Auth, string? TokenProtected, string? Model, string? ReasoningEffort, string? ContextTier, string? Login, DateTimeOffset TestedAt);
/// <summary>Connections chosen in the settings screen; they override appsettings.json. Secrets are stored only in protected form.</summary>
public sealed record SavedAi(string? Provider = null, SavedFoundry? Foundry = null, SavedCopilot? Copilot = null, string? Protection = null);

public sealed class AiSettingsStore(string path, ISecretProtector protector)
{
    readonly Lock _gate = new();
    public string Path => path;
    public ISecretProtector Protector => protector;

    public static ISecretProtector DefaultProtector() => OperatingSystem.IsWindows() ? new DpapiProtector() : new NoSecretStorage();

    public SavedAi Load()
    {
        lock (_gate)
        {
            if (!File.Exists(path)) return new();
            try { return JsonSerializer.Deserialize<SavedAi>(File.ReadAllText(path), Json.Options) ?? new(); }
            catch (JsonException) { return new(); }   // a damaged file falls back to appsettings.json; the next save rewrites it
        }
    }

    public void Update(Func<SavedAi, SavedAi> change) => TryUpdate(change);

    internal bool TryUpdate(Func<SavedAi, SavedAi?> change)
    {
        lock (_gate)
        {
            if (change(Load()) is not { } next) return false;
            next = next with { Protection = protector.Kind };
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            if (!OperatingSystem.IsWindows())
                using (File.Create(tmp)) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllText(tmp, JsonSerializer.Serialize(next, Json.Indented));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
    }

    public string? Protect(string? secret) => string.IsNullOrWhiteSpace(secret) ? null : protector.Protect(secret.Trim());

    /// <summary>Null when nothing is stored or it cannot be decrypted here (another Windows user or PC) — the user is asked to enter it again.</summary>
    public string? Reveal(string? blob)
    {
        if (string.IsNullOrEmpty(blob)) return null;
        try { return protector.Unprotect(blob); }
        catch (Exception e) when (e is Win32Exception or FormatException or NotSupportedException or System.Security.Cryptography.CryptographicException or DecoderFallbackException) { return null; }
    }
}

public static class SavedAiExtensions
{
    public static FoundryConnection ToConnection(this SavedFoundry f, AiSettingsStore store) =>
        new(f.Endpoint, f.Deployment, f.EmbeddingDeployment, f.Auth, f.TenantId, f.Auth == "key" ? store.Reveal(f.ApiKeyProtected) : null, f.ImageDeployment);

    public static CopilotConnection ToConnection(this SavedCopilot c, AiSettingsStore store) =>
        new(c.Auth, c.Auth == "token" ? store.Reveal(c.TokenProtected) : null, c.Model, c.ReasoningEffort, c.ContextTier);
}
