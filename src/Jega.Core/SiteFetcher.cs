using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Jega.Core.Resources;
using static Jega.Core.Texts;
using Pt = (double X, double Y);

namespace Jega.Core;

/// <summary>
/// Fetches what Load from Address needs straight from the public services, with the user's own keys (Settings,
/// This PC): the address point (Adressevælger), the lot and what's around it (Datafordeler WFS: Matriklen,
/// GeoDanmark) and the terrain (Datafordeler WCS: Danmarks Højdemodel). Gives the terrain CSV (TerrainGrid) and the
/// site plan DXF (SiteplanDxf), so the rest of Jega reads them as a terrain of the user's own. All coordinates ETRS89/UTM32 (m).
/// </summary>
public sealed class SiteFetcher(string datafordelerKey, string addressToken)
{
    /// <summary>The credit the open data's terms ask for, wherever Jega shows what it fetched (DATA.md).</summary>
    public static string Credit => Strings.Fetch_Credit;

    /// <summary>Adressevælger's documented example token, which works while its user management isn't live.</summary>
    public const string DefaultAddressToken = "adressevaelger123";

    private const string AddressUrl = "https://adressevaelger.dk";
    private const string WfsUrl = "https://wfs.datafordeler.dk/";
    private const string DhmUrl = "https://wcs.datafordeler.dk/DHMNEDBOER/dhm_wcs/1.0.0/WCS";
    private const double Cell = HeightCells.Size;
    private static int MaxPx => Config.Current.Fetch.MaxCellsPerCall;

    // How much is fetched round the lot, the grids offered, the calls at a time and their size: Config.Fetch.

    /// <summary>The most terrain points Jega fetches (Config.Fetch.MaxPoints).</summary>
    public static long MaxPoints => Config.Current.Fetch.MaxPoints;

    /// <summary>The grid spacings offered for a large lot (m): the model's own 0.4 m, then coarser ones the service resamples to.</summary>
    public static double[] Cells => Config.Current.Fetch.Cells;

    /// <summary>The terrain grid for a lot at one spacing: its size in cells and how many calls fetch it.</summary>
    public sealed record TerrainPlan(double Cell, int W, int H)
    {
        public long Points => (long)W * H;

        public int Parts => (int)Math.Ceiling(W / (double)MaxPx) * (int)Math.Ceiling(H / (double)MaxPx);

        public bool Fits => Points <= MaxPoints;
    }

    /// <summary>The terrain grid Fetch would make for the lot (UTM32, m) at $cell: the lot plus 5 m, on whole cells.</summary>
    public static TerrainPlan PlanTerrain(List<Pt> lot, double cell) => Plan(lot, Config.Current.Fetch.TerrainMarginM, cell);

    /// <summary>
    /// How far round the lot (m) Fetch takes the height model at $cell: Config's ContextMarginM, so the context
    /// buildings come from the same calls, less in steps of 10 m while it would take more parts than the terrain alone
    /// (more calls only for a large lot, and only as many as its terrain needs) or more than MaxPoints; never less than
    /// the terrain's own margin, so the terrain is never coarser for it.
    /// </summary>
    public static double HeightMargin(List<Pt> lot, double cell)
    {
        var fetch = Config.Current.Fetch;
        var terrain = PlanTerrain(lot, cell);
        for (var margin = fetch.ContextMarginM; margin > fetch.TerrainMarginM; margin -= 10)
        {
            if (Plan(lot, margin, cell) is { Fits: true } plan && plan.Parts <= terrain.Parts)
            {
                return margin;
            }
        }
        return fetch.TerrainMarginM;
    }

    /// <summary>The grid Fetch takes the terrain and the surface on, each (the lot plus HeightMargin at $cell): as many parts as the terrain's own.</summary>
    public static TerrainPlan PlanHeights(List<Pt> lot, double cell) => Plan(lot, HeightMargin(lot, cell), cell);

    /// <summary>The cell the context buildings' heights are fetched at (FetchContext): the finest whose lot plus
    /// ContextMarginM stays within MaxPoints; 0 if none (over about 2 km).</summary>
    public static double ContextCell(List<Pt> lot) =>
        Cells.FirstOrDefault(c => Plan(lot, Config.Current.Fetch.ContextMarginM, c).Fits);

    /// <summary>
    /// Whether Fetch's own grids give the context buildings as FetchContext would: the whole ContextMarginM round the
    /// lot, at its cell. Else (a large lot, or a coarser grid chosen) Fetch keeps none, and Context Buildings fetches the
    /// whole ring when asked, rather than showing a smaller or coarser one without a word.
    /// </summary>
    public static bool ContextFromFetch(List<Pt> lot, double cell) =>
        HeightMargin(lot, cell) == Config.Current.Fetch.ContextMarginM && cell == ContextCell(lot);

    /// <summary>The grid over the lot plus $margin (m) at $cell, on whole cells.</summary>
    public static TerrainPlan Plan(List<Pt> lot, double margin, double cell)
    {
        var box = HeightCells.Snap(lot.Min(p => p.X), lot.Min(p => p.Y), lot.Max(p => p.X), lot.Max(p => p.Y), margin, cell);
        return new TerrainPlan(cell, HeightCells.Count(box.X2 - box.X1, cell), HeightCells.Count(box.Y2 - box.Y1, cell));
    }

    /// <summary>The lot (cadastral parcel) the address point lies on, its outline in UTM32 m.</summary>
    public async Task<List<Pt>> Lot(double x, double y, CancellationToken ct = default)
    {
        if (_key.Length == 0)
        {
            throw new FetchException(Strings.Fetch_NoKey);
        }
        var near = await Wfs("lots", (x - 2, y - 2, x + 2, y + 2), ct);
        return near.FirstOrDefault(f => Geometry.Inside(f.Rings, x, y))?.Rings[0]
               ?? throw new FetchException(Strings.Fetch_NoLot);
    }

    private static readonly Dictionary<string, (string Service, string Type)> Layers = new()
    {
        ["lots"] = ("MATRIKLEN2/MatGaeldendeOgForeloebigWFS", "mat:Jordstykke_Gaeldende"),
        ["buildings"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "gdk60:Bygning"),
        ["roads"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "gdk60:Vejkant"),
        // Every kind of water in one call (WFS 2.0, one query per type): all of it is drawn as VAND.
        ["water"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3",
            "(gdk60:Soe)(gdk60:Bassin)(gdk60:Vandloebsmidte)(gdk60:Vandloebskant)(gdk60:Afvandingsgroeft)(gdk60:Vaadomraade)(gdk60:Kyst)"),
        // Points with a level (3D), seen from the air: a cover of any kind (what is under it GeoDanmark doesn't say) and
        // the road gullies.
        ["manholes"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "gdk60:Broenddaeksel"),
        ["gullies"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "gdk60:Nedloebsrist"),
        // The trees seen from the air (points, no species or size), the tree groups and woods (outlines), the hedges (lines).
        ["trees"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "gdk60:Trae"),
        ["treeGroups"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "(gdk60:Traegruppe)(gdk60:Skov)"),
        ["hedges"] = ("GEODANMARKVEKTOR/GeoDanmark60_NOHIST_GML3", "gdk60:Hegn"),
    };

    // Behind an office's proxy that asks for a login, the Windows user's own, as the browser does. The services see
    // who calls them (User-Agent), so they can tell Jega's calls from unknown ones.
    private static readonly HttpClient Http = NewHttp();

