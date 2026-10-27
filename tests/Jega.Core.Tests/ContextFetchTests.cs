using Jega.Core;
using Pt = (double X, double Y);

namespace Jega.Core.Tests;

/// <summary>SiteFetcher.ContextJson on a made-up site (no network): read back by Context.Parse, as the masses do.</summary>
public class ContextFetchTests
{
    // 40 × 40 cells of 0.4 m from (1000, 2016) down to (1016, 2000): the terrain flat at 10 m, roofs drawn into the surface.
    private static (SiteFetcher.Dtm Dtm, SiteFetcher.Dtm Dsm) Site(params (double X1, double Y1, double X2, double Y2, float Top)[] roofs)
    {
        const int n = 40;
        var terrain = Enumerable.Repeat(10f, n * n).ToArray();
        var surface = (float[])terrain.Clone();
        for (var j = 0; j < n; j++)
        {
            for (var i = 0; i < n; i++)
            {
                var (x, y) = (1000 + (i + 0.5) * 0.4, 2016 - (j + 0.5) * 0.4);
                foreach (var r in roofs.Where(r => x > r.X1 && x < r.X2 && y > r.Y1 && y < r.Y2))
                {
                    surface[j * n + i] = r.Top;
                }
            }
        }
        return (new(1000, 2016, 0.4, 0.4, n, n, terrain), new(1000, 2016, 0.4, 0.4, n, n, surface));
    }

    private static SiteFetcher.Feature Building(double x1, double y1, double x2, double y2, string? bbr = null) =>
        new([[(x1, y1), (x2, y1), (x2, y2), (x1, y2), (x1, y1)]], bbr is null ? [] : new() { ["BBRUUID"] = bbr });

    private static readonly List<Pt> Lot = [(1000, 2000), (1008, 2000), (1008, 2016), (1000, 2016)];
    private static readonly (double, double, double, double) Box = (1000, 2000, 1016, 2016);

    [Fact]
    public void Buildings_get_ground_top_and_own()
    {
        var (dtm, dsm) = Site((1002, 2002, 1006, 2006, 16f), (1010, 2010, 1014, 2014, 13.5f));
        var json = SiteFetcher.ContextJson([Building(1002, 2002, 1006, 2006, "h"), Building(1010, 2010, 1014, 2014)], dtm, dsm, Lot, Box, 1004, 2004);
        var buildings = Context.Parse(json);

        Assert.Equal(2, buildings.Count);
        var (house, neighbour) = (buildings[0], buildings[1]);
        Assert.True(house.Own);
        Assert.False(neighbour.Own);
        Assert.Equal("h", house.Bbr);
        Assert.Equal((10000.0, 16000.0), (house.Base, house.Top));
        Assert.Equal(13500, neighbour.Top);
        // mm from the address point, the closing corner left out.
        Assert.Equal([new(-2000, -2000), new(2000, -2000), new(2000, 2000), new(-2000, 2000)], house.Ring);
    }

    [Fact]
    public void From_the_project_s_origin()
    {
        // The outlines in mm from the origin, 1 m west and 1 m north of the address point; the file says which.
        var (dtm, dsm) = Site((1002, 2002, 1006, 2006, 16f));
        var json = SiteFetcher.ContextJson([Building(1002, 2002, 1006, 2006)], dtm, dsm, Lot, Box, 1004, 2004, (1003, 2005));
        Assert.Contains("\"origin\":\"project origin\"", json);
        Assert.Equal([new(-1000, -3000), new(3000, -3000), new(3000, 1000), new(-1000, 1000)], Context.Parse(json)[0].Ring);
    }

    [Fact]
    public void Low_small_and_outside_buildings_are_left_out()
    {
        var (dtm, dsm) = Site((1002, 2002, 1006, 2006, 11f));
        var json = SiteFetcher.ContextJson(
            [Building(1002, 2002, 1006, 2006), Building(1010, 2010, 1010.5, 2010.5), Building(1100, 2100, 1104, 2104)],
            dtm, dsm, Lot, Box, 1004, 2004);

        Assert.Empty(Context.Parse(json));  // 1 m high, one cell, and beyond the box
    }
}
