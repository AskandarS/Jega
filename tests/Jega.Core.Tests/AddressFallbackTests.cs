using System.Diagnostics;
using System.Text.Json;
using Jega.Core;

namespace Jega.Core.Tests;

/// <summary>
/// The address field's fallback: when it leaves Adressevælger (AddressFallback), and what it asks Danmarks
/// Adresseregister instead (AddressSearch.Register, RegisterHits, SearchRegister). The register's GraphQL only has a
/// case-sensitive "starts with", so the typed text is put in the register's form; Pick then sorts on the typed words.
/// </summary>
public class AddressFallbackTests
{
    private const string Id1 = "0a3f50a0-0000-32b8-e044-0003ba298011";
    private const string Id2 = "0a3f50a0-0000-32b8-e044-0003ba298012";
    private const string Id3 = "0a3f50a0-0000-32b8-e044-0003ba298013";
    private const string Id4 = "0a3f50a0-0000-32b8-e044-0003ba298014";

    // 20 public addresses (town halls, museums and the like), each typed three
    // ways: as the register writes it, lower-case with the town but no comma or postcode, and street and number alone.
    // All three give the street and number in the register's form first.
    [Theory]
    [InlineData("Skolegade 1, 7100 Vejle")]
    [InlineData("Willy Sørensens Plads 1, 7100 Vejle")]
    [InlineData("Gormsgade 23, 7300 Jelling")]
    [InlineData("Brøndumsvej 4, 9990 Skagen")]
    [InlineData("Rådhuspladsen 2, 8000 Aarhus C")]
    [InlineData("Munkemøllestræde 3, 5000 Odense C")]
    [InlineData("Flakhaven 2, 5000 Odense C")]
    [InlineData("Rådhuspladsen 1, 1550 København V")]
    [InlineData("Bredgade 68, 1260 København K")]
    [InlineData("Stændertorvet 1, 4000 Roskilde")]
    [InlineData("Domkirkepladsen 3, 4000 Roskilde")]
    [InlineData("Boulevarden 13, 9000 Aalborg")]
    [InlineData("Torvegade 74, 6700 Esbjerg")]
    [InlineData("Laksetorvet 1, 8900 Randers C")]
    [InlineData("Akseltorv 1, 6000 Kolding")]
    [InlineData("Torvet 5, 7400 Herning")]
    [InlineData("Torvet 15, 6760 Ribe")]
    [InlineData("Gl Strandvej 13, 3050 Humlebæk")]
    [InlineData("Ramsherred 5, 5700 Svendborg")]
    [InlineData("Rådhustorvet 10, 6400 Sønderborg")]
    public void The_register_s_form_from_every_way_of_typing(string official)
    {
        var comma = official.IndexOf(',');
        var streetAndNumber = official[..comma];
        var number = streetAndNumber[(streetAndNumber.LastIndexOf(' ') + 1)..];
        var town = official[(comma + 1)..].Trim().Split(' ', 2)[1];
        foreach (var typed in new[] { official, $"{streetAndNumber} {town}".ToLowerInvariant(), streetAndNumber })
        {
            var query = AddressSearch.Register(typed);
            Assert.Equal($"{streetAndNumber},", query.Prefixes[0]);
            Assert.Contains(streetAndNumber, query.Prefixes);
            Assert.Equal(number, query.Number);
        }
    }

    [Fact]
    public void A_dot_ending_a_word_is_tried_kept_and_dropped()
    {
        // The register writes "Gl Strandvej"; typed with the dot, the dropped form finds it.
        var query = AddressSearch.Register("Gl. Strandvej 13 Humlebæk");
        Assert.Equal(["Gl. Strandvej 13,", "Gl Strandvej 13,", "Gl. Strandvej 13", "Gl Strandvej 13"], query.Prefixes);
    }

    [Fact]
    public void A_capital_after_every_dot_and_hyphen()
    {
        // "H.C. Andersens Boulevard" as the register writes it, kept; "H.C" dropped finds nothing, and does no harm.
        Assert.Equal(["H.C. Andersens Boulevard 5,", "H.C Andersens Boulevard 5,", "H.C. Andersens Boulevard 5", "H.C Andersens Boulevard 5"],
            AddressSearch.Register("h.c. andersens boulevard 5").Prefixes);
        Assert.Equal("Ny-Eksempelvej 2,", AddressSearch.Register("ny-eksempelvej 2").Prefixes[0]);
    }

    [Fact]
    public void Small_words_stay_small_unless_first()
    {
        Assert.Equal("Eksempelvej ved Søen og Skoven 4,", AddressSearch.Register("eksempelvej ved søen og skoven 4").Prefixes[0]);
        Assert.Equal("Ved Eksempelvej 4,", AddressSearch.Register("ved eksempelvej 4").Prefixes[0]);
    }

