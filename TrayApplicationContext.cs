using DisplaySwitcher.Config;
using DisplaySwitcher.Display;
using DisplaySwitcher.Hotkeys;
using DisplaySwitcher.Model;
using DisplaySwitcher.UI;

namespace DisplaySwitcher;

/// <summary>
/// The tray-resident application: owns the NotifyIcon, its right-click menu,
/// the preset list, and global hotkey registrations.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly HotkeyManager _hotkeys = new();
    private readonly Dictionary<int, Preset> _hotkeyToPreset = new();

    private AppConfig _config;

    public TrayApplicationContext()
    {
        _config = ConfigStore.Load();

        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "Display Switcher",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowPresetPickerOrCapture();

        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        RebuildMenu();
        RegisterHotkeys();
    }

    // ---- Menu ----

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        if (_config.Presets.Count == 0)
        {
            var none = new ToolStripMenuItem("(no presets yet)") { Enabled = false };
            menu.Items.Add(none);
        }
        else
        {
            foreach (var preset in _config.Presets)
            {
                var item = new ToolStripMenuItem(preset.Name);
                if (!string.IsNullOrWhiteSpace(preset.Hotkey))
                    item.ShortcutKeyDisplayString = preset.Hotkey;
                var captured = preset;
                item.Click += (_, _) => ApplyPreset(captured);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Capture current configuration…", null, (_, _) => CaptureCurrentConfiguration());

        if (_config.Presets.Count > 0)
        {
            var manage = new ToolStripMenuItem("Manage presets");
            foreach (var preset in _config.Presets)
            {
                var sub = new ToolStripMenuItem(preset.Name);
                var captured = preset;
                sub.DropDownItems.Add("Set hotkey…", null, (_, _) => SetHotkey(captured));
                sub.DropDownItems.Add("Rename…", null, (_, _) => RenamePreset(captured));
                sub.DropDownItems.Add("Overwrite with current layout", null, (_, _) => OverwritePreset(captured));
                sub.DropDownItems.Add("Delete", null, (_, _) => DeletePreset(captured));
                manage.DropDownItems.Add(sub);
            }
            menu.Items.Add(manage);
        }

        menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Run at Windows startup")
        {
            Checked = StartupManager.IsEnabled(),
            CheckOnClick = true,
        };
        startup.Click += (_, _) => StartupManager.SetEnabled(startup.Checked);
        menu.Items.Add(startup);

        var taskbarFix = new ToolStripMenuItem("Refresh taskbar after switching")
        {
            Checked = _config.RefreshTaskbarAfterApply,
            CheckOnClick = true,
            ToolTipText = "Toggles the Task View button so the taskbar reappears after a resolution change.",
        };
        taskbarFix.Click += (_, _) =>
        {
            _config.RefreshTaskbarAfterApply = taskbarFix.Checked;
            ConfigStore.Save(_config);
        };
        menu.Items.Add(taskbarFix);

        menu.Items.Add("Open config file", null, (_, _) => OpenConfigFile());
        menu.Items.Add("Reload config", null, (_, _) => ReloadConfig());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon.ContextMenuStrip = menu;
    }

    private void ShowPresetPickerOrCapture()
    {
        if (_config.Presets.Count == 0)
            CaptureCurrentConfiguration();
    }

    // ---- Preset actions ----

    private void ApplyPreset(Preset preset)
    {
        bool ok = DisplayManager.Apply(preset, out string message);
        if (ok && _config.RefreshTaskbarAfterApply)
            TaskbarFixer.RefreshTaskbar();
        _notifyIcon.ShowBalloonTip(
            2000,
            ok ? "Display Switcher" : "Display Switcher – failed",
            message,
            ok ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private void CaptureCurrentConfiguration()
    {
        var displays = DisplayManager.CaptureCurrent();
        if (displays.Count == 0)
        {
            MessageBox.Show("No active displays were detected.", "Display Switcher",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string defaultName = $"Preset {_config.Presets.Count + 1}";
        string? name = InputDialog.Show("Capture configuration",
            "Name this configuration (current resolution, layout and scaling will be saved):",
            defaultName);
        if (name == null)
            return;

        // null = cancelled (no hotkey assigned); "" = explicitly no hotkey.
        string? hotkey = HotkeyCaptureDialog.Show();

        _config.Presets.Add(new Preset
        {
            Name = name,
            Hotkey = hotkey ?? string.Empty,
            Displays = displays,
        });

        SaveAndRefresh();
    }

    private void OverwritePreset(Preset preset)
    {
        if (MessageBox.Show($"Replace '{preset.Name}' with the current display layout?",
                "Display Switcher", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        preset.Displays = DisplayManager.CaptureCurrent();
        SaveAndRefresh();
    }

    private void RenamePreset(Preset preset)
    {
        string? name = InputDialog.Show("Rename preset", "New name:", preset.Name);
        if (name == null)
            return;
        preset.Name = name;
        SaveAndRefresh();
    }

    private void SetHotkey(Preset preset)
    {
        // null = cancelled (leave unchanged); "" = cleared; otherwise a captured combo.
        string? hotkey = HotkeyCaptureDialog.Show(preset.Hotkey);
        if (hotkey == null)
            return;

        preset.Hotkey = hotkey;
        SaveAndRefresh();
    }

    private void DeletePreset(Preset preset)
    {
        if (MessageBox.Show($"Delete preset '{preset.Name}'?", "Display Switcher",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        _config.Presets.Remove(preset);
        SaveAndRefresh();
    }

    // ---- Hotkeys ----

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        _hotkeyToPreset.Clear();

        foreach (var preset in _config.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Hotkey))
                continue;
            int id = _hotkeys.Register(preset.Hotkey);
            if (id > 0)
                _hotkeyToPreset[id] = preset;
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (_hotkeyToPreset.TryGetValue(id, out var preset))
            ApplyPreset(preset);
    }

    // ---- Persistence / lifecycle ----

    private void SaveAndRefresh()
    {
        try
        {
            ConfigStore.Save(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save config:\n{ex.Message}", "Display Switcher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RebuildMenu();
        RegisterHotkeys();
    }

    private void ReloadConfig()
    {
        _config = ConfigStore.Load();
        RebuildMenu();
        RegisterHotkeys();
    }

    private void OpenConfigFile()
    {
        try
        {
            if (!File.Exists(ConfigStore.ConfigPath))
                ConfigStore.Save(_config);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ConfigStore.ConfigPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open config file:\n{ex.Message}", "Display Switcher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        _hotkeys.Dispose();
        _notifyIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeys.Dispose();
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
