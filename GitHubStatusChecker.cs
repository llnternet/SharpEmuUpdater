using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SharpEmuUpdater;

/// <summary>
/// Checks GitHub's own status page (githubstatus.com, a completely separate service from
/// api.github.com -- no auth needed) for any active incident affecting API requests. Queried
/// reactively, only when an actual API call has already failed -- not polled continuously, since
/// it's only ever relevant right when something's already gone wrong. Confirmed live: a real
/// "Degraded REST API Availability" incident on GitHub's own status page exactly overlapped a
/// confusing stretch of 401/503 errors this app hit, which -- without this -- just looked like an
/// unexplained local problem (bad token? rate limited? something in the app?) with no way to tell
/// it was actually GitHub's own infrastructure having a bad day.
/// </summary>
public static class GitHubStatusChecker
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// The most recently updated unresolved incident whose name mentions "API" or "rate limit"
    /// -- null if the status page has nothing relevant right now, or if the status page itself
    /// couldn't be reached (never lets a status-check failure mask or replace the real error).
    /// </summary>
    public static async Task<GitHubIncident?> GetActiveApiIncidentAsync(CancellationToken ct)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<UnresolvedIncidentsResponse>(
                "https://www.githubstatus.com/api/v2/incidents/unresolved.json", ct);
            return (response?.Incidents ?? new())
                .Where(i => i.AffectsBuildTracking)
                .OrderByDescending(i => i.UpdatedAt)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}

public sealed class UnresolvedIncidentsResponse
{
    [JsonPropertyName("incidents")]
    public List<GitHubIncident> Incidents { get; set; } = new();
}

public sealed class GitHubIncident
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // "investigating", "identified", "monitoring" -- resolved incidents don't appear in the
    // unresolved.json feed at all, so this is always one of those three in practice.
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("impact")]
    public string Impact { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("shortlink")]
    public string ShortLink { get; set; } = "";

    [JsonPropertyName("incident_updates")]
    public List<GitHubIncidentUpdate> IncidentUpdates { get; set; } = new();

    /// <summary>The newest update's own message -- e.g. "API Requests is experiencing degraded
    /// performance. We are continuing to investigate." -- more specific and current than just the
    /// incident's own title.</summary>
    public string? LatestUpdateBody => IncidentUpdates.OrderByDescending(u => u.DisplayAt).FirstOrDefault()?.Body;

    // A real incident's own title is often generic ("Incident with GitHub Actions", "Disruption
    // with some GitHub services") and won't literally contain "API" even when it's actively
    // breaking the API Requests/Actions calls this app depends on -- confirmed live, both of
    // those exact titles were active simultaneously while this app was hitting real 503s, and
    // the old name-only filter (just Name.Contains("API")/"rate limit") missed both. Each
    // incident update carries its own affected_components snapshot (a component's status can
    // change between updates), so only the LATEST update's list reflects what's actually affected
    // right now -- an earlier update naming a component that's since recovered shouldn't count.
    private static readonly string[] RelevantComponentNames = { "API Requests", "Actions" };

    public bool AffectsBuildTracking =>
        Name.Contains("API", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
        (IncidentUpdates.OrderByDescending(u => u.DisplayAt).FirstOrDefault()?.AffectedComponents ?? new())
            .Any(c => RelevantComponentNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
}

public sealed class GitHubIncidentUpdate
{
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("display_at")]
    public DateTimeOffset DisplayAt { get; set; }

    [JsonPropertyName("affected_components")]
    public List<AffectedComponentDto>? AffectedComponents { get; set; }
}

public sealed class AffectedComponentDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
