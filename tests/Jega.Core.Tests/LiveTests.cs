using Jega.Core;

namespace Jega.Core.Tests;

/// <summary>
/// The whole pipe against the real services: address → lot → terrain CSV and site plan DXF, for the test case
/// Skagens Museum (Brøndumsvej 4, 9990 Skagen), compared with what Jega fetched there before (Data/skagens-museum-*). Runs
/// only with a Datafordeler key in the environment variable JEGA_DATAFORDELER_KEY (never in the code); without it the
/// tests show as skipped. The files land in JEGA_LIVE_OUT (default: the temp folder) for a look in CAD.
/// </summary>
public class LiveTests
{
    private static string Key => Environment.GetEnvironmentVariable("JEGA_DATAFORDELER_KEY")!;

    [LiveFact("JEGA_DATAFORDELER_KEY")]
    public async Task Skagens_Museum_from_address_to_files()
    {
        var fetcher = new SiteFetcher(Key, "");

        var hits = await fetcher.SearchAddress("Brøndumsvej 4 Skagen");
        var hit = Assert.Single(hits, h => h.Text.StartsWith("Brøndumsvej 4,") && h.Text.Contains("9990"));
        var (text, x, y) = await fetcher.AddressPoint(hit.Id!);
        // 150 m round the lot, so the plan reaches the paths (STI) round the museum's garden.
        var site = await fetcher.Fetch(x, y, text, siteplanMarginM: 150);
        var (csv, dxf) = (site.Csv, site.Dxf);
        // The highest terrain along the house (2.96 in the stored data).
        Assert.InRange(site.HouseGroundMm ?? 0, 2800, 3100);

        var outDir = Environment.GetEnvironmentVariable("JEGA_LIVE_OUT") is { Length: > 0 } o ? o : Path.GetTempPath();
        File.WriteAllText(Path.Combine(outDir, "jega-skagens-museum-terrain.csv"), csv);
        File.WriteAllText(Path.Combine(outDir, "jega-skagens-museum-siteplan.dxf"), dxf);

        // The terrain: the same grid as the stored one, point for point (the height model may be updated since, so the
        // elevations get a little slack).
        var grid = TerrainGrid.Parse(csv);
        var reference = TerrainGrid.Load(Path.Combine(AppContext.BaseDirectory, "Data", "skagens-museum-terrain.csv"));
        Assert.Equal((reference.Nx, reference.Ny, reference.Step), (grid.Nx, grid.Ny, grid.Step));
        Assert.Equal((reference.X0, reference.Y0), (grid.X0, grid.Y0));
        var worst = Enumerable.Range(0, grid.Nx).SelectMany(i => Enumerable.Range(0, grid.Ny).Select(j => Math.Abs(grid[i, j] - reference[i, j]))).Max();
        Assert.True(worst < 50, $"largest difference to the stored CSV: {worst:0.0} mm");

        // The site plan: the lot on GRUND, neighbours, buildings, road and path edges around it (no water within 150 m).
        foreach (var layer in new[] { "GRUND", "SKEL", "BYGNINGER", "VEJKANT", "STI", "AFSTAND_SKEL", "KOTER" })
        {
            Assert.Contains($"0\nPOLYLINE\n8\n{layer}\n", dxf);
        }
        Assert.Contains("0\nTEXT\n8\nSKELMAAL\n", dxf);
        Assert.Contains("0\nTEXT\n8\nKOTER\n", dxf);
        Assert.Contains("0\nTEXT\n8\nTAGKOTER\n", dxf);
        Assert.NotEmpty(Surroundings.Parse(site.Surroundings).Roofs!);

        // The context, as Context Buildings fetches it: the lot is too large for the load's own calls to take the whole
        // ring round it. The same buildings as the stored ones, each matched by its BBR id, with about the same ground
        // and top.
        Assert.Null(site.Context);
        var contextJson = await fetcher.FetchContext(x, y, site.LotMm.Select(p => new Point2(p.X, p.Y)).ToList());
        File.WriteAllText(Path.Combine(outDir, "jega-skagens-museum-context.json"), contextJson);
        var context = Context.Parse(contextJson);
        var stored = Context.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "skagens-museum-context.json")));
        Assert.Equal(stored.Count(b => b.Own), context.Count(b => b.Own));
        Assert.InRange(context.Count, stored.Count - 3, stored.Count + 3);
        // Only buildings with a BBR id can be matched (sheds and the like have none).
        var withId = stored.Where(r => r.Bbr is not null).ToList();
        var matched = withId.Select(r => (r, c: context.FirstOrDefault(c => c.Bbr == r.Bbr))).Where(p => p.c is not null).ToList();
        Assert.True(matched.Count >= withId.Count - 3, $"{matched.Count} of {withId.Count} matched by BBR id");
        Assert.All(matched, p => Assert.InRange(p.c!.Top - p.r.Top, -300, 300));
        Assert.All(matched, p => Assert.InRange(p.c!.Base - p.r.Base, -300, 300));
    }

    /// <summary>
    /// Any address, for a look: JEGA_LIVE_ADDRESS (e.g. "Brøndumsvej 4, 9990 Skagen"), its first hit fetched as
    /// Load from Address does and the files written to JEGA_LIVE_OUT as jega-live-*. JEGA_LIVE_EAVES=1: the roofs as
    /// ridge over eaves; JEGA_LIVE_SETBACK: the setback line's distance, m. Nothing checked; without the address (or
    /// the key) it shows as skipped.
    /// </summary>
    [LiveFact("JEGA_DATAFORDELER_KEY", "JEGA_LIVE_ADDRESS")]
    public async Task Any_address_to_files()
    {
        var address = Environment.GetEnvironmentVariable("JEGA_LIVE_ADDRESS")!;
        var fetcher = new SiteFetcher(Key, "");
        var hit = (await fetcher.SearchAddress(address))[0];
        var (text, x, y) = await fetcher.AddressPoint(hit.Id!);
        var roofs = Environment.GetEnvironmentVariable("JEGA_LIVE_EAVES") == "1" ? SiteplanDxf.RoofLabels.Default with { RidgeOnly = false } : null;
        double? setback = double.TryParse(Environment.GetEnvironmentVariable("JEGA_LIVE_SETBACK"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
        var site = await fetcher.Fetch(x, y, text, setbackM: setback, roofLabels: roofs);
        var outDir = Environment.GetEnvironmentVariable("JEGA_LIVE_OUT") is { Length: > 0 } o ? o : Path.GetTempPath();
        File.WriteAllText(Path.Combine(outDir, "jega-live-terrain.csv"), site.Csv);
        File.WriteAllText(Path.Combine(outDir, "jega-live-siteplan.dxf"), site.Dxf);
        File.WriteAllText(Path.Combine(outDir, "jega-live-address.txt"), text);
    }
}

/// <summary>A test that needs these environment variables: skipped, and said so, when one is missing or empty.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(params string[] variables)
    {
        var missing = variables.Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToList();
        if (missing.Count > 0)
        {
            Skip = $"Needs {string.Join(" and ", missing)}.";
        }
    }
}
