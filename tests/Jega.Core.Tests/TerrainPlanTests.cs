using Jega.Core;
using Pt = (double X, double Y);

namespace Jega.Core.Tests;

/// <summary>SiteFetcher.PlanTerrain: how large a lot's terrain grid gets, and in how many calls (no network).</summary>
public class TerrainPlanTests
{
    // A square lot of $side m with its south-west corner on whole cells.
    private static List<Pt> Square(double side) => [(1000, 2000), (1000 + side, 2000), (1000 + side, 2000 + side), (1000, 2000 + side)];

    [Fact]
    public void A_house_lot_is_one_call_at_the_models_own_grid()
    {
        var p = SiteFetcher.PlanTerrain(Square(30), 0.4);
        // 30 m + 5 m on each side = 40 m, snapped outwards to whole cells (one more for float noise at the edge): 101.
        Assert.Equal((101, 101, 1), (p.W, p.H, p.Parts));
        Assert.True(p.Fits);
    }

    [Fact]
    public void A_large_lot_is_fetched_in_parts()
    {
        // 300 m + 10 m, snapped outwards: 776 cells a side, so 2 × 2 calls of at most 400.
        var p = SiteFetcher.PlanTerrain(Square(300), 0.4);
        Assert.Equal((776, 776, 4), (p.W, p.H, p.Parts));
        Assert.True(p.Fits);
    }

    [Fact]
    public void A_very_large_lot_only_fits_a_coarser_grid()
    {
        // 600 m: 1,525² points at 0.4 m is over the million; 0.8 m gives 763² (the box snapped to whole 0.8 m).
        Assert.False(SiteFetcher.PlanTerrain(Square(600), 0.4).Fits);
        var coarse = SiteFetcher.PlanTerrain(Square(600), 0.8);
        Assert.True(coarse.Fits);
        Assert.Equal(2 * 2, coarse.Parts);
    }

    [Fact]
    public void Too_large_for_any_grid()
    {
        Assert.DoesNotContain(SiteFetcher.Cells, c => SiteFetcher.PlanTerrain(Square(2500), c).Fits);
    }
}

/// <summary>SiteFetcher.Place: the parts of a large terrain put together into one grid (rows from the north).</summary>
public class TerrainPartsTests
{
    [Fact]
    public void Four_parts_make_one_grid()
    {
        // A 3 × 3 grid from parts of at most 2 × 2: values are 10 × row + column, so each cell says where it belongs.
        var (w, h) = (3, 3);
        var z = new float[w * h];
        foreach (var (pi, pj) in new[] { (0, 0), (2, 0), (0, 2), (2, 2) })
        {
            var (pw, ph) = (Math.Min(2, w - pi), Math.Min(2, h - pj));
            var part = new float[pw * ph];
            for (var r = 0; r < ph; r++)
            {
                for (var c = 0; c < pw; c++)
                {
                    part[r * pw + c] = 10 * (pj + r) + pi + c;
                }
            }
            SiteFetcher.Place(z, w, new SiteFetcher.Dtm(0, 0, 0.4, 0.4, pw, ph, part), pi, pj);
        }
        Assert.Equal([0, 1, 2, 10, 11, 12, 20, 21, 22], z);
    }
}
