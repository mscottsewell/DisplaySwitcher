# Display Switcher

A lightweight Windows 11 system-tray utility for instantly switching your monitor
setup between saved configurations — resolution, multi-monitor layout, orientation,
refresh rate, and per-monitor DPI scaling — from the tray menu or a global hotkey.

Built for the "I'm about to share my screen in Teams" moment: capture a comfortable
layout once, bind it to a hotkey, and flip back and forth in a keystroke.

## Features

- **Preset configurations** — capture your current multi-monitor layout (resolution,
  position/relationship between monitors, orientation, refresh rate, and scaling) and
  save it under a name.
- **Global hotkeys** — bind any preset to a shortcut (e.g. `Ctrl+Alt+1`) and switch
  without touching the mouse. Hotkeys are captured by *pressing* the combination, not
  typing it.
- **Tray menu** — right-click the tray icon to apply a preset, manage presets, or
  capture a new one.
- **Taskbar auto-fix** — works around the Windows 11 quirk where the primary taskbar
  disappears after a resolution change, by toggling the Task View button so Explorer
  redraws the taskbar (no Explorer restart). Can be turned off in the menu.
- **Run at startup** — optional, via the per-user registry `Run` key.
- **Single-instance** and self-contained: the tray icon is drawn at runtime, no
  external assets required.

## Requirements

- Windows 10 / 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build, or the .NET 9 Desktop
  Runtime to run a framework-dependent build.

## Build & run

```powershell
# Debug run
dotnet run --project DisplaySwitcher.csproj

# Release build
dotnet build DisplaySwitcher.csproj -c Release
.\bin\Release\net9.0-windows\DisplaySwitcher.exe
```

To produce a self-contained single-file executable (no runtime install required):

```powershell
dotnet publish DisplaySwitcher.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

## Usage

1. Launch the app — a two-monitor icon appears in the system tray.
2. Arrange your displays the way you want (via Windows Settings), then right-click the
   tray icon and choose **Capture current configuration…**. Give it a name and,
   optionally, press a hotkey combination to bind it.
3. Switch any time by clicking the preset in the tray menu or pressing its hotkey.
4. Use **Manage presets** to set/clear hotkeys, rename, overwrite with the current
   layout, or delete a preset.

### Menu reference

| Item | Action |
| --- | --- |
| *Preset name* | Apply that configuration |
| Capture current configuration… | Save the current layout as a new preset |
| Manage presets | Set hotkey, rename, overwrite, or delete a preset |
| Run at Windows startup | Toggle launching at sign-in |
| Refresh taskbar after switching | Toggle the Windows 11 taskbar auto-fix |
| Open config file | Open `presets.json` |
| Reload config | Re-read the config from disk |
| Exit | Quit |

## Configuration

Presets are stored as JSON at:

```
%AppData%\DisplaySwitcher\presets.json
```

## How it works

- **Resolution / position / orientation / refresh** use the GDI display APIs
  (`EnumDisplayDevices`, `EnumDisplaySettings`, `ChangeDisplaySettingsEx`). Changes for
  all monitors are staged with `CDS_NORESET` and committed once so the whole layout
  switches atomically.
- **Per-monitor DPI scaling** uses the Connecting-and-Configuring-Displays (CCD) APIs
  with the undocumented DPI-scale device-info types, matching the Windows Settings
  scaling slider.
- **Global hotkeys** use `RegisterHotKey` against a hidden message window; the capture
  dialog uses a low-level keyboard hook so it can read the pressed combination.

## Project structure

```
Program.cs                  Entry point (single-instance guard)
TrayApplicationContext.cs   Tray icon, context menu, hotkey wiring
Config/                     JSON load/save + startup registry helper
Display/                    DisplayManager (apply/capture) + TaskbarFixer
Hotkeys/                    Global hotkey registration + parsing
Model/                      Preset / DisplayState / AppConfig
Native/                     P/Invoke declarations + DPI helper
UI/                         Input + hotkey-capture dialogs, tray icon factory
```

## Notes & limitations

- Designed and tested on Windows 11. The taskbar-fix step is Windows 11 specific and
  is a no-op (harmless) elsewhere.
- The DPI-scaling APIs are undocumented; behavior can change between Windows builds.
- Some special keyboard keys (e.g. the Office key) may not be usable as hotkeys if the
  OS or firmware handles them before they reach the app.

## License

[MIT](LICENSE)
