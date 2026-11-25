using System.IO;
using System.Text;

namespace ClipShot.Services;

/// <summary>
/// Append-only log so a silent WinExe crash can still be diagnosed.
/// </summary>
internal static class StartupLog
{
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipShot",
        "startup.log");

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(directory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
        catch
        {
            // Never block startup on logging.
        }
    }

    public static void WriteException(string context, Exception exception) =>
        Write($"{context}: {exception}");
}
