using System.Linq;
using System.Windows;
using ClipShot.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace ClipShot;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// True when launched from the Windows startup entry — stay in the tray.
    /// </summary>
    public static bool StartMinimizedToTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Stay alive when the main window is hidden to the tray. Exit only via tray → Exit
        // or Application.Shutdown().
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartMinimizedToTray = e.Args.Any(IsAutostartArgument) || WasStartedByStartupTask();
        base.OnStartup(e);
    }

    private static bool WasStartedByStartupTask()
    {
        if (!PackageEnvironment.IsPackaged)
            return false;

        try
        {
            var args = AppInstance.GetActivatedEventArgs();
            return args?.Kind == ActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAutostartArgument(string argument) =>
        string.Equals(argument, StartupManager.AutostartArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "/autostart", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "-autostart", StringComparison.OrdinalIgnoreCase);
}
