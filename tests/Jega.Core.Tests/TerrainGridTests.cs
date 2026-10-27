using Jega.Core;

namespace Jega.Core.Tests;

public class TerrainGridTests
{
    // 3 × 2 nodes, 400 mm apart, from (-400, 0): z = 10000 + x / 10 + y / 20, a plane, so bilinear is exact.
    private const string Plane = "-400.0,0.0,9960.0\n0.0,0.0,10000.0\n400.0,0.0,10040.0\n-400.0,400.0,9980.0\n0.0,400.0,10020.0\n400.0,400.0,10060.0\n";

    private static void Fails(string csv, string message) =>
        Assert.Contains(message, Assert.Throws<TerrainGridException>(() => TerrainGrid.Parse(csv)).Message);

    [Fact]
    public void ReadsTheGrid()
    {
        var g = TerrainGrid.Parse(Plane);
        Assert.Equal((-400.0, 0.0, 400.0, 3, 2), (g.X0, g.Y0, g.Step, g.Nx, g.Ny));
        Assert.Equal((400.0, 400.0), (g.MaxX, g.MaxY));
        Assert.Equal(10060.0, g[2, 1]);
    }

    [Fact]
    public void LineOrderAndLineEndingsDontMatter()
    {
        var shuffled = string.Join("\r\n", Plane.Trim().Split('\n').Reverse());
        Assert.Equal(TerrainGrid.Parse(Plane)[1, 1], TerrainGrid.Parse(shuffled)[1, 1]);
    }

    [Theory]
    [InlineData(0, 0, 10000)]       // on a node
    [InlineData(200, 200, 10030)]   // in the middle of a cell
    [InlineData(400, 400, 10060)]   // the far corner, on the frame
    [InlineData(-123, 321, 10003.75)]
    public void ElevationIsBilinear(double x, double y, double z) =>
        Assert.Equal(z, TerrainGrid.Parse(Plane).ElevationAt(x, y), 6);

    [Fact]
    public void BilinearUsesAllFourCorners()
    {
        // One raised corner: the middle of the cell gets a quarter of it.
        var g = TerrainGrid.Parse("0,0,0\n400,0,0\n0,400,0\n400,400,1000\n");
        Assert.Equal(250, g.ElevationAt(200, 200), 6);
    }

    [Fact]
    public void OutsideTheFrame()
    {
        var g = TerrainGrid.Parse(Plane);
        Assert.False(g.Contains(401, 0));
        Assert.False(g.Contains(0, -1));
        Assert.True(g.Contains(-400, 400));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.ElevationAt(0, 500));
    }

    [Fact]
    public void RefusesAHeader() => Fails("X,Y,Z\n" + Plane, "Line 1 is not three numbers");

    [Fact]
    public void RefusesDecimalCommas() => Fails("0,0,10000,5\n", "Line 1 is not three numbers");

    [Fact]
    public void RefusesSemicolons() => Fails("0;0;10000\n", "Line 1 is not three numbers");

    [Fact]
    public void RefusesAHole() => Fails(string.Join('\n', Plane.Split('\n').Skip(1)), "The grid has holes: 5 points, but 3 × 2 = 6 expected.");

    // Points on a diagonal span a box of n × n cells: refused before a grid that size is made.
    [Fact]
    public void RefusesScatteredPointsBeforeMakingTheGrid() =>
        Fails(string.Concat(Enumerable.Range(0, 2000).Select(i => $"{i * 400},{i * 400},10000\n")),
            "The grid has holes: 2000 points, but 2000 × 2000 = 4000000 expected.");

    [Fact]
    public void RefusesADuplicate() => Fails(Plane + "0.0,0.0,10000.0\n", "is in the file more than once");

    [Fact]
    public void RefusesUnevenSpacing() =>
        Fails("0,0,1\n400,0,1\n1000,0,1\n0,400,1\n400,400,1\n1000,400,1\n", "not a regular grid");

    [Fact]
    public void RefusesDifferentSpacingInXAndY() =>
        Fails("0,0,1\n400,0,1\n0,500,1\n400,500,1\n", "not a regular grid");

    [Fact]
    public void RefusesANoDataValue() => Fails(Plane.Replace("10020.0", "-9999000.0"), "Line 5 has the elevation -9999.000 m");

    [Fact]
    public void RefusesTooFewPoints() => Fails("0,0,1\n400,0,1\n", "too few points");

    [Fact]
    public void RefusesPointsOnALine() => Fails("0,0,1\n400,0,1\n800,0,1\n1200,0,1\n", "lie on a line");
}
