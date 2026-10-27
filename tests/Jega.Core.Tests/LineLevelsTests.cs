using Jega.Core;

namespace Jega.Core.Tests;

public class LineLevelsTests
{
    // z = 10000 + x / 10 over a 4 × 4 m grid from (-1000, -1000): 100 mm rise per metre east.
    private static TerrainGrid Slope()
    {
        var lines = new List<string>();
        for (var y = -1000; y <= 3000; y += 400)
        for (var x = -1000; x <= 3000; x += 400)
            lines.Add($"{x},{y},{10000 + x / 10.0}");
        return TerrainGrid.Parse(string.Join('\n', lines));
    }

    private static IReadOnlyList<Point2> Piece(params (double X, double Y)[] pts) => pts.Select(p => new Point2(p.X, p.Y)).ToList();

    [Fact]
    public void AnOpenLineHasALevelAtEveryEndAndJoint()
    {
        // East 2 m, then north 1 m: three vertices, two pieces.
        var r = LineLevels.Compute(Slope(), [Piece((0, 0), (2000, 0)), Piece((2000, 0), (2000, 1000))], offset: 150);
        Assert.False(r.Closed);
        Assert.Equal(new int?[] { 0, 1, 2 }, r.Vertices.Select(v => v.Corner));
        Assert.Equal(new double[] { 10000, 10200, 10200 }, r.Vertices.Select(v => Math.Round(v.Terrain, 6)));
        Assert.Equal(10350, r.Vertices[1].Level, 6);
    }

    [Fact]
    public void FallAndSlopePerPiece()
    {
        // Uphill 200 mm over 2 m east (+100 ‰), then flat going north.
        var r = LineLevels.Compute(Slope(), [Piece((0, 0), (2000, 0)), Piece((2000, 0), (2000, 1000))]);
        Assert.Equal(2, r.Falls.Count);
        Assert.Equal((2000.0, 200.0, 100.0), (r.Falls[0].Length, Math.Round(r.Falls[0].Fall, 6), Math.Round(r.Falls[0].SlopePermille, 6)));
        Assert.Equal(0, r.Falls[1].Fall, 6);
        // Drawn the other way, the same piece falls.
        Assert.Equal(-200, LineLevels.Compute(Slope(), [Piece((2000, 0), (0, 0))]).Falls[0].Fall, 6);
    }

    [Fact]
    public void AClosedLineEndsWhereItStarted()
    {
        // A 2 × 1 m rectangle: four vertices, four pieces, the last back to the first.
        var r = LineLevels.Compute(Slope(),
            [Piece((0, 0), (2000, 0)), Piece((2000, 0), (2000, 1000)), Piece((2000, 1000), (0, 1000)), Piece((0, 1000), (0, 0))]);
        Assert.True(r.Closed);
        Assert.Equal(4, r.Vertices.Count);
        Assert.Equal(new double[] { 200, 0, -200, 0 }, r.Falls.Select(f => Math.Round(f.Fall, 6)));
    }

    [Fact]
    public void HighestAndLowestCanLieBetweenVertices()
    {
        // A V-shaped valley along x: z = 10000 + |x - 1000| / 10, crossed by one straight line from 0 to 2000.
        var lines = new List<string>();
        for (var y = -1000; y <= 3000; y += 400)
        for (var x = -1000; x <= 3000; x += 400)
            lines.Add($"{x},{y},{10000 + Math.Abs(x - 1000) / 10.0}");
        var r = LineLevels.Compute(TerrainGrid.Parse(string.Join('\n', lines)), [Piece((-200, 0), (2200, 0))]);
        Assert.Null(r.Lowest.Corner);
        Assert.Equal(1000, r.Lowest.X, 6);
        Assert.Equal(10000, r.Lowest.Terrain, 6);
        Assert.NotNull(r.Highest.Corner);
    }

    [Fact]
    public void AnArcIsOnePieceWithItsEndsAsVertices()
    {
        var arc = Piece((0, 0), (500, 300), (1000, 400), (1500, 300), (2000, 0));
        var r = LineLevels.Compute(Slope(), [arc]);
        Assert.Equal(2, r.Vertices.Count);
        Assert.Single(r.Falls);
        Assert.True(r.Falls[0].Length > 2000);
        Assert.Equal(200, r.Falls[0].Fall, 6);
    }

    [Fact]
    public void StopsWhenTheLineLeavesTheGrid() =>
        Assert.StartsWith("The line is partly outside the terrain grid",
            Assert.Throws<TerrainGridException>(() => LineLevels.Compute(Slope(), [Piece((0, 0), (5000, 0))])).Message);

    [Fact]
    public void NeedsAPiece() =>
        Assert.Throws<ArgumentException>(() => LineLevels.Compute(Slope(), []));
}
