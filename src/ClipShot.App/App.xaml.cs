using System.Linq;
using System.Threading;
using System.Windows;
using ClipShot.Services;
using MessageBox = System.Windows.MessageBox;

namespace ClipShot;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\ClipShot";
    private const string ShowEventName = @"Local\ClipShot.Show";
    private static Mutex? _instance;
    private static EventWaitHandle? _showRequest;

    /// <summary>
    /// True when launched from the Windows startup entry — stay in the tray.
    /// </summary>
    public static bool StartMinimizedToTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // StartupUri would show a window before Shutdown can run. Never use it.
        StartupUri = null;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartMinimizedToTray = e.Args.Any(IsAutostartArgument);

        _instance = new Mutex(true, InstanceMutexName, out var created);
        if (!created)
        {
            // Start menu or a second shortcut while ClipShot is already in the tray.
            SignalRunningInstance();
            if (!NativeMethods.TryActivateMainWindow() && !StartMinimizedToTray)
            {
                MessageBox.Show(
                    "ClipShot is already running in the system tray.\n\n" +
                    "Open it from the tray icon, or right-click the tray icon and choose Exit, then start ClipShot again.",
                    "ClipShot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Environment.Exit(0);
            return;
        }

        // Stay alive when the main window is hidden to the tray. Exit only via tray → Exit
        // or Application.Shutdown().
        base.OnStartup(e);

        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var window = new MainWindow();
        MainWindow = window;
        window.ListenForShowRequests(_showRequest);
        window.StartWhenShellIsReady();
    }

    private static void SignalRunningInstance()
    {
        try
        {
            using var showRequest = EventWaitHandle.OpenExisting(ShowEventName);
            showRequest.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // An older build is running and does not listen for this signal.
        }
    }

    private static bool IsAutostartArgument(string argument) =>
        string.Equals(argument, StartupManager.AutostartArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "/autostart", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, "-autostart", StringComparison.OrdinalIgnoreCase);
}
