using System.Net.Http;
using System.Text.Json;

namespace ClipShot.Services;

internal sealed record LatestRelease(Version Version, Uri Url);

internal static class UpdateChecker
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task<LatestRelease?> GetLatestReleaseAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/niimbu/clipshot/releases/latest");
        request.Headers.UserAgent.ParseAdd("ClipShot-UpdateChecker");
        using var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        var url = root.GetProperty("html_url").GetString();

        if (!Version.TryParse(tag, out var version) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var releaseUri) ||
            releaseUri.Host != "github.com")
            return null;

        return new LatestRelease(version, releaseUri);
    }
}