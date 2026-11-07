using System.Diagnostics;
using System.Text;

namespace VideoCreator.Media;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, string CommandLine, TimeSpan Elapsed);

public sealed class FFmpegException(string message, string code = "E-RND-FFMPEG") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Runs ffmpeg/ffprobe with argument arrays (never through a shell — NFR-SEC-06).</summary>
public sealed class FFmpeg
{
    public string FfmpegPath { get; }
    public string FfprobePath { get; }
    public string FontsDir { get; }
    public Action<string>? Log { get; set; }
    public string Threads { get; init; } = "8";   // fixed for deterministic x264 output (NFR-REL-04)

    public FFmpeg(string binDir, string fontsDir)
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        FfmpegPath = Path.Combine(binDir, "ffmpeg" + exe);
        FfprobePath = Path.Combine(binDir, "ffprobe" + exe);
        FontsDir = fontsDir;
        if (!File.Exists(FfmpegPath)) throw new FileNotFoundException($"ffmpeg not found at {FfmpegPath}. Run scripts/get-tools.ps1.");
    }

    /// <summary>Find tools/ffmpeg and tools/fonts by walking up from a start directory.</summary>
    public static FFmpeg Locate(string? configuredBin = null, string? configuredFonts = null, string? start = null)
    {
        if (configuredBin is { Length: > 0 } && configuredFonts is { Length: > 0 }) return new FFmpeg(configuredBin, configuredFonts);
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            var bin = Path.Combine(dir.FullName, "tools", "ffmpeg", "bin");
            if (Directory.Exists(bin)) return new FFmpeg(configuredBin ?? bin, configuredFonts ?? Path.Combine(dir.FullName, "tools", "fonts"));
            dir = dir.Parent;
        }
        throw new FileNotFoundException("tools/ffmpeg not found. Run scripts/get-tools.ps1.");
    }

    public Task<ProcessResult> RunFfmpegAsync(IEnumerable<string> args, CancellationToken ct = default, string? cwd = null, Stream? stdout = null)
        => RunAsync(FfmpegPath, args, ct, cwd, stdout);

    public Task<ProcessResult> RunProbeAsync(IEnumerable<string> args, CancellationToken ct = default)
        => RunAsync(FfprobePath, args, ct, null, null);

    async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct, string? cwd, Stream? stdout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = stdout == null ? Encoding.UTF8 : null,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (cwd != null) psi.WorkingDirectory = cwd;
        foreach (var a in args) psi.ArgumentList.Add(a);
        var cmd = Path.GetFileName(exe) + " " + string.Join(' ', psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        Log?.Invoke(cmd);
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi) ?? throw new FFmpegException($"failed to start {exe}");
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } });
        var errTask = p.StandardError.ReadToEndAsync(CancellationToken.None);
        string outText = "";
        if (stdout != null) await p.StandardOutput.BaseStream.CopyToAsync(stdout, CancellationToken.None);
        else outText = await p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        await p.WaitForExitAsync(CancellationToken.None);
        var err = await errTask;
        ct.ThrowIfCancellationRequested();
        return new ProcessResult(p.ExitCode, outText, err, cmd, sw.Elapsed);
    }

    public async Task<ProcessResult> RunFfmpegCheckedAsync(IEnumerable<string> args, CancellationToken ct = default, string? cwd = null)
    {
        var r = await RunFfmpegAsync(args, ct, cwd);
        if (r.ExitCode != 0) throw new FFmpegException($"ffmpeg failed ({r.ExitCode}): {Tail(r.StdErr)}");
        return r;
    }

    public async Task<string> VersionAsync()
    {
        var r = await RunFfmpegAsync(["-hide_banner", "-version"]);
        return r.StdOut.Split('\n').FirstOrDefault()?.Trim() ?? "";
    }

    /// <summary>NFR-MNT-02: verify required filters exist in this build.</summary>
    public async Task<List<string>> MissingFiltersAsync()
    {
        var r = await RunFfmpegAsync(["-hide_banner", "-filters"]);
        var need = new[] { "subtitles", "loudnorm", "xfade", "sidechaincompress", "boxblur", "scdet", "alimiter", "blackdetect", "freezedetect" };
        return need.Where(f => !System.Text.RegularExpressions.Regex.IsMatch(r.StdOut, $@"\s{f}\s")).ToList();
    }

    public static string Tail(string s, int lines = 6) => string.Join('\n', s.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(lines)).Trim();

    /// <summary>Path for a libavfilter option value inside -filter_complex: quoted at graph level, ':' escaped for the option parser
    /// (verified: unquoted "C\:/..." fails, "'C\:/...'" works).</summary>
    public static string FilterPath(string path) => "'" + path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "'\\''") + "'";
}
