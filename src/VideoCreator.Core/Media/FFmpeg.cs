using System.Diagnostics;
using System.Text;

namespace VideoCreator.Media;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, string CommandLine, TimeSpan Elapsed);

public sealed class FFmpegException(string message, string code = "E-RND-FFMPEG") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Reports on the calling thread, in order (unlike <see cref="Progress{T}"/>, which posts to the thread pool and can reorder updates).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

public static class ProgressExtensions
{
    /// <summary>Maps 0..1 of a sub-step onto [from, to] of the parent.</summary>
    public static IProgress<double>? Slice(this IProgress<double>? parent, double from, double to) =>
        parent == null ? null : new SyncProgress<double>(v => parent.Report(from + (to - from) * Math.Clamp(v, 0, 1)));
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

    /// <param name="progress">Measured fraction 0..1 of <paramref name="duration"/> seconds of output, parsed from <c>-progress pipe:1</c>
    /// (monotonic; reports 1 when ffmpeg says <c>progress=end</c>). Ignored when <paramref name="stdout"/> carries data or the duration is unknown.</param>
    public Task<ProcessResult> RunFfmpegAsync(IEnumerable<string> args, CancellationToken ct = default, string? cwd = null, Stream? stdout = null, IProgress<double>? progress = null, double duration = 0)
    {
        if (progress == null || stdout != null || !(duration > 0)) return RunAsync(FfmpegPath, args, ct, cwd, stdout, null);
        var meter = new ProgressMeter(progress, duration);
        return RunAsync(FfmpegPath, ["-progress", "pipe:1", "-nostats", .. args], ct, cwd, null, meter);
    }

    public Task<ProcessResult> RunProbeAsync(IEnumerable<string> args, CancellationToken ct = default)
        => RunAsync(FfprobePath, args, ct, null, null, null);

    /// <summary>Turns ffmpeg <c>-progress</c> key=value lines into a throttled, never-decreasing fraction.</summary>
    public sealed class ProgressMeter(IProgress<double> target, double duration)
    {
        public const double Step = 0.01;
        double _last = -1, _max;

        public void Line(string line)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) return;
            var key = line.AsSpan(0, eq);
            var val = line.AsSpan(eq + 1).Trim();
            if (key.SequenceEqual("progress")) { if (val.SequenceEqual("end")) Emit(1); return; }
            // out_time_ms is also microseconds (historic ffmpeg naming)
            if (!(key.SequenceEqual("out_time_us") || key.SequenceEqual("out_time_ms"))) return;
            if (!long.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var us) || us < 0) return;
            _max = Math.Max(_max, Math.Min(0.999, us / 1e6 / duration));
            if (_max - _last >= Step) Emit(_max);
        }

        void Emit(double v) { if (v <= _last) return; _last = v; target.Report(v); }
    }

    async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct, string? cwd, Stream? stdout, ProgressMeter? meter)
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
        else if (meter != null)
        {
            while (await p.StandardOutput.ReadLineAsync(CancellationToken.None) is string line) meter.Line(line);
        }
        else outText = await p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        await p.WaitForExitAsync(CancellationToken.None);
        var err = await errTask;
        ct.ThrowIfCancellationRequested();
        return new ProcessResult(p.ExitCode, outText, err, cmd, sw.Elapsed);
    }

    public async Task<ProcessResult> RunFfmpegCheckedAsync(IEnumerable<string> args, CancellationToken ct = default, string? cwd = null, IProgress<double>? progress = null, double duration = 0)
    {
        var r = await RunFfmpegAsync(args, ct, cwd, null, progress, duration);
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
