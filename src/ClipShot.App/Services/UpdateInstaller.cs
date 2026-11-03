using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ClipShot.Services;

internal static class UpdateInstaller
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static async Task DownloadAndRestartAsync(Uri downloadUri)
    {
        if (downloadUri.Scheme != Uri.UriSchemeHttps || downloadUri.Host != "github.com" ||
            !downloadUri.AbsolutePath.StartsWith("/niimbu/clipshot/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update URL is not an official ClipShot release asset.");

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !string.Equals(Path.GetFileName(executable), "ClipShot.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current application path could not be determined.");

        var staged = Path.Combine(Path.GetDirectoryName(executable)!, ".ClipShot.update.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), "ClipShot-update-" + Guid.NewGuid().ToString("N") + ".ps1");
        using var response = await Client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 100000000) throw new InvalidDataException("The update package is unexpectedly large.");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length < 2 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') throw new InvalidDataException("The downloaded update is not a valid Windows executable.");
        await File.WriteAllBytesAsync(staged, bytes);

        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference = 'Stop'\n" +
            "$processId = " + Environment.ProcessId + "\n" +
            "while (Get-Process -Id $processId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }\n" +
            "Move-Item -LiteralPath " + Quote(staged) + " -Destination " + Quote(executable) + " -Force\n" +
            "Start-Process -FilePath " + Quote(executable) + "\n" +
            "Remove-Item -LiteralPath $PSCommandPath -Force\n";
        await File.WriteAllTextAsync(scriptPath, script, new System.Text.UTF8Encoding(false));
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
}