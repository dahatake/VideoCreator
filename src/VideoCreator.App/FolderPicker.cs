using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
#if WINDOWS
using WinForms = System.Windows.Forms;
#endif

namespace VideoCreator.App;

public interface IFolderPicker
{
    Task<string?> PickAsync(string? initialPath, CancellationToken ct);
    /// <summary>Same as <see cref="PickAsync(string?, CancellationToken)"/> with a caller-specific window title (null = the default title).</summary>
    Task<string?> PickAsync(string? initialPath, string? title, CancellationToken ct) => PickAsync(initialPath, ct);
}

/// <summary>
/// Windows: the .NET WinForms FolderBrowserDialog in-process. On .NET (Core) with visual styles
/// enabled it is the Explorer-style IFileOpenDialog, not the legacy SHBrowseForFolder tree.
/// macOS: the standard "choose folder" panel through osascript.
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
    public const string Title = "オープニング・クロージング・場面転換の素材フォルダーを選択";
#if WINDOWS
    static readonly Guid ClientId = new("3c1f6a8e-5b7d-4f0e-9a2c-6d4b8e1f0a57");
    static readonly Lock UiInitLock = new();
    static bool _uiInitialized;
#endif
    readonly SemaphoreSlim _gate = new(1);

    public Task<string?> PickAsync(string? initialPath, CancellationToken ct) => PickAsync(initialPath, null, ct);

    public async Task<string?> PickAsync(string? initialPath, string? title, CancellationToken ct)
    {
        title = string.IsNullOrWhiteSpace(title) ? Title : new string(title.Trim().Where(ch => !char.IsControl(ch)).Take(200).ToArray());
#if WINDOWS
        if (!OperatingSystem.IsWindows())
#else
        if (!OperatingSystem.IsMacOS())
#endif
            throw new PlatformNotSupportedException("フォルダー選択画面は Windows と macOS で利用できます。フルパスを手入力してください");
        ct.ThrowIfCancellationRequested();
        if (!await _gate.WaitAsync(0, ct))
            throw new InvalidOperationException("フォルダー選択画面はすでに開いています");
        try
        {
            string? path;
#if WINDOWS
            var session = new DialogSession(initialPath, title);
            var thread = new Thread(session.Run) { IsBackground = true, Name = "FolderPicker" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            using (ct.Register(session.Cancel))
                path = await session.Completion; // release the gate only after the dialog has really closed
#else
            path = await PickOnMacAsync(initialPath, title, ct);
#endif
            ct.ThrowIfCancellationRequested();
            if (path != null && (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)))
                throw new IOException("選択したフォルダーが見つかりません。もう一度選択してください");
            return path;
        }
        finally { _gate.Release(); }
    }

    // The title and path travel as argv, never spliced into the script. "activate" brings the panel in front of the browser.
    static readonly string[] MacScript =
    [
        "on run argv",
        "activate",
        "if item 2 of argv is \"\" then",
        "set f to choose folder with prompt (item 1 of argv)",
        "else",
        "set f to choose folder with prompt (item 1 of argv) default location (POSIX file (item 2 of argv))",
        "end if",
        "return POSIX path of f",
        "end run",
    ];

    static async Task<string?> PickOnMacAsync(string? initialPath, string title, CancellationToken ct)
    {
        var start = new ProcessStartInfo("/usr/bin/osascript") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var line in MacScript) { start.ArgumentList.Add("-e"); start.ArgumentList.Add(line); }
        start.ArgumentList.Add(title);
        start.ArgumentList.Add(ExistingDirectory(initialPath) ?? "");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("フォルダー選択画面を開けませんでした");
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { process.Kill(); } catch (InvalidOperationException) { } throw; }
        if (process.ExitCode == 0)
        {
            var picked = (await stdout).TrimEnd('\n', '\r');
            return picked.Length == 0 ? null : Path.TrimEndingDirectorySeparator(picked);
        }
        var error = await stderr;
        if (error.Contains("(-128)")) return null;   // the user pressed Cancel
        throw new InvalidOperationException($"フォルダー選択画面を開けませんでした（{error.Trim()}）");
    }

    static string? ExistingDirectory(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path) ? Path.GetFullPath(path) : null; }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

