using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WinForms = System.Windows.Forms;

namespace VideoCreator.App;

public interface IFolderPicker
{
    Task<string?> PickAsync(string? initialPath, CancellationToken ct);
}

/// <summary>
/// Shows the .NET <see cref="WinForms.FolderBrowserDialog"/> in-process. On .NET (Core) with visual styles
/// enabled it is the Explorer-style IFileOpenDialog, not the legacy SHBrowseForFolder tree.
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
    public const string Title = "オープニング・クロージング・場面転換の素材フォルダーを選択";
    static readonly Guid ClientId = new("3c1f6a8e-5b7d-4f0e-9a2c-6d4b8e1f0a57");
    static readonly Lock UiInitLock = new();
    static bool _uiInitialized;
    readonly SemaphoreSlim _gate = new(1);

    public async Task<string?> PickAsync(string? initialPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("フォルダー選択画面は Windows で利用できます。フルパスを手入力してください");
        ct.ThrowIfCancellationRequested();
        if (!await _gate.WaitAsync(0, ct))
            throw new InvalidOperationException("フォルダー選択画面はすでに開いています");
        try
        {
            var session = new DialogSession(initialPath);
            var thread = new Thread(session.Run) { IsBackground = true, Name = "FolderPicker" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            string? path;
            using (ct.Register(session.Cancel))
                path = await session.Completion; // release the gate only after the dialog has really closed
            ct.ThrowIfCancellationRequested();
            if (path != null && (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)))
                throw new IOException("選択したフォルダーが見つかりません。もう一度選択してください");
            return path;
        }
        finally { _gate.Release(); }
    }

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

    sealed class DialogSession(string? initialPath)
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

                // A transparent topmost owner on the cursor's screen keeps the dialog in front of the browser.
                var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
                using var owner = new WinForms.Form
                {
                    FormBorderStyle = WinForms.FormBorderStyle.None, ShowInTaskbar = false, TopMost = true,
                    StartPosition = WinForms.FormStartPosition.Manual, Opacity = 0, Size = new(1, 1),
                    Location = new(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2),
                };
                using var dialog = new WinForms.FolderBrowserDialog
                {
                    Description = Title, UseDescriptionForTitle = true, ShowNewFolderButton = false,
                    AutoUpgradeEnabled = true, ClientGuid = ClientId, AddToRecent = false,
                };
                if (ExistingDirectory(initialPath) is { } initial) dialog.SelectedPath = initial;
                owner.Show();
                owner.Activate();
                // Owned dialogs do not reliably inherit TopMost; raise the dialog itself once it exists.
                using var raise = new WinForms.Timer { Interval = 50 };
                raise.Tick += (_, _) => { if (NativeMethods.RaiseDialog(NativeMethods.GetCurrentThreadId())) raise.Stop(); };
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

        static string? ExistingDirectory(string? path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path) ? Path.GetFullPath(path) : null; }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
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

        public static bool RaiseDialog(uint threadId)
        {
            var raised = false;
            EnumThreadWindows(threadId, (hwnd, _) =>
            {
                if (!IsDialog(hwnd) || !IsWindowVisible(hwnd)) return true;
                const uint NoSizeNoMove = 0x0001 | 0x0002;
                SetWindowPos(hwnd, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, NoSizeNoMove);
                SetForegroundWindow(hwnd); // best effort; Windows may only flash the taskbar button
                raised = true;
                return false;
            }, 0);
            return raised;
        }
    }
}
