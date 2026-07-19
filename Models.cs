using System.Text.Json.Serialization;

namespace SharpEmuUpdater;

public sealed class WorkflowRunsResponse
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("workflow_runs")]
    public List<WorkflowRun> WorkflowRuns { get; set; } = new();
}

public sealed class WorkflowRun
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    // The "Build and Release #N" number shown on GitHub's own Actions tab -- sequential per
    // workflow, unlike Id above (a large, globally-unique-across-all-of-GitHub identifier used
    // for API calls/upgrade-vs-downgrade ordering). This is purely for display.
    [JsonPropertyName("run_number")]
    public int RunNumber { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("display_title")]
    public string DisplayTitle { get; set; } = "";

    // Populated when this run is tied to an open pull request against the run's own repo (e.g. a
    // contributor's own "push" runs on their fork are never associated with one, even if that
    // branch also has an open PR upstream -- GitHub only links runs to PRs within the same repo).
    [JsonPropertyName("pull_requests")]
    public List<PullRequestRef> PullRequests { get; set; } = new();

    [JsonPropertyName("head_branch")]
    public string HeadBranch { get; set; } = "";

    [JsonPropertyName("head_sha")]
    public string HeadSha { get; set; } = "";

    // "push", "pull_request", "workflow_dispatch", etc. Matters because a pull_request-triggered
    // run checks out GitHub's synthetic PR merge-preview commit by default, not the branch's own
    // head commit -- its packaged artifact ends up versioned by a different sha than head_sha
    // reports. See GetRecentClassifiedRunsAsync, which prefers "push" runs for exactly this reason.
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    // Null once a contributor deletes their fork after a PR is merged/closed -- runs with a null
    // head_repository are filtered out when scanning for active contributors.
    [JsonPropertyName("head_repository")]
    public WorkflowRunRepository? HeadRepository { get; set; }

    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    // GitHub's last-modification timestamp -- for a completed run this is effectively when it
    // finished, so (UpdatedAt - CreatedAt) is used as a build-duration estimate. Not exact (it's
    // "last touched", not a dedicated "completed_at" field, and CreatedAt is queue time rather
    // than actual start time), but close enough to be useful and needs no extra API call.
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    // The three properties below are never populated from the Actions API (no JsonPropertyName)
    // -- they exist purely so a synthetic, Releases-backed "run" (see
    // GitHubUpdaterService.GetRecentReleaseBuildsAsync) can be built out of this same type and
    // flow through all the existing rendering/install/changelog code unchanged, instead of a
    // second parallel model + UI path.

    // Set to the release's tag name for a Releases-backed row -- checked first by ShortSha below.
    // HeadSha itself is ALSO set to the full tag name for these rows (not just this override), so
    // GetCommitsBetweenAsync's compare-API call still gets a real, resolvable git ref.
    public string? ShortShaOverride { get; set; }

    // Set to the release's tag name (e.g. "v1.4.2") for a Releases-backed row -- checked first by
    // DisplayNumber below, since a release has no Actions run number to show instead.
    public string? DisplayNumberOverride { get; set; }

    // Null for every Actions-sourced run. Set to the matched Windows asset's browser_download_url
    // for a Releases-backed row -- doubles as the "this row came from Releases, not Actions" signal
    // MainForm.ApplyUpdateAsync branches on, so no separate bool flag is needed.
    public string? WindowsAssetDownloadUrl { get; set; }

    public string ShortSha => ShortShaOverride ?? (HeadSha.Length >= 7 ? HeadSha[..7] : HeadSha);

    /// <summary>e.g. "#468" or "#468 (PR #209)" -- for display only.</summary>
    public string DisplayNumber => DisplayNumberOverride ?? (PullRequests.Count > 0
        ? $"#{RunNumber} (PR #{PullRequests[0].Number})"
        : $"#{RunNumber}");
}

public sealed class PullRequestRef
{
    [JsonPropertyName("number")]
    public int Number { get; set; }
}

public sealed class WorkflowRunRepository
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("owner")]
    public GitHubOwnerDto Owner { get; set; } = new();
}

public enum BuildOutcome
{
    Success,

    /// <summary>A genuinely failed run (conclusion "failure" or "timed_out") that immediately
    /// follows a successful one -- see Cancelled/Skipped/Pending for completed-but-not-a-real-
    /// verdict conclusions, which are excluded from this classification entirely.</summary>
    Regression,

