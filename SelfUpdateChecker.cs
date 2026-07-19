using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SharpEmuUpdater;

public sealed class SelfUpdateInfo
{
    public required string Version { get; init; }
    public required string ReleaseUrl { get; init; }
    public required string ReleaseNotes { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset? CurrentVersionPublishedAt { get; init; }
    public required string DownloadUrl { get; init; }
    public required long DownloadSizeBytes { get; init; }
}

/// <summary>
/// Checks llnternet/SharpEmuUpdater's own GitHub Releases for a version newer than
/// AppVersion.Current -- the same self-update pattern SharpEmuMobile already uses against
/// llnternet/SharpEmuTracker. Unauthenticated (a separate, non-token HttpClient): this must work
/// even for a user who hasn't set up a GitHub token yet, and it should never spend any of the
/// token's own rate-limit budget, since that's a completely separate concern from the
/// sharpemu/sharpemu build tracking this app otherwise does.
/// </summary>
public static class SelfUpdateChecker
{
    private const string RepoOwner = "llnternet";
    private const string RepoName = "SharpEmuUpdater";

    // SocketsHttpHandler with a bounded PooledConnectionLifetime, not a plain HttpClientHandler --
    // see GitHubUpdaterService/RateLimitTrackingHandler's own comment on the exact same fix, after
    // a real "No such host is known" error surfaced right after this machine woke from sleep.
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        BaseAddress = new Uri("https://api.github.com/"),
        Timeout = TimeSpan.FromSeconds(15),
    };

    static SelfUpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SharpEmuUpdater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Null if already up to date, the release has no zip asset, or the check itself
    /// failed for any reason (network hiccup, repo has no releases yet, ...) -- a failed check
    /// must never surface as an error, it just means "no update banner this time."</summary>
    public static async Task<SelfUpdateInfo?> CheckForUpdateAsync(CancellationToken ct)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubReleaseDto>(
                $"repos/{RepoOwner}/{RepoName}/releases/latest", ct);
            if (release == null) return null;

            if (!TryParseVersion(release.TagName, out var latest)) return null;
            if (!TryParseVersion(AppVersion.Current, out var current)) return null;
            if (latest <= current) return null;

            var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl)) return null;

            // Purely cosmetic (shown next to "Current Version" in the update dialog) -- a failed
            // lookup here (e.g. the current version's own release was deleted, or this build
            // predates the self-update feature entirely) just means that date is omitted, not a
            // reason to abandon reporting the real, already-confirmed newer version.
            DateTimeOffset? currentPublishedAt = null;
            try
            {
                var currentRelease = await _http.GetFromJsonAsync<GitHubReleaseDto>(
                    $"repos/{RepoOwner}/{RepoName}/releases/tags/v{AppVersion.Current}", ct);
                currentPublishedAt = currentRelease?.PublishedAt;
            }
            catch
            {
                // See above -- not fatal.
            }

            return new SelfUpdateInfo
            {
                Version = release.TagName,
                ReleaseUrl = release.HtmlUrl,
                ReleaseNotes = release.Body,
                PublishedAt = release.PublishedAt,
                CurrentVersionPublishedAt = currentPublishedAt,
                DownloadUrl = asset.BrowserDownloadUrl,
                DownloadSizeBytes = asset.Size,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tag names are published as "v1.1"; AppVersion.Current is "1.1" (no "v") -- strips
    /// a leading 'v'/'V' before parsing either one the same way.</summary>
    private static bool TryParseVersion(string raw, out Version version)
    {
        string trimmed = raw.StartsWith('v') || raw.StartsWith('V') ? raw[1..] : raw;
        return Version.TryParse(trimmed, out version!);
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("published_at")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetDto> Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
