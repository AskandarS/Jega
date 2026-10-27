using System.Globalization;

namespace Jega.Core;

/// <summary>
/// Which service the address field and the chosen address's point go to: Adressevælger first, as always; the first time
/// it refuses its token (400 for a token it doesn't know; 401, 403), is gone (404) or doesn't
/// answer (after the retries; never a cancel), Danmarks
/// Adresseregister on Datafordeleren's GraphQL with the key, for as long as this lives (one SiteFetcher: one Load from
/// Address), so a keystroke doesn't cost a failed call first. Any other failure (an unreadable answer, the office's
/// proxy) stays Adressevælger's error. No network here: the calls come in as functions (SiteFetcher's).
/// </summary>
public sealed class AddressFallback
{
    /// <summary>Why Adressevælger was left: its HTTP status, or "timeout"; null while it is used.</summary>
    public string? Reason { get; private set; }

    /// <summary>$primary (Adressevælger), or $fallback once it is left; a failure of $primary that leaves it runs
    /// $fallback at once, so the user gets an answer, not the error.</summary>
    public async Task<T> Run<T>(Func<Task<T>> primary, Func<Task<T>> fallback)
    {
        if (Reason is null)
        {
            try
            {
                return await primary();
            }
            catch (FetchException e) when (Leaves(e) is { } why)
            {
                Reason = why;
            }
        }
        return await fallback();
    }

    /// <summary>Takes note of $e from a call whose answer isn't needed (the warm-up), so the next search goes straight
    /// to the fallback if it left Adressevælger.</summary>
    public void Note(FetchException e) => Reason ??= Leaves(e);

    // A refused token, a service that is gone, or no answer: the reason to leave; anything else: null.
    private static string? Leaves(FetchException e) =>
        e.Status is 400 or 401 or 403 or 404 ? e.Status.Value.ToString(CultureInfo.InvariantCulture) : e.NoAnswer ? "timeout" : null;
}
