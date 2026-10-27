using Jega.Core;

namespace Jega.Core.Tests;

/// <summary>SiteFetcher's parsing and writing, on made-up answers (no network).</summary>
public class SiteFetcherTests
{
    // The drawing in Danish, as a project's default; in English in its own test.
    private static readonly System.Globalization.CultureInfo Da = System.Globalization.CultureInfo.GetCultureInfo("da-DK");

    private const string Gml = """
        <wfs:FeatureCollection xmlns:wfs="http://www.opengis.net/wfs/2.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:mat="m">
          <wfs:member><mat:Jordstykke_Gaeldende>
            <mat:matrikelnummer>12a</mat:matrikelnummer>
            <mat:geometri><gml:Polygon srsDimension="2"><gml:exterior><gml:LinearRing>
              <gml:posList>100 200 120 200 120 230 100 230 100 200</gml:posList>
            </gml:LinearRing></gml:exterior></gml:Polygon></mat:geometri>
          </mat:Jordstykke_Gaeldende></wfs:member>
          <wfs:member><mat:Jordstykke_Gaeldende>
            <mat:matrikelnummer>12b</mat:matrikelnummer>
            <mat:geometri><gml:Polygon srsDimension="3"><gml:exterior><gml:LinearRing>
              <gml:posList>120 200 0 140 200 0 140 230 0 120 230 0 120 200 0</gml:posList>
            </gml:LinearRing></gml:exterior></gml:Polygon></mat:geometri>
          </mat:Jordstykke_Gaeldende></wfs:member>
        </wfs:FeatureCollection>
        """;

    [Fact]
    public void Gml_gives_rings_and_properties_in_2d()
    {
        var lots = SiteFetcher.ParseGml(Gml);
        Assert.Equal(2, lots.Count);
        Assert.Equal("12a", lots[0].Props["matrikelnummer"]);
        Assert.Equal(5, lots[0].Rings[0].Count);
        Assert.Equal((140.0, 200.0), lots[1].Rings[0][1]);  // the 3D one read in steps of three
    }

    // A minimal GeoTIFF as the WCS answers: little-endian, one strip, float32, pixel size and tie point.
    [Fact]
    public void A_short_tiff_is_a_plain_fetch_error()
    {
        // 3 × 2 by its tags, 4 values in its strip: refused here, not an index error later in Elevation.
        Assert.Throws<FetchException>(() => SiteFetcher.ParseTiff(Tiff(3, 2, 100, 200.8, [10, 11, 12, 13])));
    }

    private static byte[] Tiff(int w, int h, double x0, double y0, float[] z)
    {
        var tags = new List<(ushort Tag, ushort Type, int Count, byte[] Data)>
        {
            (256, 4, 1, BitConverter.GetBytes(w)), (257, 4, 1, BitConverter.GetBytes(h)), (258, 3, 1, BitConverter.GetBytes((int)32)),
            (259, 3, 1, BitConverter.GetBytes((int)1)), (273, 4, 1, []), (279, 4, 1, BitConverter.GetBytes(z.Length * 4)),
            (339, 3, 1, BitConverter.GetBytes((int)3)),
            (33550, 12, 3, [.. BitConverter.GetBytes(0.4), .. BitConverter.GetBytes(0.4), .. BitConverter.GetBytes(0.0)]),
            (33922, 12, 6, [.. new[] { 0.0, 0.0, 0.0, x0, y0, 0.0 }.SelectMany(BitConverter.GetBytes)]),
        };
        var ifdSize = 2 + tags.Count * 12 + 4;
        var extra = new List<byte>();
        var dataAt = 8 + ifdSize;
        var extraOffsets = new Dictionary<ushort, int>();
        foreach (var t in tags.Where(t => t.Data.Length > 4))
        {
            extraOffsets[t.Tag] = dataAt + extra.Count;
            extra.AddRange(t.Data);
        }
        var pixels = dataAt + extra.Count;
        var file = new List<byte> { (byte)'I', (byte)'I', 42, 0 };
        file.AddRange(BitConverter.GetBytes(8));
        file.AddRange(BitConverter.GetBytes((ushort)tags.Count));
        foreach (var t in tags)
        {
            file.AddRange(BitConverter.GetBytes(t.Tag));
            file.AddRange(BitConverter.GetBytes(t.Type));
            file.AddRange(BitConverter.GetBytes(t.Count));
            file.AddRange(t.Tag == 273 ? BitConverter.GetBytes(pixels) : extraOffsets.TryGetValue(t.Tag, out var o) ? BitConverter.GetBytes(o) : t.Data.Take(4).ToArray());
        }
        file.AddRange(BitConverter.GetBytes(0));
        file.AddRange(extra);
        file.AddRange(z.SelectMany(BitConverter.GetBytes));
        return [.. file];
    }

