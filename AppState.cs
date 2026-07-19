using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpEmuUpdater;

public sealed class BranchInstall
{
    public long RunId { get; set; }
    public string Sha { get; set; } = "";
}

/// <summary>The last successfully-fetched "latest build" for a branch -- see
/// AppState.LatestKnownBuilds. Mirrors just the fields MainForm's own LATEST BUILD label needs to
/// re-render the same line without a live check having succeeded this session.</summary>
public sealed class LatestBuildInfo
{
    public string ShortSha { get; set; } = "";
    public string DisplayNumber { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public BuildOutcome Outcome { get; set; }
    public string DisplayTitle { get; set; } = "";
    // When this info was actually fetched -- shown alongside it so a cached/stale value (e.g.
    // from before a GitHub-side outage started) never looks indistinguishable from a fresh one.
    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class AppState
{
    // Seconds, not minutes, so the floor (see MainForm's NumericUpDown) can go below a full
    // minute for anyone who wants it -- but 5 minutes by default. A build takes several minutes
    // to run either way, and GitHub's REST API is rate-limited to 5000 requests/hour; checking
    // much more often than this buys almost nothing for a typical "let me know when there's an
    // update" user while eating into that budget for no real benefit.
    public int PollIntervalSeconds { get; set; } = 300;

    // Only ever refreshes the "latest build" status display, on a timer or via "Check Now" --
    // never downloads or applies anything by itself. Installing a build only ever happens from
    // the explicit "Select Build..." action.
    public bool AutoCheck { get; set; } = true;

    public string InstallDir { get; set; } = AppPaths.DefaultInstallDir;

    // Discord Application Client ID for Rich Presence (see DiscordPresenceManager) -- a public
    // identifier, not a secret, but empty by default since it's specific to whichever Discord
    // Application the user registers for themselves. Rich Presence is entirely disabled (no
    // connection attempted at all) while this is blank.
    public string DiscordClientId { get; set; } = "";

    // Last SharpEmu Updater version (see SelfUpdateChecker/AppVersion) already announced via
    // toast, so the same newly-published version doesn't toast again on every check.
    public string LastAnnouncedUpdaterVersion { get; set; } = "";

    // Set only by explicitly clicking "Skip This Update" in SelfUpdateAvailableForm -- unlike
    // LastAnnouncedUpdaterVersion (which just dedupes the toast), this actually suppresses the
    // update dialog itself for this specific version. "Remind Me Later"/closing the dialog
    // deliberately does NOT set this, so the dialog reappears on the next check either way.
    public string SkippedUpdaterVersion { get; set; } = "";

    // Pixel distance from the top of the Recent Builds/Activity Log SplitContainer to the
    // splitter bar -- null until the user actually drags it once, at which point it's saved so
    // the chosen split survives a restart instead of resetting to even every time.
    public int? BuildsLogSplitterDistance { get; set; }

    // Which fork's builds this app currently tracks. Defaults to the upstream repo itself, on
    // its main branch -- sharpemu/sharpemu is the main source, so "main" is the one branch name
    // that never changes; every other contributor's branch comes from whichever PR they're
    // actively building.
    public string ForkOwner { get; set; } = GitHubUpdaterService.UpstreamOwner;
    public string ForkRepo { get; set; } = GitHubUpdaterService.UpstreamRepo;
    public string ForkBranch { get; set; } = "main";

    // Every fork/branch ever installed gets its own folder under InstallDir (see
    // AppPaths.BranchFolderName) and its own entry here, so switching branches never wipes
    // another branch's build and switching back to one you've already got is recognized as
    // already installed instead of re-downloading.
    public Dictionary<string, BranchInstall> Installs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string CurrentBranchKey => AppPaths.BranchFolderName(ForkOwner, ForkBranch);

    [JsonIgnore]
    public long LastAppliedRunId => Installs.TryGetValue(CurrentBranchKey, out var v) ? v.RunId : 0;

    [JsonIgnore]
    public string LastAppliedSha => Installs.TryGetValue(CurrentBranchKey, out var v) ? v.Sha : "";

    public void RecordInstall(long runId, string sha) =>
        Installs[CurrentBranchKey] = new BranchInstall { RunId = runId, Sha = sha };

    // Keyed the same way as Installs -- per fork/branch, so switching between forks (or
    // restarting the app) doesn't lose track of what LATEST BUILD should show while a check is
    // failing (a GitHub-side outage, a network hiccup, ...) or simply hasn't run yet this session.
    public Dictionary<string, LatestBuildInfo> LatestKnownBuilds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public LatestBuildInfo? LatestKnownBuild => LatestKnownBuilds.TryGetValue(CurrentBranchKey, out var v) ? v : null;

    public void RecordLatestKnownBuild(LatestBuildInfo info) => LatestKnownBuilds[CurrentBranchKey] = info;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppState Load()
    {
        try
        {
            if (File.Exists(AppPaths.StateFile))
            {
                string json = File.ReadAllText(AppPaths.StateFile);
                var state = JsonSerializer.Deserialize<AppState>(json);
                if (state != null) return state;
            }
        }
        catch
        {
            // fall through to defaults on corrupt state
        }
        return new AppState();
    }

    public void Save()
    {
        AppPaths.EnsureDataDir();
        File.WriteAllText(AppPaths.StateFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}
