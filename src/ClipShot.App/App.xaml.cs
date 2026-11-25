using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        HookExceptionLogging();
        StartupLog.Write("OnStartup");

        // StartupUri would show a window before we control launch. Never use it.
        StartupUri = null;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartMinimizedToTray = e.Args.Any(IsAutostartArgument);
        StartupLog.Write(StartMinimizedToTray ? "Mode: tray autostart" : "Mode: normal window");

        if (!TryClaimInstance(out var created))
        {
            StartupLog.Write("Exit: could not claim instance mutex");
            MessageBox.Show(
                "ClipShot could not start because Windows blocked a second copy from opening.\n\n" +
                "Open Task Manager, end every ClipShot process, then try again.",
                "ClipShot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Environment.Exit(1);
            return;
        }

        if (!created)
        {
            StartupLog.Write("Exit: another ClipShot is already running");
            SignalRunningInstance();
            if (!NativeMethods.TryActivateMainWindow() && !StartMinimizedToTray)
            {
                MessageBox.Show(
                    "ClipShot is already running.\n\n" +
                    "Look for it in the system tray (the ^ arrow near the clock), or open Task Manager and end ClipShot, then start it again.",
                    "ClipShot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        try
        {
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var window = new MainWindow();
            MainWindow = window;
            window.ListenForShowRequests(_showRequest);

            // A Start menu launch must show the window immediately. Only sign-in waits for the shell.
            if (StartMinimizedToTray)
                window.StartWhenShellIsReady();
            else
                window.StartNormally();
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("OnStartup failed", ex);
            MessageBox.Show(
                "ClipShot could not open.\n\n" + ex.Message +
                "\n\nDetails were saved to:\n" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipShot", "startup.log"),
                "ClipShot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private static bool TryClaimInstance(out bool created)
    {
        created = false;
        try
        {
            _instance = new Mutex(true, InstanceMutexName, out created);
            return true;
        }
        catch (AbandonedMutexException)
        {
            // A previous ClipShot exited without releasing the lock. Take it and continue.
            _instance = new Mutex(true, InstanceMutexName, out created);
            created = true;
            StartupLog.Write("Recovered an abandoned instance lock");
            return true;
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("Mutex", ex);
            return false;
        }
    }

    private static void HookExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                StartupLog.WriteException("Unhandled", ex);
            else
                StartupLog.Write($"Unhandled: {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            StartupLog.WriteException("Task", args.Exception);
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupLog.WriteException("Dispatcher", e.Exception);
        MessageBox.Show(
            "ClipShot hit an unexpected error.\n\n" + e.Exception.Message +
            "\n\nDetails were saved to:\n" +
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipShot", "startup.log"),
            "ClipShot",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
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
