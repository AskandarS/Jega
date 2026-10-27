using Autodesk.Revit.DB;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// The pieces of the receipts (ReceiptText) that more than one command shows: the terrain's numbers, the levels, the
/// step to ±0.00 and the data's credit. Each command words its own result and steps; the words are in Strings.
/// </summary>
internal static class Receipts
{
    /// <summary>The footer wherever Jega shows what it fetched.</summary>
    public static string Credit => Fill(Strings.Receipt_Credit, SiteFetcher.Credit);

    /// <summary>The way to relative levels while they are absolute (AbsoluteLevels), as a line, not a step: with the ground
    /// along the house when Load from Address found it. The one text for every receipt, so Refresh can tell it's there.</summary>
    public static string FloorLine(Document doc) => ProjectStore.ReadSite(doc)?.HouseGroundMm is { } house
        ? Fill(Strings.Receipt_FloorLineHouse, Units.Level(house))
        : Strings.Receipt_FloorLine;

    /// <summary>Whether Revit's 0 is 0 m DVR90: the terrain at its real heights, about 10 m over a floor drawn at 0.</summary>
    public static bool AbsoluteLevels(Document doc) => Math.Abs(Dvr90.AtRevitZero(doc).Mm) < 1;

    /// <summary>"Absolute elevations: …", "Relative elevations: …" or "Terrain point on a level: …" (Dvr90.Describe).</summary>
    public static string Levels(Document doc)
    {
        var (mm, fromSurvey) = Dvr90.AtRevitZero(doc);
        return Dvr90.Describe(doc, mm, fromSurvey);
    }

    /// <summary>The terrain's numbers and where it landed, for the details: points, frame, place, heights.</summary>
    public static IEnumerable<string> Terrain(Document doc, TerrainGrid grid, int views)
    {
        var z = Enumerable.Range(0, grid.Nx).SelectMany(i => Enumerable.Range(0, grid.Ny).Select(j => grid[i, j])).ToList();
        yield return Fill(Strings.Receipt_Terrain, grid.Count, grid.Nx, grid.Ny, Units.Metres(grid.Step), Units.Level(z.Min()), Units.Level(z.Max()));
        // At the address point, not the site's 0,0 (the project's origin, up to 0.7 m off it).
        var (ax, ay) = Placement.AddressMm(doc) is { } a ? (a.X, a.Y) : (0d, 0d);
        if (grid.Contains(ax, ay))
        {
            yield return Fill(Strings.Receipt_AtAddress, Units.Level(grid.ElevationAt(ax, ay)));
        }
        yield return Fill(Strings.Receipt_Frame, Units.Metres(grid.X0), Units.Metres(grid.MaxX), Units.Metres(grid.Y0), Units.Metres(grid.MaxY), views);
        var p = Placement.Get(doc);
        var (x, y) = p.OriginMm;
        var angle = p.AngleToTrueNorth.ToString("0.00", Units.Numbers);
        yield return Fill(Strings.Receipt_Placed, Units.Metres(x), Units.Metres(y), angle);
    }
}