    [Fact]
    public void Tiff_and_csv_give_a_grid_core_reads()
    {
        // 3 × 2 cells from (100, 200.8) top left: rows from the top, so the CSV starts with the bottom row.
        var dtm = SiteFetcher.ParseTiff(Tiff(3, 2, 100, 200.8, [10, 11, 12, 13, 14, 15]));
        Assert.Equal((3, 2, 0.4), (dtm.W, dtm.H, dtm.Dx));

        var csv = SiteFetcher.TerrainCsv(dtm, 100, 200);
        Assert.StartsWith("200.0,200.0,13000.0\n600.0,200.0,14000.0\n", csv);
        var grid = TerrainGrid.Parse(csv);
        Assert.Equal(6, grid.Count);
        Assert.Equal(10000, grid.ElevationAt(200, 600), 3);
    }

    [Fact]
    public void A_hole_in_the_terrain_fails()
    {
        var dtm = SiteFetcher.ParseTiff(Tiff(2, 2, 0, 0.8, [10, -9999, 12, 13]));
        Assert.Throws<FetchException>(() => SiteFetcher.TerrainCsv(dtm, 0, 0));
    }

    [Fact]
    public void Dxf_has_the_lot_on_its_layer_in_mm_from_the_address_point()
    {
        var lots = SiteFetcher.ParseGml(Gml);
        var dxf = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da);
        Assert.StartsWith("0\nSECTION\n2\nHEADER\n", dxf);
        Assert.EndsWith("0\nEOF\n", dxf);
        // The own lot (the point inside 12a) on GRUND, its corner 10 m west and 15 m south of the point.
        Assert.Contains("0\nVERTEX\n8\nGRUND\n10\n-10000.000\n20\n-15000.000", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nSKEL\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nAFSTAND_SKEL\n", dxf);                  // the setback line, dashed
        Assert.Contains("2\nAFSTAND_SKEL\n70\n0\n62\n6\n6\nDASHED", dxf);
        Assert.Contains("0\nTEXT\n8\nSKELMAAL\n", dxf);                           // the lot's lengths
        Assert.Contains("\n1\n12b\n", dxf);                                       // the neighbour's parcel number
        Assert.Contains("Pr\\U+00F8vevej", dxf);  // R12: æøå escaped
        Assert.Contains(" m\\U+00B2)", dxf);       // the lot's area in m², and no mangled "Â²" from a wrong encoding
        Assert.DoesNotContain("\\U+00C2", dxf);
        foreach (var table in new[] { "VPORT", "LTYPE", "LAYER", "STYLE", "VIEW", "UCS", "APPID" })
        {
            Assert.Contains($"0\nTABLE\n2\n{table}\n", dxf);
        }
        Assert.Contains("\n1\nSituationsplan: Pr\\U+00F8vevej 1\n", dxf);  // the title, in Danish
        Assert.Contains("\n1\nSkelm\\U+00E5l (m)\n", dxf);                  // real æøå, no "Skelmaal"
    }

