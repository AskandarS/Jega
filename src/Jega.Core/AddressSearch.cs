using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jega.Core;

/// <summary>
/// The address field's suggestions: Adressevælger's own
/// order ignores the town and postcode, and a town in the text, or an address that doesn't exist, often gives no hits or
/// only phonetic guesses ("Eksempelsti" for "eksempelvej testby"). So: 200 hits; if none is on the street being typed,
/// again with the street and postcode, then with fewer words; sorted by how many of the typed words each hit matches. Streets while no house number is typed. No network
/// here: the search call comes in as $api (SiteFetcher's).
/// </summary>
public static class AddressSearch
{
    /// <summary>The hits Adressevælger is asked for: a street in many towns lists them all only with this many.</summary>
    public const int Max = 200;

    /// <summary>One hit from /husnumre/soeg: "husnummer" (an address) or "navngivenvejpostnummer"/"vejnavn" (a street).</summary>
    public sealed record Hit(string Type, string Id, string Title, string Street, string Town);

    /// <summary>
    /// A suggestion: an address (Id set), or a street (Id null): picking it puts $Fill in the field ("Eksempelvej ,
    /// 1234 Testby") with the cursor at $Caret, after the street name, ready for the house number.
    /// </summary>
    public sealed record Suggestion(string Text, string? Id = null, string? Fill = null, int Caret = 0);

    /// <summary>Suggestions for $text, at most $max; addresses if any, else streets; best match first.</summary>
    public static async Task<List<Suggestion>> Search(string text, Func<string, Task<List<Hit>>> api, int max = 8)
    {
        var hits = await api(text);
        var words = Words(text);
        var first = words.FirstOrDefault() ?? "";
        // Shorter queries to try, best first: the street and postcode (a number the street doesn't have there: its
        // numbers in that town, not that number in others; at most Max of them, so a long street's may be cut), up to
        // the house number, else drop words from the end (at most 2).
        var numberAt = Array.FindIndex(words, w => char.IsDigit(w[0]));
        var retries = new List<string[]>();
        if (numberAt >= 0 && numberAt < words.Length - 1)
        {
            if (numberAt > 0 && words.Skip(numberAt + 1).FirstOrDefault(Postcode) is { } postcode)
            {
                retries.Add([.. words[..numberAt], postcode]);
            }
            retries.Add(words[..(numberAt + 1)]);
        }
        else if (numberAt < 0)
        {
            for (var keep = words.Length - 1; keep >= 1 && retries.Count < 2; keep--)
            {
                retries.Add(words[..keep]);
            }
        }
        foreach (var shorter in retries)
        {
            if (OnStreet(hits, first))
            {
                break;
            }
            var more = await api(string.Join(' ', shorter));
            if (OnStreet(more, first))
            {
                hits = more;
            }
        }
        return Pick(words, hits, max);
    }

