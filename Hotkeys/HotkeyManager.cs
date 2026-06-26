using System.Runtime.InteropServices;

namespace DisplaySwitcher.Hotkeys;

/// <summary>
/// Registers system-wide hotkeys against a hidden message window and raises
/// <see cref="HotkeyPressed"/> with the registration id when one is pressed.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [Flags]
    private enum Modifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000,
    }

    private readonly MessageWindow _window;
    private readonly List<int> _registeredIds = new();
    private int _nextId = 1;

    public event Action<int>? HotkeyPressed;

    public HotkeyManager()
    {
        _window = new MessageWindow(OnMessage);
    }

    /// <summary>
    /// Registers a hotkey from a string like "Ctrl+Alt+1". Returns the id on success,
    /// or -1 if the string is empty/invalid or the combination is already taken.
    /// </summary>
    public int Register(string hotkey)
    {
        if (!TryParse(hotkey, out uint modifiers, out uint vk))
            return -1;

        int id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, modifiers | (uint)Modifiers.NoRepeat, vk))
            return -1;

        _registeredIds.Add(id);
        return id;
    }

    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_window.Handle, id);
        _registeredIds.Clear();
        _nextId = 1;
    }

    private void OnMessage(int id) => HotkeyPressed?.Invoke(id);

    /// <summary>
    /// Parses a hotkey string ("Ctrl+Alt+Shift+1", "Win+F12") into modifier flags and a virtual key.
    /// </summary>
    public static bool TryParse(string hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool hasKey = false;

        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= (uint)Modifiers.Control;
                    break;
                case "alt":
                    modifiers |= (uint)Modifiers.Alt;
                    break;
                case "shift":
                    modifiers |= (uint)Modifiers.Shift;
                    break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= (uint)Modifiers.Win;
                    break;
                default:
                    if (TryParseKey(raw, out vk))
                        hasKey = true;
                    else
                        return false;
                    break;
            }
        }

        return hasKey && modifiers != 0;
    }

    private static bool TryParseKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' || c is >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        // Function keys F1..F24
        if ((key.StartsWith('F') || key.StartsWith('f')) && int.TryParse(key.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (fn - 1)); // VK_F1 = 0x70
            return true;
        }

        // A few common named keys.
        switch (key.ToLowerInvariant())
        {
            case "space": vk = 0x20; return true;
            case "enter": case "return": vk = 0x0D; return true;
            case "tab": vk = 0x09; return true;
            case "home": vk = 0x24; return true;
            case "end": vk = 0x23; return true;
            case "pageup": case "pgup": vk = 0x21; return true;
            case "pagedown": case "pgdn": vk = 0x22; return true;
            case "insert": vk = 0x2D; return true;
            case "delete": case "del": vk = 0x2E; return true;
            case "up": vk = 0x26; return true;
            case "down": vk = 0x28; return true;
            case "left": vk = 0x25; return true;
            case "right": vk = 0x27; return true;
        }

        return false;
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }

    /// <summary>Hidden window that receives WM_HOTKEY messages.</summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public MessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                _onHotkey((int)m.WParam);
            base.WndProc(ref m);
        }
    }
}
