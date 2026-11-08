using System.Runtime.InteropServices;
using System.Text;
using VideoCreator.App;

namespace VideoCreator.Tests;

/// <summary>Drives the real dialog and inspects its native window tree (no mocks).</summary>
public sealed class FolderPickerTests : IDisposable
{
    readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vc-pick-" + Guid.NewGuid().ToString("N"))).FullName;
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var hwnd in OwnDialogs()) PostMessage(hwnd, 0x0010, 0, 0); // never leave a dialog behind on failure
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Real_dialog_is_modern_topmost_and_returns_selection_cancel_or_abort()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive) Assert.Skip("Needs an interactive Windows desktop");
        var picker = new FolderPicker();

        // OK returns the folder that was pre-selected from the typed path.
        var pick = picker.PickAsync(_dir, Ct);
        var dialog = await FindDialogAsync();
        var classes = Descendants(dialog);
        Assert.DoesNotContain("SHBrowseForFolder ShellNameSpace Control", classes); // legacy tree dialog
        Assert.Contains("DirectUIHWND", classes);           // Explorer-style IFileOpenDialog
        Assert.Contains("Breadcrumb Parent", classes);      // address bar
        Assert.Equal(FolderPicker.Title, TextOf(dialog));
        Assert.True(await EventuallyAsync(() => (GetWindowLongPtr(dialog, -20) & 0x8) != 0), "dialog should be topmost");
        await Assert.ThrowsAsync<InvalidOperationException>(() => picker.PickAsync(null, Ct));
        SendMessage(GetDlgItem(dialog, 1), 0x00F5, 0, 0); // BM_CLICK on the select button
        Assert.Equal(_dir, await pick.WaitAsync(TimeSpan.FromSeconds(15), Ct), ignoreCase: true);

        // User cancel returns null and the gate is released again.
        pick = picker.PickAsync(Path.Combine(_dir, "missing"), Ct);
        dialog = await FindDialogAsync();
        PostMessage(dialog, 0x0010, 0, 0); // WM_CLOSE == Cancel
        Assert.Null(await pick.WaitAsync(TimeSpan.FromSeconds(15), Ct));

        // A browser abort closes the dialog instead of leaving it orphaned.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        pick = picker.PickAsync(_dir, cts.Token);
        dialog = await FindDialogAsync();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pick.WaitAsync(TimeSpan.FromSeconds(15), Ct));
        Assert.False(IsWindow(dialog));
    }

    static async Task<nint> FindDialogAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            // The shell view is populated asynchronously; wait until either dialog flavour has its content.
            foreach (var found in OwnDialogs())
            {
                var classes = Descendants(found);
                if (GetDlgItem(found, 1) != 0 && (classes.Contains("DirectUIHWND") || classes.Contains("SHBrowseForFolder ShellNameSpace Control")))
                    return found;
            }
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("folder dialog did not appear");
    }

    static List<nint> OwnDialogs()
    {
        var list = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId && IsWindowVisible(hwnd) && ClassOf(hwnd) == "#32770") list.Add(hwnd);
            return true;
        }, 0);
        return list;
    }

    static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 30 && !condition(); i++) await Task.Delay(100, Ct);
        return condition();
    }

    static HashSet<string> Descendants(nint hwnd)
    {
        var set = new HashSet<string>();
        EnumChildWindows(hwnd, (child, _) => { set.Add(ClassOf(child)); return true; }, 0);
        return set;
    }

    static string ClassOf(nint hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
    static string TextOf(nint hwnd) { var b = new StringBuilder(512); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }

    delegate bool EnumProc(nint hwnd, nint lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, nint lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(nint parent, EnumProc cb, nint lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern nint GetDlgItem(nint hwnd, int id);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
}
