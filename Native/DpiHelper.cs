using static DisplaySwitcher.Native.NativeMethods;

namespace DisplaySwitcher.Native;

/// <summary>
/// Reads and writes the per-display DPI (scaling %) using the undocumented
/// DisplayConfig DPI-scale device info types. The relative-index algorithm matches
/// the behaviour of the Windows Settings "Scale" slider.
/// </summary>
internal static class DpiHelper
{
    // The discrete scaling percentages Windows exposes, in order.
    private static readonly int[] DpiVals = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    public readonly record struct DpiScalingInfo(uint Current, uint Recommended, uint Maximum, bool Initialized);

    /// <summary>
    /// Resolves the CCD adapter LUID + source id for a GDI device name (e.g. "\\.\DISPLAY1").
    /// </summary>
    private static bool TryGetSourceId(string gdiDeviceName, out LUID adapterId, out uint sourceId)
    {
        adapterId = default;
        sourceId = 0;

        int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (err != 0)
            return false;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != 0)
            return false;

        for (int i = 0; i < pathCount; i++)
        {
            var src = paths[i].sourceInfo;
            var name = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = src.adapterId,
                    id = src.id,
                },
            };

            if (DisplayConfigGetDeviceInfo(ref name) == 0 &&
                string.Equals(name.viewGdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                adapterId = src.adapterId;
                sourceId = src.id;
                return true;
            }
        }

        return false;
    }

    public static DpiScalingInfo GetDpiScaling(string gdiDeviceName)
    {
        if (!TryGetSourceId(gdiDeviceName, out LUID adapterId, out uint sourceId))
            return default;

        var request = new DISPLAYCONFIG_SOURCE_DPI_SCALE_GET
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DPI_SCALE_GET>(),
                adapterId = adapterId,
                id = sourceId,
            },
        };

        if (DisplayConfigGetDeviceInfo(ref request) != 0)
            return default;

        int minAbs = Math.Abs(request.minScaleRel);
        if (minAbs >= DpiVals.Length)
            return default;

        // Clamp current to the supported window.
        int cur = request.curScaleRel;
        if (cur < request.minScaleRel) cur = request.minScaleRel;
        else if (cur > request.maxScaleRel) cur = request.maxScaleRel;

        int curIndex = cur + minAbs;
        int maxIndex = request.maxScaleRel + minAbs;

        uint current = IndexToPercent(curIndex);
        uint recommended = IndexToPercent(minAbs);
        uint maximum = IndexToPercent(maxIndex);

        return new DpiScalingInfo(current, recommended, maximum, true);
    }

    public static bool SetDpiScaling(string gdiDeviceName, uint percentToSet)
    {
        if (!TryGetSourceId(gdiDeviceName, out LUID adapterId, out uint sourceId))
            return false;

        var current = new DISPLAYCONFIG_SOURCE_DPI_SCALE_GET
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_DPI_SCALE,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DPI_SCALE_GET>(),
                adapterId = adapterId,
                id = sourceId,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref current) != 0)
            return false;

        int minAbs = Math.Abs(current.minScaleRel);
        int recommendedIndex = minAbs;

        int targetIndex = PercentToIndex(percentToSet);
        if (targetIndex < 0)
            return false;

        // Constrain to what the adapter supports.
        int maxIndex = current.maxScaleRel + minAbs;
        if (targetIndex < 0) targetIndex = 0;
        if (targetIndex > maxIndex) targetIndex = maxIndex;

        int scaleRel = targetIndex - recommendedIndex;

        var setPacket = new DISPLAYCONFIG_SOURCE_DPI_SCALE_SET
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_SET_DPI_SCALE,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DPI_SCALE_SET>(),
                adapterId = adapterId,
                id = sourceId,
            },
            scaleRel = scaleRel,
        };

        return DisplayConfigSetDeviceInfo(ref setPacket) == 0;
    }

    private static uint IndexToPercent(int index)
    {
        if (index < 0) index = 0;
        if (index >= DpiVals.Length) index = DpiVals.Length - 1;
        return (uint)DpiVals[index];
    }

    private static int PercentToIndex(uint percent)
    {
        for (int i = 0; i < DpiVals.Length; i++)
        {
            if (DpiVals[i] == percent)
                return i;
        }

        // Snap to the nearest supported value if an exact match isn't found.
        int best = -1;
        int bestDelta = int.MaxValue;
        for (int i = 0; i < DpiVals.Length; i++)
        {
            int delta = Math.Abs(DpiVals[i] - (int)percent);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }
}