    /// <summary>A genuinely failed run that follows another failure -- still broken, not newly broken.</summary>
    Failed,

    /// <summary>Not completed yet, actively running (GitHub status "in_progress"). Never
    /// selectable to install -- there's no artifact yet.</summary>
    InProgress,

    /// <summary>Not completed yet, waiting to start (GitHub status "queued"/"waiting"/
    /// "requested"/"pending"). Never selectable to install.</summary>
    Queued,

    /// <summary>Technically "completed" per GitHub, but conclusion is "action_required" -- the
    /// workflow never actually ran, it's stuck waiting on a maintainer to manually approve it
    /// (common for a first-time contributor's PR). Doesn't participate in the Regression/Failed
    /// history walk (it says nothing about whether the code itself works) and is never
    /// selectable to install.</summary>
    Pending,

    /// <summary>Conclusion "cancelled" -- manually stopped or superseded by a newer run, not a
    /// verdict on the code. Excluded from the Regression/Failed walk for the same reason as
    /// Pending. Never selectable to install.</summary>
    Cancelled,

    /// <summary>Conclusion "skipped" -- never ran at all (e.g. a path filter or an "if:"
    /// condition on the job), also not a verdict on the code. Excluded from the Regression/Failed
    /// walk for the same reason as Pending. Never selectable to install.</summary>
    Skipped,
}

/// <summary>Which platforms a run actually produced an artifact for -- the build workflow builds
/// Windows, Linux, and macOS together in one run, so any combination of these can be set at once.
/// Detected by artifact-name keyword, same approach as GitHubUpdaterService.FindWindowsArtifactAsync
/// already uses for Windows specifically.</summary>
[Flags]
public enum BuildPlatforms
{
    None = 0,
    Windows = 1,
    MacOS = 2,
    Linux = 4,
}

public sealed class ClassifiedRun
{
    public required WorkflowRun Run { get; init; }
    public required BuildOutcome Outcome { get; init; }

    // Populated for Success runs only, as a side effect of the Windows-artifact-presence check
    // GetRecentClassifiedRunsAsync already does for every Success run (see
    // GitHubUpdaterService.FindWindowsArtifactAsync) -- no extra API calls beyond what already
    // happens. Null for every other outcome, and for a Success run whose artifact lookup failed.
    public long? WindowsArtifactSizeBytes { get; set; }

    // Populated for every completed/informative outcome (Success/Regression/Failed/Cancelled/
    // Skipped) -- not just the ones with a Windows artifact, so a Windows-only failure that still
    // produced Linux/macOS artifacts (or vice versa) shows accurately. None for InProgress/Queued/
    // Pending, which haven't produced anything yet.
    public BuildPlatforms AvailablePlatforms { get; set; }
}

public sealed class ArtifactsResponse
{
    [JsonPropertyName("artifacts")]
    public List<Artifact> Artifacts { get; set; } = new();
}

/// <summary>A single published GitHub Release -- see GitHubUpdaterService.GetRecentReleaseBuildsAsync,
/// which maps these into synthetic WorkflowRun/ClassifiedRun rows for the upstream-main "Recent
/// Builds" list. A separate type from SelfUpdateChecker's own private release DTO (that one is for
/// this app's own releases; this one is for SharpEmu's).</summary>
public sealed class GitHubReleaseDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // The release's own notes -- used as the row's DisplayTitle (see
    // GitHubUpdaterService.GetRecentReleaseBuildsAsync), the same role a commit message plays for
    // an Actions-based row, rather than just repeating the tag/name a second time.
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    // A token with push access can see unpublished drafts via this same endpoint -- those must
    // never show up as if they were real recent builds.
    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto> Assets { get; set; } = new();
}

public sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>Shape of GitHub's "compare two commits" API response -- only the bits this app
/// actually reads (see GitHubUpdaterService.GetCommitsBetweenAsync).</summary>
public sealed class CompareResponse
{
    [JsonPropertyName("commits")]
    public List<CompareCommit> Commits { get; set; } = new();
}

public sealed class CompareCommit
{
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = "";

    [JsonPropertyName("commit")]
    public CompareCommitDetail Commit { get; set; } = new();
}

public sealed class CompareCommitDetail
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class Artifact
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size_in_bytes")]
    public long SizeInBytes { get; set; }

    [JsonPropertyName("expired")]
    public bool Expired { get; set; }
}