    [Fact]
    public void Left_out_layers_are_not_drawn_nor_in_the_legend()
    {
        // The boundary lengths and the neighbours' boundaries left out: not drawn, no legend row, no empty layer in the
        // table. The lot, the frame and the texts asked to go too, but are drawn: the drawing can't do without them.
        var lots = SiteFetcher.ParseGml(Gml);
        var dxf = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da,
            hiddenLayers: ["SKELMAAL", "SKEL", "GRUND", "RAMME", "TEKST"]);
        Assert.DoesNotContain("\n8\nSKELMAAL\n", dxf);
        Assert.DoesNotContain("\n8\nSKEL\n", dxf);
        Assert.DoesNotContain("\n2\nSKELMAAL\n", dxf);                           // nor in the layer table
        Assert.DoesNotContain("\n1\nSkelm\\U+00E5l (m)\n", dxf);                  // nor in the legend
        Assert.DoesNotContain("\n1\nMatrikelskel, nabogrunde\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nGRUND\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nRAMME\n", dxf);
        Assert.Contains("\n1\n12b\n", dxf);                                       // the neighbour's number, on TEKST
        Assert.Contains("\n1\nMatrikelskel, grunden\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nAFSTAND_SKEL\n", dxf);                  // the rest as before
    }

    [Fact]
    public void Paths_edges_are_on_their_own_layer()
    {
        // GeoDanmark's Vejkant by its type: a path's edge on STI, a road's (and a cycle track along it) on VEJKANT, and
        // the legend's row for paths only when there is one on the plan.
        var lots = SiteFetcher.ParseGml(Gml);
        static SiteFetcher.Feature Edge(string type, double y) => new([[(95, y), (145, y)]], new() { ["type"] = type });
        var dxf = SiteplanDxf.Write(lots, [], [Edge("Kørevej", 195), Edge("Sti, diverse", 230), Edge("Hovedsti", 232), Edge("Cykelsti langs vej", 197)],
            null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da);
        Assert.Contains("0\nPOLYLINE\n8\nVEJKANT\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nSTI\n", dxf);
        Assert.Contains("2\nSTI\n70\n0\n62\n94\n", dxf);                         // its colour in the layer table
        Assert.Contains("\n1\nStikant\n", dxf);
        // 4 edges + the legend's sample each: 3 on VEJKANT, 3 on STI.
        Assert.Equal(3, dxf.Split("0\nPOLYLINE\n8\nVEJKANT\n").Length - 1);
        Assert.Equal(3, dxf.Split("0\nPOLYLINE\n8\nSTI\n").Length - 1);

        var roads = SiteplanDxf.Write(lots, [], [Edge("Kørevej", 195)], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da);
        Assert.DoesNotContain("\n8\nSTI\n", roads);
        Assert.DoesNotContain("\n1\nStikant\n", roads);
        Assert.Contains("\n1\nVejkant\n", roads);
    }

    // GeoDanmark's manhole covers and gullies: points (gml:pos) with a level.
    private const string PointsGml = """
        <wfs:FeatureCollection xmlns:wfs="http://www.opengis.net/wfs/2.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:gdk60="g">
          <wfs:member><gdk60:Broenddaeksel gml:id="gdk60.1">
            <gdk60:status>Anlagt</gdk60:status>
            <gdk60:geometri><gml:Point srsDimension="3"><gml:pos>112.5 196.25 2.36</gml:pos></gml:Point></gdk60:geometri>
          </gdk60:Broenddaeksel></wfs:member>
          <wfs:member><gdk60:Broenddaeksel gml:id="gdk60.2">
            <gdk60:geometri><gml:Point srsDimension="3"><gml:pos>500 500 -99</gml:pos></gml:Point></gdk60:geometri>
          </gdk60:Broenddaeksel></wfs:member>
        </wfs:FeatureCollection>
        """;

    [Fact]
    public void A_point_is_a_ring_of_one_with_its_level_as_a_property()
    {
        var covers = SiteFetcher.ParseGml(PointsGml);
        Assert.Equal(2, covers.Count);
        Assert.Equal([(112.5, 196.25)], covers[0].Rings[0]);
        Assert.Equal("2.36", covers[0].Props["z"]);
        Assert.Equal("Anlagt", covers[0].Props["status"]);
        Assert.Equal("-99", covers[1].Props["z"]);
    }

    [Fact]
    public void Covers_and_gullies_are_drawn_with_their_level_and_in_the_legend()
    {
        // A cover in the window with its level (2,36 beside a circle on BROENDE), one outside it (not drawn), and a
        // gully (a square on RISTE); the legend's two rows and the note under the drawing; without any, no rows.
        var lots = SiteFetcher.ParseGml(Gml);
        var covers = SiteFetcher.ParseGml(PointsGml);
        var gully = new SiteFetcher.Feature([[(130, 236)]], new() { ["z"] = "2.15" });
        var dxf = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da, manholes: covers, gullies: [gully]);
        Assert.Contains("0\nPOLYLINE\n8\nBROENDE\n", dxf);
        Assert.Contains("0\nTEXT\n8\nBROENDE\n", dxf);
        Assert.Contains("\n1\n2,36\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nRISTE\n", dxf);
        Assert.Contains("\n1\n2,15\n", dxf);
        Assert.Contains("\n1\nBr\\U+00F8ndd\\U+00E6ksel, d\\U+00E6kselkote (m DVR90)\n", dxf);
        Assert.Contains("\n1\nNedl\\U+00F8bsrist, kote (m DVR90)\n", dxf);
        Assert.Contains("Br\\U+00F8ndd\\U+00E6ksler og riste: GeoDanmark", dxf);
        // The cover in the window: its circle (12 vertices) and the legend's sample; the one at (500, 500) not drawn.
        Assert.Equal(2, dxf.Split("0\nPOLYLINE\n8\nBROENDE\n").Length - 1);

        var none = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da);
        Assert.DoesNotContain("\n8\nBROENDE\n", none);
        Assert.DoesNotContain("Nedl\\U+00F8bsrist, kote", none);
        Assert.DoesNotContain("GeoDanmark, set fra luften", none);

        var hidden = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da, manholes: covers, gullies: [gully],
            hiddenLayers: ["BROENDE"]);
        Assert.DoesNotContain("\n8\nBROENDE\n", hidden);
        Assert.DoesNotContain("\n2\nBROENDE\n", hidden);
        Assert.Contains("0\nPOLYLINE\n8\nRISTE\n", hidden);
    }

    // Danmarks Adresseregister's three GraphQL answers (the nodes), cut to what Jega reads: two address points, two
    // numbers on Prøvevej (the second without a building) and a third on a point that isn't there (left out), one street
    // with its name line.
    private const string PointNodes = """
        [{"id_lokalId":"p1","position":{"wkt":"POINT (130.5 210.25)"}}, {"id_lokalId":"p2","position":{"wkt":"POINT (138 226)"}}]
        """;
    private const string NumberNodes = """
        [{"husnummertekst":"12","adgangspunkt":"p1","navngivenVej":"v1","geoDanmarkBygning":"1095215721"},
         {"husnummertekst":"14","adgangspunkt":"p2","navngivenVej":"v1","geoDanmarkBygning":null},
         {"husnummertekst":"16","adgangspunkt":"p9","navngivenVej":"v1","geoDanmarkBygning":null}]
        """;
    private const string StreetNodes = """
        [{"id_lokalId":"v1","vejnavn":"Prøvevej","vejnavnebeliggenhed_vejnavnelinje":{"wkt":"MULTILINESTRING ((95 194.5, 145 194.5), (145 194.5, 150 198))"}}]
        """;

    private static List<SiteFetcher.HouseNumber> Numbers() => SiteFetcher.ParseAddresses(PointNodes, NumberNodes, StreetNodes);

    [Fact]
    public void Dar_s_house_numbers_are_read_with_street_building_and_name_line()
    {
        var numbers = Numbers();
        Assert.Equal(2, numbers.Count);
        Assert.Equal(("12", "Prøvevej", 130.5, 210.25, "1095215721"), (numbers[0].Number, numbers[0].Street, numbers[0].X, numbers[0].Y, numbers[0].BuildingId));
        Assert.Equal(2, numbers[0].StreetLine.Count);
        Assert.Equal([(95.0, 194.5), (145.0, 194.5)], numbers[0].StreetLine[0]);
        Assert.Null(numbers[1].BuildingId);
        Assert.Equal([[(530.0, 6170.5)]], SiteFetcher.WktLines("POINT(530 6170.5)"));
        // A street that isn't among the streets: the number keeps its point, without a name.
        var unnamed = SiteFetcher.ParseAddresses(PointNodes, NumberNodes, "[]");
        Assert.Equal(("", 0), (unnamed[0].Street, unnamed[0].StreetLine.Count));
    }

    [Fact]
    public void House_numbers_stand_in_their_building_and_street_names_along_their_line()
    {
        // 12 in the neighbour's building (GeoDanmark id 1095215721), 14 at its point (no building), the street's name
        // along its line south of the lot; the legend's rows and the note; left out when hidden.
        var lots = SiteFetcher.ParseGml(Gml);
        var house = new SiteFetcher.Feature([[(125, 205), (137, 205), (137, 215), (125, 215), (125, 205)]], new() { ["id.lokalId"] = "1095215721" });
        var numbers = Numbers();
        var dxf = SiteplanDxf.Write(lots, [house], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da, addresses: numbers);
        Assert.Contains("0\nTEXT\n8\nHUSNUMRE\n", dxf);
        Assert.Contains("\n1\n12\n", dxf);
        Assert.Contains("\n1\n14\n", dxf);
        Assert.Contains("0\nTEXT\n8\nVEJNAVNE\n", dxf);
        Assert.Contains("\n1\nPr\\U+00F8vevej\n", dxf);
        Assert.Contains("\n1\nHusnummer\n", dxf);
        Assert.Contains("\n1\nVejnavn\n", dxf);
        Assert.Contains("Vejnavne og husnumre: Danmarks Adresseregister", dxf);
        // The number in the building: its text's middle inside the building's box (mm from the address point).
        var inHouse = System.Text.RegularExpressions.Regex.Match(dxf, "0\nTEXT\n8\nHUSNUMRE\n10\n(-?[0-9.]+)\n20\n(-?[0-9.]+)\n30\n0.0\n40\n[0-9.]+\n1\n12\n");
        Assert.True(inHouse.Success);
        var (tx, ty) = (double.Parse(inHouse.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), double.Parse(inHouse.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.InRange(tx, 15000, 27000);
        Assert.InRange(ty, -10000, 0);

        var hidden = SiteplanDxf.Write(lots, [house], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da, addresses: numbers,
            hiddenLayers: ["HUSNUMRE", "VEJNAVNE"]);
        Assert.DoesNotContain("\n8\nHUSNUMRE\n", hidden);
        Assert.DoesNotContain("\n8\nVEJNAVNE\n", hidden);
        Assert.DoesNotContain("Danmarks Adresseregister. Husnummeret", hidden);
    }

    [Fact]
    public void Trees_and_hedges_are_drawn_and_texts_keep_clear_of_symbols()
    {
        // A tree (a circle with a dot on TRAEER), a tree group's outline, a hedge line on HEGN; the legend's group and
        // rows and the note. A house number whose point is on a manhole cover moves off it: texts keep clear of symbols.
        var lots = SiteFetcher.ParseGml(Gml);
        var tree = new SiteFetcher.Feature([[(100, 235)]], []);
        var grove = new SiteFetcher.Feature([[(92, 192), (98, 192), (98, 198), (92, 198), (92, 192)]], []);
        var hedge = new SiteFetcher.Feature([[(120, 236), (140, 236)]], []);
        var dxf = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da, trees: [tree], treeGroups: [grove], hedges: [hedge]);
        Assert.Contains("0\nPOLYLINE\n8\nTRAEER\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nHEGN\n", dxf);
        Assert.Contains("\n1\nBEPLANTNING\n", dxf);
        Assert.Contains("\n1\nTr\\U+00E6; tr\\U+00E6gruppe og skov som omrids\n", dxf);
        Assert.Contains("\n1\nHegn\n", dxf);
        Assert.Contains("Tr\\U+00E6er og hegn: GeoDanmark", dxf);

        var cover = new SiteFetcher.Feature([[(138, 226)]], new() { ["z"] = "2.36" });
        var numbers = Numbers();  // "14" has its point at (138, 226), on the cover
        var moved = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da, manholes: [cover], addresses: numbers);
        var at = System.Text.RegularExpressions.Regex.Match(moved, "0\nTEXT\n8\nHUSNUMRE\n10\n(-?[0-9.]+)\n20\n(-?[0-9.]+)\n30\n0.0\n40\n[0-9.]+\n1\n14\n");
        Assert.True(at.Success);
        Assert.NotEqual("11000.000", at.Groups[2].Value);  // not at y = 226 - 215 = 11 m: moved up or down, off the cover
    }

    [Fact]
    public void A_layer_whose_drawing_fails_is_left_out_and_reported()
    {
        // A cover without properties makes its step throw: the plan comes without BROENDE and says so; the rest as before.
        var lots = SiteFetcher.ParseGml(Gml);
        var broken = new SiteFetcher.Feature([[(130, 230)]], null!);
        var (dxf, skipped) = SiteplanDxf.WriteWithReport(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da,
            manholes: [broken], gullies: [new SiteFetcher.Feature([[(131, 231)]], new() { ["z"] = "2.1" })]);
        Assert.Equal(["BROENDE"], skipped);
        Assert.DoesNotContain("\n8\nBROENDE\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nRISTE\n", dxf);
        Assert.Contains("0\nPOLYLINE\n8\nGRUND\n", dxf);
        Assert.Empty(SiteplanDxf.WriteWithReport(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da).SkippedLayers);
    }

    [Fact]
    public void The_drawing_is_in_the_language_it_is_given()
    {
        // The same drawing in English: its texts, and numbers with a decimal point; the layers' names the same.
        var lots = SiteFetcher.ParseGml(Gml);
        var en = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        Assert.Contains("\n1\nSite plan: Pr\\U+00F8vevej 1\n", en);
        Assert.Contains("\n1\nLEGEND\n", en);
        Assert.Contains("\n1\n21.99\n", en);                                   // the boundary lengths' sample
        Assert.Contains("The drawing's 0,0 = the address point", en);
        Assert.Contains("0\nTEXT\n8\nSKELMAAL\n", en);
        Assert.DoesNotContain("SIGNATURFORKLARING", en);
        // In Danish, a decimal comma.
        Assert.Contains("\n1\n21,99\n", SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da));
    }

    [Fact]
    public void Without_an_origin_the_drawing_s_0_0_is_the_address_point()
    {
        // The lot's corner (100, 200) drawn from the address point, and the legend and notes say which point 0,0 is.
        var lots = SiteFetcher.ParseGml(Gml);
        var atAddress = SiteplanDxf.Write(lots, [], [], null, (90, 190, 150, 240), 110, 215, "Prøvevej 1", Da);
        Assert.Contains("0\nVERTEX\n8\nGRUND\n10\n-10000.000\n20\n-15000.000", atAddress);
        Assert.Contains("\n1\nTegningens 0,0 (adressepunktet)\n", atAddress);
        Assert.Contains("Tegningens 0,0 = adressepunktet (adressens officielle punkt i adresseregistret): E ", atAddress);
    }

    [Fact]
    public void The_legend_follows_the_frame()
    {
        // The scale follows the frame: 60 × 50 m fits A3 at 1:200 (76 × 50 m), so 10 mm on paper is 2 m; 80 m wide
        // it doesn't, so 1:500 and 5 m. The legend starts that far right of the frame and moves with it.
        var lots = SiteFetcher.ParseGml(Gml);
        foreach (var (right, gap) in new[] { (150.0, 2.0), (170.0, 5.0) })
        {
            var dxf = SiteplanDxf.Write(lots, [], [], null, (90, 190, right, 240), 110, 215, "Prøvevej 1", Da);
            var x = ((right + gap - 110) * 1000).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains($"0\nTEXT\n8\nTEKST\n10\n{x}\n", dxf);
            Assert.Contains("\n1\nSIGNATURFORKLARING\n", dxf);
        }
    }

    [Fact]
    public void A_label_lands_inside_a_bent_road_parcel()
    {
        // An L-shaped strip 4 m wide round a corner: its vertex average falls in the bend, outside the strip.
        List<(double X, double Y)> road = [(0, 0), (30, 0), (30, 4), (4, 4), (4, 30), (0, 30), (0, 0)];
        var (cx, cy) = Geometry.Centre(road);
        Assert.False(Geometry.Inside(road, cx, cy));
        var (lx, ly) = Geometry.LabelPoint(road);
        Assert.True(Geometry.Inside(road, lx, ly));
        // An ordinary lot keeps its middle.
        List<(double X, double Y)> lot = [(0, 0), (20, 0), (20, 30), (0, 30), (0, 0)];
        Assert.Equal(Geometry.Centre(lot), Geometry.LabelPoint(lot));
    }

    [Fact]
    public void The_lot_centre_is_its_centre_of_area_or_inside_it()
    {
        // A rectangle with an extra vertex on one side: the centre of area, not the vertex average.
        List<(double X, double Y)> lot = [(0, 0), (10, 0), (20, 0), (20, 30), (0, 30), (0, 0)];
        var (cx, cy) = Geometry.LotCentre(lot);
        Assert.Equal((10.0, 15.0), (Math.Round(cx, 6), Math.Round(cy, 6)));
        // Open or clockwise, the same.
        Assert.Equal((10.0, 15.0), Geometry.LotCentre([(0, 30), (20, 30), (20, 0), (0, 0)]) is var (ox, oy) ? (Math.Round(ox, 6), Math.Round(oy, 6)) : default);
        // An L whose centre of area falls in the bend, outside it: a point inside instead.
        List<(double X, double Y)> bent = [(0, 0), (30, 0), (30, 4), (4, 4), (4, 30), (0, 30), (0, 0)];
        var (lx, ly) = Geometry.LotCentre(bent);
        Assert.True(Geometry.Inside(bent, lx, ly));
    }

    [Fact]
    public void A_neighbour_s_boundary_leaves_out_what_it_shares_with_the_lot()
    {
        // The lot 0..20 × 0..30 with its south-east corner cut; the neighbour east of it shares x = 20 from y = 10 to 30,
        // and the corner (20, 10), as Matriklen's parcels do. Its other edges stay, as one open line.
        List<(double X, double Y)> lot = [(0, 0), (15, 0), (20, 10), (20, 30), (0, 30), (0, 0)];
        List<(double X, double Y)> east = [(20, 5), (40, 5), (40, 30), (20, 30), (20, 10), (20, 5)];
        var parts = Geometry.Unshared(east, [lot], 0.01);
        Assert.NotNull(parts);
        Assert.Equal([(20.0, 10.0), (20.0, 5.0), (40.0, 5.0), (40.0, 30.0), (20.0, 30.0)], Assert.Single(parts));
        // A neighbour that only touches at a corner, or not at all: null, so it stays one closed ring.
        Assert.Null(Geometry.Unshared([(20, 30), (40, 30), (40, 50), (20, 50), (20, 30)], [lot], 0.01));
        // A parcel the lot's boundary wholly runs along (a hole in it, or the same ring): nothing drawn as a neighbour.
        Assert.Empty(Geometry.Unshared(lot, [lot], 0.01)!);
    }

    [Fact]
    public void A_parcel_inside_another_is_a_hole_in_it()
    {
        // One parcel inside another: the outer parcel's GML has the inner one as a hole. A 20 × 30 m
        // lot with a 4 × 5 m hole: 580 m², not 600, and a point in the hole is on the inner parcel, not the outer.
        List<(double X, double Y)> outline = [(0, 0), (20, 0), (20, 30), (0, 30), (0, 0)];
        List<(double X, double Y)> hole = [(8, 10), (8, 15), (12, 15), (12, 10), (8, 10)];
        var outer = new SiteFetcher.Feature([outline, hole], new() { ["matrikelnummer"] = "1a", ["registreretAreal"] = "580" });
        var inner = new SiteFetcher.Feature([hole], new() { ["matrikelnummer"] = "1b" });
        Assert.Equal(580, Geometry.Area(outer.Rings), 6);
        Assert.Equal(20, Geometry.Area(inner.Rings), 6);
        Assert.False(Geometry.Inside(outer.Rings, 10, 12));
        Assert.True(Geometry.Inside(outer.Rings, 2, 2));
        var s = new Surroundings(10, 12, 50, [outer, inner], [], []);
        Assert.Equal(hole, s.Lot);
        Assert.Equal(outline, (s with { X = 2, Y = 2 }).Lot);
        // The note names the parcel left out; without one inside, it says nothing of it.
        var dxf = SiteplanDxf.Write([outer, inner], [], [], null, (-10, -10, 30, 40), 2, 2, "Prøvevej 1", Da);
        Assert.Contains("(+0,0 %);", dxf);
        Assert.Contains("medregnet", dxf);
        var alone = SiteplanDxf.Write([outer with { Rings = [outline] }], [], [], null, (-10, -10, 30, 40), 2, 2, "Prøvevej 1", Da);
        Assert.DoesNotContain("medregnet", alone);
    }

    [Fact]
    public void Lines_are_cut_to_the_window()
    {
        var pieces = Geometry.ClipLine([(-10, 5), (20, 5)], (0, 0, 10, 10));
        Assert.Single(pieces);
        Assert.Equal([(0.0, 5.0), (10.0, 5.0)], pieces[0]);
        Assert.Empty(Geometry.ClipRing([(20, 20), (30, 20), (30, 30)], (0, 0, 10, 10)));
    }
}
