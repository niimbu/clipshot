using Microsoft.Win32;

namespace ClipShot.Services;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipShot";
    public const string AutostartArgument = "--autostart";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var configuredCommand = key?.GetValue(ValueName) as string;
        return LooksLikeOurStartupEntry(configuredCommand);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Couldn't access the current user's Windows startup settings.");

        if (enabled)
            key.SetValue(ValueName, GetStartupCommand());
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Rewrites the Run key to the current command (includes --autostart) when ClipShot
    /// is already configured to start with Windows.
    /// </summary>
    public static void EnsureCurrentCommand()
    {
        if (IsEnabled())
            SetEnabled(true);
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
