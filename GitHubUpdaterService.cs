using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SharpEmuUpdater;

public sealed class GitHubUpdaterService : IDisposable
{
    // The canonical upstream repo -- forks are always listed relative to this, regardless of
    // which fork (including the upstream itself) is currently selected for builds.
    public const string UpstreamOwner = "sharpemu";
    public const string UpstreamRepo = "sharpemu";

    private const string BuildWorkflowName = "Build and Release";
    private const string GuiSettingsFileName = "gui-settings.json";

    // The build workflow produces one artifact per platform and has already renamed them once
    // (sharpemu-win64-<sha> -> sharpemu-win-x64-<sha> when cross-platform support landed) --
    // rather than hardcode whatever the exact current name happens to be, FindWindowsArtifactAsync
    // below picks the Windows one out of a run's artifacts by keyword, so a future rename doesn't
    // need another one-line fix here and applies automatically to every fork/branch. This list is
    // deliberately generous (covers platforms SharpEmu doesn't currently target too, e.g.
    // freebsd/wasm) so a future platform addition is less likely to slip through as an
    // unrecognized, ambiguous candidate -- see FindWindowsArtifactAsync's own diagnostic warning
    // for what happens when a candidate genuinely can't be resolved either way.
    private static readonly string[] OtherPlatformKeywords =
        { "linux", "osx", "mac", "darwin", "android", "ios", "freebsd", "wasm", "steamdeck" };

    // "cancelled"/"skipped"/"action_required" (see GetRecentClassifiedRunsAsync's
    // nonVerdictOutcomes) never actually tell you anything about whether the code works -- when
    // picking which of a commit's sibling runs (push vs pull_request) is canonical, one of these
    // must never outrank an actual success/failure/timed_out verdict on the *other* sibling, or a
    // genuinely successful build can end up hidden behind an uninformative "cancelled" on its
    // push counterpart. See the GroupBy ordering in GetRecentClassifiedRunsAsync below.
    private static bool IsInformativeConclusion(string? conclusion) =>
        conclusion is "success" or "failure" or "timed_out" or "neutral";

