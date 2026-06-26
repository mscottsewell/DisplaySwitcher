using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DisplaySwitcher.Display;

/// <summary>
/// Works around a Windows 11 quirk where the primary taskbar vanishes after a
/// resolution change. Replicates the manual fix of toggling the "Task view" button
/// off and back on, which forces Explorer to rebuild the taskbar without restarting it.
/// </summary>
public static class TaskbarFixer
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ValueName = "ShowTaskViewButton";

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>
    /// Toggles the Task View button setting off and on so Explorer redraws the taskbar.
    /// Best-effort: any failure is swallowed so it never breaks the display-apply flow.
    /// </summary>
    public static void RefreshTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, writable: true);
            if (key == null)
                return;

            int original = key.GetValue(ValueName) is int v ? v : 1;
            int toggled = original == 0 ? 1 : 0;

            // Flip to the opposite state, let Explorer react, then restore the user's choice.
            key.SetValue(ValueName, toggled, RegistryValueKind.DWord);
            Notify();
            Thread.Sleep(150);
            key.SetValue(ValueName, original, RegistryValueKind.DWord);
            Notify();
        }
        catch
        {
            // Taskbar cleanup is a nicety; never throw from the apply path.
        }
    }

    private static void Notify() =>
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "TraySettings",
            SMTO_ABORTIFHUNG, 1000, out _);
}
