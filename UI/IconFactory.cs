using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DisplaySwitcher.UI;

/// <summary>Generates the tray icon at runtime so no .ico asset is required.</summary>
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Two overlapping monitors to suggest "switch displays".
            using var back = new SolidBrush(Color.FromArgb(120, 144, 226));
            using var front = new SolidBrush(Color.FromArgb(33, 96, 207));
            using var pen = new Pen(Color.White, 1.5f);

            g.FillRectangle(back, 3, 5, 18, 13);
            g.DrawRectangle(pen, 3, 5, 18, 13);

            g.FillRectangle(front, 11, 13, 18, 13);
            g.DrawRectangle(pen, 11, 13, 18, 13);
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