    [Fact]
    public void A_letter_in_the_number_is_upper_case_and_without_a_number_the_street_alone()
    {
        Assert.Equal(["Eksempelvej 1A,", "Eksempelvej 1A"], AddressSearch.Register("eksempelvej 1a testby").Prefixes);
        var street = AddressSearch.Register("eksempelvej");
        Assert.Equal(["Eksempelvej"], street.Prefixes);
        Assert.Null(street.Number);
        Assert.Empty(AddressSearch.Register("1234 testby").Prefixes);
    }

    // The register's answer for "Eksempelvej 1", as DAR_Husnummer gives it: the number in three towns, a letter, and 10.
    private const string Nodes = $$"""
        [{"id_lokalId":"{{Id1}}","husnummertekst":"1","adgangsadressebetegnelse":"Eksempelvej 1, 5678 Andeby"},
         {"id_lokalId":"{{Id2}}","husnummertekst":"10","adgangsadressebetegnelse":"Eksempelvej 10, 1234 Testby"},
         {"id_lokalId":"{{Id3}}","husnummertekst":"1A","adgangsadressebetegnelse":"Eksempelvej 1A, 9999 Prøveby"},
         {"id_lokalId":"{{Id4}}","husnummertekst":"1","adgangsadressebetegnelse":"Eksempelvej 1, Udkanten, 1234 Testby"}]
        """;

    [Fact]
    public void Register_hits_keep_the_number_and_its_letters_not_10_and_split_at_the_last_comma()
    {
        var hits = AddressSearch.RegisterHits(Nodes, "1");
        Assert.Equal([Id1, Id3, Id4], hits.Select(h => h.Id));
        Assert.Equal(new AddressSearch.Hit("husnummer", Id4, "Eksempelvej 1, Udkanten, 1234 Testby", "Eksempelvej 1, Udkanten", "1234 Testby"), hits[2]);
        Assert.Equal(4, AddressSearch.RegisterHits(Nodes, null).Count);
    }

    [Fact]
    public async Task The_typed_town_wins_without_its_postcode()
    {
        var asked = new List<string>();
        Task<List<JsonElement>> ByPrefix(string prefix)
        {
            asked.Add(prefix);
            return Task.FromResult(JsonDocument.Parse(Nodes).RootElement.EnumerateArray().ToList());
        }
        var found = await AddressSearch.SearchRegister("eksempelvej 1 testby", ByPrefix);
        Assert.Equal(["Eksempelvej 1,", "Eksempelvej 1"], asked);
        // Each address once, Testby's first; 10 left out.
        Assert.Equal(["Eksempelvej 1, Udkanten, 1234 Testby", "Eksempelvej 1, 5678 Andeby", "Eksempelvej 1A, 9999 Prøveby"],
            found.Select(s => s.Text));
        Assert.All(found, s => Assert.NotNull(s.Id));
    }

    [Theory]
    [InlineData(400, "400")]
    [InlineData(401, "401")]
    [InlineData(403, "403")]
    [InlineData(404, "404")]
    public async Task A_refused_token_or_a_service_gone_leaves_Adressevaelger_for_good(int status, string reason)
    {
        var fallback = new AddressFallback();
        var primaryCalls = 0;
        Task<string> Primary()
        {
            primaryCalls++;
            throw new FetchException("Adressevælger", status);
        }
        Assert.Equal("register", await fallback.Run(Primary, () => Task.FromResult("register")));
        Assert.Equal(reason, fallback.Reason);
        // The next search goes straight to the register, without a failed call to Adressevælger first.
        Assert.Equal("register", await fallback.Run(Primary, () => Task.FromResult("register")));
        Assert.Equal(1, primaryCalls);
    }

    [Fact]
    public async Task No_answer_leaves_it_too()
    {
        var fallback = new AddressFallback();
        Assert.Equal("register", await fallback.Run<string>(() => throw new FetchException("Adressevælger", noAnswer: true),
            () => Task.FromResult("register")));
        Assert.Equal("timeout", fallback.Reason);
    }

    [Fact]
    public async Task A_cancel_or_any_other_failure_stays_Adressevaelger_s()
    {
        var fallback = new AddressFallback();
        var registerCalls = 0;
        Task<string> Register()
        {
            registerCalls++;
            return Task.FromResult("register");
        }
        await Assert.ThrowsAsync<OperationCanceledException>(() => fallback.Run<string>(() => throw new OperationCanceledException(), Register));
        await Assert.ThrowsAsync<FetchException>(() => fallback.Run<string>(() => throw new FetchException("unreadable"), Register));
        await Assert.ThrowsAsync<FetchException>(() => fallback.Run<string>(() => throw new FetchException("server error", 500), Register));
        Assert.Null(fallback.Reason);
        Assert.Equal(0, registerCalls);
        Assert.Equal("adressevælger", await fallback.Run(() => Task.FromResult("adressevælger"), Register));
    }

