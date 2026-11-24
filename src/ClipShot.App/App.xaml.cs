using System.Linq;
using System.Windows;
using ClipShot.Services;

namespace ClipShot;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// True when launched from the Windows startup entry — stay in the tray.
    /// </summary>
    public static bool StartMinimizedToTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (UpdateInstaller.ShouldCloseUpdateRelaunch())
        {
            Shutdown();
            return;
        }

        // Stay alive when the main window is hidden to the tray. Exit only via tray → Exit
        // or Application.Shutdown().
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartMinimizedToTray = e.Args.Any(IsAutostartArgument);
        // StartupUri would build the window during sign-in, before Explorer can
        // show it or deliver shortcuts. Create it after the shell is ready.
        StartupUri = null;
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.StartWhenShellIsReady();
    }

    private static bool IsAutostartArgument(string argument) =>
        string.Equals(argument, StartupManager.AutostartArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "/autostart", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "-autostart", StringComparison.OrdinalIgnoreCase);
}
