using System.Text.Json.Serialization;

namespace DisplaySwitcher.Model;

/// <summary>
/// A saved state for a single monitor: resolution, position (its relationship to
/// the other monitors), orientation, refresh rate and DPI scaling percentage.
/// </summary>
public sealed class DisplayState
{
    /// <summary>GDI device name, e.g. "\\.\DISPLAY1". Stable per adapter/port.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Friendly monitor name for display in the UI.</summary>
    public string FriendlyName { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Desktop position. (0,0) is the primary monitor.</summary>
    public int PositionX { get; set; }
    public int PositionY { get; set; }

    public int RefreshRate { get; set; }

    /// <summary>0 = default, 1 = 90°, 2 = 180°, 3 = 270° (DMDO_* values).</summary>
    public int Orientation { get; set; }

    /// <summary>DPI scaling percentage, e.g. 100, 125, 150. 0 = leave unchanged.</summary>
    public uint ScalingPercent { get; set; }

    [JsonIgnore]
    public bool IsPrimary => PositionX == 0 && PositionY == 0;
}

/// <summary>
/// A named, hotkey-bound collection of monitor states applied together.
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = "New Preset";

    /// <summary>Hotkey string such as "Ctrl+Alt+1". Empty = no hotkey.</summary>
    public string Hotkey { get; set; } = string.Empty;

    public List<DisplayState> Displays { get; set; } = new();
}

/// <summary>Root configuration persisted to disk.</summary>
public sealed class AppConfig
{
    public List<Preset> Presets { get; set; } = new();

    /// <summary>
    /// When true, toggle the Task View button after applying a preset to work around
    /// the Windows 11 bug where the taskbar disappears after a resolution change.
    /// </summary>
    public bool RefreshTaskbarAfterApply { get; set; } = true;
}
