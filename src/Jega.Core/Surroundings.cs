using System.Globalization;
using System.Text.Json;
using Jega.Core.Resources;
using static Jega.Core.Texts;
using Pt = (double X, double Y);

namespace Jega.Core;

/// <summary>
/// What Load from Address fetched around the lot for the site plan: the lots, buildings, road edges and water within
/// $Margin m of the lot (UTM32, m), and the address point. Kept in the model, so the site plan can be drawn again for
/// any area up to $Margin, and with another setback, without fetching again (Settings). Kept before the water: none.
/// $FetchedUtc: when it was fetched, the date the drawing's credit gives; kept before it: none (the caller's, else today).
/// $Roofs: the roofs on the lot (SiteFetcher.Roofs), kept as numbers, as the surface isn't; kept before them: none.
/// $Manholes, $Gullies: GeoDanmark's manhole covers and road gullies, points with their level ("z"); kept before them: none.
/// $Addresses: the house numbers round the lot with their streets (SiteFetcher.Addresses); kept before them: none.
/// $Trees (points), $TreeGroups (tree groups and woods, outlines), $Hedges (lines): GeoDanmark's; kept before them: none.
/// </summary>
public sealed record Surroundings(double X, double Y, double Margin, List<SiteFetcher.Feature> Lots,
    List<SiteFetcher.Feature> Buildings, List<SiteFetcher.Feature> Roads, List<SiteFetcher.Feature>? Water = null,
    DateTime? FetchedUtc = null, List<SiteFetcher.RoofLevels>? Roofs = null,
    List<SiteFetcher.Feature>? Manholes = null, List<SiteFetcher.Feature>? Gullies = null,
    List<SiteFetcher.HouseNumber>? Addresses = null,
    List<SiteFetcher.Feature>? Trees = null, List<SiteFetcher.Feature>? TreeGroups = null, List<SiteFetcher.Feature>? Hedges = null)
{
    private const int Version = 1;

    /// <summary>The lot the address point lies on, or null.</summary>
    public List<Pt>? Lot => Lots.FirstOrDefault(l => Geometry.Inside(l.Rings, X, Y))?.Rings[0];

    /// <summary>The site plan's window: the lot plus $margin (m) on every side; around the address point without a lot.</summary>
    public static (double X1, double Y1, double X2, double Y2) Window(List<Pt>? lot, double x, double y, double margin) =>
        lot is null
            ? (x - margin, y - margin, x + margin, y + margin)
            : (lot.Min(p => p.X) - margin, lot.Min(p => p.Y) - margin, lot.Max(p => p.X) + margin, lot.Max(p => p.Y) + margin);

    /// <summary>
    /// The site plan DXF for $marginM (at most Margin) around the lot, from these and the terrain (the stored CSV's
    /// grid, for the spot elevations; null = none). The same drawing Load from Address makes with that area;
    /// $origin: the project's (ProjectOrigin; UTM32, m), the terrain's and the drawing's 0,0; null = the address point.
    /// $address goes in the title, $language is the drawing's; $hiddenLayers: the layers left out (ProjectOptions.HiddenLayers);
    /// $roofLabels: which roofs get a level (ProjectOptions.RoofLabels). $turnDeg: the drawing turned to the project's
    /// working direction (SiteplanDxf.Write; the project's angle to true north); 0 = true north up. $fitTurnDeg: the
    /// turn the frame must hold the lot at too (SiteplanDxf.Write), so the plan keeps its frame for either view north.
    /// </summary>
    public string SitePlan(TerrainGrid? terrain, string address, CultureInfo language, double marginM, double setbackM,
        IReadOnlyCollection<string>? hiddenLayers = null, SiteplanDxf.RoofLabels? roofLabels = null, (double E, double N)? origin = null,
        double turnDeg = 0, double fitTurnDeg = 0)
    {
        if (marginM > Margin + 1e-9)
        {
            throw new ArgumentOutOfRangeException(nameof(marginM), $"The surroundings reach {Margin} m around the lot, not {marginM} m.");
        }
        var (e0, n0) = origin ?? (X, Y);
        var dtm = terrain is null ? null : SiteFetcher.FromGrid(terrain, e0, n0);
        return SiteplanDxf.Write(Lots, Buildings, Roads, dtm, Window(Lot, X, Y, marginM), X, Y, address, language, setbackM, Water, FetchedUtc, Roofs, hiddenLayers, roofLabels, origin,
            Manholes, Gullies, Addresses, Trees, TreeGroups, Hedges, turnDeg, fitTurnDeg);
    }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        version = Version,
        x = X,
        y = Y,
        margin = Margin,
        lots = Lots.Select(Dto),
        buildings = Buildings.Select(Dto),
        roads = Roads.Select(Dto),
        water = (Water ?? []).Select(Dto),
        fetchedUtc = FetchedUtc?.ToString("o", CultureInfo.InvariantCulture),
        roofs = (Roofs ?? []).Select(r => new { x = Math.Round(r.X, 3), y = Math.Round(r.Y, 3), ridge = Math.Round(r.Ridge, 3), eave = r.Eave is { } e ? Math.Round(e, 3) : (double?)null }),
        manholes = (Manholes ?? []).Select(Dto),
        gullies = (Gullies ?? []).Select(Dto),
        addresses = (Addresses ?? []).Select(a => new
        {
            number = a.Number, street = a.Street, x = Math.Round(a.X, 3), y = Math.Round(a.Y, 3), building = a.BuildingId,
            line = a.StreetLine.Select(l => l.Select(p => new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3) })),
        }),
        trees = (Trees ?? []).Select(Dto),
        treeGroups = (TreeGroups ?? []).Select(Dto),
        hedges = (Hedges ?? []).Select(Dto),
        writtenBy = Build.Version,
    });

    /// <summary>The stored text; throws FormatException if it isn't one (from another build, or damaged).</summary>
    public static Surroundings Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != Version)
            {
                throw new FormatException(Strings.Surroundings_OtherVersion);
            }
            List<SiteFetcher.Feature> Features(string name) => root.GetProperty(name).EnumerateArray().Select(f => new SiteFetcher.Feature(
                f.GetProperty("rings").EnumerateArray().Select(r => r.EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList()).ToList(),
                f.GetProperty("props").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""))).ToList();
            // When it was fetched: added within version 1, so missing (kept before) is none.
            DateTime? fetched = root.TryGetProperty("fetchedUtc", out var f) && f.ValueKind == JsonValueKind.String
                && DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
            // The roofs: added within version 1 too, so missing (kept before) is none.
            var roofs = root.TryGetProperty("roofs", out var r) ? r.EnumerateArray().Select(o => new SiteFetcher.RoofLevels(
                o.GetProperty("x").GetDouble(), o.GetProperty("y").GetDouble(), o.GetProperty("ridge").GetDouble(),
                o.GetProperty("eave") is { ValueKind: JsonValueKind.Number } e ? e.GetDouble() : null)).ToList() : null;
            // Optional layers: missing (kept before they were, or not fetched) is none.
            List<SiteFetcher.Feature>? Kept(string name) => root.TryGetProperty(name, out _) ? Features(name) : null;
            var addresses = root.TryGetProperty("addresses", out var ad) ? ad.EnumerateArray().Select(a => new SiteFetcher.HouseNumber(
                a.GetProperty("number").GetString() ?? "", a.GetProperty("street").GetString() ?? "",
                a.GetProperty("x").GetDouble(), a.GetProperty("y").GetDouble(),
                a.TryGetProperty("building", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null,
                a.GetProperty("line").EnumerateArray().Select(l => l.EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList()).ToList())).ToList() : null;
            return new Surroundings(root.GetProperty("x").GetDouble(), root.GetProperty("y").GetDouble(), root.GetProperty("margin").GetDouble(),
                Features("lots"), Features("buildings"), Features("roads"), Kept("water"), fetched, roofs, Kept("manholes"), Kept("gullies"), addresses,
                Kept("trees"), Kept("treeGroups"), Kept("hedges"));
        }
        // IndexOutOfRange: a point of one number.
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new FormatException(Fill(Strings.Surroundings_Unreadable, e.Message));
        }
    }

    private static object Dto(SiteFetcher.Feature f) => new
    {
        rings = f.Rings.Select(r => r.Select(p => new[] { Math.Round(p.X, 3), Math.Round(p.Y, 3) })),
        props = f.Props,
    };
}
