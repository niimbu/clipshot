using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;

namespace ClipShot.Services;

internal static class UpdateInstaller
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static async Task DownloadAndInstallAsync(
        Uri downloadUri,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (downloadUri.Scheme != Uri.UriSchemeHttps || downloadUri.Host != "github.com" ||
            !downloadUri.AbsolutePath.StartsWith("/niimbu/clipshot/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update URL is not an official ClipShot release asset.");

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !string.Equals(Path.GetFileName(executable), "ClipShot.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current application path could not be determined.");

        var staged = Path.Combine(Path.GetDirectoryName(executable)!, ".ClipShot.update.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), "ClipShot-update-" + Guid.NewGuid().ToString("N") + ".ps1");

        using var response = await Client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 100000000)
            throw new InvalidDataException("The update package is unexpectedly large.");

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new MemoryStream(total is > 0 and <= int.MaxValue ? (int)total.Value : 0);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            readTotal += read;
            if (total is > 0)
                progress?.Report(Math.Clamp(readTotal / (double)total.Value, 0, 1));
            else
                progress?.Report(-1); // indeterminate
        }

        var bytes = destination.ToArray();
        if (bytes.Length < 2 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new InvalidDataException("The downloaded update is not a valid Windows executable.");

        progress?.Report(1);
        await File.WriteAllBytesAsync(staged, bytes, cancellationToken);

        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference = 'Stop'\n" +
            "$processId = " + Environment.ProcessId + "\n" +
            "while (Get-Process -Id $processId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }\n" +
            "Move-Item -LiteralPath " + Quote(staged) + " -Destination " + Quote(executable) + " -Force\n" +
            "try { Unblock-File -LiteralPath " + Quote(executable) + " -ErrorAction SilentlyContinue } catch {}\n" +
            "try {\n" +
            "  $settingsDir = Join-Path $env:APPDATA 'ClipShot'\n" +
            "  New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null\n" +
            "  $ticks = (Get-Item -LiteralPath " + Quote(executable) + ").LastWriteTimeUtc.Ticks\n" +
            "  Set-Content -LiteralPath (Join-Path $settingsDir 'last-install') -Value $ticks -Encoding ascii\n" +
            "} catch {}\n" +
            "Remove-Item -LiteralPath $PSCommandPath -Force\n";
        await File.WriteAllTextAsync(scriptPath, script, new System.Text.UTF8Encoding(false), cancellationToken);
        var updaterProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (updaterProcess is null)
            throw new InvalidOperationException("Windows could not start the update helper.");
    }

    /// <summary>
    /// Older installers start ClipShot again after replacing the exe. Close that launch.
    /// A matching last-install stamp means this start was requested, so leave it open.
    /// </summary>
    public static bool ShouldCloseUpdateRelaunch()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                return false;

            var written = File.GetLastWriteTimeUtc(executable);
            var age = DateTime.UtcNow - written;
            if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(2))
                return false;

            var settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipShot");
            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "ClipShot");
            var installedCopy = string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(executable) ?? ""),
                Path.GetFullPath(installDirectory),
                StringComparison.OrdinalIgnoreCase);
            var hasSettings = File.Exists(Path.Combine(settingsDirectory, "settings.json"));
            if (!installedCopy && !hasSettings)
                return false;

            Directory.CreateDirectory(settingsDirectory);
            var markerPath = Path.Combine(settingsDirectory, "last-install");
            var stamp = written.Ticks.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(markerPath) &&
                long.TryParse(File.ReadAllText(markerPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var markedTicks))
            {
                var marked = new DateTime(markedTicks, DateTimeKind.Utc);
                if ((marked - written).Duration() <= TimeSpan.FromSeconds(2))
                    return false;
            }

            File.WriteAllText(markerPath, stamp);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