#if WINDOWS
    [SupportedOSPlatform("windows")]
    static void EnsureUiInitialized()
    {
        lock (UiInitLock)
        {
            if (_uiInitialized) return;
            // Must precede the first window in the process. The modern dialog itself is selected by
            // AutoUpgradeEnabled (WinForms falls back to the legacy one only in Safe Mode or on failure).
            WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
            WinForms.Application.EnableVisualStyles();
            _uiInitialized = true;
        }
    }

    sealed class DialogSession(string? initialPath, string title)
    {
        readonly TaskCompletionSource<string?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        volatile bool _cancelled;
        uint _threadId;

        public Task<string?> Completion => _done.Task;

        [SupportedOSPlatform("windows")]
        public void Run()
        {
            try
            {
                Volatile.Write(ref _threadId, NativeMethods.GetCurrentThreadId());
                EnsureUiInitialized();
                if (_cancelled) { _done.TrySetResult(null); return; }

                // A transparent owner on the cursor's screen places the dialog on the screen the user is looking at.
                var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
                using var owner = new WinForms.Form
                {
                    FormBorderStyle = WinForms.FormBorderStyle.None, ShowInTaskbar = false,
                    StartPosition = WinForms.FormStartPosition.Manual, Opacity = 0, Size = new(1, 1),
                    Location = new(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2),
                };
                using var dialog = new WinForms.FolderBrowserDialog
                {
                    Description = title, UseDescriptionForTitle = true, ShowNewFolderButton = false,
                    AutoUpgradeEnabled = true, ClientGuid = ClientId, AddToRecent = false,
                };
                if (ExistingDirectory(initialPath) is { } initial) dialog.SelectedPath = initial;
                owner.Show();
                owner.Activate();
                // The browser owns the foreground (the click came from it), so this background process must
                // take it explicitly once the dialog exists; retry briefly because the dialog appears asynchronously.
                using var raise = new WinForms.Timer { Interval = 50 };
                var attempts = 0;
                raise.Tick += (_, _) => { if (NativeMethods.RaiseDialog(NativeMethods.GetCurrentThreadId()) || ++attempts >= 60) raise.Stop(); };
                raise.Start();
                var result = _cancelled ? WinForms.DialogResult.Cancel : dialog.ShowDialog(owner);
                _done.TrySetResult(result == WinForms.DialogResult.OK ? dialog.SelectedPath : null);
            }
            catch (Exception e) { _done.TrySetException(e); }
        }

        public void Cancel()
        {
            _cancelled = true;
            // The dialog may still be starting; keep closing it until the UI thread reports completion.
            _ = Task.Run(async () =>
            {
                while (!_done.Task.IsCompleted)
                {
                    var id = Volatile.Read(ref _threadId);
                    if (id != 0 && OperatingSystem.IsWindows()) NativeMethods.CloseDialogs(id);
                    await Task.WhenAny(_done.Task, Task.Delay(50));
                }
            });
        }
    }

    static class NativeMethods
    {
        const uint WM_CLOSE = 0x0010;
        delegate bool EnumWindowsProc(nint hwnd, nint lParam);

        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, nint lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
        [DllImport("user32.dll")] static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool BringWindowToTop(nint hwnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, nint processId);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);

        static bool IsDialog(nint hwnd)
        {
            var name = new StringBuilder(64);
            return GetClassName(hwnd, name, name.Capacity) > 0 && name.ToString() == "#32770";
        }

        // Only the common dialog (#32770) is closed; WM_CLOSE there is the same as pressing Cancel.
        public static void CloseDialogs(uint threadId) => EnumThreadWindows(threadId, (hwnd, _) =>
        {
            if (IsDialog(hwnd)) PostMessage(hwnd, WM_CLOSE, 0, 0);
            return true;
        }, 0);

        /// <summary>True once the visible dialog is the foreground window.</summary>
        public static bool RaiseDialog(uint threadId)
        {
            nint dialog = 0;
            EnumThreadWindows(threadId, (hwnd, _) =>
            {
                if (!IsDialog(hwnd) || !IsWindowVisible(hwnd)) return true;
                dialog = hwnd;
                return false;
            }, 0);
            if (dialog == 0) return false;
            if (GetForegroundWindow() == dialog) return true;

            // Windows' foreground lock refuses SetForegroundWindow from a background process; sharing the
            // foreground thread's input state for the call is the documented way around it.
            var foreground = GetWindowThreadProcessId(GetForegroundWindow(), 0);
            var attached = foreground != 0 && foreground != threadId && AttachThreadInput(threadId, foreground, true);
            try
            {
                // TOPMOST then NOTOPMOST lifts the dialog (and its owner) above other apps without pinning it there.
                const uint NoSizeNoMove = 0x0001 | 0x0002;
                SetWindowPos(dialog, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, NoSizeNoMove);
                SetWindowPos(dialog, -2 /* HWND_NOTOPMOST */, 0, 0, 0, 0, NoSizeNoMove);
                BringWindowToTop(dialog);
                SetForegroundWindow(dialog);
            }
            finally { if (attached) AttachThreadInput(threadId, foreground, false); }
            return GetForegroundWindow() == dialog;
        }
    }
#endif
}
