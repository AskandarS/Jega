using System.Text.Json;
using Jega.Core;

namespace Jega.Core.Tests;

/// <summary>The address field's suggestions (AddressSearch) on made-up hits.</summary>
public class AddressSearchTests
{
    private const string Id1 = "0a3f50a0-0000-32b8-e044-0003ba298018";
    private const string Id2 = "0a3f50a0-0000-32b8-e044-0003ba298019";

    private static AddressSearch.Hit Address(string id, string title) => new("husnummer", id, title, title, "");

    [Fact]
    public void Answer_gives_hits_with_street_and_town()
    {
        var json = JsonDocument.Parse("""
            {"status":"ok","fund":[
              {"type":"husnummer","id":"a","titel":"Brøndumsvej 4, 9990 Skagen"},
              {"type":"navngivenvejpostnummer","titel":"Eksempelvej","vejnavn":"Eksempelvej","postnr":1234,"postdistrikt":"Testby"}]}
            """).RootElement;
        var hits = AddressSearch.Hits(json);
        Assert.Equal(new AddressSearch.Hit("husnummer", "a", "Brøndumsvej 4, 9990 Skagen", "Brøndumsvej 4, 9990 Skagen", ""), hits[0]);
        Assert.Equal(("Eksempelvej", "1234 Testby"), (hits[1].Street, hits[1].Town));
    }

    [Fact]
    public void Best_match_first_service_order_breaks_ties()
    {
        var hits = new List<AddressSearch.Hit>
        {
            Address(Id1, "Eksempelvej 45, 5678 Andeby"),
            Address(Id2, "Eksempelvej 45, 1234 Testby"),
            Address("not-an-id", "Eksempelvej 45, 1234 Testby"),
        };
        var picked = AddressSearch.Pick(AddressSearch.Words("eksempelvej 45 testby"), hits, 8);
        Assert.Equal(["Eksempelvej 45, 1234 Testby", "Eksempelvej 45, 5678 Andeby"], picked.Select(s => s.Text));
    }

    [Fact]
    public void Street_fills_in_and_waits_for_the_number()
    {
        var hits = new List<AddressSearch.Hit> { new("navngivenvejpostnummer", "", "Eksempelvej", "Eksempelvej", "1234 Testby") };
        var s = Assert.Single(AddressSearch.Pick(["eksempelvej"], hits, 8));
        Assert.Equal(new AddressSearch.Suggestion("Eksempelvej, 1234 Testby", null, "Eksempelvej , 1234 Testby", 12), s);
    }

    [Fact]
    public void Addresses_before_streets()
    {
        var hits = new List<AddressSearch.Hit>
        {
            new("navngivenvejpostnummer", "", "Brøndumsvej", "Brøndumsvej", "9990 Skagen"),
            Address(Id1, "Brøndumsvej 4, 9990 Skagen"),
        };
        Assert.Equal(Id1, Assert.Single(AddressSearch.Pick(["brøndumsvej"], hits, 8)).Id);
    }

    [Fact]
    public async Task No_hit_on_the_street_asks_again_up_to_the_house_number()
    {
        var asked = new List<string>();
        Task<List<AddressSearch.Hit>> Api(string q)
        {
            asked.Add(q);
            return Task.FromResult(q == "eksempelvej 45"
                ? new List<AddressSearch.Hit> { Address(Id1, "Eksempelvej 45, 1234 Testby") }
                : [Address(Id2, "Eksempelstien 4, 1234 Testby")]);  // the service's phonetic guess
        }
        var found = await AddressSearch.Search("Eksempelvej 45 Testby", Api);
        Assert.Equal(["Eksempelvej 45 Testby", "eksempelvej 45"], asked);
        Assert.Equal(Id1, Assert.Single(found).Id);
    }

    [Fact]
    public async Task A_number_the_street_lacks_asks_with_the_street_and_postcode_first()
    {
        var asked = new List<string>();
        Task<List<AddressSearch.Hit>> Api(string q)
        {
            asked.Add(q);
            return Task.FromResult(q switch
            {
                // The street's numbers in that town. Score gives each the same (the "1" typed starts "1A", "10" and
                // the postcode 1234 alike), so the service's order is what puts 1A first.
                "eksempelvej 1234" => new List<AddressSearch.Hit>
                {
                    Address(Id1, "Eksempelvej 1A, 1234 Testby"),
                    Address(Id2, "Eksempelvej 2, 1234 Testby"),
                    Address(Id2, "Eksempelvej 10, 1234 Testby"),
                },
                "eksempelvej 1" => [Address(Id2, "Eksempelvej 1, 5678 Andeby")],
                _ => [],
            });
        }
        var found = await AddressSearch.Search("Eksempelvej 1, 1234 Testby", Api);
        Assert.Equal(["Eksempelvej 1, 1234 Testby", "eksempelvej 1234"], asked);
        Assert.Equal(["Eksempelvej 1A, 1234 Testby", "Eksempelvej 2, 1234 Testby", "Eksempelvej 10, 1234 Testby"], found.Select(s => s.Text));
    }

    [Fact]
    public async Task Street_not_in_that_town_asks_up_to_the_house_number_after()
    {
        var asked = new List<string>();
        var found = await AddressSearch.Search("Eksempelvej 1, 1234 Testby", q =>
        {
            asked.Add(q);
            return Task.FromResult(q == "eksempelvej 1" ? new List<AddressSearch.Hit> { Address(Id1, "Eksempelvej 1, 5678 Andeby") } : []);
        });
        Assert.Equal(["Eksempelvej 1, 1234 Testby", "eksempelvej 1234", "eksempelvej 1"], asked);
        Assert.Equal(Id1, Assert.Single(found).Id);
    }

    [Fact]
    public async Task On_the_street_asks_once()
    {
        var asked = 0;
        var found = await AddressSearch.Search("Brøndumsvej 4 Skagen", _ =>
        {
            asked++;
            return Task.FromResult(new List<AddressSearch.Hit> { Address(Id1, "Brøndumsvej 4, 9990 Skagen") });
        });
        Assert.Equal(1, asked);
        Assert.Single(found);
    }
}
