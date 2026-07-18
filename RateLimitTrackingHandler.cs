namespace SharpEmuUpdater;

/// <summary>Captures GitHub's X-RateLimit-Remaining/X-RateLimit-Limit response headers off every
/// request GitHubUpdaterService's HttpClient makes -- lets the UI show a live "API calls left"
/// readout without threading rate-limit tracking through every individual service method. Also
/// watches for GitHub's SEPARATE secondary/abuse rate limit -- a completely different mechanism
/// from the primary hourly quota above (bursty request patterns can trip it well before Remaining
/// ever reaches zero), which the primary counter alone gives no visibility into at all.</summary>
public sealed class RateLimitTrackingHandler : DelegatingHandler
{
    public int? Remaining { get; private set; }
    public int? Limit { get; private set; }

    // GitHub's documented secondary/abuse rate limit response: HTTP 403 with "rate limit"
    // somewhere in the JSON error message, usually (not always) with a Retry-After header giving
    // a concrete cooldown in seconds. Cleared only on a genuine subsequent success -- a different
    // kind of failure (e.g. a real transient 5xx) shouldn't quietly erase "you were just rate
    // limited" before that cooldown has actually had a chance to matter.
    public bool IsSecondaryRateLimited { get; private set; }
    public int? RetryAfterSeconds { get; private set; }

    public event Action? Updated;

    // PooledConnectionLifetime bounds how long a pooled connection is reused before being torn
    // down and reconnected -- without it, a connection that's been sitting idle across a PC sleep
    // cycle can go stale (the OS/network silently drops it), and the next request on it fails
    // with a connection/DNS error instead of transparently reconnecting. Same fix already applied
    // to SharpEmuMobile's copy of this file after an identical report there.
    public RateLimitTrackingHandler() : base(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);

        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues) &&
            int.TryParse(remainingValues.FirstOrDefault(), out int remaining))
            Remaining = remaining;

        if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limitValues) &&
            int.TryParse(limitValues.FirstOrDefault(), out int limit))
            Limit = limit;

        bool wasSecondaryLimited = IsSecondaryRateLimited;
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Buffer the body first rather than reading it directly -- whatever actually handles
            // this response afterward (EnsureSuccessStatusCode's caller, error-message display,
            // ...) still needs to see the same content, and a stream can normally only be read
            // once.
            await response.Content.LoadIntoBufferAsync();
            string body = await response.Content.ReadAsStringAsync(ct);
            if (body.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                IsSecondaryRateLimited = true;
                RetryAfterSeconds = response.Headers.RetryAfter?.Delta is TimeSpan delta
                    ? (int)Math.Ceiling(delta.TotalSeconds)
                    : null;
            }
        }
        else if (response.IsSuccessStatusCode)
        {
            IsSecondaryRateLimited = false;
            RetryAfterSeconds = null;
        }

        if (Remaining.HasValue || Limit.HasValue || IsSecondaryRateLimited != wasSecondaryLimited)
            Updated?.Invoke();

        return response;
    }
}
