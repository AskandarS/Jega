using Jega.Core;

namespace Jega.Core.Tests;

public class EdgeTests
{
    // A 2 × 1 m rectangle, counter-clockwise from the origin.
    private static readonly Point2[] Rect = [new(0, 0), new(2000, 0), new(2000, 1000), new(0, 1000)];

    [Fact]
    public void SamplesEveryCornerAndStepsNoLongerThanTheStep()
    {
        var pts = EdgeSampler.Sample(Rect);
        // 2000 mm = 5 steps of 400, 1000 mm = 3 steps of 333.3: 5 + 3 + 5 + 3.
        Assert.Equal(16, pts.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, pts.Where(p => p.Corner is not null).Select(p => p.Corner!.Value));
        Assert.Equal(new EdgePoint(2000, 2000, 0, 1), pts[5]);
        Assert.Equal(1000.0 / 3, pts[6].Distance - pts[5].Distance, 6);
        Assert.Equal(6000, EdgeSampler.Perimeter(Rect));
    }

    [Fact]
    public void AClosedRingIsTheSameAsAnOpenOne() =>
        Assert.Equal(EdgeSampler.Sample(Rect), EdgeSampler.Sample([.. Rect, Rect[0]]));

    [Fact]
    public void NeedsThreeCorners() =>
        Assert.Throws<ArgumentException>(() => EdgeSampler.Sample([new(0, 0), new(1, 0), new(0, 0)]));

    // z = 10000 + x / 10 over a 4 × 4 m grid from (-1000, -1000).
    private static TerrainGrid Slope()
    {
        var lines = new List<string>();
        for (var y = -1000; y <= 3000; y += 400)
        for (var x = -1000; x <= 3000; x += 400)
            lines.Add($"{x},{y},{10000 + x / 10.0}");
        return TerrainGrid.Parse(string.Join('\n', lines));
    }

    [Fact]
    public void LevelsAreTerrainPlusOffset()
    {
        var r = EdgeLevels.Compute(Slope(), Rect, offset: 150);
        Assert.Equal(10200, r.Highest.Terrain, 6);
        Assert.Equal(10350, r.Highest.Level, 6);
        Assert.Equal(10000, r.Lowest.Terrain, 6);
        Assert.Equal(200, r.Span, 6);
        Assert.Equal(new double[] { 10000, 10200, 10200, 10000 }, r.Corners.Select(c => Math.Round(c.Terrain, 6)));
        Assert.Equal(6000, r.Length);
    }

    [Fact]
    public void NegativeOffsetForFoundations() =>
        Assert.Equal(9100, EdgeLevels.Compute(Slope(), Rect, offset: -900).Lowest.Level, 6);

    [Fact]
    public void StopsWhenTheEdgeLeavesTheGrid()
    {
        var e = Assert.Throws<TerrainGridException>(() =>
            EdgeLevels.Compute(Slope(), [new(0, 0), new(5000, 0), new(5000, 1000), new(0, 1000)]));
        Assert.StartsWith("The edge is partly outside the terrain grid", e.Message);
        Assert.Equal("The edge is entirely outside the terrain grid.", Assert.Throws<TerrainGridException>(() =>
            EdgeLevels.Compute(Slope(), [new(9000, 9000), new(9900, 9000), new(9900, 9900)])).Message);
    }

    [Fact]
    public void RefusesAnOffsetThatIsNotANumber() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EdgeLevels.Compute(Slope(), Rect, double.NaN));
}
