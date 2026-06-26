using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DisplaySwitcher.UI;

/// <summary>
/// Captures a global-hotkey combination by letting the user physically press it.
/// Uses a low-level keyboard hook so modifier keys (including Win) and special keys
/// are observed raw and are swallowed while the dialog is open (so pressing Win does
/// not open the Start menu, etc.).
///
/// Returns via <see cref="Show"/>: a combo string like "Ctrl+Alt+1", an empty string
/// ("no hotkey"), or null if the user cancelled.
/// </summary>
public sealed class HotkeyCaptureDialog : Form
{
    // ---- Win32 hook plumbing ----
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // Virtual key codes for modifiers.
    private const uint VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_CONTROL = 0x11;
    private const uint VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_MENU = 0x12;
    private const uint VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_SHIFT = 0x10;
    private const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint VK_ESCAPE = 0x1B;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private bool _ctrl, _alt, _shift, _win;

    private readonly Label _preview;
    private readonly Label _status;
    private readonly Label _raw;
    private readonly List<string> _recentKeys = new();

    private string? _result;
    public string? Result => _result;

    public HotkeyCaptureDialog(string current)
    {
        _proc = HookCallback;

        Text = "Set hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 234);

        var prompt = new Label
        {
            Text = "Press the key combination you want (e.g. Ctrl + Alt + 1).",
            Location = new Point(12, 12),
            Size = new Size(356, 20),
        };

        _preview = new Label
        {
            Text = string.IsNullOrWhiteSpace(current) ? "(press keys…)" : current,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(12, 40),
            Size = new Size(356, 40),
        };

        _status = new Label
        {
            Text = "Tip: include Ctrl, Alt, Shift or Win.",
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 88),
            Size = new Size(356, 20),
        };

        var rawHeader = new Label
        {
            Text = "Keys detected (useful for the Office key):",
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 114),
            Size = new Size(356, 18),
        };

        _raw = new Label
        {
            Text = "—",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(FontFamily.GenericMonospace, 8.5f),
            ForeColor = SystemColors.GrayText,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(12, 132),
            Size = new Size(356, 40),
        };

        var noHotkey = new Button
        {
            Text = "No hotkey",
            Location = new Point(12, 192),
            Size = new Size(90, 28),
            TabStop = false,
        };
        noHotkey.Click += (_, _) =>
        {
            _result = string.Empty;
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(293, 192),
            Size = new Size(75, 28),
            TabStop = false,
        };

        Controls.Add(prompt);
        Controls.Add(_preview);
        Controls.Add(_status);
        Controls.Add(rawHeader);
        Controls.Add(_raw);
        Controls.Add(noHotkey);
        Controls.Add(cancel);
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        base.OnFormClosed(e);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        int msg = (int)wParam;
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        uint vk = data.vkCode;

        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

        if (isDown)
            BeginInvoke(() => RecordRaw(vk));

        if (UpdateModifier(vk, isDown))
        {
            BeginInvoke(UpdatePreview);
            return (IntPtr)1; // swallow modifier so it can't leak to the OS
        }

        if (isUp)
            return (IntPtr)1; // swallow key-up of the final key too

        if (isDown)
        {
            if (vk == VK_ESCAPE)
            {
                BeginInvoke(() => { DialogResult = DialogResult.Cancel; Close(); });
                return (IntPtr)1;
            }

            string? token = KeyToken(vk);
            if (token == null)
            {
                BeginInvoke(() => _status.Text = "That key can't be used as a hotkey. Try another.");
                return (IntPtr)1;
            }

            if (!(_ctrl || _alt || _shift || _win))
            {
                BeginInvoke(() => _status.Text = "Add Ctrl, Alt, Shift or Win, then press the key.");
                return (IntPtr)1;
            }

            string combo = BuildCombo(token);
            BeginInvoke(() =>
            {
                _result = combo;
                _preview.Text = combo;
                DialogResult = DialogResult.OK;
                Close();
            });
            return (IntPtr)1;
        }

        return (IntPtr)1;
    }

    private bool UpdateModifier(uint vk, bool isDown)
    {
        switch (vk)
        {
            case VK_LCONTROL or VK_RCONTROL or VK_CONTROL:
                _ctrl = isDown; return true;
            case VK_LMENU or VK_RMENU or VK_MENU:
                _alt = isDown; return true;
            case VK_LSHIFT or VK_RSHIFT or VK_SHIFT:
                _shift = isDown; return true;
            case VK_LWIN or VK_RWIN:
                _win = isDown; return true;
            default:
                return false;
        }
    }

    private void UpdatePreview()
    {
        var parts = CurrentModifiers();
        _preview.Text = parts.Count > 0 ? string.Join(" + ", parts) + " + …" : "(press keys…)";
    }

    /// <summary>Appends a raw key event to the rolling "keys detected" readout.</summary>
    private void RecordRaw(uint vk)
    {
        string token = KeyToken(vk) ?? VkName(vk);
        _recentKeys.Add($"{token} (0x{vk:X2})");
        if (_recentKeys.Count > 6)
            _recentKeys.RemoveAt(0);
        _raw.Text = string.Join("  ", _recentKeys);
    }

    /// <summary>Friendly name for virtual keys that aren't valid hotkey tokens (modifiers, etc.).</summary>
    private static string VkName(uint vk) => vk switch
    {
        VK_LCONTROL => "LCtrl",
        VK_RCONTROL => "RCtrl",
        VK_CONTROL => "Ctrl",
        VK_LMENU => "LAlt",
        VK_RMENU => "RAlt",
        VK_MENU => "Alt",
        VK_LSHIFT => "LShift",
        VK_RSHIFT => "RShift",
        VK_SHIFT => "Shift",
        VK_LWIN => "LWin",
        VK_RWIN => "RWin",
        0x5D => "Menu/Apps",
        _ => "vk",
    };

    private List<string> CurrentModifiers()
    {
        var parts = new List<string>();
        if (_ctrl) parts.Add("Ctrl");
        if (_alt) parts.Add("Alt");
        if (_shift) parts.Add("Shift");
        if (_win) parts.Add("Win");
        return parts;
    }

    private string BuildCombo(string keyToken)
    {
        var parts = CurrentModifiers();
        parts.Add(keyToken);
        return string.Join("+", parts);
    }

    /// <summary>Maps a virtual-key code to a token understood by HotkeyManager.TryParse.</summary>
    private static string? KeyToken(uint vk)
    {
        // Digits (top row) and letters.
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        // Numpad digits.
        if (vk is >= 0x60 and <= 0x69) return ((char)('0' + (vk - 0x60))).ToString();
        // Function keys F1..F24.
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);

        return vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x2D => "Insert",
            0x2E => "Delete",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => null,
        };
    }

    /// <summary>
    /// Shows the capture dialog. Returns a combo string, "" for "no hotkey", or null if cancelled.
    /// </summary>
    public static string? Show(string current = "")
    {
        using var dialog = new HotkeyCaptureDialog(current);
        return dialog.ShowDialog() == DialogResult.OK ? dialog.Result : null;
    }
}
