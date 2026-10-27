using Jega.Core;
using Pt = (double X, double Y);

namespace Jega.Core.Tests;

/// <summary>Surroundings: what Load from Address keeps, so the site plan can be drawn again from the model (no network).</summary>
public class SurroundingsTests
{
    // An address point at (1110, 2215) on a 20 × 30 m lot, a neighbour east of it, a house on the lot, a road south.
    private static readonly List<Pt> LotRing = [(1100, 2200), (1120, 2200), (1120, 2230), (1100, 2230), (1100, 2200)];
    private static readonly double X = 1110, Y = 2215;

    private static SiteFetcher.Feature F(List<Pt> ring, params (string, string)[] props) =>
        new([ring], props.ToDictionary(p => p.Item1, p => p.Item2));

    // Fetched at noon UTC, so the date is the same in Denmark (the credit shows it in local time).
    private static readonly DateTime Fetched = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static Surroundings Sample() => new(X, Y, 50,
        [F(LotRing, ("matrikelnummer", "12a"), ("registreretAreal", "600")),
         F([(1120, 2200), (1140, 2200), (1140, 2230), (1120, 2230), (1120, 2200)], ("matrikelnummer", "12b"))],
        [F([(1105, 2210), (1115, 2210), (1115, 2220), (1105, 2220), (1105, 2210)], ("BBRUUID", "house"))],
        [F([(1080, 2195), (1160, 2195)], ("type", "Kørevej"))], FetchedUtc: Fetched);

