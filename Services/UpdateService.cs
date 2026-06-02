#if !STORE_BUILD
using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Shelf.Services;

// Result of a GitHub "latest release" check. LatestVersion is a display string
// ("1.2.0"); ReleaseUrl points at the Releases page to open in a browser.
public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string LatestVersion { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
}

// Lightweight update checker for PORTABLE builds only (compiled out of STORE_BUILD,
// where Microsoft Store handles updates and directing users elsewhere is discouraged).
// Pure utility - it does NOT touch AppSettings; the daily throttle and persistence
// live in App.xaml.cs so this stays free of an App reference.
public static class UpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/bridges-net-ua/shelf/releases/latest";
    private const string ReleasesPage =
        "https://github.com/bridges-net-ua/shelf/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's REST API rejects requests without a User-Agent (returns 403).
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Shelf-UpdateCheck");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    // Queries GitHub for the latest published release. Returns null on any failure
    // (no network, 404 when there are no releases, malformed JSON, timeout).
    public static async Task<UpdateCheckResult?> CheckAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ApiUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl)) return null;

            var latest = ParseTag(tagEl.GetString());
            if (latest == null) return null;

            return new UpdateCheckResult
            {
                UpdateAvailable = Normalize(latest) > Normalize(Current),
                LatestVersion = ToDisplay(latest),
                ReleaseUrl = ReleasesPage
            };
        }
        catch
        {
            return null;
        }
    }

    // True when `latestVersionString` (e.g. "1.2.0", stored from a prior check) is a
    // newer release than the running build. Used by the About badge without a network call.
    public static bool IsNewer(string? latestVersionString, Version current)
    {
        var latest = ParseTag(latestVersionString);
        return latest != null && Normalize(latest) > Normalize(current);
    }

    // Tags are "vMAJOR.MINOR.PATCH"; strip an optional leading "v". Returns null if unparseable.
    private static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        return Version.TryParse(s, out var v) ? v : null;
    }

    // Compare only Major.Minor.Build - our release tags use three components, and
    // Version treats an unspecified revision as -1 which would otherwise make
    // "1.1.1" compare LESS than the assembly's "1.1.1.0".
    private static Version Normalize(Version v) =>
        new Version(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);

    private static string ToDisplay(Version v) =>
        v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
}
#endif
