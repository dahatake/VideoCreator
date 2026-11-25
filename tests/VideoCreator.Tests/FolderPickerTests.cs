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
        // no window can come in front of the lock screen, so nothing about the dialog can be measured while it is shown
        if (IsLockScreen(GetForegroundWindow())) Assert.Skip("The desktop is locked (lock screen in front)");
        var picker = new FolderPicker();

        // OK returns the folder that was pre-selected from the typed path.
        var pick = picker.PickAsync(_dir, Ct);
        var dialog = await FindDialogAsync();
        var classes = Descendants(dialog);
        Assert.DoesNotContain("SHBrowseForFolder ShellNameSpace Control", classes); // legacy tree dialog
        Assert.Contains("DirectUIHWND", classes);           // Explorer-style IFileOpenDialog
        Assert.Contains("Breadcrumb Parent", classes);      // address bar
        Assert.Equal(FolderPicker.Title, TextOf(dialog));
        Assert.True(await EventuallyAsync(() => IsInFront(dialog)), $"dialog should be the foreground window with no normal window above it; in front: {Describe(GetForegroundWindow())}, above: {Describe(FirstNormalWindowAbove(dialog))}");
        Assert.Equal(0, GetWindowLongPtr(dialog, -20) & 0x8); // not pinned above other apps once it is in front
        await Assert.ThrowsAsync<InvalidOperationException>(() => picker.PickAsync(null, Ct));
        SendMessage(GetDlgItem(dialog, 1), 0x00F5, 0, 0); // BM_CLICK on the select button
        Assert.Equal(_dir, await pick.WaitAsync(TimeSpan.FromSeconds(15), Ct), ignoreCase: true);

        // User cancel returns null and the gate is released again; a caller title replaces the default.
        pick = picker.PickAsync(Path.Combine(_dir, "missing"), "Choose the projects folder", Ct);
        dialog = await FindDialogAsync();
        Assert.Equal("Choose the projects folder", TextOf(dialog));
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

    // Foreground, and every visible window enumerated before it (z-order, top first) is a topmost one such as the taskbar.
    static bool IsInFront(nint dialog) => GetForegroundWindow() == dialog && FirstNormalWindowAbove(dialog) == 0;

    static nint FirstNormalWindowAbove(nint dialog)
    {
        nint above = 0;
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == dialog) return false;
            if (IsWindowVisible(hwnd) && (GetWindowLongPtr(hwnd, -20) & 0x8) == 0) above = hwnd;
            return above == 0;
        }, 0);
        return above;
    }

    static bool IsLockScreen(nint hwnd)
    {
        if (hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return p.ProcessName.Equals("LockApp", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    // Another program taking the focus during the test (an interactive desktop in use) reads differently from a dialog that never rose.
    static string Describe(nint hwnd)
    {
        if (hwnd == 0) return "none";
        GetWindowThreadProcessId(hwnd, out var pid);
        string name;
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); name = p.ProcessName; } catch (ArgumentException) { name = "?"; }
        return $"{name} (pid {pid}{(pid == Environment.ProcessId ? ", this test" : "")}) \"{TextOf(hwnd)}\" [{ClassOf(hwnd)}]";
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
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern nint GetDlgItem(nint hwnd, int id);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
}
