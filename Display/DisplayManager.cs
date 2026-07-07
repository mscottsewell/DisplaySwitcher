using DisplaySwitcher.Model;
using DisplaySwitcher.Native;
using static DisplaySwitcher.Native.NativeMethods;

namespace DisplaySwitcher.Display;

/// <summary>
/// Captures the current multi-monitor layout into a <see cref="Preset"/> and applies a
/// saved preset back to the system (resolution, position, orientation, refresh, DPI).
/// </summary>
public static class DisplayManager
{
    /// <summary>
    /// Enumerates every monitor attached to the desktop and snapshots its current state.
    /// </summary>
    public static List<DisplayState> CaptureCurrent()
    {
        var result = new List<DisplayState>();

        var device = new DISPLAY_DEVICE();
        device.cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();

        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
            {
                device.cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();
                continue;
            }

            string gdiName = device.DeviceName;

            var mode = new DEVMODE();
            mode.dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>();
            if (EnumDisplaySettings(gdiName, ENUM_CURRENT_SETTINGS, ref mode))
            {
                var dpi = DpiHelper.GetDpiScaling(gdiName);

                result.Add(new DisplayState
                {
                    DeviceName = gdiName,
                    FriendlyName = GetFriendlyName(gdiName),
                    Width = (int)mode.dmPelsWidth,
                    Height = (int)mode.dmPelsHeight,
                    PositionX = mode.dmPositionX,
                    PositionY = mode.dmPositionY,
                    RefreshRate = (int)mode.dmDisplayFrequency,
                    Orientation = (int)mode.dmDisplayOrientation,
                    ScalingPercent = dpi.Initialized ? dpi.Current : 0,
                });
            }

            device.cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();
        }

        return result;
    }

    /// <summary>
    /// Gets a human-friendly monitor name (the monitor's DeviceString) for a GDI adapter name.
    /// </summary>
    private static string GetFriendlyName(string gdiName)
    {
        var monitor = new DISPLAY_DEVICE();
        monitor.cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();
        if (EnumDisplayDevices(gdiName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString))
            return monitor.DeviceString;
        return gdiName;
    }

    /// <summary>
    /// Applies a preset. Returns true on success; <paramref name="message"/> describes the outcome.
    /// </summary>
    public static bool Apply(Preset preset, out string message)
    {
        if (preset.Displays.Count == 0)
        {
            message = "Preset has no displays.";
            return false;
        }

        // 1) Apply DPI scaling first (independent of the mode change).
        foreach (var d in preset.Displays)
        {
            if (d.ScalingPercent > 0)
                DpiHelper.SetDpiScaling(d.DeviceName, d.ScalingPercent);
        }

        // 2) Stage resolution/position/orientation for every display with CDS_NORESET,
        //    then commit once so the whole layout changes atomically.
        var attached = new HashSet<string>(GetAttachedDeviceNames(), StringComparer.OrdinalIgnoreCase);
        int staged = 0;

        foreach (var d in preset.Displays)
        {
            if (!attached.Contains(d.DeviceName))
                continue;

            var mode = new DEVMODE();
            mode.dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>();
            if (!EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref mode))
                continue;

            mode.dmPelsWidth = (uint)d.Width;
            mode.dmPelsHeight = (uint)d.Height;
            mode.dmPositionX = d.PositionX;
            mode.dmPositionY = d.PositionY;
            mode.dmDisplayOrientation = (uint)d.Orientation;
            mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_POSITION | DM_DISPLAYORIENTATION;

            if (d.RefreshRate > 0)
            {
                mode.dmDisplayFrequency = (uint)d.RefreshRate;
                mode.dmFields |= DM_DISPLAYFREQUENCY;
            }

            // Swap width/height for portrait orientations so the mode is valid.
            if (d.Orientation == 1 || d.Orientation == 3)
            {
                (mode.dmPelsWidth, mode.dmPelsHeight) = (mode.dmPelsHeight, mode.dmPelsWidth);
            }

            uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
            if (d.IsPrimary)
                flags |= CDS_SET_PRIMARY;

            int rc = ChangeDisplaySettingsEx(d.DeviceName, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
            if (rc != DISP_CHANGE_SUCCESSFUL)
            {
                message = $"Failed to stage '{d.FriendlyName}' ({d.Width}x{d.Height}): {DescribeResult(rc)}.";
                return false;
            }

            staged++;
        }

        if (staged == 0)
        {
            message = "None of the preset's displays are currently attached.";
            return false;
        }

        // 3) Commit all staged changes.
        int commit = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (commit != DISP_CHANGE_SUCCESSFUL)
        {
            message = $"Failed to apply layout: {DescribeResult(commit)}.";
            return false;
        }

        message = $"Applied '{preset.Name}'.";
        return true;
    }

    private static IEnumerable<string> GetAttachedDeviceNames()
    {
        var device = new DISPLAY_DEVICE();
        device.cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                yield return device.DeviceName;
            device.cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();
        }
    }

    private static string DescribeResult(int code) => code switch
    {
        DISP_CHANGE_RESTART => "a restart is required",
        DISP_CHANGE_FAILED => "the display driver failed the change",
        DISP_CHANGE_BADMODE => "the resolution/refresh combination is not supported",
        DISP_CHANGE_NOTUPDATED => "unable to write settings to the registry",
        DISP_CHANGE_BADFLAGS => "invalid flags",
        DISP_CHANGE_BADPARAM => "invalid parameter",
        DISP_CHANGE_BADDUALVIEW => "unsupported on this dual-view configuration",
        _ => $"error code {code}",
    };
}
