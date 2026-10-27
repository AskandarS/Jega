using Jega.Core;

namespace Jega.Core.Tests;

/// <summary>
/// The test case: Skagens Museum, Brøndumsvej 4, 9990 Skagen (a public museum, no client's), on its real 0.4 m terrain
/// CSV, sampled every 0.4 m. The expected values are Jega's own, so this guards against changes; the method itself was
/// checked against an independent calculation on an earlier case.
/// </summary>
public class SkagensMuseumTests
{
    // The house's outline (the largest building on the lot, GeoDanmark), metres from the address point.
    private static readonly Point2[] House = new (double X, double Y)[]
    {
        (-9.41, 6.17), (-6.28, -7.69), (-7.56, -7.98), (-4.81, -20.17), (-2.00, -19.54), (-1.23, -22.95), (9.08, -20.63),
        (8.35, -17.37), (11.85, -16.58), (9.06, -4.24), (6.88, -4.73), (4.98, 3.74), (6.21, 4.02), (5.33, 7.91),
        (4.40, 7.70), (2.63, 15.58), (25.07, 20.63), (24.76, 22.00), (26.09, 22.30), (27.04, 18.11), (33.15, 19.49),
        (31.32, 27.99), (25.49, 26.68), (25.91, 24.81), (24.21, 24.43), (23.40, 28.00), (9.69, 24.91), (9.10, 27.52),
        (0.66, 25.62), (1.11, 23.62), (-13.24, 20.39), (-13.04, 19.50), (-12.55, 19.61),
    }.Select(p => new Point2(p.X * 1000, p.Y * 1000)).ToArray();

    private static readonly TerrainGrid Grid = TerrainGrid.Load(Path.Combine(AppContext.BaseDirectory, "Data", "skagens-museum-terrain.csv"));

    [Fact]
    public void TheCsvIsACompleteGrid()
    {
        Assert.Equal((354, 222, 78588, 400.0), (Grid.Nx, Grid.Ny, Grid.Count, Grid.Step));
        Assert.Equal((-72310.0, -36780.0), (Grid.X0, Grid.Y0));
    }

    [Fact]
    public void LevelsAlongTheHouse()
    {
        var r = EdgeLevels.Compute(Grid, House, offset: 150);
        Assert.Equal(530, r.Points.Count);
        Assert.Equal(205411.1, r.Length, 1);
        Assert.Equal(2958.521, r.Highest.Terrain, 3);
        Assert.Equal((4115.9, -21747.0), (Math.Round(r.Highest.X, 1), Math.Round(r.Highest.Y, 1)));
        Assert.Null(r.Highest.Corner);  // along a side, not at a corner
        Assert.Equal(-269.263, r.Lowest.Terrain, 3);
        Assert.Equal(3227.783, r.Span, 3);
        Assert.Equal(3108.521, r.Highest.Level, 3);
    }

    [Fact]
    public void CornerLevels()
    {
        // Printed to 0.1 µm; compared within 1 µm, so no value sits on a rounding edge.
        double[] expected =
        [
            2469.6125, 2467.8631, 2434.2750, -269.2625, 1023.9850, 1614.9333, 2613.6713, 2688.2225, 2553.4400, 2285.8643,
            2337.2925, 2149.8563, 2094.2900, 2174.1690, 2205.7475, 2267.6955, 2105.0886, 1662.6150, 1891.7700, 2168.3994,
            2172.4949, 2178.5847, 164.2175, 1037.5925, 997.1345, 2161.7926, 2239.8350, 2208.1963, 2278.1825, 2302.7550,
            2679.1601, 2850.7698, 2655.1220,
        ];
        var actual = EdgeLevels.Compute(Grid, House).Corners.Select(c => c.Terrain).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        Assert.All(expected.Zip(actual), p => Assert.Equal(p.First, p.Second, 0.001));
    }
}
