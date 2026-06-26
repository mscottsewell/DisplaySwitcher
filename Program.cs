using System.Threading;

namespace DisplaySwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Ensure only one instance runs (hotkeys/tray icon would otherwise collide).
        using var mutex = new Mutex(initiallyOwned: true, "DisplaySwitcher.SingleInstance", out bool createdNew);
        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