    // A 404 from a repo-scoped GitHub API call almost always just means that repo/fork has since
    // been deleted, renamed to something no longer reachable, or made private -- routine churn
    // for a fork that showed up in an earlier "recently pushed" scan, not something the user did
    // anything wrong to cause or can do anything about. Logging it as if it were a problem (next
    // to genuinely actionable failures like a network hiccup or a rate limit) just trains the
    // user to ignore the Activity Log. Anything else (network failure, secondary rate limit,
    // unexpected shape) still gets logged -- those ARE worth knowing about.
    public static bool IsNotFound(Exception ex) =>
        ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound };

    /// <summary>
    /// A bare 401/403 from GetFromJsonAsync surfaces as "Response status code does not indicate
    /// success: 403 (Forbidden)" with nothing about WHY -- unhelpful either way, but especially so
    /// for a fine-grained token, whose access is scoped per-repository and per-permission rather
    /// than the single all-or-nothing 'repo' scope classic tokens use, so "wrong scope" isn't the
    /// only explanation anymore (the repo might just not be in the token's selected-repositories
    /// list at all). Rethrown as something that actually says what to check; anything that isn't a
    /// 401/403 passes through unchanged since this has nothing useful to add for those.
    /// </summary>
    private static Exception EnrichAuthError(HttpRequestException ex, string action)
    {
        if (ex.StatusCode is not (System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized))
            return ex;
        return new InvalidOperationException(
            $"GitHub rejected {action} ({(int)ex.StatusCode.Value} {ex.StatusCode}). " +
            "A classic Personal Access Token needs the 'repo' scope. A fine-grained token needs " +
            "'Actions: Read-only' and 'Contents: Read-only' permissions, AND this specific repository " +
            "must be included in that token's repository access list (fine-grained tokens don't see " +
            "every repo by default the way a classic token with 'repo' scope does). Update token.txt " +
            "with a token that has that access and click 'Reload Token from File'.", ex);
    }

    // Shared cap for "how many of these can be in flight at once" wherever this service fires a
    // batch of same-shape requests in parallel (GetRecentClassifiedRunsAsync's per-Success-run
    // artifact lookup, GetActiveContributorsAsync's per-fork scan) -- unbounded parallelism risks
    // tripping GitHub's secondary/abuse rate limiting and, worse, connection-pool contention that
    // makes the whole batch slower than a bounded one, not faster; fully sequential takes tens of
    // seconds since every request is its own round-trip. This is the middle ground.
    private const int MaxConcurrentApiCalls = 8;

    private readonly HttpClient _http;
    private readonly RateLimitTrackingHandler _rateLimitHandler = new();

    // A second, deliberately unauthenticated client for downloading a GitHub Release asset's
    // browser_download_url -- that URL is public, non-expiring, and commonly resolves to a
    // different host (objects.githubusercontent.com) on its very first hop, not just after a
    // redirect. _http carries an Authorization: Bearer {token} default header on every request it
    // sends; reusing it here would leak the user's GitHub token to that third-party host for no
    // reason, since release assets need no token at all.
    private readonly HttpClient _downloadHttp = new();

    public string Owner { get; }
    public string Repo { get; }

    // Null until the first API response comes back. GitHub's core REST rate limit is 5000
    // requests/hour for an authenticated token -- exposed here (rather than hardcoded 5000 in the
    // UI) so a fine-grained/lower-tier token's actual limit is reflected accurately if it ever
    // differs.
    public int? RateLimitRemaining => _rateLimitHandler.Remaining;
    public int? RateLimitLimit => _rateLimitHandler.Limit;

    // GitHub's secondary/abuse rate limit -- a separate mechanism from the primary hourly quota
    // above, tripped by bursty request patterns well before Remaining would ever reach zero.
    public bool IsSecondaryRateLimited => _rateLimitHandler.IsSecondaryRateLimited;
    public int? RateLimitRetryAfterSeconds => _rateLimitHandler.RetryAfterSeconds;

    public event Action? RateLimitUpdated
    {
        add => _rateLimitHandler.Updated += value;
        remove => _rateLimitHandler.Updated -= value;
    }

    /// <summary>True only for the canonical upstream repo on its "main" branch -- the project's
    /// own tagged-release line, not a contributor's in-progress branch. Used to switch "Recent
    /// Builds" (and the ACTIVE FORK/SOURCE REPOSITORY label) from Actions CI run history over to
    /// this repo's actual published GitHub Releases. Plain string params (not an instance method)
    /// so BuildPickerForm -- which only has Owner/Repo/branch on hand, no AppState -- can call it
    /// the same way MainForm does.</summary>
    public static bool IsUpstreamMainBranch(string owner, string repo, string branch) =>
        string.Equals(owner, UpstreamOwner, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(repo, UpstreamRepo, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(branch, "main", StringComparison.OrdinalIgnoreCase);

    public GitHubUpdaterService(string token, string owner = UpstreamOwner, string repo = UpstreamRepo)
    {
        Owner = owner;
        Repo = repo;

        _http = new HttpClient(_rateLimitHandler)
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromMinutes(5),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SharpEmuUpdater", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Follows GitHub's own repo-rename/transfer redirect to find the CURRENT owner/repo for
    /// whatever owner/repo is passed in -- e.g. par274/sharpemu became sharpemu/sharpemu once
    /// already. GitHub 301s requests for a renamed/transferred repo's old path, and HttpClient
    /// follows redirects by default, so the response's own full_name reflects the new identity
    /// with no extra work; reading it back here means a future rename is picked up automatically
    /// the next time this runs, with no code change needed. Falls back to the input pair,
    /// unchanged, on any failure (network hiccup, repo deleted, etc.) so a lookup problem never
    /// breaks something that was already working.
    /// </summary>
    public async Task<(string Owner, string Repo)> ResolveRepoAsync(string owner, string repo, CancellationToken ct)
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<RepoIdentityDto>($"repos/{owner}/{repo}", ct);
            var parts = dto?.FullName.Split('/', 2);
            if (parts is { Length: 2 })
            {
                if (!string.Equals(parts[0], owner, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(parts[1], repo, StringComparison.OrdinalIgnoreCase))
                    Logger.Log($"{owner}/{repo} is now {parts[0]}/{parts[1]} on GitHub -- switching to that automatically.");
                return (parts[0], parts[1]);
            }
        }
        catch (Exception ex)
        {
            if (!IsNotFound(ex))
                Logger.Log($"Could not resolve current identity for {owner}/{repo}, using it as-is: {ex.Message}");
        }
        return (owner, repo);
    }

    /// <summary>
    /// Finds developers currently active on sharpemu -- one entry per contributor *per branch*,
    /// so someone with several branches in flight shows up once for each. Two-stage scan: first
    /// the canonical upstream repo's own Actions history (resolved fresh via ResolveRepoAsync,
    /// regardless of whichever fork is currently tracked) surfaces who's actively opening PRs or
    /// pushing directly to it; then each of those contributors' own fork gets scanned too, since a
    /// lot of their branches (e.g. xnetcat's dozen-plus build branches on xnetcat/sharpemu) are
    /// only ever pushed to their own fork and never show up in upstream's Actions history at all.
    /// Each contributor's own fork is *also* resolved fresh before that second-stage scan -- not
    /// just upstream -- so a contributor renaming their own account/fork is picked up the same
    /// way a rename of the main developer's repo is. Deduplicated by owner+branch, keeping the
    /// most recent run. Fetched fresh every call -- no local caching, so the list stays current.
    /// </summary>
    public async Task<List<ForkInfo>> GetActiveContributorsAsync(CancellationToken ct)
    {
        var (upstreamOwner, upstreamRepo) = await ResolveRepoAsync(UpstreamOwner, UpstreamRepo, ct);
        return await GetActiveContributorsAsync(upstreamOwner, upstreamRepo, ct);
    }

    /// <summary>Same as the parameterless overload, but for a caller that already resolved the
    /// upstream owner/repo itself (e.g. to also show it in a UI label) and doesn't want to pay
    /// for a second identical resolve call.</summary>
    // Most forks of a popular repo never open a PR, so they never show up in upstream's own
    // Actions history at all -- the fork list itself is the only way to find them. Scanning every
    // fork (frequently 100+) for its own Actions history on every refresh isn't worth the request
    // budget, especially since most of them never actually ran the build workflow; the most
    // recently pushed-to ones are the best proxy for "someone's actively building here right now."
    private const int MaxForksToScan = 40;

    public async Task<List<ForkInfo>> GetActiveContributorsAsync(string upstreamOwner, string upstreamRepo, CancellationToken ct)
    {
        var upstreamRuns = await FetchBuildRunsAsync(upstreamOwner, upstreamRepo, ct);
        var all = ToForkInfos(upstreamRuns);

        // Repos to scan for their own Actions history, keyed by owner login so a contributor who
        // already showed up via upstream's own run history (accurate, already-known repo name)
        // isn't scanned a second time under a name pulled from the /forks listing instead.
        var reposByOwner = all
            .Where(f => !string.Equals(f.RepoFullName, $"{upstreamOwner}/{upstreamRepo}", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.OwnerLogin, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RepoFullName, StringComparer.OrdinalIgnoreCase);

        // Fills in everyone else: forks that never opened a PR against upstream, and so were
        // never discoverable from upstream's own Actions history above at all.
        try
        {
            var recentForks = await FetchMostRecentlyPushedForksAsync(upstreamOwner, upstreamRepo, MaxForksToScan, ct);
            foreach (var fork in recentForks)
                reposByOwner.TryAdd(fork.Owner.Login, fork.FullName);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not list forks of {upstreamOwner}/{upstreamRepo}: {ex.Message}");
        }

        // Each contributor is resolved fresh before being scanned, same as upstream, so a
        // contributor renaming their own fork/account shows up under the new name too -- not
        // just the main developer's repo. Branch renames need no such step: the branch shown for
        // each row is always whatever head_branch that specific live Actions run actually has, so
        // a renamed branch just naturally appears as its own (new) row from live data, with no
        // caching anywhere in this path to go stale in the first place.
        using var throttle = new SemaphoreSlim(MaxConcurrentApiCalls);
        var scanTasks = reposByOwner.Values.Select(async repoFullName =>
        {
            var parts = repoFullName.Split('/', 2);
            if (parts.Length != 2) return new List<ForkInfo>();

            await throttle.WaitAsync(ct);
            try
            {
                var (contribOwner, contribRepo) = await ResolveRepoAsync(parts[0], parts[1], ct);
                var ownRuns = await FetchBuildRunsAsync(contribOwner, contribRepo, ct);
                return ToForkInfos(ownRuns);
            }
            catch (Exception ex)
            {
                if (!IsNotFound(ex))
                    Logger.Log($"Could not scan {repoFullName}'s own Actions history for more active branches: {ex.Message}");
                return new List<ForkInfo>();
            }
            finally
            {
                throttle.Release();
            }
        });

        foreach (var ownForks in await Task.WhenAll(scanTasks))
            all.AddRange(ownForks);

        return all
            .GroupBy(f => $"{f.OwnerLogin}/{f.Branch}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.PushedAt).First())
            .ToList();
    }

    /// <summary>
    /// The `take` most recently pushed-to forks of upstreamOwner/upstreamRepo, per GitHub's own
    /// /forks listing (up to 300 forks fetched across 3 pages before ranking, so a repo with many
    /// forks still surfaces its truly most-active ones rather than whatever the first API page
    /// happens to contain).
    /// </summary>
    private async Task<List<RepoForkDto>> FetchMostRecentlyPushedForksAsync(string upstreamOwner, string upstreamRepo, int take, CancellationToken ct)
    {
        var forks = new List<RepoForkDto>();
        for (int page = 1; page <= 3; page++)
        {
            var pageForks = await _http.GetFromJsonAsync<List<RepoForkDto>>(
                $"repos/{upstreamOwner}/{upstreamRepo}/forks?per_page=100&page={page}", ct);
            if (pageForks == null || pageForks.Count == 0) break;
            forks.AddRange(pageForks);
            if (pageForks.Count < 100) break;
        }

        return forks
            .Where(f => !string.IsNullOrEmpty(f.Owner.Login) && !string.IsNullOrEmpty(f.FullName))
            .OrderByDescending(f => f.PushedAt)
            .Take(take)
            .ToList();
    }

    private async Task<List<WorkflowRun>> FetchBuildRunsAsync(string owner, string repo, CancellationToken ct)
    {
        var response = await _http.GetFromJsonAsync<WorkflowRunsResponse>(
            $"repos/{owner}/{repo}/actions/runs?per_page=100", ct);

        return (response?.WorkflowRuns ?? new())
            .Where(r => r.Name == BuildWorkflowName && r.HeadRepository != null && !string.IsNullOrEmpty(r.HeadRepository.Owner.Login))
            .ToList();
    }

    private static List<ForkInfo> ToForkInfos(IEnumerable<WorkflowRun> runs) =>
        runs.Select(r => new ForkInfo
        {
            OwnerLogin = r.HeadRepository!.Owner.Login,
            RepoFullName = r.HeadRepository!.FullName,
            HtmlUrl = r.HeadRepository!.HtmlUrl,
            Branch = r.HeadBranch,
            PushedAt = r.CreatedAt,
            PullRequestNumber = r.PullRequests.Count > 0 ? r.PullRequests[0].Number : null,
        }).ToList();

    /// <summary>
    /// Fetches the most recent "Build and Release" runs on the given branch. Completed ones are
    /// classified as Success, Regression (broke right after a success), or Failed (still broken);
    /// still-running ones show up too, as InProgress or Queued, always ahead of the completed
    /// history since they're by definition the newest activity. Newest first overall.
    ///
    /// A single push to an open PR's branch commonly fires the workflow twice -- once for the
    /// "push" event and once for the "pull_request" event -- both reporting the same head_sha,
    /// but the pull_request run checks out GitHub's synthetic merge-preview commit by default, so
    /// its artifact ends up versioned by a *different* sha than head_sha says. Only one sibling per
    /// commit is ever returned -- the other is dropped entirely, not shown in any form.
    ///
    /// Which sibling wins isn't simply "always push": a real success/failure verdict always
    /// outranks an uninformative "cancelled"/"skipped"/"action_required" conclusion on the other
    /// sibling first (see IsInformativeConclusion) -- otherwise a push that got cancelled (e.g.
    /// superseded by a later push before it finished) could hide a pull_request run that actually
    /// completed successfully for that exact commit. Only once both siblings are informative, or
    /// neither is, does "push" win the tie-break, for the artifact-naming reason above. This means
    /// a pull_request run can end up the one kept when its push sibling never produced a real
    /// verdict -- accepted as the lesser problem versus hiding a genuine success entirely.
    /// </summary>
    public async Task<List<ClassifiedRun>> GetRecentClassifiedRunsAsync(int count, string branch, CancellationToken ct)
    {
        // Fetches a much larger window than count and filters/trims client-side (see Take(count)
        // below), rather than asking the API for exactly `count` -- the branch filter alone
        // doesn't restrict to this workflow, so a noisy, unrelated workflow that also runs on
        // this branch (e.g. one that comments a build link on every push, sometimes multiple
        // times per commit) can otherwise crowd every "Build and Release" run entirely out of a
        // small recent window, leaving nothing left after the Name == BuildWorkflowName filter.
        WorkflowRunsResponse? response;
        try
        {
            response = await _http.GetFromJsonAsync<WorkflowRunsResponse>(
                $"repos/{Owner}/{Repo}/actions/runs?branch={Uri.EscapeDataString(branch)}&per_page=100", ct);
        }
        catch (HttpRequestException ex)
        {
            throw EnrichAuthError(ex, $"listing Actions runs for {Owner}/{Repo}");
        }

        var allRuns = (response?.WorkflowRuns ?? new())
            .Where(r => r.HeadBranch == branch && r.Name == BuildWorkflowName)
            .ToList();

        // A cross-repo PR (the common case: a contributor's own fork, branch pushed there, PR
        // opened against upstream) splits its two sibling events across two different repos --
        // the "push" run lives in the fork's own Actions history (already fetched above), but the
        // "pull_request" run actually executes, and is recorded, under the UPSTREAM repo instead,
        // since that's whose workflow file and base ref it ran against. Without this, tracking a
        // contributor's own fork could never find that sibling at all -- unlike a same-repo
        // branch (e.g. a maintainer's own branch directly in sharpemu/sharpemu), where both
        // events already land in the same repo's history. GitHub's branch filter matches
        // head_branch regardless of which repo actually owns that branch, so querying upstream by
        // this same branch name surfaces it; head_repository is checked afterward to make sure
        // what comes back is actually this fork's PR and not some other contributor's
        // same-named branch.
        if (!string.Equals(Owner, UpstreamOwner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Repo, UpstreamRepo, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var upstreamResponse = await _http.GetFromJsonAsync<WorkflowRunsResponse>(
                    $"repos/{UpstreamOwner}/{UpstreamRepo}/actions/runs?branch={Uri.EscapeDataString(branch)}&per_page=100", ct);
                var existingIds = new HashSet<long>(allRuns.Select(r => r.Id));
                allRuns.AddRange((upstreamResponse?.WorkflowRuns ?? new()).Where(r =>
                    r.HeadBranch == branch
                    && r.Name == BuildWorkflowName
                    && r.HeadRepository != null
                    && string.Equals(r.HeadRepository.FullName, $"{Owner}/{Repo}", StringComparison.OrdinalIgnoreCase)
                    && !existingIds.Contains(r.Id)));
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not check upstream for cross-repo sibling runs: {ex.Message}");
            }
        }

        var completedAll = allRuns.Where(r => r.Status == "completed").ToList();

        // Only "push" and "pull_request" runs ever need collapsing onto one canonical entry per
        // commit -- that pairing is the one place the same head_sha can legitimately mean "the
        // same code, reported under two different artifact shas" (see this method's own doc
        // comment). Any other event sharing a head_sha with one of those -- most commonly
        // workflow_dispatch, someone manually re-running the workflow against an existing commit
        // -- is its own distinct, independently-produced build with no such ambiguity, and must
        // keep its own entry rather than being silently discarded as if it were a duplicate.
        // Grouping by run Id instead of head_sha for those event types means each keeps its own
        // one-run group and always survives the Select(...).First() below untouched.
        var completedDeduped = completedAll
            .GroupBy(r => r.Event is "push" or "pull_request" ? $"sha:{r.HeadSha}" : $"run:{r.Id}")
            .Select(g => g
                .OrderByDescending(r => IsInformativeConclusion(r.Conclusion))
                .ThenByDescending(r => r.Event == "push")
                .ThenByDescending(r => r.CreatedAt)
                .First())
            .ToList();

        // Some completed conclusions aren't a real verdict on the code at all: "action_required"
        // (never ran, stuck waiting on a maintainer's manual approval -- common for a first-time
        // contributor's PR), "cancelled" (manually stopped, or superseded by a newer run), and
        // "skipped" (never ran, e.g. a path filter or an "if:" condition on the job). None of
        // these say anything about whether the code works, so all three are excluded from the
        // Regression/Failed walk below entirely and shown as their own distinct outcome instead.
        var nonVerdictOutcomes = new Dictionary<string, BuildOutcome>(StringComparer.OrdinalIgnoreCase)
        {
            ["action_required"] = BuildOutcome.Pending,
            ["cancelled"] = BuildOutcome.Cancelled,
            ["skipped"] = BuildOutcome.Skipped,
        };

        var runsOldestFirst = completedDeduped
            .Where(r => r.Conclusion == null || !nonVerdictOutcomes.ContainsKey(r.Conclusion))
            .OrderBy(r => r.CreatedAt)
            .ToList();

        var classified = new List<ClassifiedRun>(runsOldestFirst.Count);
        bool? prevWasSuccess = null;
        foreach (var run in runsOldestFirst)
        {
            bool isSuccess = run.Conclusion == "success";
            BuildOutcome outcome = isSuccess
                ? BuildOutcome.Success
                : (prevWasSuccess ?? true) ? BuildOutcome.Regression : BuildOutcome.Failed;

            classified.Add(new ClassifiedRun { Run = run, Outcome = outcome });
            prevWasSuccess = isSuccess;
        }
        classified.Reverse();

        classified.AddRange(completedDeduped
            .Where(r => r.Conclusion != null && nonVerdictOutcomes.ContainsKey(r.Conclusion))
            .Select(r => new ClassifiedRun { Run = r, Outcome = nonVerdictOutcomes[r.Conclusion!] }));
        classified = classified.OrderByDescending(c => c.Run.CreatedAt).ToList();

        // The workflow builds Windows, Linux, and macOS together in one run and can still come
        // back an overall "success" even if only the Windows leg actually failed (a matrix job
        // failing doesn't necessarily fail the whole run) -- this app only ever cares about
        // Windows, so a "Success" that has no Windows artifact to show for it isn't a real,
        // installable success from here.
        //
        // Cancelled/Skipped get the exact same check, for a related but distinct reason: a
        // GitHub Actions matrix build defaults to "fail-fast", so if one platform's job fails (or
        // the run is manually cancelled) while Windows's own job is still running, GitHub can
        // cancel the Windows leg specifically while Linux/macOS jobs that already finished keep
        // their artifacts -- so "Cancelled" at the run level doesn't reliably mean nothing at all
        // was produced, and conversely doesn't guarantee Windows specifically has anything either.
        // Confirmed live: a "Cancelled" run on sharpemu/sharpemu was still showing in Recent
        // Builds (and could even be picked as "newest", burying the actual latest real Windows
        // build behind it) despite having zero usable artifacts for this app to do anything with.
        // Regression/Failed/InProgress/Queued/Pending are deliberately left alone -- Failed and
        // Regression are real verdicts worth showing (a broken build) regardless of what partial
        // artifacts exist, and InProgress/Queued/Pending haven't produced anything to check yet.
        //
        // Throttled the same way GetActiveContributorsAsync throttles its own fork scans (see
        // MaxConcurrentApiCalls's comment) -- this used to be a plain unthrottled Task.WhenAll,
        // which was fine back when this only ever classified a handful of runs, but now that the
        // caller can ask for up to ~100, an active branch can have 30-50+ Success runs, and firing
        // that many simultaneous requests at once was measured taking ~4 seconds to complete
        // (connection-pool contention/GitHub's own secondary rate limiting), which is exactly the
        // "list takes a while to show up" lag that traced back to here.
        var runsNeedingWindowsCheck = classified
            .Where(c => c.Outcome is BuildOutcome.Success or BuildOutcome.Cancelled or BuildOutcome.Skipped)
            .ToList();
        if (runsNeedingWindowsCheck.Count > 0)
        {
            using var artifactThrottle = new SemaphoreSlim(MaxConcurrentApiCalls);

            // The artifact's own size comes along for free here -- same lookup already needed to
            // decide whether a Windows build exists at all, so ClassifiedRun.WindowsArtifactSizeBytes
            // (used to show download size in the build picker) costs nothing extra. LookupFailed
            // is tracked separately from "Artifact is null" -- a genuine "this run has no Windows
            // artifact" has to still exclude the run below, but a caught exception (a transient
            // lookup hiccup) must NOT, or a run that may well be fine gets hidden for no reason.
            var windowsChecks = await Task.WhenAll(runsNeedingWindowsCheck.Select(async c =>
            {
                await artifactThrottle.WaitAsync(ct);
                try { return (Run: c, Artifact: await FindWindowsArtifactAsync(c.Run.Id, ct), LookupFailed: false); }
                catch { return (Run: c, Artifact: (Artifact?)null, LookupFailed: true); }
                finally { artifactThrottle.Release(); }
            }));

            foreach (var check in windowsChecks)
                if (check.Artifact != null)
                    check.Run.WindowsArtifactSizeBytes = check.Artifact.SizeInBytes;

            var noWindowsBuild = new HashSet<ClassifiedRun>(windowsChecks
                .Where(x => x.Artifact == null && !x.LookupFailed)
                .Select(x => x.Run));
            if (noWindowsBuild.Count > 0)
                classified = classified.Where(c => !noWindowsBuild.Contains(c)).ToList();
        }

        // Platform labels (Windows/macOS/Linux) for every completed/informative run left in the
        // list -- not just the ones the Windows-artifact filter above checked, so a Regression/
        // Failed run still shows accurately which platforms it actually produced something for.
        // Reuses GetArtifactCandidatesAsync's per-run cache, so a run already checked above (every
        // Success/Cancelled/Skipped) costs no extra network call here.
        if (classified.Count > 0)
        {
            using var platformThrottle = new SemaphoreSlim(MaxConcurrentApiCalls);
            await Task.WhenAll(classified.Select(async c =>
            {
                await platformThrottle.WaitAsync(ct);
                try { c.AvailablePlatforms = await GetAvailablePlatformsAsync(c.Run.Id, ct); }
                catch { /* leave as BuildPlatforms.None -- a lookup hiccup just means no labels this time */ }
                finally { platformThrottle.Release(); }
            }));
        }

        // Not completed yet -- no conclusion to classify against the history above, so these are
        // never Success/Regression/Failed. Always newest-first and ahead of the completed list.
        // Same push/pull_request-only collapsing as completedDeduped above, for the same reason.
        var active = allRuns
            .Where(r => r.Status != "completed")
            .GroupBy(r => r.Event is "push" or "pull_request" ? $"sha:{r.HeadSha}" : $"run:{r.Id}")
            .Select(g => g
                .OrderByDescending(r => r.Event == "push")
                .ThenByDescending(r => r.CreatedAt)
                .First())
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ClassifiedRun
            {
                Run = r,
                Outcome = r.Status == "in_progress" ? BuildOutcome.InProgress : BuildOutcome.Queued,
            });

        // `count` distinct builds, one canonical entry each -- sibling runs of the same commit
        // (e.g. the "pull_request"-triggered twin of the canonical "push" run) are deliberately
        // not included here at all; only the canonical run for a commit is ever shown.
        return active.Concat(classified).Take(count).ToList();
    }

    /// <summary>
    /// The Recent Builds data source for the upstream repo's own "main" branch specifically (see
    /// GitHubUpdaterService.IsUpstreamMainBranch) -- published GitHub Releases instead of Actions
    /// CI run history, since sharpemu/sharpemu's main branch is the project's own tagged-release
    /// line, not a contributor's in-progress branch. Maps each release into a synthetic
    /// WorkflowRun/ClassifiedRun (see WorkflowRun's ShortShaOverride/DisplayNumberOverride/
    /// WindowsAssetDownloadUrl) so the entire existing rendering/context-menu/changelog/install
    /// pipeline keeps working unchanged. Outcome is always Success -- a published release can't
    /// "fail" the way a CI run can.
    /// </summary>
    public async Task<List<ClassifiedRun>> GetRecentReleaseBuildsAsync(int count, CancellationToken ct)
    {
        List<GitHubReleaseDto>? releases;
        try
        {
            releases = await _http.GetFromJsonAsync<List<GitHubReleaseDto>>(
                $"repos/{Owner}/{Repo}/releases?per_page={count}", ct);
        }
        catch (HttpRequestException ex)
        {
            throw EnrichAuthError(ex, $"listing releases for {Owner}/{Repo}");
        }

        var result = new List<ClassifiedRun>();
        foreach (var release in releases ?? new())
        {
            // A token with push access can see unpublished drafts via this same endpoint -- those
            // must never show up as if they were a real recent build.
            if (release.Draft) continue;

            GitHubReleaseAssetDto? windowsAsset = null;
            var platforms = BuildPlatforms.None;
            foreach (var asset in release.Assets)
            {
                if (OtherPlatformKeywords.Any(k => asset.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    if (asset.Name.Contains("osx", StringComparison.OrdinalIgnoreCase)
                        || asset.Name.Contains("mac", StringComparison.OrdinalIgnoreCase)
                        || asset.Name.Contains("darwin", StringComparison.OrdinalIgnoreCase))
                        platforms |= BuildPlatforms.MacOS;
                    else if (asset.Name.Contains("linux", StringComparison.OrdinalIgnoreCase))
                        platforms |= BuildPlatforms.Linux;
                    continue;
                }
                if (asset.Name.Contains("win", StringComparison.OrdinalIgnoreCase))
                {
                    platforms |= BuildPlatforms.Windows;
                    windowsAsset ??= asset;
                }
            }

            // No Windows asset on this release at all -- nothing installable, so it shouldn't
            // show up in the list (mirrors GetRecentClassifiedRunsAsync's own "no Windows
            // artifact" exclusion for the Actions path).
            if (windowsAsset == null) continue;

            var run = new WorkflowRun
            {
                Id = release.Id,
                HeadSha = release.TagName,
                ShortShaOverride = release.TagName,
                // Left blank (not the tag name again) -- DisplayNumber's whole purpose for an
                // Actions run is to show something ShortSha doesn't (a run/PR number); for a
                // release, ShortSha already IS the tag name, so repeating it here would just show
                // the same value twice in adjacent columns.
                DisplayNumberOverride = "",
                // The release's own notes (first line), the same role a commit message plays for
                // an Actions row -- falls back to the release name, then the bare tag, only if a
                // release was published with no notes at all. Not release.Name directly, which
                // would just repeat the tag a second time (it's almost always "SharpEmu {tag}").
                DisplayTitle = FirstNonBlankLine(release.Body)
                    ?? (string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name),
                CreatedAt = release.PublishedAt,
                UpdatedAt = release.PublishedAt,
                HtmlUrl = release.HtmlUrl,
                WindowsAssetDownloadUrl = windowsAsset.BrowserDownloadUrl,
            };

            result.Add(new ClassifiedRun
            {
                Run = run,
                Outcome = BuildOutcome.Success,
                WindowsArtifactSizeBytes = windowsAsset.Size,
                AvailablePlatforms = platforms,
            });
        }

        return result.Take(count).ToList();
    }

    /// <summary>The Recent Builds list renders DisplayTitle on one line -- a multi-line release
    /// body would otherwise show raw "\n"-joined text squashed onto that single line. Returns null
    /// (not "") for a blank/whitespace-only body so the caller's own fallback chain applies.</summary>
    private static string? FirstNonBlankLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    /// <summary>
    /// Commits between two shas on this repo, newest first -- the actual git history, not the
    /// "Build and Release" run history (GetRecentClassifiedRunsAsync's list). Distinct because not
    /// every commit necessarily has its own successful run: one might get skipped by a path
    /// filter, or superseded by a later push before its own run ever finished. Used for a simple
    /// changelog between the installed build and the latest one -- so upgrading shows everything
    /// that actually changed, not just the newest commit's own title.
    /// </summary>
    public async Task<List<(string ShortSha, string Message)>> GetCommitsBetweenAsync(string baseSha, string headSha, CancellationToken ct)
    {
        var response = await _http.GetFromJsonAsync<CompareResponse>(
            $"repos/{Owner}/{Repo}/compare/{Uri.EscapeDataString(baseSha)}...{Uri.EscapeDataString(headSha)}", ct);

        return (response?.Commits ?? new())
            .Select(c => (ShortSha: c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha, Message: c.Commit.Message.Split('\n')[0]))
            .Reverse() // GitHub returns oldest-first; every other list in this app reads newest-first
            .ToList();
    }

    // Raw (non-expired) artifact list per run, shared by FindWindowsArtifactAsync and
    // GetAvailablePlatformsAsync so a run's artifacts are only ever fetched over the network
    // once per ArtifactCacheDuration, no matter how many different platform-related questions get
    // asked about it. Time-bounded (not cached forever for the service instance's lifetime, which
    // is how this worked originally) -- GitHub artifacts DO change after a run completes: they
    // expire (a configurable retention period, default 90 days but often set much shorter) or can
    // be deleted outright, and a run's own conclusion/outcome never changing doesn't mean its
    // artifact list is equally permanent. Caching forever meant a platform label (or the
    // Windows-artifact-presence check) could keep reporting something as available long after it
    // actually expired, for as long as this service instance (and so this app) stayed running.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, (List<Artifact> Candidates, DateTimeOffset FetchedAt)> _artifactsListCache = new();
    private static readonly TimeSpan ArtifactCacheDuration = TimeSpan.FromHours(1);

    // Which run IDs have already logged the "couldn't identify a Windows artifact" diagnostic
    // below -- a genuinely ambiguous run stays ambiguous for as long as this cache entry is fresh;
    // re-logging once per ArtifactCacheDuration refresh (rather than truly once-ever) is fine,
    // since a rename that fixes the ambiguity is exactly the kind of change worth re-surfacing.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _warnedAmbiguousArtifactRunIds = new();

    private async Task<List<Artifact>> GetArtifactCandidatesAsync(long runId, CancellationToken ct)
    {
        if (_artifactsListCache.TryGetValue(runId, out var cachedEntry)
            && DateTimeOffset.UtcNow - cachedEntry.FetchedAt < ArtifactCacheDuration)
        {
            return cachedEntry.Candidates;
        }

        var response = await _http.GetFromJsonAsync<ArtifactsResponse>(
            $"repos/{Owner}/{Repo}/actions/runs/{runId}/artifacts", ct);
        var candidates = (response?.Artifacts ?? new()).Where(a => !a.Expired).ToList();

        // Only cache once there's something real to remember -- an empty list can just mean a
        // very recently completed run's artifacts haven't finished uploading yet, and has to be
        // allowed to resolve differently on a later retry rather than getting stuck.
        if (candidates.Count > 0) _artifactsListCache[runId] = (candidates, DateTimeOffset.UtcNow);
        return candidates;
    }

    /// <summary>Which platforms (Windows/macOS/Linux) a run actually produced an artifact for --
    /// by the same keyword approach as FindWindowsArtifactAsync, but checking every candidate
    /// rather than picking just one, since a single run can (and normally does) have all three at
    /// once.</summary>
    public async Task<BuildPlatforms> GetAvailablePlatformsAsync(long runId, CancellationToken ct)
    {
        var candidates = await GetArtifactCandidatesAsync(runId, ct);
        var platforms = BuildPlatforms.None;
        foreach (var a in candidates)
        {
            if (a.Name.Contains("win", StringComparison.OrdinalIgnoreCase))
                platforms |= BuildPlatforms.Windows;
            else if (a.Name.Contains("osx", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("mac", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("darwin", StringComparison.OrdinalIgnoreCase))
                platforms |= BuildPlatforms.MacOS;
            else if (a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase))
                platforms |= BuildPlatforms.Linux;
        }
        return platforms;
    }

    /// <summary>
    /// Picks the Windows build out of whatever artifacts a run produced, by keyword rather than
    /// an exact expected name -- see the comment on OtherPlatformKeywords above for why.
    /// </summary>
    public async Task<Artifact?> FindWindowsArtifactAsync(long runId, CancellationToken ct)
    {
        // No separate result-level cache here (there used to be one) -- GetArtifactCandidatesAsync
        // is already time-bounded-cached, so recomputing which candidate is the Windows one from
        // that list costs nothing extra network-wise, and correctly picks up a rename/removal
        // once the underlying candidates cache itself refreshes instead of a stale hit lasting
        // for the rest of this service instance's lifetime.
        var candidates = await GetArtifactCandidatesAsync(runId, ct);
        Artifact? result;
        if (candidates.Count == 0)
        {
            result = null;
        }
        else
        {
            // Anything naming another platform is never the Windows build, even if it also
            // happens to contain "win" as a substring (e.g. "darwin").
            var notOtherPlatform = candidates
                .Where(a => !OtherPlatformKeywords.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var windowsMatch = notOtherPlatform.FirstOrDefault(a => a.Name.Contains("win", StringComparison.OrdinalIgnoreCase));
            // No artifact name gave a confident signal either way -- if the run only produced one
            // artifact at all, that's the only build there is, so use it rather than reporting
            // "not found". With more than one ambiguous candidate there's no safe guess to make.
            result = windowsMatch ?? (notOtherPlatform.Count == 1 ? notOtherPlatform[0] : null);

            // Specifically the "genuinely can't tell" case -- 2+ candidates that aren't
            // confidently another platform, but none of them look like Windows by name either.
            // The most likely real-world cause: whoever maintains this workflow renamed the
            // Windows artifact to something that no longer contains "win" at all -- something
            // OtherPlatformKeywords can never anticipate on its own, since it's a list of
            // platforms this app already knows about, not a crystal ball for a rename it's never
            // seen. Logged once per run (not the normal per-check dedup pattern used elsewhere,
            // since a run's own artifacts are immutable once uploaded) so this reads as "the
            // naming may have changed, here's exactly what showed up" instead of every affected
            // build just silently vanishing from the list with zero explanation.
            if (result == null && notOtherPlatform.Count > 1 && _warnedAmbiguousArtifactRunIds.TryAdd(runId, 0))
            {
                Logger.Log($"Run {runId}: could not tell which of these artifacts is the Windows build -- " +
                    $"{string.Join(", ", notOtherPlatform.Select(a => a.Name))}. If none of these are actually " +
                    "Windows builds, this is expected; if one of them is, the artifact naming convention may have " +
                    "changed and this app's Windows-detection keywords may need updating.");
            }
        }

        return result;
    }

    /// <summary>
    /// Downloads the artifact zip and extracts it into installDir/{branchFolder}/{sha}/ -- each
    /// tracked fork/branch gets its own subfolder (see AppPaths.BranchFolderName) so switching
    /// forks never touches another branch's build. If previousSha points at an existing build in
    /// that same branch folder, that build's gui-settings.json (game library folders, log
    /// settings, etc. -- SharpEmu is portable and keeps that file next to its own exe) is copied
    /// into the new build. Old builds are deliberately never deleted -- every sha you've ever
    /// downloaded stays on disk so switching back to it later is a local reuse, not a re-download.
    /// Returns the extraction folder path. progress, if given, is reported a 0..1 fraction of
    /// bytes-read-so-far / Content-Length as the download streams in -- silently never reported
    /// if the server doesn't send a Content-Length (GitHub always does for artifact zips in
    /// practice, but nothing here depends on that).
    /// </summary>
    public async Task<string> DownloadAndExtractArtifactAsync(
        Artifact artifact, string sha, string installDir, string branchFolder, string? previousSha, CancellationToken ct,
        IProgress<double>? progress = null)
    {
        string branchDir = Path.Combine(installDir, branchFolder);
        Directory.CreateDirectory(branchDir);

        string tempZip = Path.Combine(Path.GetTempPath(), $"{artifact.Name}-{Guid.NewGuid():N}.zip");

        try
        {
            using (var response = await _http.GetAsync(
                $"repos/{Owner}/{Repo}/actions/artifacts/{artifact.Id}/zip", HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException(
                        $"GitHub rejected the artifact download ({(int)response.StatusCode} {response.StatusCode}). " +
                        "Listing runs works with almost any token, but downloading artifact zips needs a classic " +
                        "Personal Access Token with the 'repo' scope checked (fine-grained tokens need 'Actions: Read-only'). " +
                        "Update token.txt with a token that has that scope and click 'Reload Token from File'.");
                }
                response.EnsureSuccessStatusCode();
                await DownloadToFileAsync(response, tempZip, progress, ct);
            }

            return FinishExtraction(tempZip, branchDir, sha, previousSha);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    /// <summary>
    /// Downloads a GitHub Release asset (its public, non-expiring browser_download_url -- never an
    /// Actions artifact ID) and extracts it the same way DownloadAndExtractArtifactAsync does.
    /// Uses _downloadHttp (unauthenticated, see its own doc comment) rather than _http, since this
    /// URL needs no token and often resolves to a non-api.github.com host on the first hop. A
    /// failure here is far more likely a stale/rotated asset URL than an auth problem, so unlike
    /// the Actions path there's no 401/403-specific messaging.
    /// </summary>
    public async Task<string> DownloadAndExtractReleaseAssetAsync(
        string downloadUrl, string sha, string installDir, string branchFolder, string? previousSha, CancellationToken ct,
        IProgress<double>? progress = null)
    {
        string branchDir = Path.Combine(installDir, branchFolder);
        Directory.CreateDirectory(branchDir);

        string tempZip = Path.Combine(Path.GetTempPath(), $"release-asset-{Guid.NewGuid():N}.zip");

        try
        {
            using (var response = await _downloadHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await DownloadToFileAsync(response, tempZip, progress, ct);
            }

            return FinishExtraction(tempZip, branchDir, sha, previousSha);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    /// <summary>Shared byte-streaming-with-progress loop, used by both the Actions-artifact and
    /// Release-asset download paths -- identical either way once a successful HttpResponseMessage
    /// is in hand.</summary>
    private static async Task DownloadToFileAsync(HttpResponseMessage response, string destPath, IProgress<double>? progress, CancellationToken ct)
    {
        long? totalBytes = response.Content.Headers.ContentLength;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(destPath);

        if (progress == null || totalBytes is not > 0)
        {
            await httpStream.CopyToAsync(fileStream, ct);
        }
        else
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
                progress.Report((double)totalRead / totalBytes.Value);
            }
        }
    }

    /// <summary>Shared post-download tail (extract, unwrap, carry over settings), used by both the
    /// Actions-artifact and Release-asset download paths -- identical either way once the zip is
    /// on disk at tempZip.</summary>
    private static string FinishExtraction(string tempZip, string branchDir, string sha, string? previousSha)
    {
        string extractDir = Path.Combine(branchDir, sha);
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);

        ZipFile.ExtractToDirectory(tempZip, extractDir);
        UnwrapNestedZips(extractDir);
        CarryOverGuiSettings(branchDir, previousSha, sha, extractDir);
        return extractDir;
    }

    /// <summary>
    /// Copies gui-settings.json forward from whatever build was previously active in this branch
    /// folder into the one just installed, so game library folders/log settings etc. follow you
    /// across switches. Deliberately does NOT delete the old build's folder -- every build you've
    /// downloaded is kept on disk so switching back to it is a local reuse (see
    /// GitHubUpdaterService.FindExistingBuild/ReuseExistingBuild), never a re-download.
    /// </summary>
    private static void CarryOverGuiSettings(string branchDir, string? previousSha, string newSha, string newExtractDir)
    {
        if (string.IsNullOrEmpty(previousSha) || previousSha == newSha)
            return;

        string oldSettings = Path.Combine(branchDir, previousSha, GuiSettingsFileName);
        if (!File.Exists(oldSettings))
            return;

        try
        {
            Logger.Log($"Carrying over {GuiSettingsFileName} (game library folders, etc.) from build {previousSha}.");
            File.Copy(oldSettings, Path.Combine(newExtractDir, GuiSettingsFileName), overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not carry over {GuiSettingsFileName} from the previous build: {ex.Message}");
        }
    }

    /// <summary>
    /// GitHub always wraps whatever was uploaded in its own zip container. This workflow uploads
    /// a single zip (whatever it's named -- see FindWindowsArtifactAsync) as the artifact content
    /// itself, so one extraction only recovers that inner zip -- this unwraps it (and any further
    /// accidental nesting) in place.
    /// </summary>
    private static void UnwrapNestedZips(string dir)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            if (Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories).Length > 0)
                return;

            var nestedZips = Directory.GetFiles(dir, "*.zip", SearchOption.TopDirectoryOnly);
            if (nestedZips.Length == 0)
                return;

            foreach (var nested in nestedZips)
            {
                ZipFile.ExtractToDirectory(nested, dir, overwriteFiles: true);
                File.Delete(nested);
            }
        }
    }

    /// <summary>
    /// Finds the emulator executable inside an extracted build folder, preferring
    /// a file with "sharpemu" in its name.
    /// </summary>
    public static string? FindExecutable(string extractDir)
    {
        if (!Directory.Exists(extractDir)) return null;

        var exeFiles = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
        return exeFiles.FirstOrDefault(f =>
                   Path.GetFileNameWithoutExtension(f).Contains("sharpemu", StringComparison.OrdinalIgnoreCase))
               ?? exeFiles.FirstOrDefault();
    }

    /// <summary>
    /// Looks for a given build's sha already sitting on disk anywhere under installDir -- in any
    /// branch subfolder, or directly at the root (the flat installDir/{sha}/ layout this app used
    /// before builds were organized per branch, in case someone points InstallDir at an old
    /// install location) -- pure filesystem check, no network involved. Lets the caller skip
    /// downloading entirely whenever the exact same build is already available anywhere.
    /// </summary>
    public static string? FindExistingBuild(string installDir, string sha)
    {
        if (!Directory.Exists(installDir)) return null;

        string flatCandidate = Path.Combine(installDir, sha);
        if (FindExecutable(flatCandidate) != null)
            return flatCandidate;

        foreach (string branchDir in Directory.GetDirectories(installDir))
        {
            string candidate = Path.Combine(branchDir, sha);
            if (FindExecutable(candidate) != null)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Points installDir/{branchFolder}/{sha}/ at a build already sitting on disk somewhere else
    /// under installDir, instead of downloading it again -- a local copy (or, if it's already
    /// exactly at the target -- e.g. re-selecting a build already installed for this same branch
    /// -- nothing at all) rather than a network round-trip. Same gui-settings.json carryover as
    /// an actual download; old builds are never deleted (see CarryOverGuiSettings).
    /// </summary>
    public static string ReuseExistingBuild(string existingDir, string installDir, string branchFolder, string sha, string? previousSha)
    {
        string branchDir = Path.Combine(installDir, branchFolder);
        Directory.CreateDirectory(branchDir);
        string targetDir = Path.Combine(branchDir, sha);

        if (!string.Equals(Path.GetFullPath(existingDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            CopyDirectoryRecursive(existingDir, targetDir);
        }

        CarryOverGuiSettings(branchDir, previousSha, sha, targetDir);
        return targetDir;
    }

    // Internal, not private -- MainForm.MoveDirectory (BrowseForInstallDirAsync's cross-drive
    // fallback, same "Directory.Move can't cross volumes" situation as ReuseExistingBuild's own
    // use above) shares this instead of keeping its own duplicate copy.
    internal static void CopyDirectoryRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    public void Dispose()
    {
        _http.Dispose();
        _downloadHttp.Dispose();
    }
}
