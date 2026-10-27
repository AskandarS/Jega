using Jega.Core;

namespace Jega.Core.Tests;

public class LotTerrainTests
{
    // z = 10000 + x / 10 over a 4 × 4 m grid from (-1000, -1000): nodes at -1000, -600, -200, 200, ... 3000.
    private static TerrainGrid Slope()
    {
        var lines = new List<string>();
        for (var y = -1000; y <= 3000; y += 400)
        for (var x = -1000; x <= 3000; x += 400)
            lines.Add($"{x},{y},{10000 + x / 10.0}");
        return TerrainGrid.Parse(string.Join('\n', lines));
    }

    private static readonly Point2[] Square = [new(0, 0), new(2400, 0), new(2400, 2400), new(0, 2400)];

    [Fact]
    public void InsideIsEverySecondNodeByDefault()
    {
        var r = LotTerrain.Compute(Slope(), Square);
        // Every second node from the grid's corner: 600, 1400, 2200 in both directions.
        Assert.Equal(9, r.Inside.Count);
        Assert.Equal(new double[] { 600, 1400, 2200 }, r.Inside.Select(p => p.X).Distinct().Order());
        Assert.All(r.Inside, p => Assert.Equal(10000 + p.X / 10, p.Z, 6));
    }

    [Fact]
    public void AHoleGetsItsOwnEdgeAndNoNodeInsideIt()
    {
        // A parcel inside the lot that isn't part of it: 0.8 × 0.8 m around the middle node.
        Point2[] hole = [new(1000, 1000), new(1800, 1000), new(1800, 1800), new(1000, 1800)];
        var r = LotTerrain.Compute(Slope(), Square, holes: [hole]);
        Assert.Equal(8, r.Inside.Count);  // the 9 of the lot less (1400, 1400)
        Assert.DoesNotContain(r.Inside, p => p.X == 1400 && p.Y == 1400);
        Assert.Equal(24 + 8, r.Edge.Count);  // the lot's 9600 mm and the hole's 3200 mm, in 400 mm steps
        Assert.Contains(r.Edge, p => p.X == 1000 && p.Y == 1000);
    }

    [Fact]
    public void TheAverageLeavesTheHolesOut()
    {
        // The hole over the lot's high (east) side: without it the average is lower.
        Point2[] hole = [new(1500, 100), new(2300, 100), new(2300, 2300), new(1500, 2300)];
        var grid = Slope();
        var with = LotTerrain.LevelAt(ProjectOptions.LevelAverage, LotTerrain.Compute(grid, Square, holes: [hole]), grid, Square, null, [hole]);
        var without = LotTerrain.LevelAt(ProjectOptions.LevelAverage, LotTerrain.Compute(grid, Square), grid, Square, null);
        Assert.True(with < without);
    }

    [Fact]
    public void TheEdgeIsSampledLikeTheLevels()
    {
        var r = LotTerrain.Compute(Slope(), Square);
        Assert.Equal(EdgeLevels.Compute(Slope(), Square).Points.Select(p => new Point3(p.X, p.Y, p.Terrain)), r.Edge);
        Assert.Equal(24, r.Edge.Count);  // 9600 mm in 400 mm steps
        Assert.Equal(r.Edge.Count + r.Inside.Count, r.All.Count());
    }

    [Fact]
    public void NodesOnOrNearTheEdgeAreLeftOut()
    {
        // The outline runs through the nodes at 200 and 2200: those are on the edge, so only 600 to 1800 remain.
        Point2[] onNodes = [new(200, 200), new(2200, 200), new(2200, 2200), new(200, 2200)];
        var r = LotTerrain.Compute(Slope(), onNodes, interiorStep: 400);
        Assert.Equal(16, r.Inside.Count);
        Assert.Equal(600, r.Inside.Min(p => p.X));
        Assert.Equal(1800, r.Inside.Max(p => p.X));
    }

    [Fact]
    public void FollowsALotThatIsNotConvex()
    {
        // An L: the square without its north-east quarter.
        Point2[] l = [new(0, 0), new(2400, 0), new(2400, 1200), new(1200, 1200), new(1200, 2400), new(0, 2400)];
        var r = LotTerrain.Compute(Slope(), l, interiorStep: 400);
        Assert.Equal(36 - 9, r.Inside.Count);
        Assert.DoesNotContain(r.Inside, p => p.X > 1200 && p.Y > 1200);
    }

    [Fact]
    public void AClosedRingIsTheSameAsAnOpenOne() =>
        Assert.Equal(LotTerrain.Compute(Slope(), Square).All, LotTerrain.Compute(Slope(), [.. Square, Square[0]]).All);

    [Fact]
    public void StopsWhenTheLotLeavesTheGrid() =>
        Assert.Throws<TerrainGridException>(() =>
            LotTerrain.Compute(Slope(), [new(0, 0), new(5000, 0), new(5000, 1000), new(0, 1000)]));

    [Theory]
    [InlineData(ProjectOptions.LevelLowest, 10000)]    // the west edge, x = 0
    [InlineData(ProjectOptions.LevelHighest, 10240)]   // the east edge, x = 2400
    [InlineData(ProjectOptions.LevelAverage, 10120)]   // the nodes inside, x = 200 to 2200, average 1200
    [InlineData(ProjectOptions.LevelAddress, 10000)]   // the address point, 0,0
    [InlineData(ProjectOptions.LevelHouse, 10180.5)]   // as Load from Address found it
    public void TheTerrainLevelIsWhereTheSettingsSay(string at, double expected)
    {
        var r = LotTerrain.Compute(Slope(), Square);
        Assert.Equal(expected, LotTerrain.LevelAt(at, r, Slope(), Square, 10180.5)!.Value, 6);
    }

    // The site's 0,0 is the project's origin; the address point lies off it (here 0.5 m east, up the slope).
    [Fact]
    public void TheAddressLevelIsAtTheAddressPointNotTheOrigin()
    {
        var r = LotTerrain.Compute(Slope(), Square);
        Assert.Equal(10050, LotTerrain.LevelAt(ProjectOptions.LevelAddress, r, Slope(), Square, null, addressMm: new Point2(500, 0))!.Value, 6);
    }

    [Fact]
    public void NoHouseOrAnAddressOffTheGridGivesNoLevel()
    {
        var r = LotTerrain.Compute(Slope(), Square);
        Assert.Null(LotTerrain.LevelAt(ProjectOptions.LevelHouse, r, Slope(), Square, null));
        // A grid that doesn't reach 0,0.
        var lines = new List<string>();
        for (var y = 400; y <= 2800; y += 400)
        for (var x = 400; x <= 2800; x += 400)
            lines.Add($"{x},{y},10000");
        var off = TerrainGrid.Parse(string.Join('\n', lines));
        Point2[] lot = [new(800, 800), new(2000, 800), new(2000, 2000), new(800, 2000)];
        Assert.Null(LotTerrain.LevelAt(ProjectOptions.LevelAddress, LotTerrain.Compute(off, lot), off, lot, null));
    }

    [Fact]
    public void RefusesASpacingOfZero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LotTerrain.Compute(Slope(), Square, interiorStep: 0));
}
