namespace Jega.Core;

/// <summary>
/// Whether a call to a service is tried again, and after how long: a server error (Datafordeleren's WFS answers 500 now
/// and then, and the same call works a moment later), no answer at all (a network blip, a timeout) and 429 (too many
/// calls) pass; an answer that won't change (401, 403, 404 …) doesn't. Three tries in all. A call the user cancelled is
/// never tried again: that is the caller's (SiteFetcher.Get).
/// </summary>
public static class Retry
{
    public const int Tries = 3;

    // The most a 429 may make the user wait between two tries.
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// After try $attempt (1 = the first) failed: how long to wait before the next, or null to give up. $status: the
    /// HTTP status, null for no answer; $retryAfter: how long a 429 asked to wait, if it said. 1 s, then 2 s; a 429 as
    /// asked, at most 5 s.
    /// </summary>
    public static TimeSpan? Wait(int attempt, int? status, TimeSpan? retryAfter = null)
    {
        if (attempt >= Tries || status is { } s && s != 429 && s < 500)
        {
            return null;
        }
        var wait = TimeSpan.FromSeconds(attempt);
        return status == 429 && retryAfter is { } asked && asked > TimeSpan.Zero ? (asked < MaxWait ? asked : MaxWait) : wait;
    }
}
