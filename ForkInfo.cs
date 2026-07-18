using System.Text.Json.Serialization;

namespace SharpEmuUpdater;

/// <summary>
/// One developer actively building against the upstream repo -- derived from that repo's own
/// Actions run history (see GitHubUpdaterService.GetActiveContributorsAsync), not from GitHub's
/// /forks endpoint. Branch is the specific branch their most recent "Build and Release" run
/// actually built (e.g. "main" for direct pushes to sharpemu/sharpemu itself, or a PR branch
/// name for anyone else).
/// </summary>
public sealed class ForkInfo
{
    public required string OwnerLogin { get; init; }
    public required string RepoFullName { get; init; }
    public required string HtmlUrl { get; init; }
    public required string Branch { get; init; }
    public DateTimeOffset PushedAt { get; init; }

    // Pull request this branch's most recent build was associated with, if any -- null for a
    // direct push not tied to an open PR (e.g. par274/sharpemu's own main branch).
    public int? PullRequestNumber { get; init; }

    public string Owner => RepoFullName.Split('/')[0];
}

public sealed class GitHubOwnerDto
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
}

/// <summary>Minimal shape used only to read a repo's current canonical name back off a
/// (possibly redirected) response -- see GitHubUpdaterService.ResolveRepoAsync.</summary>
public sealed class RepoIdentityDto
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";
}

/// <summary>One entry from GitHub's own /forks listing -- used to discover forks that have never
/// shown up in upstream's Actions history at all (no PR ever opened), see
/// GitHubUpdaterService.FetchMostRecentlyPushedForksAsync.</summary>
public sealed class RepoForkDto
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("pushed_at")]
    public DateTimeOffset PushedAt { get; set; }

    [JsonPropertyName("owner")]
    public GitHubOwnerDto Owner { get; set; } = new();
}
