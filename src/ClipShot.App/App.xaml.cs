using System.Windows;

namespace ClipShot;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Stay alive when the main window is hidden to the tray. Exit only via tray → Exit
        // or Application.Shutdown().
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
    }
}
