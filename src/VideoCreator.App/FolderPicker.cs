using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VideoCreator.App;

public interface IFolderPicker
{
    Task<string?> PickAsync(string? initialPath, CancellationToken ct);
}

public sealed class FolderPicker : IFolderPicker
{
    readonly SemaphoreSlim _gate = new(1);

    public async Task<string?> PickAsync(string? initialPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("OS のフォルダー選択は Windows で利用できます。フルパスを手入力してください");
        if (!await _gate.WaitAsync(0, ct))
            throw new InvalidOperationException("フォルダー選択画面はすでに開いています");
        try
        {
            // Pass user input as encoded data, never as executable PowerShell.
            var initial = Convert.ToBase64String(Encoding.UTF8.GetBytes(initialPath ?? ""));
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
                Add-Type -AssemblyName System.Windows.Forms
                $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
                $dialog.Description = 'Select the opening / closing / scene-card folder'
                $dialog.ShowNewFolderButton = $false
                $initial = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{initial}}'))
                if ([IO.Directory]::Exists($initial)) { $dialog.SelectedPath = $initial }
                try {
                    $path = $null
                    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $path = $dialog.SelectedPath }
                    ConvertTo-Json -InputObject @{ path = $path } -Compress
                } finally { $dialog.Dispose() }
                """;
            var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi) ?? throw new IOException("フォルダー選択画面を起動できませんでした");
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try { await process.WaitForExitAsync(ct); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            var text = await output;
            var detail = await error;
            if (process.ExitCode != 0) throw new IOException("フォルダー選択に失敗しました: " + detail.Trim());
            using var doc = JsonDocument.Parse(text);
            var path = doc.RootElement.GetProperty("path").GetString();
            if (path != null && (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)))
                throw new IOException("選択したフォルダーが見つかりません。もう一度選択してください");
            return path;
        }
        finally { _gate.Release(); }
    }
}
