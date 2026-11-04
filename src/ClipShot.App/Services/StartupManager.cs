using Microsoft.Win32;

namespace ClipShot.Services;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipShot";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var configuredCommand = key?.GetValue(ValueName) as string;
        return string.Equals(configuredCommand, GetStartupCommand(), StringComparison.OrdinalIgnoreCase);
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

    private static string GetStartupCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Couldn't determine the ClipShot application path.");

        return $"\"{executablePath}\"";
    }
}