    /// <summary>The hits, scored and sorted; the service's own order breaks ties.</summary>
    public static List<Suggestion> Pick(string[] words, List<Hit> hits, int max)
    {
        var addresses = new List<(int Score, int Order, Suggestion S)>();
        var streets = new List<(int Score, int Order, Suggestion S)>();
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            if (hit.Type == "husnummer" && Guid.TryParse(hit.Id, out _))
            {
                addresses.Add((Score(words, hit.Title), i, new Suggestion(hit.Title, hit.Id)));
            }
            else if (hit.Type is "navngivenvejpostnummer" or "vejnavn")
            {
                var label = hit.Town.Length > 0 ? $"{hit.Street}, {hit.Town}" : hit.Street;
                var fill = hit.Town.Length > 0 ? $"{hit.Street} , {hit.Town}" : $"{hit.Street} ";
                streets.Add((Score(words, label), i, new Suggestion(label, null, fill, hit.Street.Length + 1)));
            }
        }
        return (addresses.Count > 0 ? addresses : streets)
            .OrderByDescending(s => s.Score).ThenBy(s => s.Order).Take(max).Select(s => s.S).ToList();
    }

    /// <summary>The hits in /husnumre/soeg's answer ("fund").</summary>
    public static List<Hit> Hits(JsonElement answer)
    {
        static string Str(JsonElement e, params string[] names)
        {
            foreach (var name in names)
            {
                if (e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    return v.ToString();
                }
            }
            return "";
        }
        if (!answer.TryGetProperty("fund", out var fund) || fund.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return fund.EnumerateArray().Select(h =>
        {
            var postcode = Str(h, "postnr");
            return new Hit(Str(h, "type"), Str(h, "id"), Str(h, "titel"), Str(h, "vejnavn", "vejNavn", "titel"),
                postcode.Length > 0 ? $"{postcode} {Str(h, "postdistrikt")}" : "");
        }).ToList();
    }

    /// <summary>Lower-cased words of an address text, split on spaces and commas.</summary>
    public static string[] Words(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"[\s,]+").Where(w => w.Length > 0).ToArray();

    /// <summary>
    /// What the fallback asks Danmarks Adresseregister for (AddressFallback): its GraphQL only has a case-sensitive
    /// "starts with" on the address's text, no ranking, so the typed text is put in the register's own form. $Prefixes:
    /// the street (the words before the first one that starts with a digit), each word with a capital letter (after a dot
    /// or hyphen too: "H.C. Andersens Boulevard"), but "af", "ved", "til", "i" and "og" small; a dot ending a word tried
    /// both kept and dropped (the register writes "Gl Strandvej"); with a house number, each form with it and a comma (that
    /// number in every town) and without (its letters too: "1A"); without one, the street alone (its numbers). $Number:
    /// the typed house number, lower-cased, to keep "1" from giving "10"; null without one.
    /// </summary>
    public sealed record RegisterQuery(IReadOnlyList<string> Prefixes, string? Number);

    // Small in a street's name, unless first: "Sankt Peders Stræde", "Tårnby Torv", "Ved Stranden", "Allé til Skoven".
    private static readonly string[] SmallWords = ["af", "ved", "til", "i", "og"];

    /// <summary>The fallback's query for $text (RegisterQuery). No street typed: no prefixes.</summary>
    public static RegisterQuery Register(string text)
    {
        var words = Words(text);
        var numberAt = Array.FindIndex(words, w => char.IsDigit(w[0]));
        var street = numberAt >= 0 ? words[..numberAt] : words;
        var number = numberAt >= 0 ? words[numberAt] : null;
        if (street.Length == 0)
        {
            return new RegisterQuery([], number);
        }
        var kept = string.Join(' ', street.Select(Capital));
        var dropped = string.Join(' ', street.Select((w, i) => Capital(w.TrimEnd('.'), i)));
        string[] forms = dropped == kept ? [kept] : [kept, dropped];
        if (number is null)
        {
            return new RegisterQuery(forms, null);
        }
        var upper = number.ToUpperInvariant();
        return new RegisterQuery([.. forms.Select(f => $"{f} {upper},"), .. forms.Select(f => $"{f} {upper}")], number);
    }

    // A word of a street's name in the register's form: a capital first and after a dot or hyphen; the small words small.
    private static string Capital(string word, int at)
    {
        if (at > 0 && SmallWords.Contains(word))
        {
            return word;
        }
        var c = word.ToCharArray();
        for (var i = 0; i < c.Length; i++)
        {
            if (i == 0 || c[i - 1] is '.' or '-')
            {
                c[i] = char.ToUpperInvariant(c[i]);
            }
        }
        return new string(c);
    }

    /// <summary>
    /// The fallback's DAR_Husnummer nodes (id_lokalId, husnummertekst, adgangsadressebetegnelse) as hits, each address
    /// once, its text split at the last comma into street and town; with $number only the numbers that are it or it
    /// with a letter ("1", "1A", not "10").
    /// </summary>
    public static List<Hit> RegisterHits(IEnumerable<JsonElement> nodes, string? number)
    {
        var hits = new List<Hit>();
        foreach (var n in nodes)
        {
            var id = n.GetProperty("id_lokalId").GetString() ?? "";
            var text = n.GetProperty("husnummertekst").GetString() ?? "";
            var title = n.GetProperty("adgangsadressebetegnelse").GetString() ?? "";
            if (number is not null && !(text.ToLowerInvariant().StartsWith(number, StringComparison.Ordinal)
                    && (text.Length == number.Length || !char.IsDigit(text[number.Length]))))
            {
                continue;
            }
            var comma = title.LastIndexOf(',');
            hits.Add(new Hit("husnummer", id, title, comma > 0 ? title[..comma] : title, comma > 0 ? title[(comma + 1)..].Trim() : ""));
        }
        return [.. hits.DistinctBy(h => h.Id)];
    }

    /// <summary>RegisterHits from a JSON array of nodes, as the tests give them.</summary>
    public static List<Hit> RegisterHits(string nodes, string? number)
    {
        using var doc = JsonDocument.Parse(nodes);
        return RegisterHits(doc.RootElement.EnumerateArray().ToList(), number);
    }

    /// <summary>
    /// The fallback's suggestions for $text: each of its prefixes (Register) through $byPrefix (the register's current
    /// house numbers whose text starts with it), scored and sorted on the typed words as Pick does, so a town typed
    /// without its postcode still wins ("skolegade 1 vejle": Vejle's first). Addresses only: a street alone gives its
    /// numbers.
    /// </summary>
    public static async Task<List<Suggestion>> SearchRegister(string text, Func<string, Task<List<JsonElement>>> byPrefix, int max = 8)
    {
        var query = Register(text);
        var hits = new List<Hit>();
        foreach (var prefix in query.Prefixes)
        {
            hits.AddRange(RegisterHits(await byPrefix(prefix), query.Number));
        }
        return Pick(Words(text), [.. hits.DistinctBy(h => h.Id)], max);
    }

    /// <summary>Whether a typed word is a postcode: four digits.</summary>
    private static bool Postcode(string word) => word.Length == 4 && word.All(char.IsDigit);

    /// <summary>Whether any hit is on a street starting with the first typed word (not just a phonetic guess).</summary>
    private static bool OnStreet(List<Hit> hits, string first) =>
        first.Length > 0 && hits.Any(h => h.Street.ToLowerInvariant().StartsWith(first, StringComparison.Ordinal));

    /// <summary>How many of the typed words start a word in the address, e.g. "testby" and "12" in "…, 1234 Testby".</summary>
    private static int Score(string[] typed, string title)
    {
        var words = Words(title);
        return typed.Count(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal)));
    }
}