    private static HttpClient NewHttp()
    {
        var http = new HttpClient(new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd(Build.Version.Length > 0 ? $"Jega/{Build.Version}" : "Jega");
        return http;
    }
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly string _key = datafordelerKey.Trim();
    private readonly string _token = addressToken.Trim() is { Length: > 0 } t ? t : DefaultAddressToken;

    // Adressevælger until it refuses its token, is gone or doesn't answer; then Danmarks Adresseregister on Datafordeleren's
    // GraphQL, with the key, for the rest of this fetcher's life (AddressFallback).
    private readonly AddressFallback _fallback = new();

    /// <summary>Said once the address field has left Adressevælger (AddressFallback): in the address window and in the
    /// receipt after Load from Address; null while Adressevælger answers.</summary>
    public string? AddressFallbackNote => _fallback.Reason is { } why ? Fill(Strings.Fetch_AddressFallback, FallbackWhy(why)) : null;

    // AddressFallback.Reason in words, with the HTTP status first: "400, unknown token"; no answer has none.
    private static string FallbackWhy(string reason) => reason switch
    {
        "400" => $"{reason}, {Strings.Fetch_FallbackWhyUnknownToken}",
        "401" or "403" => $"{reason}, {Strings.Fetch_FallbackWhyRefused}",
        "404" => $"{reason}, {Strings.Fetch_FallbackWhyGone}",
        _ => Strings.Fetch_FallbackWhyNoAnswer,
    };

    /// <summary>The address field's suggestions for the text (AddressSearch): addresses ("Skolegade 1, 7100 Vejle",
    /// with Adressevælger's id), or streets while no house number is typed. In the fallback, addresses only, from the
    /// register (AddressSearch.SearchRegister); their ids are the same.</summary>
    public Task<List<AddressSearch.Suggestion>> SearchAddress(string text, CancellationToken ct = default) =>
        _fallback.Run(
            () => AddressSearch.Search(text, async q =>
            {
                var answer = await GetJson($"{AddressUrl}/husnumre/soeg?tekst={Esc(q)}&maksimum={AddressSearch.Max}&token={Esc(_token)}", Strings.Fetch_AddressSearch, ct);
                return Parse(Strings.Fetch_AddressSearch, () => AddressSearch.Hits(answer));
            }),
            () => SearchRegister(text, ct));

    /// <summary>The fallback's search on its own: the register's current house numbers for the text
    /// (AddressSearch.SearchRegister). SearchAddress uses it once Adressevælger is left; the live tests run it directly,
    /// since Adressevælger answers any token.</summary>
    public Task<List<AddressSearch.Suggestion>> SearchRegister(string text, CancellationToken ct = default) =>
        AddressSearch.SearchRegister(text, prefix => AllNodes("DAR_Husnummer",
            $"where: {{ adgangsadressebetegnelse: {{ startsWith: {JsonSerializer.Serialize(prefix)} }}, status: {{ eq: \"3\" }} }}, {Now()}",
            "id_lokalId husnummertekst adgangsadressebetegnelse", Strings.Fetch_AddressSearch, ct, only: 100));

    /// <summary>
    /// One tiny search, its answer thrown away: the first call in Revit is slow (loading and compiling the HTTP and
    /// JSON code, DNS, TLS, proxy detection), so the window makes it on opening, before the first real search.
    /// </summary>
    public async Task WarmUp()
    {
        try
        {
            await GetJson($"{AddressUrl}/husnumre/soeg?tekst=a&maksimum=1&token={Esc(_token)}", Strings.Fetch_AddressSearch, default);
        }
        catch (FetchException e)
        {
            // A refused token or no answer here already: the first search goes straight to the fallback.
            _fallback.Note(e);
        }
    }

    /// <summary>The address's official text and its point (the DXF's and CSV's 0,0). In the fallback, from the register:
    /// the house number's text and its address point, then the point's position.</summary>
    public Task<(string Text, double X, double Y)> AddressPoint(string id, CancellationToken ct = default) =>
        _fallback.Run(async () =>
        {
            var json = await GetJson($"{AddressUrl}/husnumre/{Esc(id)}?token={Esc(_token)}", Strings.Fetch_AddressLookup, ct);
            return Parse(Strings.Fetch_AddressLookup, () =>
            {
                var h = json.GetProperty("husnummer");
                var c = h.GetProperty("adgangspunkt").GetProperty("koordinater");
                return (h.GetProperty("adgangsadressebetegnelse").GetString() ?? "", c.GetProperty("x").GetDouble(), c.GetProperty("y").GetDouble());
            });
        }, () => RegisterPoint(id, ct));

    /// <summary>The fallback's lookup on its own: a chosen address in the register (Adressevælger's ids are the
    /// register's). AddressPoint uses it once Adressevælger is left; the live tests run it directly.</summary>
    public async Task<(string Text, double X, double Y)> RegisterPoint(string id, CancellationToken ct = default)
    {
        var what = Strings.Fetch_AddressLookup;
        var numbers = await AllNodes("DAR_Husnummer", $"where: {{ id_lokalId: {{ eq: {JsonSerializer.Serialize(id)} }} }}, {Now()}",
            "adgangsadressebetegnelse adgangspunkt", what, ct);
        var (text, pointId) = Parse(what, () => numbers is [var n, ..]
            ? (n.GetProperty("adgangsadressebetegnelse").GetString() ?? "", n.GetProperty("adgangspunkt").GetString() ?? "")
            : throw new FetchException(Fill(Strings.Fetch_Unreadable, what, "no such house number")));
        var points = await AllNodes("DAR_Adressepunkt", $"where: {{ id_lokalId: {{ eq: {JsonSerializer.Serialize(pointId)} }} }}, {Now()}",
            "position { wkt }", what, ct);
        return Parse(what, () => points is [var p, ..] && WktLines(p.GetProperty("position").GetProperty("wkt").GetString() ?? "") is [[var (x, y), ..], ..]
            ? (text, x, y)
            : throw new FetchException(Fill(Strings.Fetch_Unreadable, what, "no address point")));
    }

    /// <summary>What Fetch gives: the terrain CSV, the site plan DXF, the highest terrain along the house on the
    /// lot (mm DVR90; null without a building on it; suggested for ±0.00), and the lot's outline (mm from the project's
    /// origin or the address point, true north, as the CSV), kept in the model so the lot is known without the DXF import; and the
    /// surroundings fetched for the site plan (Surroundings.ToJson), kept too, to draw it again for another area; and the
    /// context buildings (ContextJson), from the same calls, for Context Buildings; null when the surface couldn't be had
    /// ($NoSurface: then no roofs either, which the receipt says). $NoRoads, $NoWater, $NoLots (the lots round it),
    /// $NoBuildings (then no roofs or context either), $NoManholes (the manhole covers or the gullies), $NoAddresses
    /// (the street names and house numbers), $NoVegetation (the trees, tree groups or hedges): that layer couldn't be
    /// fetched, the site plan is without it (the receipt says so too). $SkippedLayers: the layers whose drawing failed
    /// (SiteplanLayers names), left out of the site plan; the receipt says so too.</summary>
    public sealed record Site(string Csv, string Dxf, double? HouseGroundMm, List<Pt> LotMm, string Surroundings, string? Context,
        bool NoSurface = false, bool NoRoads = false, bool NoWater = false, bool NoLots = false, bool NoBuildings = false,
        bool NoManholes = false, bool NoAddresses = false, bool NoVegetation = false, IReadOnlyList<string>? SkippedLayers = null);

    /// <summary>
    /// A house number round the lot from Danmarks Adresseregister: its text ("12A"), its street, its address point
    /// (UTM32, m), the GeoDanmark building it belongs to (its `id.lokalId`; null = none registered) and the street's
    /// name line (the register's own, where the name is meant to stand; UTM32, m; empty = none).
    /// </summary>
    public sealed record HouseNumber(string Number, string Street, double X, double Y, string? BuildingId, List<List<Pt>> StreetLine);

    // Danmarks Adresseregister on Datafordeleren's GraphQL: the same key as the WFS and WCS, as a query parameter;
    // every query names the time it asks for (now). Three queries: the address points in the box (page by page), the
    // house numbers on those points, the streets of those numbers with their name lines. (Datafordeleren's REST,
    // open without a key, is retired on 15 January 2027.)
    private const string DarUrl = "https://graphql.datafordeler.dk/DAR/v3";

    // Datafordeleren's GraphQL refuses an "in" list of more than 100 ids (DAF-GQL-0016); seen live 25 Oct 2026.
    private const int InListMax = 100;

    /// <summary>The current house numbers (status 3) in the box (UTM32, m), with their streets.</summary>
    public async Task<List<HouseNumber>> Addresses((double X1, double Y1, double X2, double Y2) box, CancellationToken ct = default)
    {
        var what = Strings.Fetch_Addresses;
        var at = Now();
        var polygon = string.Create(Inv, $"POLYGON(({box.X1:F2} {box.Y1:F2}, {box.X2:F2} {box.Y1:F2}, {box.X2:F2} {box.Y2:F2}, {box.X1:F2} {box.Y2:F2}, {box.X1:F2} {box.Y1:F2}))");
        // 1. The address points in the box; 2. the house numbers on those points, 100 points a call (the service's
        // limit for an "in" list); 3. the streets of those numbers, with the name lines. Each page by page.
        var points = await AllNodes("DAR_Adressepunkt", $"where: {{ position: {{ within: {{ wkt: \"{polygon}\", crs: 25832 }} }} }}, {at}",
            "id_lokalId position { wkt }", what, ct);
        var numbers = new List<JsonElement>();
        foreach (var ids in Ids(points, "id_lokalId", what).Chunk(InListMax))
        {
            numbers.AddRange(await AllNodes("DAR_Husnummer", $"where: {{ adgangspunkt: {{ in: {JsonSerializer.Serialize(ids)} }}, status: {{ eq: \"3\" }} }}, {at}",
                "husnummertekst adgangspunkt navngivenVej geoDanmarkBygning", what, ct));
        }
        var streets = new List<JsonElement>();
        foreach (var ids in Ids(numbers, "navngivenVej", what).Chunk(InListMax))
        {
            streets.AddRange(await AllNodes("DAR_NavngivenVej", $"where: {{ id_lokalId: {{ in: {JsonSerializer.Serialize(ids)} }} }}, {at}",
                "id_lokalId vejnavn vejnavnebeliggenhed_vejnavnelinje { wkt }", what, ct));
        }
        return Parse(what, () => ParseAddresses(points, numbers, streets));
    }

    // The time every DAR query asks for: now, as both the time in force and the time registered.
    private static string Now()
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv);
        return $"virkningstid: \"{now}\", registreringstid: \"{now}\"";
    }

    // All nodes of one DAR connection, 1000 a page (first/after with the answer's pageInfo); $only: just the first that
    // many, one page (the address field's fallback). An answer without the connection, a page that says there is more
    // but gives no cursor, or a cursor that doesn't move are all FetchExceptions, so the layer is left out instead of the
    // whole fetch failing or the plan quietly missing numbers.
    private async Task<List<JsonElement>> AllNodes(string connection, string arguments, string fields, string what, CancellationToken ct,
        int? only = null)
    {
        var nodes = new List<JsonElement>();
        string? after = null;
        do
        {
            var page = after is null ? "" : $", after: {JsonSerializer.Serialize(after)}";
            var data = await GraphQl($"{{ {connection}({arguments}, first: {only ?? 1000}{page}) {{ pageInfo {{ hasNextPage endCursor }} nodes {{ {fields} }} }} }}", what, ct);
            var (pageNodes, next) = Parse(what, () =>
            {
                var c = data.GetProperty(connection);
                var info = c.GetProperty("pageInfo");
                var cursor = info.GetProperty("hasNextPage").GetBoolean()
                    ? info.GetProperty("endCursor").GetString() ?? throw new FetchException(Fill(Strings.Fetch_Unreadable, what, "hasNextPage without endCursor"))
                    : null;
                return (c.GetProperty("nodes").EnumerateArray().ToList(), cursor);
            });
            if (next is not null && next == after)
            {
                throw new FetchException(Fill(Strings.Fetch_Unreadable, what, "the paging doesn't advance"));
            }
            nodes.AddRange(pageNodes);
            after = only is null ? next : null;
        } while (after is not null);
        return nodes;
    }

    // The distinct, non-empty values of one string field of the nodes (an id list for an "in" filter).
    private static List<string> Ids(IEnumerable<JsonElement> nodes, string field, string what) => Parse(what, () =>
        nodes.Select(n => n.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
            .OfType<string>().Where(v => v.Length > 0).Distinct().ToList());

    // One GraphQL query to DAR: the "data" of the answer; errors in the answer are a FetchException.
    private async Task<JsonElement> GraphQl(string query, string what, CancellationToken ct)
    {
        var body = await Get($"{DarUrl}?apiKey={Esc(_key)}", what, ct, JsonSerializer.Serialize(new { query }));
        return Parse(what, () =>
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                throw new FetchException(Fill(Strings.Fetch_Unreadable, what, errors[0].GetProperty("message").GetString() ?? "GraphQL error"));
            }
            return root.GetProperty("data");
        });
    }

    /// <summary>The three GraphQL answers' nodes → house numbers: a point (id_lokalId, position.wkt), a number
    /// (husnummertekst, adgangspunkt, navngivenVej, geoDanmarkBygning), a street (id_lokalId, vejnavn,
    /// vejnavnebeliggenhed_vejnavnelinje.wkt). A number whose point isn't among the points is left out; one whose
    /// street isn't among the streets gets no name.</summary>
    public static List<HouseNumber> ParseAddresses(IEnumerable<JsonElement> points, IEnumerable<JsonElement> numbers, IEnumerable<JsonElement> streets)
    {
        var at = points.Where(p => p.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object)
            .Select(p => (Id: p.GetProperty("id_lokalId").GetString() ?? "", Lines: WktLines(p.GetProperty("position").GetProperty("wkt").GetString() ?? "")))
            .Where(p => p.Lines is [[_, ..], ..]).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Lines[0][0]);
        var named = streets.Select(s => (Id: s.GetProperty("id_lokalId").GetString() ?? "", Name: s.GetProperty("vejnavn").GetString() ?? "",
                Line: s.TryGetProperty("vejnavnebeliggenhed_vejnavnelinje", out var l) && l.ValueKind == JsonValueKind.Object
                    ? WktLines(l.GetProperty("wkt").GetString() ?? "") : []))
            .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => (g.First().Name, g.First().Line));
        var result = new List<HouseNumber>();
        foreach (var h in numbers)
        {
            if (!h.TryGetProperty("adgangspunkt", out var ap) || ap.GetString() is not { } pointId || !at.TryGetValue(pointId, out var point))
            {
                continue;
            }
            var (name, line) = h.TryGetProperty("navngivenVej", out var v) && v.GetString() is { } streetId && named.TryGetValue(streetId, out var s)
                ? s : ("", new List<List<Pt>>());
            var building = h.TryGetProperty("geoDanmarkBygning", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            result.Add(new HouseNumber(h.GetProperty("husnummertekst").GetString() ?? "", name, point.X, point.Y,
                string.IsNullOrEmpty(building) ? null : building, line));
        }
        return result;
    }

    /// <summary>ParseAddresses from three JSON arrays of nodes, as the tests give them.</summary>
    public static List<HouseNumber> ParseAddresses(string pointNodes, string numberNodes, string streetNodes)
    {
        using var p = JsonDocument.Parse(pointNodes);
        using var n = JsonDocument.Parse(numberNodes);
        using var s = JsonDocument.Parse(streetNodes);
        return ParseAddresses(p.RootElement.EnumerateArray().ToList(), n.RootElement.EnumerateArray().ToList(), s.RootElement.EnumerateArray().ToList());
    }

    /// <summary>The point lists of a WKT POINT, LINESTRING or MULTILINESTRING (UTM32, m): each innermost parenthesis
    /// one list.</summary>
    public static List<List<Pt>> WktLines(string wkt) =>
        System.Text.RegularExpressions.Regex.Matches(wkt, @"\(([^()]+)\)").Select(m => m.Groups[1].Value.Split(',')
            .Select(p => p.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.Length >= 2).Select(c => (Pt)(double.Parse(c[0], Inv), double.Parse(c[1], Inv))).ToList()).ToList();

    /// <summary>
    /// The terrain CSV and the site plan DXF for the address point ($x, $y); the DXF shows the lot plus
    /// $siteplanMargin (m) on every side, and its frame, legend and notes follow that window; $setbackM: the dashed
    /// line inside the lot (0 = none); both null = Config's. $progress hears each step ("Lot …"), for the window. $lot:
    /// the lot if already fetched (Lot, to ask about a large one first); $cell: the terrain grid's spacing (m), 0.4 = the
    /// model's own. The CSV, the lot and the context are from $origin, the project's (ProjectOrigin; UTM32, m), also the DXF's 0,0; null = the address point.
    /// $address goes in the DXF's title; its language is the one spoken (Language). $hiddenLayers: the DXF's layers left
    /// out (ProjectOptions.HiddenLayers), $roofLabels which roofs get a level (ProjectOptions.RoofLabels); the
    /// surroundings are kept whole.
    /// </summary>
    public async Task<Site> Fetch(double x, double y, string address, double? siteplanMarginM = null,
        double? setbackM = null, IProgress<string>? progress = null, CancellationToken ct = default,
        List<Pt>? lot = null, double cell = Cell, IReadOnlyCollection<string>? hiddenLayers = null,
        SiteplanDxf.RoofLabels? roofLabels = null, (double E, double N)? origin = null)
    {
        var siteplanMargin = siteplanMarginM ?? Config.Current.Siteplan.MarginM;
        if (_key.Length == 0)
        {
            throw new FetchException(Strings.Fetch_NoKey);
        }
        // The lot the address point lies on.
        if (lot is null)
        {
            progress?.Report(Strings.Fetch_StepLot);
            lot = await Lot(x, y, ct);
        }
        var plan = PlanTerrain(lot, cell);
        if (!plan.Fits)
        {
            throw new FetchException(Fill(Strings.Fetch_LotTooLarge, cell, plan.Points, MaxPoints));
        }
        var (minX, minY, maxX, maxY) = (lot.Min(p => p.X), lot.Min(p => p.Y), lot.Max(p => p.X), lot.Max(p => p.Y));

        // Its surroundings for the drawing, and the height model round it once for everything: the terrain and the
        // surface on the same cells, over the context's box (HeightMargin; snapped outwards to whole cells, so at 0.4 m
        // the values are the model's, not resampled), fetched in parallel. The terrain CSV is the lot plus 5 m cut from it;
        // the roofs on the lot and the context buildings come from both.
        var heightMargin = HeightMargin(lot, cell);
        var heights = PlanHeights(lot, cell);
        progress?.Report(heights.Parts > 1 ? Fill(Strings.Fetch_StepSurroundingsParts, heights.Parts) : Strings.Fetch_StepSurroundings);
        // The surroundings as far as Settings can ask without fetching again (or further, if it already asks for more);
        // never less than the context's box, whose buildings come from here too.
        var reach = Math.Max(Math.Max(Config.Current.Fetch.SurroundingsMarginM, Config.Current.Fetch.ContextMarginM), siteplanMargin);
        var fetched = Core.Surroundings.Window(lot, x, y, reach);
        // Only the lot (above) and the terrain are needed; every layer round them is a help, the load goes on without it.
        var lots = Optional(Wfs("lots", fetched, ct));
        var buildings = Optional(Wfs("buildings", fetched, ct));
        var roads = Optional(Wfs("roads", fetched, ct));
        var water = Optional(Wfs("water", fetched, ct));
        var manholes = Optional(Wfs("manholes", fetched, ct));
        var gullies = Optional(Wfs("gullies", fetched, ct));
        var addresses = Optional(Addresses(fetched, ct));
        var trees = Optional(Wfs("trees", fetched, ct));
        var treeGroups = Optional(Wfs("treeGroups", fetched, ct));
        var hedges = Optional(Wfs("hedges", fetched, ct));
        var heightBox = HeightCells.Snap(minX, minY, maxX, maxY, heightMargin, cell);
        var dtm = Dhm(heightBox, ct, cell: cell);
        var dsm = Surface(heightBox, cell, ct);
        await Task.WhenAll(lots, buildings, roads, water, manholes, gullies, addresses, trees, treeGroups, hedges, dtm, dsm);

        progress?.Report(Strings.Fetch_StepSiteplan);
        // The surface is a help: without it (the call failed, or a grid that doesn't match) no roofs and no context,
        // the rest as always.
        var surface = dsm.Result is { } s && s.W == dtm.Result.W && s.H == dtm.Result.H ? s : null;
        var terrain = Crop(dtm.Result, HeightCells.Snap(minX, minY, maxX, maxY, Config.Current.Fetch.TerrainMarginM, cell));
        // The site's 0,0: the project's origin, else the address point.
        var (e0, n0) = origin ?? (x, y);
        var csv = TerrainCsv(terrain, e0, n0);
        // Without the lots round it: the lot alone (its outline, no number or area); without the buildings: none drawn,
        // and no roofs or context either (Context Buildings fetches them when asked).
        var lotList = lots.Result ?? [new Feature([lot], [])];
        var buildingList = buildings.Result ?? [];
        var lotRings = lotList.FirstOrDefault(f => Geometry.Inside(f.Rings, x, y))?.Rings ?? [lot];
        var roofs = surface is null || buildings.Result is null ? null : Roofs(buildingList, lotRings, dtm.Result, surface);
        var context = surface is null || buildings.Result is null || !ContextFromFetch(lot, cell) ? null
            : ContextJson(buildingList, dtm.Result, surface, lot, heightBox, x, y, origin);
        var surroundings = new Surroundings(x, y, reach, lotList, buildingList, roads.Result ?? [], water.Result, DateTime.UtcNow, roofs,
            manholes.Result, gullies.Result, addresses.Result, trees.Result, treeGroups.Result, hedges.Result);
        var lotMm = lot.Select(p => (Math.Round((p.X - e0) * 1000, 1), Math.Round((p.Y - n0) * 1000, 1))).ToList();
        if (lotMm.Count > 1 && lotMm[0] == lotMm[^1])
        {
            lotMm.RemoveAt(lotMm.Count - 1);
        }
        // The drawing from the terrain as it is kept (the CSV's), so drawn again from the model it is the same.
        var (dxf, skipped) = SiteplanDxf.WriteWithReport(lotList, buildingList, roads.Result ?? [], terrain, Core.Surroundings.Window(lot, x, y, siteplanMargin),
            x, y, address, Language.Current, setbackM, water.Result, surroundings.FetchedUtc, roofs, hiddenLayers, roofLabels, origin,
            manholes.Result, gullies.Result, addresses.Result, trees.Result, treeGroups.Result, hedges.Result);
        return new Site(csv, dxf, HouseGround(buildingList, lotRings, terrain), lotMm, surroundings.ToJson(), context, surface is null,
            roads.Result is null, water.Result is null, lots.Result is null, buildings.Result is null,
            manholes.Result is null || gullies.Result is null, addresses.Result is null,
            trees.Result is null || treeGroups.Result is null || hedges.Result is null, skipped);
    }

    // A layer the site plan can do without (roads, water, the addresses), or null if it can't be had: the load goes on without it.
    private static async Task<T?> Optional<T>(Task<T> layer) where T : class
    {
        try
        {
            return await layer;
        }
        catch (FetchException)
        {
            return null;
        }
    }

    // The surface over the box, or null if it can't be had: the roofs and the context are a help, the load goes on without them.
    private async Task<Dtm?> Surface((double X1, double Y1, double X2, double Y2) box, double cell, CancellationToken ct)
    {
        try
        {
            return await Dhm(box, ct, "dhm_overflade", cell);
        }
        catch (FetchException)
        {
            return null;
        }
    }

    /// <summary>The part of $dtm inside $box (on its cells, as HeightCells.Snap gives them at the same spacing).</summary>
    public static Dtm Crop(Dtm dtm, (double X1, double Y1, double X2, double Y2) box)
    {
        var (i0, j0) = ((int)Math.Round((box.X1 - dtm.X0) / dtm.Dx), (int)Math.Round((dtm.Y0 - box.Y2) / dtm.Dy));
        var (w, h) = (HeightCells.Count(box.X2 - box.X1, dtm.Dx), HeightCells.Count(box.Y2 - box.Y1, dtm.Dy));
        if (i0 < 0 || j0 < 0 || i0 + w > dtm.W || j0 + h > dtm.H)
        {
            throw new ArgumentOutOfRangeException(nameof(box), $"The box ({w} × {h} cells at {i0}, {j0}) isn't inside the grid ({dtm.W} × {dtm.H}).");
        }
        var z = new float[(long)w * h];
        for (var r = 0; r < h; r++)
        {
            Array.Copy(dtm.Z, (long)(j0 + r) * dtm.W + i0, z, (long)r * w, w);
        }
        return new Dtm(dtm.X0 + i0 * dtm.Dx, dtm.Y0 - j0 * dtm.Dy, dtm.Dx, dtm.Dy, w, h, z);
    }

    /// <summary>
    /// The height model's grid back from a stored terrain (TerrainGrid: cell centres in mm from ($x, $y), the project's
    /// origin or the address point, rows from the south): what SiteplanDxf needs for its spot elevations when it draws
    /// from the model.
    /// </summary>
    public static Dtm FromGrid(TerrainGrid grid, double x, double y)
    {
        var cell = grid.Step / 1000;
        var z = new float[grid.Nx * grid.Ny];
        for (var row = 0; row < grid.Ny; row++)
        {
            for (var i = 0; i < grid.Nx; i++)
            {
                z[row * grid.Nx + i] = (float)(grid[i, grid.Ny - 1 - row] / 1000);  // Dtm's rows run from the north
            }
        }
        return new Dtm(x + grid.X0 / 1000 - cell / 2, y + grid.MaxY / 1000 + cell / 2, cell, cell, grid.Nx, grid.Ny, z);
    }

    /// <summary>
    /// The highest terrain (mm DVR90) along the largest building on the lot, every 0.4 m and at every corner of its
    /// outline: offered in Settings as a start for ±0.00 (a floor there never stands below the ground), from the outline and the
    /// terrain fetched anyway. Null without a building. $lot: the parcel's rings (its outline and holes).
    /// </summary>
    public static double? HouseGround(List<Feature> buildings, List<List<Pt>> lot, Dtm dtm)
    {
        var house = buildings.Select(b => b.Rings[0])
            .Where(r => Geometry.Centre(r) is var c && Geometry.Inside(lot, c.X, c.Y))
            .MaxBy(r => Math.Abs(Geometry.Area(r)));
        if (house is null)
        {
            return null;
        }
        var levels = new List<double>();
        for (var i = 0; i + 1 < house.Count; i++)
        {
            var (a, b) = (house[i], house[i + 1]);
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) / Cell));
            for (var k = 0; k <= steps; k++)
            {
                if (Elevation(dtm, a.X + (b.X - a.X) * k / steps, a.Y + (b.Y - a.Y) * k / steps) is { } z)
                {
                    levels.Add(z);
                }
            }
        }
        return levels.Count > 0 ? Math.Round(levels.Max() * 1000) : null;
    }


    /// <summary>
    /// The buildings around the lot with their heights, as the context file (version 1, only "buildings";
    /// Context.Parse reads it): outlines from GeoDanmark, heights from Danmarks Højdemodel's terrain and surface.
    /// $x, $y: the address point (UTM32, m); $lotMm: the lot, mm from $origin, the project's (ProjectOrigin), or from
    /// the address point without one, as the context's outlines. The heights at the model's 0.4 m, in parts if the box
    /// is larger than one call; coarser for a very large lot (the roofs' percentiles hold at 0.8 m too).
    /// </summary>
    public async Task<string> FetchContext(double x, double y, IReadOnlyList<Point2> lotMm, IProgress<string>? progress = null,
        CancellationToken ct = default, (double E, double N)? origin = null)
    {
        if (_key.Length == 0)
        {
            throw new FetchException(Strings.Fetch_NoKey);
        }
        var (e0, n0) = origin ?? (x, y);
        List<Pt> lot = [.. lotMm.Select(p => (e0 + p.X / 1000, n0 + p.Y / 1000))];
        var (minX, minY, maxX, maxY) = (lot.Min(p => p.X), lot.Min(p => p.Y), lot.Max(p => p.X), lot.Max(p => p.Y));
        // The finest spacing whose box stays within the points Jega fetches; none = too large.
        var cell = ContextCell(lot);
        if (cell == 0)
        {
            throw new FetchException(Strings.Fetch_ContextTooLarge);
        }
        var box = HeightCells.Snap(minX, minY, maxX, maxY, Config.Current.Fetch.ContextMarginM, cell);
        progress?.Report(Strings.Fetch_StepContext);
        var buildings = Wfs("buildings", box, ct);
        var dtm = Dhm(box, ct, cell: cell);
        var dsm = Dhm(box, ct, "dhm_overflade", cell);
        await Task.WhenAll(buildings, dtm, dsm);
        return ContextJson(buildings.Result, dtm.Result, dsm.Result, lot, box, x, y, origin);
    }

    /// <summary>
    /// The context JSON (buildings only) from the buildings, the terrain $dtm and the surface
    /// $dsm (same cells), the lot and the box (UTM32, m). Per building in the box: its outline in mm from $origin, the
    /// project's (ProjectOrigin), or from the address point ($x, $y) without one, its ground (the lowest terrain at its
    /// corners) and top (the 90th percentile of the surface inside it: the roof without a chimney), mm DVR90, and
    /// whether it stands on the lot ("own"). Under 4 surface cells or lower than 1.5 m: left out.
    /// </summary>
    public static string ContextJson(List<Feature> buildings, Dtm dtm, Dtm dsm, List<Pt> lot,
        (double X1, double Y1, double X2, double Y2) box, double x, double y, (double E, double N)? origin = null)
    {
        static long Mm(double m) => (long)Math.Round(m * 1000);
        var (e0, n0) = origin ?? (x, y);
        var list = new List<object>();
        foreach (var b in buildings)
        {
            var ring = b.Rings[0];
            if (ring.Max(p => p.X) < box.X1 || ring.Min(p => p.X) > box.X2 || ring.Max(p => p.Y) < box.Y1 || ring.Min(p => p.Y) > box.Y2)
            {
                continue;
            }
            if (Roof(ring, dtm, dsm) is not var (bottom, top, _, _) || top - bottom < Config.Current.Fetch.ContextMinHeightM)
            {
                continue;
            }
            var pts = ring.Count > 1 && ring[0] == ring[^1] ? ring.Take(ring.Count - 1) : ring;
            var (cx, cy) = Geometry.Centre(ring);
            list.Add(new
            {
                own = Geometry.Inside(lot, cx, cy),
                bbr = b.Props.GetValueOrDefault("BBRUUID"),
                @base = Mm(bottom),
                top = Mm(top),
                ring = pts.Select(p => new[] { Mm(p.X - e0), Mm(p.Y - n0) }),
            });
        }
        return JsonSerializer.Serialize(new
        {
            version = 1,
            units = "mm",
            origin = origin is null ? "address point" : "project origin",
            z = "DVR90",
            source = "GeoDanmark (outlines), Danmarks Hoejdemodel terrain and surface (heights); indicative",
            writtenBy = Build.Version,
            buildings = list,
        });
    }

    // A building's ground (the lowest terrain at its corners), top (the 90th percentile of the surface cells whose
    // centres lie inside it: the roof without a chimney), ridge (the 98th: OK tag, still without a chimney or a single
    // wrong cell) and eaves (the 20th of the cells within Config's EaveBandM of the outline: tagfod; null if none), m
    // DVR90; null under 4 cells or without terrain at the corners. Indicative: 0.4 m cells read a narrow ridge a little
    // low, and trees over the roof read high.
    private static (double Base, double Top, double Ridge, double? Eave)? Roof(List<Pt> ring, Dtm dtm, Dtm dsm)
    {
        var i0 = Math.Max(0, (int)Math.Floor((ring.Min(p => p.X) - dtm.X0) / dtm.Dx));
        var i1 = Math.Min(dtm.W - 1, (int)Math.Floor((ring.Max(p => p.X) - dtm.X0) / dtm.Dx));
        var j0 = Math.Max(0, (int)Math.Floor((dtm.Y0 - ring.Max(p => p.Y)) / dtm.Dy));
        var j1 = Math.Min(dtm.H - 1, (int)Math.Floor((dtm.Y0 - ring.Min(p => p.Y)) / dtm.Dy));
        var roof = new List<double>();
        var rim = new List<double>();
        for (var j = j0; j <= j1; j++)
        {
            for (var i = i0; i <= i1; i++)
            {
                double s = dsm.Z[j * dsm.W + i];
                if (HeightCells.Valid(s) && HeightCells.Centre(dtm, i, j) is var (cx, cy) && Geometry.Inside(ring, cx, cy))
                {
                    roof.Add(s);
                    if (Geometry.DistanceToEdges(ring, (cx, cy)) <= Config.Current.Fetch.EaveBandM)
                    {
                        rim.Add(s);
                    }
                }
            }
        }
        var edge = ring.Select(p => Elevation(dtm, p.X, p.Y)).OfType<double>().ToList();
        if (roof.Count < 4 || edge.Count == 0)
        {
            return null;
        }
        roof.Sort();
        rim.Sort();
        static double Q(List<double> v, double f) => v[(int)Math.Floor(f * (v.Count - 1))];
        return (edge.Min(), Q(roof, 0.9), Q(roof, 0.98), rim.Count > 0 ? Q(rim, 0.2) : null);
    }

    /// <summary>
    /// The roofs of the buildings on the lot ($lot: the parcel's rings), for the site plan: each one's middle (UTM32,
    /// m), ridge and eaves (m DVR90, see Roof), from the terrain $dtm and the surface $dsm (same cells). Left out as in
    /// the context: under 4 cells, or lower than Config's ContextMinHeightM.
    /// </summary>
    public static List<RoofLevels> Roofs(List<Feature> buildings, List<List<Pt>> lot, Dtm dtm, Dtm dsm)
    {
        var roofs = new List<RoofLevels>();
        foreach (var ring in buildings.Select(b => b.Rings[0]))
        {
            var (cx, cy) = Geometry.Centre(ring);
            if (Geometry.Inside(lot, cx, cy) && Roof(ring, dtm, dsm) is var (bottom, top, ridge, eave)
                && top - bottom >= Config.Current.Fetch.ContextMinHeightM)
            {
                // As they are kept (Surroundings: mm), so a redraw from the model reads the same.
                roofs.Add(new RoofLevels(Math.Round(cx, 3), Math.Round(cy, 3), Math.Round(ridge, 3), eave is { } e ? Math.Round(e, 3) : null));
            }
        }
        return roofs;
    }

    /// <summary>A roof on the lot for the site plan: the building's middle (UTM32, m), its ridge and eaves (m DVR90; no eaves without cells along the outline).</summary>
    public sealed record RoofLevels(double X, double Y, double Ridge, double? Eave);

    /// <summary>
    /// "Test key" in Settings: one small call to each service, with what it answered: (service, null = OK, else why).
    /// $Via: the address search found the address, but through the register, Adressevælger left (its token unknown or
    /// refused): why, as the fallback's line gives it ("400, unknown token"); still OK, said.
    /// </summary>
    public async Task<List<(string Service, string? Problem, string? Via)>> Test()
    {
        async Task<string?> Try(Func<Task> call)
        {
            try
            {
                await call();
                return null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return e.Message;
            }
        }
        // A spot in Copenhagen (Rådhuspladsen); 2 × 2 m, so the answers are tiny.
        var (x, y) = (724_630.0, 6_175_870.0);
        return
        [
            (Strings.Fetch_TestMapData, _key.Length == 0 ? Strings.Fetch_TestNoKey : await Try(() => Wfs("lots", (x - 1, y - 1, x + 1, y + 1))), null),
            (Strings.Fetch_TestTerrain, _key.Length == 0 ? Strings.Fetch_TestNoKey : await Try(() => Dhm((x - 0.8, y - 0.8, x + 0.8, y + 0.8))), null),
            (Strings.Fetch_TestAddresses, await Try(() => SearchAddress("Rådhuspladsen 1")), _fallback.Reason is { } why ? FallbackWhy(why) : null),
        ];
    }

    private async Task<List<Feature>> Wfs(string layer, (double X1, double Y1, double X2, double Y2) box, CancellationToken ct = default)
    {
        var (service, type) = Layers[layer];
        var what = Fill(Strings.Fetch_MapData, layer);
        var bbox = string.Create(Inv, $"{box.X1:F2},{box.Y1:F2},{box.X2:F2},{box.Y2:F2},urn:ogc:def:crs:EPSG::25832");
        var body = await Get($"{WfsUrl}{service}/1.0.0/WFS?service=WFS&version=2.0.0&request=GetFeature&typeNames={Esc(type)}" +
                             $"&bbox={Esc(bbox)}&apiKey={Esc(_key)}", what, ct);
        var gml = System.Text.Encoding.UTF8.GetString(body);
        if (!gml.Contains("FeatureCollection"))
        {
            throw new FetchException(Fill(Strings.Fetch_NotMapData, what));
        }
        // The service answers at most 30,000 features a call (far more than a lot's surroundings): should it ever cut
        // the answer short, say so rather than draw a plan with buildings missing.
        if (Count(gml, "numberMatched") is { } matched && Count(gml, "numberReturned") is { } returned && returned < matched)
        {
            throw new FetchException(Fill(Strings.Fetch_FeaturesCut, what, returned, matched));
        }
        return Parse(what, () => ParseGml(gml));
    }

    // A count the WFS answer states on its FeatureCollection (numberMatched="427"); null if absent or "unknown".
    private static long? Count(string gml, string attribute) =>
        System.Text.RegularExpressions.Regex.Match(gml, attribute + "=\"(\\d+)\"") is { Success: true } m ? long.Parse(m.Groups[1].Value, Inv) : null;

    /// <summary>
    /// The height model over the box (on whole $cell): $coverage dhm_terraen (the terrain) or dhm_overflade (the
    /// surface: roofs and tree crowns). Larger than one call allows (Config.Fetch.MaxCellsPerCall a side): fetched in parts, a few at a
    /// time, and put together into one grid.
    /// </summary>
    private async Task<Dtm> Dhm((double X1, double Y1, double X2, double Y2) box, CancellationToken ct = default,
        string coverage = "dhm_terraen", double cell = Cell)
    {
        var (w, h) = (HeightCells.Count(box.X2 - box.X1, cell), HeightCells.Count(box.Y2 - box.Y1, cell));
        if (w <= MaxPx && h <= MaxPx)
        {
            // The size asked for, as the parts below are checked: the terrain is cut and the surface laid on it by cells.
            var one = await DhmPart(box, w, h, coverage, ct);
            if (one.W != w || one.H != h)
            {
                throw new FetchException(Fill(Strings.Fetch_PartSize, Strings.Fetch_Terrain, one.W, one.H, w, h));
            }
            return one;
        }
        var z = new float[(long)w * h];
        using var slots = new SemaphoreSlim(Config.Current.Fetch.ParallelParts);
        var parts = new List<Task>();
        for (var j0 = 0; j0 < h; j0 += MaxPx)
        {
            for (var i0 = 0; i0 < w; i0 += MaxPx)
            {
                var (pi, pj, pw, ph) = (i0, j0, Math.Min(MaxPx, w - i0), Math.Min(MaxPx, h - j0));
                parts.Add(Task.Run(async () =>
                {
                    await slots.WaitAsync(ct);
                    try
                    {
                        // Rows run from the top (north): this part starts pi cells east of the box's west edge, pj down from its north edge.
                        var x1 = box.X1 + pi * cell;
                        var y2 = box.Y2 - pj * cell;
                        var part = await DhmPart((x1, y2 - ph * cell, x1 + pw * cell, y2), pw, ph, coverage, ct);
                        if (part.W != pw || part.H != ph)
                        {
                            throw new FetchException(Fill(Strings.Fetch_PartSize, Strings.Fetch_Terrain, part.W, part.H, pw, ph));
                        }
                        Place(z, w, part, pi, pj);
                    }
                    finally
                    {
                        slots.Release();
                    }
                }, ct));
            }
        }
        await Task.WhenAll(parts);
        return new Dtm(box.X1, box.Y2, cell, cell, w, h, z);
    }

    /// <summary>Copies a part's cells into the whole grid $z ($w wide, rows from the top), its top left cell at (pi, pj).</summary>
    public static void Place(float[] z, int w, Dtm part, int pi, int pj)
    {
        for (var r = 0; r < part.H; r++)
        {
            Array.Copy(part.Z, (long)r * part.W, z, (long)(pj + r) * w + pi, part.W);
        }
    }

    // One call: the box as w × h cells.
    private async Task<Dtm> DhmPart((double X1, double Y1, double X2, double Y2) box, int w, int h, string coverage, CancellationToken ct)
    {
        var bbox = string.Create(Inv, $"{box.X1:F2},{box.Y1:F2},{box.X2:F2},{box.Y2:F2}");
        var tif = await Get($"{DhmUrl}?service=WCS&version=1.0.0&request=GetCoverage&coverage={coverage}&crs=EPSG:25832" +
                            $"&bbox={bbox}&width={w}&height={h}&format=GTiff&apiKey={Esc(_key)}", Strings.Fetch_Terrain, ct);
        return Parse(Strings.Fetch_Terrain, () => ParseTiff(tif));
    }

    /// <summary>A feature from the WFS: its rings or lines (2D, m) and its simple properties. A point feature (a manhole
    /// cover) is a ring of one point, its level (3D) as the property "z" (m DVR90, invariant).</summary>
    public sealed record Feature(List<List<Pt>> Rings, Dictionary<string, string> Props);

    /// <summary>GML 3.2 members → features.</summary>
    public static List<Feature> ParseGml(string gml)
    {
        var features = new List<Feature>();
        static bool IsPos(XElement e) => e.Name.LocalName is "posList" or "pos";
        // Several queries in one call answer with a collection per query inside the outer one: only their features count.
        foreach (var f in XDocument.Parse(gml).Descendants().Where(e => e.Name.LocalName == "member").SelectMany(e => e.Elements())
                     .Where(e => e.Name.LocalName != "FeatureCollection"))
        {
            var props = f.Elements().Where(c => !c.Descendants().Any(IsPos))
                .GroupBy(c => c.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value.Trim());
            var rings = new List<List<Pt>>();
            foreach (var pl in f.Descendants().Where(IsPos))
            {
                var dim = pl.AncestorsAndSelf().Select(a => (string?)a.Attribute("srsDimension")).FirstOrDefault(a => a is not null) is { } d ? int.Parse(d, Inv) : 2;
                var v = pl.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, Inv)).ToArray();
                var pts = new List<Pt>();
                for (var i = 0; i + 1 < v.Length; i += dim)
                {
                    pts.Add((v[i], v[i + 1]));
                }
                rings.Add(pts);
                // A point's level (a cover's, a grate's), kept as a property: Rings stay 2D.
                if (pl.Name.LocalName == "pos" && dim >= 3 && v.Length >= 3)
                {
                    props["z"] = v[2].ToString("0.###", Inv);
                }
            }
            if (rings.Count > 0)
            {
                features.Add(new Feature(rings, props));
            }
        }
        return features;
    }

    /// <summary>The height model's grid: top left corner, cell size, size, elevations row by row from the top.</summary>
    public sealed record Dtm(double X0, double Y0, double Dx, double Dy, int W, int H, float[] Z);

    /// <summary>The WCS answer: an uncompressed little-endian float32 GeoTIFF, read by a minimal reader.</summary>
    public static Dtm ParseTiff(byte[] tif)
    {
        if (tif.Length < 8 || tif[0] != 'I' || tif[1] != 'I' || tif[2] != 42 || tif[3] != 0)
        {
            throw new FetchException(Strings.Fetch_NotGeoTiff);
        }
        int U16(int o) => BitConverter.ToUInt16(tif, o);
        int U32(int o) => (int)BitConverter.ToUInt32(tif, o);
        var tags = new Dictionary<int, double[]>();
        var ifd = U32(4);
        for (var i = 0; i < U16(ifd); i++)
        {
            var e = ifd + 2 + i * 12;
            var (tag, type, count) = (U16(e), U16(e + 2), U32(e + 4));
            var size = type switch { 3 => 2, 4 => 4, 12 => 8, _ => 1 };
            var at = count * size > 4 ? U32(e + 8) : e + 8;
            tags[tag] = Enumerable.Range(0, count).Select(k => type switch
            {
                3 => U16(at + 2 * k),
                4 => U32(at + 4 * k),
                12 => BitConverter.ToDouble(tif, at + 8 * k),
                _ => double.NaN,
            }).ToArray();
        }
        // 256/257 size, 258 bits, 259 compression (1 = none), 339 sample format (3 = float), 273/279 strips,
        // 33550 pixel size, 33922 tie point (pixel 0,0 → x, y).
        double Tag(int t, double fallback) => tags.TryGetValue(t, out var v) ? v[0] : fallback;
        if (Tag(259, 1) != 1 || Tag(258, 0) != 32 || Tag(339, 3) != 3 || !tags.ContainsKey(273) || !tags.ContainsKey(33922) || !tags.ContainsKey(33550))
        {
            throw new FetchException(Strings.Fetch_GeoTiffLayout);
        }
        var raw = new List<byte>();
        for (var k = 0; k < tags[273].Length; k++)
        {
            raw.AddRange(tif.AsSpan((int)tags[273][k], (int)tags[279][k]).ToArray());
        }
        var bytes = raw.ToArray();
        var z = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, z, 0, z.Length * 4);
        var (w, h) = ((int)tags[256][0], (int)tags[257][0]);
        if (z.Length < w * h)
        {
            throw new FetchException(Strings.Fetch_GeoTiffShort);
        }
        return new Dtm(tags[33922][3], tags[33922][4], tags[33550][0], tags[33550][1], w, h, z);
    }

    /// <summary>The terrain elevation (m DVR90) at a point, between the four nearest cells; null outside or on no-data.</summary>
    public static double? Elevation(Dtm dtm, double x, double y)
    {
        // Cell centres sit half a cell in from the top left corner.
        var fx = (x - dtm.X0) / dtm.Dx - 0.5;
        var fy = (dtm.Y0 - y) / dtm.Dy - 0.5;
        var (i, j) = ((int)Math.Floor(fx), (int)Math.Floor(fy));
        if (i < 0 || j < 0 || i + 1 >= dtm.W || j + 1 >= dtm.H)
        {
            return null;
        }
        var (tx, ty) = (fx - i, fy - j);
        double Z(int a, int b) => dtm.Z[b * dtm.W + a];
        double[] v = [Z(i, j), Z(i + 1, j), Z(i, j + 1), Z(i + 1, j + 1)];
        if (!v.All(HeightCells.Valid))
        {
            return null;
        }
        return (v[0] * (1 - tx) + v[1] * tx) * (1 - ty) + (v[2] * (1 - tx) + v[3] * tx) * ty;
    }

    /// <summary>
    /// The terrain as Jega's CSV: one line per cell centre, "X,Y,Z" in mm, X/Y from ($ox, $oy), the project's origin
    /// or the address point, Z DVR90, rows from the south, each from the west. A hole in the data fails: Core needs a whole grid.
    /// </summary>
    public static string TerrainCsv(Dtm dtm, double ox, double oy)
    {
        // + 0.0: never "-0.0" for float noise around 0.
        static string Mm(double m) => (Math.Round(m * 1000, 1, MidpointRounding.AwayFromZero) + 0.0).ToString("0.0", Inv);
        var sb = new System.Text.StringBuilder();
        for (var j = dtm.H - 1; j >= 0; j--)  // the rows run from the top, so the last one is the southernmost
        {

            for (var i = 0; i < dtm.W; i++)
            {
                double z = dtm.Z[j * dtm.W + i];
                if (!HeightCells.Valid(z))
                {
                    throw new FetchException(Strings.Fetch_Hole);
                }
                var (x, y) = HeightCells.Centre(dtm, i, j);
                sb.Append(Mm(x - ox)).Append(',').Append(Mm(y - oy)).Append(',').Append(Mm(z)).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static async Task<JsonElement> GetJson(string url, string what, CancellationToken ct)
    {
        var body = await Get(url, what, ct);
        var json = Parse(what, () => JsonDocument.Parse(body).RootElement);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("status", out var status) || status.ToString() != "ok")
        {
            throw new FetchException(Fill(Strings.Fetch_Status, what,
                json.ValueKind == JsonValueKind.Object && json.TryGetProperty("status", out var s) ? s.ToString() : Strings.Fetch_NoStatus));
        }
        return json;
    }

    /// <summary>
    /// Reads a service's answer: anything that goes wrong reading it (an HTML page instead of JSON, a missing field, a
    /// broken GML or GeoTIFF) becomes a FetchException naming the service, so the windows always get a plain reason.
    /// </summary>
    private static T Parse<T>(string what, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception e) when (e is not FetchException and not OperationCanceledException)
        {
            throw new FetchException(Fill(Strings.Fetch_Unreadable, what, e.Message));
        }
    }

    /// <summary>The body of a GET, or of a POST of $postJson (GraphQL); $ct cancelled = OperationCanceledException,
    /// never a FetchException.</summary>
    private static async Task<byte[]> Get(string url, string what, CancellationToken ct, string? postJson = null)
    {
        HttpResponseMessage response;
        // A server error, no answer and 429 are tried again before it fails (Retry): Datafordeleren's WFS answers HTTP
        // 500 now and then and the same call works a moment later (2 in 5 runs in a test).
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage answer;
            try
            {
                answer = postJson is null
                    ? await Http.GetAsync(url, ct)
                    : await Http.PostAsync(url, new StringContent(postJson, System.Text.Encoding.UTF8, "application/json"), ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (Retry.Wait(attempt, null) is not { } afterNone)
                {
                    throw new FetchException(Fill(Strings.Fetch_NoAnswer, what, e.Message), noAnswer: true);
                }
                await Task.Delay(afterNone, ct);
                continue;
            }
            if (answer.IsSuccessStatusCode || Retry.Wait(attempt, (int)answer.StatusCode, RetryAfter(answer)) is not { } wait)
            {
                response = answer;
                break;
            }
            answer.Dispose();
            await Task.Delay(wait, ct);
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Adressevælger refuses its token, never the user's Datafordeler key: the message says which.
                var refused = url.StartsWith(AddressUrl, StringComparison.Ordinal) ? Strings.Fetch_TokenRefused : Strings.Fetch_KeyRefused;
                throw new FetchException(Fill(refused, what, (int)response.StatusCode), (int)response.StatusCode);
            }
            if (response.StatusCode is HttpStatusCode.ProxyAuthenticationRequired)
            {
                // The office's network, not Jega's key or the service: the message says whom to ask.
                throw new FetchException(Fill(Strings.Fetch_ProxyRefused, what));
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new FetchException(Fill(Strings.Fetch_Http, what, (int)response.StatusCode), (int)response.StatusCode);
            }
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    // How long a 429 asked to wait (Retry-After, as seconds or a date), if it said.
    private static TimeSpan? RetryAfter(HttpResponseMessage answer) =>
        answer.Headers.RetryAfter is { } after ? after.Delta ?? after.Date - DateTimeOffset.UtcNow : null;

    private static string Esc(string s) => Uri.EscapeDataString(s);
}

/// <summary>Fetching failed; the message says which service and why, for the user. $status: the HTTP status it
/// failed on, if any; $noAnswer: the service didn't answer at all (after the retries), not cancelled.</summary>
public sealed class FetchException(string message, int? status = null, bool noAnswer = false) : Exception(message)
{
    public int? Status { get; } = status;

    public bool NoAnswer { get; } = noAnswer;
}