    [Fact]
    public void The_warm_up_s_failure_is_noted()
    {
        var fallback = new AddressFallback();
        fallback.Note(new FetchException("unreadable"));
        Assert.Null(fallback.Reason);
        fallback.Note(new FetchException("Adressevælger", 403));
        fallback.Note(new FetchException("Adressevælger", noAnswer: true));
        Assert.Equal("403", fallback.Reason);
    }

    // The same 20 public addresses as above, for the live comparison.
    private static readonly string[] Public =
    [
        "Skolegade 1, 7100 Vejle", "Willy Sørensens Plads 1, 7100 Vejle", "Gormsgade 23, 7300 Jelling",
        "Brøndumsvej 4, 9990 Skagen", "Rådhuspladsen 2, 8000 Aarhus C", "Munkemøllestræde 3, 5000 Odense C",
        "Flakhaven 2, 5000 Odense C", "Rådhuspladsen 1, 1550 København V", "Bredgade 68, 1260 København K",
        "Stændertorvet 1, 4000 Roskilde", "Domkirkepladsen 3, 4000 Roskilde", "Boulevarden 13, 9000 Aalborg",
        "Torvegade 74, 6700 Esbjerg", "Laksetorvet 1, 8900 Randers C", "Akseltorv 1, 6000 Kolding",
        "Torvet 5, 7400 Herning", "Torvet 15, 6760 Ribe", "Gl Strandvej 13, 3050 Humlebæk", "Ramsherred 5, 5700 Svendborg",
        "Rådhustorvet 10, 6400 Sønderborg",
    ];

    /// <summary>
    /// Live: the 20 public addresses typed three ways through Jega's own search, Adressevælger's (SearchAddress, with
    /// AddressSearch's retries) and the fallback's (SearchRegister: Adressevælger answers any token, so it is run
    /// directly); the place of the right address among the suggestions (0 = not among them) and the time, as a table in
    /// JEGA_LIVE_OUT (default: the temp folder). Each address looked up in both, the same text and point. The loose form
    /// ("skolegade 1 vejle") must give the right address first in the register at least 18 times in 20.
    /// </summary>
    [LiveFact("JEGA_DATAFORDELER_KEY")]
    public async Task Live_the_register_finds_the_address_typed_loosely()
    {
        var fetcher = new SiteFetcher(Environment.GetEnvironmentVariable("JEGA_DATAFORDELER_KEY")!, "");
        var rows = new List<string> { "address\tform\ttyped\tadressevaelger\tregister\tms_adressevaelger\tms_register" };
        var looseFirst = 0;
        foreach (var official in Public)
        {
            var comma = official.IndexOf(',');
            var streetAndNumber = official[..comma];
            var town = official[(comma + 1)..].Trim().Split(' ', 2)[1];
            foreach (var (form, typed) in new[] { ("official", official), ("loose", $"{streetAndNumber} {town}".ToLowerInvariant()), ("street", streetAndNumber) })
            {
                var clock = Stopwatch.StartNew();
                var av = await fetcher.SearchAddress(typed);
                var avMs = clock.ElapsedMilliseconds;
                clock.Restart();
                var register = await fetcher.SearchRegister(typed);
                var registerMs = clock.ElapsedMilliseconds;
                var (avAt, registerAt) = (av.FindIndex(s => s.Text == official) + 1, register.FindIndex(s => s.Text == official) + 1);
                looseFirst += form == "loose" && registerAt == 1 ? 1 : 0;
                rows.Add($"{official}\t{form}\t{typed}\t{avAt}\t{registerAt}\t{avMs}\t{registerMs}");
                await Task.Delay(300);
            }
            var id = (await fetcher.SearchRegister(official)).First(s => s.Text == official).Id!;
            var (avText, avX, avY) = await fetcher.AddressPoint(id);
            var (registerText, registerX, registerY) = await fetcher.RegisterPoint(id);
            Assert.Equal(avText, registerText);
            Assert.True(Math.Abs(avX - registerX) < 0.01 && Math.Abs(avY - registerY) < 0.01, $"{official}: the points differ");
        }
        var outDir = Environment.GetEnvironmentVariable("JEGA_LIVE_OUT") is { Length: > 0 } o ? o : Path.GetTempPath();
        File.WriteAllLines(Path.Combine(outDir, "jega-address-search.tsv"), rows);
        Assert.True(looseFirst >= 18, $"the loose form first in the register {looseFirst} times in 20");
    }
}
