using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ClipShot.Services;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipShot";
    private const string StartupTaskId = "ClipShotStartup";
    public const string AutostartArgument = "--autostart";

    public static bool IsEnabled()
    {
        if (PackageEnvironment.IsPackaged)
            return IsStartupTaskEnabled();

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var configuredCommand = key?.GetValue(ValueName) as string;
        return LooksLikeOurStartupEntry(configuredCommand);
    }

    public static void SetEnabled(bool enabled)
    {
        if (PackageEnvironment.IsPackaged)
        {
            SetStartupTaskEnabled(enabled);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Couldn't access the current user's Windows startup settings.");

        if (enabled)
            key.SetValue(ValueName, GetStartupCommand());
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Rewrites the Run key to the current command (includes --autostart) when ClipShot
    /// is already configured to start with Windows. No-op for packaged Store installs.
    /// </summary>
    public static void EnsureCurrentCommand()
    {
        if (PackageEnvironment.IsPackaged)
            return;

        if (IsEnabled())
            SetEnabled(true);
    }

    private static bool IsStartupTaskEnabled()
    {
        var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    private static void SetStartupTaskEnabled(bool enabled)
    {
        var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        if (enabled)
        {
            switch (task.State)
            {
                case StartupTaskState.Disabled:
                    var result = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
                    if (result is StartupTaskState.DisabledByUser)
                    {
                        throw new InvalidOperationException(
                            "Windows blocked enabling ClipShot at startup. Turn it on in Settings → Apps → Startup.");
                    }
                    break;
                case StartupTaskState.DisabledByUser:
                    throw new InvalidOperationException(
                        "ClipShot startup was turned off in Windows Settings. Enable it under Settings → Apps → Startup.");
                case StartupTaskState.Enabled:
                case StartupTaskState.EnabledByPolicy:
                    break;
            }
        }
        else if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
        {
            task.Disable();
        }
    }

    private static string GetStartupCommand()
    {
        var executablePath = GetExecutablePath();
        return $"\"{executablePath}\" {AutostartArgument}";
    }

    private static bool LooksLikeOurStartupEntry(string? configuredCommand)
    {
        if (string.IsNullOrWhiteSpace(configuredCommand))
            return false;

        string executablePath;
        try
        {
            executablePath = GetExecutablePath();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (string.Equals(configuredCommand, GetStartupCommand(), StringComparison.OrdinalIgnoreCase))
            return true;

        // Legacy entries written before --autostart was added.
        var quotedPath = $"\"{executablePath}\"";
        if (string.Equals(configuredCommand, quotedPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(configuredCommand, executablePath, StringComparison.OrdinalIgnoreCase))
            return true;

        return configuredCommand.StartsWith(quotedPath, StringComparison.OrdinalIgnoreCase) ||
               configuredCommand.StartsWith(executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExecutablePath()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Couldn't determine the ClipShot application path.");

        return executablePath;
    }
}
