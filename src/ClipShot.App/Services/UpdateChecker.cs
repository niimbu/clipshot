using System.Net.Http;
using System.Text.Json;

namespace ClipShot.Services;

internal sealed record LatestRelease(Version Version, Uri Url, Uri ExecutableUrl);

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
        if (!Version.TryParse(tag, out var version) || !Uri.TryCreate(page, UriKind.Absolute, out var pageUri) || pageUri.Host != "github.com") return null;
        if (!root.TryGetProperty("assets", out var assets)) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), "ClipShot.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var download = asset.GetProperty("browser_download_url").GetString();
            if (Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) &&
                downloadUri.Scheme == Uri.UriSchemeHttps && downloadUri.Host == "github.com" &&
                downloadUri.AbsolutePath.StartsWith("/niimbu/clipshot/releases/download/", StringComparison.OrdinalIgnoreCase))
                return new LatestRelease(version, pageUri, downloadUri);
        }
        return null;
    }
}