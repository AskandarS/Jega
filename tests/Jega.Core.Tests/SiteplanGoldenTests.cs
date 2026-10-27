using System.Globalization;
using Jega.Core;

namespace Jega.Core.Tests;

/// <summary>
/// The site plan drawn from Skagens Museum's kept surroundings (Data/skagens-museum-surroundings.json, its fetchedUtc
/// set to noon UTC, so the credit's date is the same from UTC−12 to UTC+11) and terrain, compared character for
/// character with the drawings the code made before (Data/skagens-museum-siteplan*.dxf): a change to SiteplanDxf that
/// is only meant to tidy it must leave them as they are. A change meant to change the drawing saves them again: with
/// JEGA_UPDATE_GOLDEN=1, dotnet test writes the drawings over the files in the source folder instead of comparing. The
/// files are read with \n line ends, as the DXF is written, whatever git checked them out with.
/// </summary>
public class SiteplanGoldenTests
{
    private const string Address = "Brøndumsvej 4, 9990 Skagen";

    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    private static readonly Surroundings Kept = Surroundings.Parse(File.ReadAllText(Data("skagens-museum-surroundings.json")));

    private static readonly TerrainGrid Grid = TerrainGrid.Load(Data("skagens-museum-terrain.csv"));

    private static string Golden(string name) => File.ReadAllText(Data(name)).ReplaceLineEndings("\n");

