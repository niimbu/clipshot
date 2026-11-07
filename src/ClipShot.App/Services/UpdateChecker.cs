using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipShot.Services;

public sealed record LatestRelease(
    Version Version,
    Uri Url,
    Uri ExecutableUrl,
    string Title,
    string Notes);

internal static class UpdateChecker
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<LatestRelease?> GetLatestReleaseAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/niimbu/clipshot/releases/latest");
        request.Headers.UserAgent.ParseAdd("ClipShot-UpdateChecker");
        using var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        var page = root.GetProperty("html_url").GetString();
        if (!Version.TryParse(tag, out var version) || !Uri.TryCreate(page, UriKind.Absolute, out var pageUri) || pageUri.Host != "github.com")
            return null;
        if (!root.TryGetProperty("assets", out var assets))
            return null;

        Uri? downloadUri = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), "ClipShot.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            var download = asset.GetProperty("browser_download_url").GetString();
            if (Uri.TryCreate(download, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" &&
                uri.AbsolutePath.StartsWith("/niimbu/clipshot/releases/download/", StringComparison.OrdinalIgnoreCase))
            {
                downloadUri = uri;
                break;
            }
        }

        if (downloadUri is null)
            return null;

        var name = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var body = root.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString()
            : null;

        var title = string.IsNullOrWhiteSpace(name)
            ? $"ClipShot {version.ToString(3)}"
            : name.Trim();
        var notes = FormatReleaseNotes(body);

        return new LatestRelease(version, pageUri, downloadUri, title, notes);
    }

    internal static string FormatReleaseNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "Bug fixes and improvements.";

        var text = markdown.Replace("\r\n", "\n").Trim();
        // Drop the auto-generated "Full Changelog" footer — the in-app notes are enough.
        text = Regex.Replace(text, @"\n*\*\*Full Changelog\*\*:.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"^#+\s*", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = Regex.Replace(text, @"__(.+?)__", "$1");
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "$1");
        text = Regex.Replace(text, @"^[-*]\s+", "• ", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        var cleaned = text.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Bug fixes and improvements." : cleaned;
    }
}