    // The terrain under the lot + 5 m on the model's 0.4 m cells, sloping east; heights on whole centimetres.
    private static SiteFetcher.Dtm Terrain()
    {
        var (x0, y0, w, h) = (1095.0, 2235.0, 75, 100);  // top left corner, cells
        var z = new float[w * h];
        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++)
            {
                z[j * w + i] = (float)Math.Round(10 + i * 0.02, 2);
            }
        }
        return new SiteFetcher.Dtm(x0, y0, 0.4, 0.4, w, h, z);
    }

    [Fact]
    public void Kept_and_read_back_unchanged()
    {
        var s = Sample();
        var back = Surroundings.Parse(s.ToJson());
        Assert.Equal((s.X, s.Y, s.Margin), (back.X, back.Y, back.Margin));
        Assert.Equal(s.Lots.Count, back.Lots.Count);
        Assert.Equal(s.Lots[0].Rings[0], back.Lots[0].Rings[0]);
        Assert.Equal("12a", back.Lots[0].Props["matrikelnummer"]);
        Assert.Equal("Kørevej", back.Roads[0].Props["type"]);
        Assert.Equal(LotRing, back.Lot);
        Assert.Equal(Fetched, back.FetchedUtc);
        Assert.Equal(DateTimeKind.Utc, back.FetchedUtc!.Value.Kind);
    }

    [Fact]
    public void Covers_and_gullies_are_kept_with_their_level_and_missing_is_none()
    {
        var cover = F([(1125, 2197)], ("z", "2.36"));
        var s = Sample() with { Manholes = [cover], Gullies = [] };
        var back = Surroundings.Parse(s.ToJson());
        Assert.Equal([(1125.0, 2197.0)], back.Manholes![0].Rings[0]);
        Assert.Equal("2.36", back.Manholes[0].Props["z"]);
        Assert.Empty(back.Gullies!);
        // Kept before the points were: the fields are missing, still version 1, read as none.
        Assert.Null(Surroundings.Parse(Sample().ToJson().Replace(",\"manholes\":[],\"gullies\":[]", "")).Manholes);
    }

    [Fact]
    public void House_numbers_are_kept_with_street_building_and_line()
    {
        var number = new SiteFetcher.HouseNumber("12A", "Prøvevej", 1125.5, 2197.25, "1095215721", [[(1080, 2194), (1160, 2194)]]);
        var s = Sample() with { Addresses = [number, number with { Number = "14", BuildingId = null, StreetLine = [] }] };
        var back = Surroundings.Parse(s.ToJson());
        Assert.Equal(number, back.Addresses![0] with { StreetLine = number.StreetLine });
        Assert.Equal([(1080.0, 2194.0), (1160.0, 2194.0)], back.Addresses[0].StreetLine[0]);
        Assert.Null(back.Addresses[1].BuildingId);
        Assert.Empty(back.Addresses[1].StreetLine);
        Assert.Null(Surroundings.Parse(Sample().ToJson().Replace(",\"addresses\":[]", "")).Addresses);
    }

    [Fact]
    public void Trees_tree_groups_and_hedges_are_kept()
    {
        var s = Sample() with { Trees = [F([(1101, 2201)])], TreeGroups = [], Hedges = [F([(1100, 2199), (1120, 2199)], ("hegnstype", "Levende hegn"))] };
        var back = Surroundings.Parse(s.ToJson());
        Assert.Equal([(1101.0, 2201.0)], back.Trees![0].Rings[0]);
        Assert.Empty(back.TreeGroups!);
        Assert.Equal("Levende hegn", back.Hedges![0].Props["hegnstype"]);
        Assert.Null(Surroundings.Parse(Sample().ToJson().Replace(",\"trees\":[],\"treeGroups\":[],\"hedges\":[]", "")).Trees);
    }

    [Fact]
    public void Kept_before_the_date_was_kept_it_has_none()
    {
        // A model from before fetchedUtc: the field is missing, still version 1; the credit then falls back.
        var old = (Sample() with { FetchedUtc = null }).ToJson().Replace(",\"fetchedUtc\":null", "");
        Assert.DoesNotContain("fetchedUtc", old);
        Assert.Null(Surroundings.Parse(old).FetchedUtc);
    }

    [Fact]
    public void The_credit_gives_the_date_it_was_fetched()
    {
        // The day it was fetched where the drawing is made (local time): 18.10.2026 in Denmark, a day on in UTC+12.
        var s = Sample();
        var dtm = TerrainGrid.Parse(SiteFetcher.TerrainCsv(Terrain(), X, Y));
        var day = Fetched.ToLocalTime().ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"hentet {day}.", s.SitePlan(dtm, "Prøvevej 1", System.Globalization.CultureInfo.GetCultureInfo("da-DK"), 10, 0));
        Assert.Contains($"fetched {day}.", s.SitePlan(dtm, "Prøvevej 1", System.Globalization.CultureInfo.GetCultureInfo("en-GB"), 10, 0));
    }

    [Fact]
    public void The_stored_terrain_gives_the_same_grid_back()
    {
        var dtm = Terrain();
        var back = SiteFetcher.FromGrid(TerrainGrid.Parse(SiteFetcher.TerrainCsv(dtm, X, Y)), X, Y);
        Assert.Equal((dtm.X0, dtm.Y0, dtm.Dx, dtm.W, dtm.H), (Math.Round(back.X0, 6), Math.Round(back.Y0, 6), back.Dx, back.W, back.H));
        Assert.Equal(dtm.Z, back.Z.Select(v => (float)Math.Round(v, 4)));
    }

    [Theory]
    [InlineData(10, 2.5)]
    [InlineData(30, 0)]
    public void Drawn_again_from_the_model_it_is_the_drawing_Load_from_Address_makes(double margin, double setback)
    {
        var s = Sample();
        var dtm = Terrain();
        var da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");
        var direct = SiteplanDxf.Write(s.Lots, s.Buildings, s.Roads, dtm, Surroundings.Window(LotRing, X, Y, margin), X, Y, "Prøvevej 1", da, setback,
            fetchedUtc: Fetched);
        var again = Surroundings.Parse(s.ToJson()).SitePlan(TerrainGrid.Parse(SiteFetcher.TerrainCsv(dtm, X, Y)), "Prøvevej 1", da, margin, setback);
        Assert.Equal(direct, again);
    }

    [Fact]
    public void Drawn_again_from_the_project_s_origin_with_the_terrain_kept_from_it()
    {
        // An origin 2 m east and 2 m south of the address point (set before the address point moved, say): the terrain
        // kept from it, and the drawing as Load from Address makes it from there.
        var s = Sample();
        var dtm = Terrain();
        var origin = (E: 1112.0, N: 2213.0);
        var da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");
        var direct = SiteplanDxf.Write(s.Lots, s.Buildings, s.Roads, dtm, Surroundings.Window(LotRing, X, Y, 10), X, Y, "Prøvevej 1", da, 2.5,
            fetchedUtc: Fetched, origin: origin);
        var again = Surroundings.Parse(s.ToJson()).SitePlan(TerrainGrid.Parse(SiteFetcher.TerrainCsv(dtm, origin.E, origin.N)), "Prøvevej 1", da, 10, 2.5,
            origin: origin);
        Assert.Equal(direct, again);
        Assert.Contains("NULPUNKT", again);
    }

    [Fact]
    public void The_roofs_on_the_lot_from_the_surface()
    {
        // The house (1105-1115, 2210-2220) with a gable roof: 15 m at its long sides, 18 m along its middle; the
        // surface is the terrain everywhere else. A shed on the neighbour's lot isn't the lot's.
        var dtm = Terrain();
        var z = (float[])dtm.Z.Clone();
        for (var j = 0; j < dtm.H; j++)
        {
            for (var i = 0; i < dtm.W; i++)
            {
                var (cx, cy) = (dtm.X0 + (i + 0.5) * dtm.Dx, dtm.Y0 - (j + 0.5) * dtm.Dy);
                if (cx > 1105 && cx < 1115 && cy > 2210 && cy < 2220)
                {
                    z[j * dtm.W + i] = (float)(15 + (5 - Math.Abs(cx - 1110)) * 0.6);
                }
            }
        }
        var dsm = dtm with { Z = z };
        var shed = F([(1125, 2210), (1130, 2210), (1130, 2215), (1125, 2215), (1125, 2210)]);
        var roofs = SiteFetcher.Roofs([.. Sample().Buildings, shed], [LotRing], dtm, dsm);
        var roof = Assert.Single(roofs);
        Assert.Equal((1110.0, 2215.0), (roof.X, roof.Y));
        Assert.InRange(roof.Ridge, 17.7, 18.0);
        Assert.InRange(roof.Eave!.Value, 15.0, 15.6);
    }

    [Fact]
    public void The_terrain_is_cut_from_the_larger_grid_cell_for_cell()
    {
        // The context's box fetched once; the terrain CSV's box (10 cells in, 5 down, 20 × 30) cut from it unchanged.
        var big = Terrain();
        var cut = SiteFetcher.Crop(big, (1095 + 0.4 * 10, 2235 - 0.4 * 35, 1095 + 0.4 * 30, 2235 - 0.4 * 5));
        Assert.Equal((20, 30), (cut.W, cut.H));
        Assert.Equal((1099.0, 2233.0), (Math.Round(cut.X0, 6), Math.Round(cut.Y0, 6)));
        for (var r = 0; r < cut.H; r++)
        {
            for (var c = 0; c < cut.W; c++)
            {
                Assert.Equal(big.Z[(5 + r) * big.W + 10 + c], cut.Z[r * cut.W + c]);
            }
        }
    }

    // A w × h m lot with a corner at 0,0.
    private static List<Pt> Lot(double w, double h) => [(0, 0), (w, 0), (w, h), (0, h), (0, 0)];

    // The calls a load makes to the height model: terrain and surface, each in as many parts as its box needs.
    private static int HeightCalls(List<Pt> lot, double cell) =>
        2 * SiteFetcher.PlanHeights(lot, cell).Parts;

    [Theory]
    [InlineData(20, 30, 40, 2)]    // an ordinary lot: the whole context box, one call per layer
    [InlineData(80, 80, 40, 2)]    // 400 × 400 cells on the 0.4 m grid: still one call each
    [InlineData(136, 153, 10, 4)]  // a large lot: the terrain needs 2 parts, so the box stops where a third would start
    [InlineData(300, 300, 10, 8)]
    public void Calls_per_load_by_the_lots_size(double w, double h, double margin, int calls)
    {
        Assert.Equal(margin, SiteFetcher.HeightMargin(Lot(w, h), 0.4));
        Assert.Equal(calls, HeightCalls(Lot(w, h), 0.4));
    }

    [Fact]
    public void Never_more_parts_than_the_terrain_alone_nor_more_points_than_allowed()
    {
        // Every lot the terrain can be fetched for, from 10 to 400 m a side, square and long.
        for (var w = 10.0; w <= 400; w += 7)
        {
            foreach (var h in new[] { w, w / 2, w * 1.5 })
            {
                var lot = Lot(w, h);
                if (!SiteFetcher.PlanTerrain(lot, 0.4).Fits)
                {
                    continue;
                }
                var heights = SiteFetcher.PlanHeights(lot, 0.4);
                var terrainParts = SiteFetcher.PlanTerrain(lot, 0.4).Parts;
                Assert.True(heights.Parts <= terrainParts, $"{w} × {h} m: {heights.Parts} parts");
                Assert.True(heights.Fits, $"{w} × {h} m: {heights.Points} points");
                // And as far as that allows: the next step out (40, 30, 20, 10, then the terrain's 5) would break one of the two.
                var margin = SiteFetcher.HeightMargin(lot, 0.4);
                if (margin < 40)
                {
                    var next = margin == 5 ? 10 : margin + 10;
                    var further = SiteFetcher.Plan(lot, next, 0.4);
                    Assert.False(further.Fits && further.Parts <= terrainParts, $"{w} × {h} m: {margin} m, but {next} m fits");
                }
            }
        }
    }

    [Theory]
    [InlineData(20, 30, 0.4, true)]     // an ordinary lot: the context from the load, as Context Buildings would fetch it
    [InlineData(136, 153, 0.4, false)]  // the load's ring is smaller: none kept, the button fetches the whole one
    [InlineData(200, 200, 0.4, true)]   // the terrain in 4 parts anyway: the whole ring fits in as many
    [InlineData(200, 200, 0.8, false)]  // a coarser grid chosen: none kept, the button fetches it at 0.4 m
    [InlineData(350, 350, 0.8, true)]   // too large for 0.4 m round it: the button would take 0.8 m too, so kept
    public void The_load_keeps_the_context_only_as_good_as_the_buttons(double w, double h, double cell, bool kept)
    {
        Assert.Equal(kept, SiteFetcher.ContextFromFetch(Lot(w, h), cell));
    }

    [Fact]
    public void The_roofs_are_kept_and_drawn_on_their_own_layer()
    {
        var s = Sample() with { Roofs = [new SiteFetcher.RoofLevels(1110, 2215, 14.37, 11.2)] };
        var back = Surroundings.Parse(s.ToJson());
        Assert.Equal(s.Roofs, back.Roofs);
        var da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");
        var dxf = back.SitePlan(null, "Prøvevej 1", da, 10, 0);
        Assert.Contains("\n8\nTAGKOTER\n", dxf);
        Assert.Contains("0\nTEXT\n8\nTAGKOTER\n", dxf);
        // The number only, the ridge inside the house: on the plan and in the legend's sample.
        Assert.Equal(2, Lines(dxf, "14,37"));
        Assert.Equal(0, Lines(dxf, "11,20"));
        Assert.Contains("Tagkoter: OK tag (m DVR90, ca.)", dxf);
        Assert.Contains("Tag: OK tag", dxf);
        // Asked for, the ridge over the eaves, where they fit (here they do).
        var both = back.SitePlan(null, "Prøvevej 1", da, 10, 0, roofLabels: new(25, false, true));
        Assert.Equal(2, Lines(both, "14,37"));
        Assert.Equal(2, Lines(both, "11,20"));
        Assert.Contains("Tagkoter: OK tag over tagfod (m DVR90, ca.)", both);
    }

    // How many texts are $s.
    private static int Lines(string dxf, string s) => dxf.Split($"\n1\n{s}\n").Length - 1;

    [Fact]
    public void A_roof_level_stands_in_its_building_along_its_long_side()
    {
        // A house 20 m long and 6 m wide, its long side 30° from east: the two lines inside it, turned 30°; turned
        // the other way round (-150°), the same, so it reads from below.
        static List<Pt> House(double deg)
        {
            var (c, s) = (Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180));
            List<Pt> ring = [.. new (double U, double V)[] { (-10, -3), (10, -3), (10, 3), (-10, 3), (-10, -3) }
                .Select(p => (Pt)(1110 + p.U * c - p.V * s, 2215 + p.U * s + p.V * c))];
            return ring;
        }
        foreach (var deg in new[] { 30.0, -150.0 })
        {
            var ((mx, my), angle, longSide, shortSide) = Geometry.LongBox(House(deg));
            Assert.Equal((1110.0, 2215.0, 30.0, 20.0, 6.0), (Math.Round(mx, 6), Math.Round(my, 6), Math.Round(angle, 6), Math.Round(longSide, 6), Math.Round(shortSide, 6)));
        }
        var s = Sample() with { Buildings = [F(House(30))], Roofs = [new SiteFetcher.RoofLevels(1110, 2215, 14.37, 11.2)] };
        var dxf = s.SitePlan(null, "Prøvevej 1", System.Globalization.CultureInfo.GetCultureInfo("da-DK"), 10, 0, roofLabels: new(25, false, true));
        Assert.Contains("\n1\n14,37\n50\n30.00\n", dxf);
        Assert.Contains("\n1\n11,20\n50\n30.00\n", dxf);
    }

    [Fact]
    public void Roof_levels_are_filtered_from_what_is_kept()
    {
        // A 9 m² shed beside the 100 m² house, each with its roof: by default the shed gets none (Config's 25 m²), with
        // 0 it does. Its area comes from the kept buildings, nothing fetched.
        var s = Sample() with
        {
            Buildings = [.. Sample().Buildings, F([(1102, 2224), (1105, 2224), (1105, 2227), (1102, 2227), (1102, 2224)])],
            Roofs = [new SiteFetcher.RoofLevels(1110, 2215, 14.37, 11.2), new SiteFetcher.RoofLevels(1103.5, 2225.5, 12.5, 11)],
        };
        var da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");
        var normal = s.SitePlan(null, "Prøvevej 1", da, 10, 0);
        Assert.Equal(2, Lines(normal, "14,37"));
        Assert.Equal(0, Lines(normal, "12,50"));
        Assert.Equal(1, Lines(s.SitePlan(null, "Prøvevej 1", da, 10, 0, roofLabels: new(0, false, true)), "12,50"));
        // The ridge alone: the numbers, the legend row and the note say so.
        var ridge = s.SitePlan(null, "Prøvevej 1", da, 10, 0, roofLabels: new(25, true, true));
        Assert.Equal(2, Lines(ridge, "14,37"));
        Assert.Equal(0, Lines(ridge, "11,20"));
        Assert.Contains("Tagkoter: OK tag (m DVR90, ca.)", ridge);
        Assert.Contains("Tag: OK tag i DVR90", ridge);
        // Eight roofs on a building half a metre across, with no room for any: left out, or (as before) put on its
        // middle on one line anyway.
        var tiny = F([(1103.25, 2225.25), (1103.75, 2225.25), (1103.75, 2225.75), (1103.25, 2225.75), (1103.25, 2225.25)]);
        var crowded = Sample() with
        {
            Buildings = [.. Sample().Buildings, tiny],
            Roofs = [.. Enumerable.Range(0, 8).Select(i => new SiteFetcher.RoofLevels(1103.5, 2225.5, 50 + i, 10))],
        };
        // The roofs' own ridges (no other text on the plan is 50-57).
        static int Count(string dxf) => System.Text.RegularExpressions.Regex.Matches(dxf, "\n1\n5[0-7],00( / 10,00)?\n").Count;
        Assert.Equal(0, Count(crowded.SitePlan(null, "Prøvevej 1", da, 10, 0, roofLabels: new(0, true, true))));
        Assert.Equal(8, Count(crowded.SitePlan(null, "Prøvevej 1", da, 10, 0, roofLabels: new(0, false, false))));
    }

    [Fact]
    public void Without_roofs_no_layer_row_or_note()
    {
        // Kept before the roofs were (the field missing), or none on the lot: the drawing as before.
        var old = Sample().ToJson().Replace(",\"roofs\":[]", "");
        Assert.DoesNotContain("roofs", old);
        var dxf = Surroundings.Parse(old).SitePlan(null, "Prøvevej 1", System.Globalization.CultureInfo.GetCultureInfo("da-DK"), 10, 0);
        Assert.DoesNotContain("\n8\nTAGKOTER\n", dxf);
        Assert.DoesNotContain("Tagkoter", dxf);
        Assert.DoesNotContain("Tag: OK tag", dxf);
    }

    [Fact]
    public void Left_out_layers_are_passed_on_and_go_with_their_legend_rows_and_notes()
    {
        // The roofs, the spot elevations and the setback left out, drawn from the stored surroundings as Export and the
        // redraw do: nothing on them, no layer in the table, no legend row, no roof note.
        var s = Sample() with { Roofs = [new SiteFetcher.RoofLevels(1110, 2215, 14.37, 11.2)] };
        var da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");
        var grid = TerrainGrid.Parse(SiteFetcher.TerrainCsv(Terrain(), X, Y));
        var all = s.SitePlan(grid, "Prøvevej 1", da, 10, 2.5);
        var dxf = s.SitePlan(grid, "Prøvevej 1", da, 10, 2.5, hiddenLayers: ["TAGKOTER", "KOTER", "AFSTAND_SKEL"]);
        foreach (var layer in new[] { "TAGKOTER", "KOTER", "AFSTAND_SKEL" })
        {
            Assert.Contains($"\n8\n{layer}\n", all);
            Assert.DoesNotContain($"\n8\n{layer}\n", dxf);
            Assert.DoesNotContain($"\n2\n{layer}\n", dxf);
        }
        foreach (var text in new[] { "Tagkoter: OK tag (m DVR90, ca.)", "Tag: OK tag", "Terr\\U+00E6nkote (m DVR90)", "m fra skel (tiln" })
        {
            Assert.Contains(text, all);
            Assert.DoesNotContain(text, dxf);
        }
    }

    [Fact]
    public void The_buildings_note_goes_only_with_both_building_layers()
    {
        var s = Sample();
        var da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");
        const string note = "Bygninger er GeoDanmarks tagflader";
        Assert.Contains(note, s.SitePlan(null, "Prøvevej 1", da, 10, 0, hiddenLayers: ["BYGNINGER"]));
        Assert.DoesNotContain(note, s.SitePlan(null, "Prøvevej 1", da, 10, 0, hiddenLayers: ["BYGNINGER", "BYGNINGER_NABO"]));
    }

    [Fact]
    public void Further_than_was_fetched_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample().SitePlan(null, "t", System.Globalization.CultureInfo.InvariantCulture, 60, 0));
    }

    [Fact]
    public void Not_surroundings_is_a_format_error()
    {
        Assert.Throws<FormatException>(() => Surroundings.Parse("{\"version\":2}"));
        Assert.Throws<FormatException>(() => Surroundings.Parse("not json"));
        // Damaged: no lots, a version that's text, a point of one number.
        Assert.Throws<FormatException>(() => Surroundings.Parse("""{"version":1,"x":0,"y":0,"margin":50}"""));
        Assert.Throws<FormatException>(() => Surroundings.Parse("""{"version":"1"}"""));
        Assert.Throws<FormatException>(() => Surroundings.Parse(
            """{"version":1,"x":0,"y":0,"margin":50,"lots":[{"rings":[[[0]]],"props":{}}],"buildings":[],"roads":[]}"""));
    }
}