    // Compares $dxf with the golden file $name, or with JEGA_UPDATE_GOLDEN=1 saves it as Data/$name next to the
    // project file, the folder git keeps, not the copy in bin.
    private static void AssertGolden(string name, string dxf)
    {
        if (Environment.GetEnvironmentVariable("JEGA_UPDATE_GOLDEN") != "1")
        {
            Assert.Equal(Golden(name), dxf);
            return;
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Jega.Core.Tests.csproj")))
            dir = dir.Parent ?? throw new DirectoryNotFoundException($"Jega.Core.Tests.csproj not found above {AppContext.BaseDirectory}");
        File.WriteAllText(Path.Combine(dir.FullName, "Data", name), dxf.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Danish_with_the_defaults()
    {
        var dxf = Kept.SitePlan(Grid, Address, CultureInfo.GetCultureInfo("da-DK"), 10, 2.5);
        AssertGolden("skagens-museum-siteplan.dxf", dxf);
    }

    [Fact]
    public void English_from_the_origin_with_layers_hidden_and_ridge_over_eaves()
    {
        // 0,0 at the project's origin; two layers left out; ridge over eaves on every building from 10 m², a crowded one kept.
        var origin = ProjectOrigin.Of(Kept.X, Kept.Y);
        var terrain = TerrainGrid.Parse(SiteFetcher.TerrainCsv(SiteFetcher.FromGrid(Grid, Kept.X, Kept.Y), origin.E, origin.N));
        var dxf = Kept.SitePlan(terrain, Address, CultureInfo.GetCultureInfo("en-GB"), 50, 3, ["SKELMAAL", "STI"],
            new SiteplanDxf.RoofLabels(10, false, false), origin);
        AssertGolden("skagens-museum-siteplan-options.dxf", dxf);
    }

    // The defaults' drawing with 0,0 at $origin.
    private static string Danish((double E, double N) origin) =>
        SiteplanDxf.Write(Kept.Lots, Kept.Buildings, Kept.Roads, SiteFetcher.FromGrid(Grid, Kept.X, Kept.Y),
            Surroundings.Window(Kept.Lot, Kept.X, Kept.Y, 10), Kept.X, Kept.Y, Address, CultureInfo.GetCultureInfo("da-DK"), 2.5,
            Kept.Water, Kept.FetchedUtc, Kept.Roofs, origin: origin);

    [Fact]
    public void Danish_from_the_project_s_origin()
    {
        // The address point to whole metres, 0.09 m east and 0.18 m south of it: the cross at 0,0 on NULPUNKT, a small
        // one at the address point on ADRESSEPUNKT, both in the legend; the note gives both.
        var dxf = Danish(ProjectOrigin.Of(Kept.X, Kept.Y));
        AssertGolden("skagens-museum-siteplan-origin.dxf", dxf);
    }

    [Fact]
    public void Drawn_again_from_the_terrain_kept_from_the_origin()
    {
        // As the model keeps it with an origin: the terrain's CSV from the origin, and the same drawing.
        var origin = ProjectOrigin.Of(Kept.X, Kept.Y);
        var terrain = TerrainGrid.Parse(SiteFetcher.TerrainCsv(SiteFetcher.FromGrid(Grid, Kept.X, Kept.Y), origin.E, origin.N));
        var dxf = Kept.SitePlan(terrain, Address, CultureInfo.GetCultureInfo("da-DK"), 10, 2.5, origin: origin);
        AssertGolden("skagens-museum-siteplan-origin.dxf", dxf);
    }

    [Fact]
    public void An_origin_at_the_address_point_draws_as_without_one() =>
        AssertGolden("skagens-museum-siteplan.dxf", Danish((Kept.X, Kept.Y)));

    [Fact]
    public void Turned_to_project_north_the_lot_turns_and_the_frame_and_texts_stay_straight()
    {
        var da = CultureInfo.GetCultureInfo("da-DK");
        var straight = Kept.SitePlan(Grid, Address, da, 10, 2.5);
        var turned = Kept.SitePlan(Grid, Address, da, 10, 2.5, turnDeg: 90);
        // The lot's first corner, turned 90° counter-clockwise about the address point (the drawing's 0,0, in mm).
        var (ax, ay) = Vertices(straight, "GRUND")[0];
        var (bx, by) = Vertices(turned, "GRUND")[0];
        Assert.Equal(-ay, bx, 1);
        Assert.Equal(ax, by, 1);
        // The frame still lies straight: two distinct x and two distinct y among its corners.
        var frame = Vertices(turned, "RAMME").Take(4).ToList();
        Assert.Equal(2, frame.Select(p => Math.Round(p.X)).Distinct().Count());
        Assert.Equal(2, frame.Select(p => Math.Round(p.Y)).Distinct().Count());
        // The title stands straight, the north arrow's N is turned 90° with the arrow, and the note says so.
        Assert.Contains("\n1\nSituationsplan: Br\\U+00F8ndumsvej 4, 9990 Skagen\n50\n0.00\n", turned);
        Assert.Contains("\n1\nN\n50\n90.00\n", turned);
        Assert.Contains("drejet 90,0\\U+00B0 fra geografisk nord", turned);
        Assert.DoesNotContain("arbejdsretning", straight);
        // The terrain is read where the corners are on the ground, not where they are drawn. Turned 180° (the window
        // keeps its shape, so the scale and the corners marked are the same; turned 90° it can change scale): the same
        // levels at the corners, and the notes' terrain on the lot and at the address point are the same.
        var upsideDown = Kept.SitePlan(Grid, Address, da, 10, 2.5, turnDeg: 180);
        Assert.Equal(Vertices(straight, "KOTER").Count, Vertices(upsideDown, "KOTER").Count);
        Assert.Equal(Texts(straight, "KOTER"), Texts(upsideDown, "KOTER"));
        Assert.Equal(Texts(straight, "TEKST").Where(t => t.StartsWith("Terr")), Texts(upsideDown, "TEKST").Where(t => t.StartsWith("Terr")));
        Assert.Contains(Texts(straight, "TEKST"), t => t.StartsWith("Terr"));
        AssertGolden("skagens-museum-siteplan-turned.dxf", Kept.SitePlan(Grid, Address, da, 10, 2.5, turnDeg: 30));
    }

    [Fact]
    public void The_frame_and_the_scale_stay_when_the_view_switches_between_true_and_project_north()
    {
        // A project turned 30°: drawn north up for a True North view and turned for a Project North view, each with the
        // project's turn to fit (fitTurnDeg), the frame has the same size and the scale is the same, so switching the
        // view doesn't make the plan jump.
        var da = CultureInfo.GetCultureInfo("da-DK");
        var trueNorth = Kept.SitePlan(Grid, Address, da, 10, 2.5, fitTurnDeg: 30);
        var projectNorth = Kept.SitePlan(Grid, Address, da, 10, 2.5, turnDeg: 30, fitTurnDeg: 30);
        static (double W, double H) Size(List<(double X, double Y)> c) => (c.Max(p => p.X) - c.Min(p => p.X), c.Max(p => p.Y) - c.Min(p => p.Y));
        var (a, b) = (Size(Vertices(trueNorth, "RAMME").Take(4).ToList()), Size(Vertices(projectNorth, "RAMME").Take(4).ToList()));
        Assert.Equal(a.W, b.W, 1);
        Assert.Equal(a.H, b.H, 1);
        static string Scale(string dxf) => System.Text.RegularExpressions.Regex.Match(dxf, @"1:([\d.,]+) \(A3\)").Groups[1].Value;
        Assert.Equal(Scale(trueNorth), Scale(projectNorth));
        Assert.NotEmpty(Scale(trueNorth));
        // Without a turn at all the frame lies tight round the lot, as before (the golden file).
        AssertGolden("skagens-museum-siteplan.dxf", Kept.SitePlan(Grid, Address, da, 10, 2.5));
    }

    // The vertices of the polylines on $layer, in the file's order (mm).
    private static List<(double X, double Y)> Vertices(string dxf, string layer) =>
        System.Text.RegularExpressions.Regex.Matches(dxf, $"\n0\nVERTEX\n8\n{layer}\n10\n([-0-9.]+)\n20\n([-0-9.]+)\n")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();

    // The texts on $layer, sorted.
    private static List<string> Texts(string dxf, string layer) =>
        System.Text.RegularExpressions.Regex.Matches(dxf, $"\n0\nTEXT\n8\n{layer}\n(?:[^\n]*\n){{9}}([^\n]*)\n").Select(m => m.Groups[1].Value).Order().ToList();
}
