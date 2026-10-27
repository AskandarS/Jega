using Jega.Core;
using Pt = (double X, double Y);

namespace Jega.Core.Tests;

public class LabelTests
{
    // A house 14 m long and 6 m wide, along east.
    private static readonly List<Pt> House = [(0, 0), (14, 0), (14, 6), (0, 6), (0, 0)];

    [Fact]
    public void Two_lines_fit_at_the_largest_height_that_leaves_room()
    {
        // Ridge over eaves, 2.8 m tall (1.4 mm at 1:2000): 6.9 m with the margin, too much for 6 m; 2.4 m fits.
        string[] lines = ["43,80", "43,72"];
        Assert.Empty(Label.Spots(House, lines, 2.8));
        Assert.Equal(2.4, Label.Even([(House, lines)], [2.8, 2.6, 2.4, 2.2, 2.0], 0.9));
        // One line fits at 2.8, in the middle, along the house, the first line on top.
        var block = Label.Spots(House, ["43,80"], 2.8).First();
        Assert.Equal((7.0, 3.0, 0.0), (Math.Round(block.X, 6), Math.Round(block.Y, 6), Math.Round(block.AngleDeg, 6)));
        var two = Label.Spots(House, lines, 2.4).First().Placed().ToList();
        Assert.True(two[0].Y > two[1].Y);
        Assert.Equal("43,80", two[0].Text);
    }

    [Fact]
    public void The_setback_line_is_trimmed_at_a_notch()
    {
        // A lot 30 × 12 m with a notch 1 m deep and 2 m wide in its south side: shifted 2.5 m in, the notch's short edges
        // overshoot and loop. Trimmed, every piece lies on the lot at least 2.5 m from its boundary, and no two cross.
        List<Pt> lot = [(0, 0), (14, 0), (14, 1), (16, 1), (16, 0), (30, 0), (30, 12), (0, 12), (0, 0)];
        var pieces = Geometry.Setback(lot, 2.5);
        Assert.NotEmpty(pieces);
        foreach (var piece in pieces)
        {
            foreach (var p in piece)
            {
                Assert.True(Geometry.DistanceToEdges(lot, p) >= 2.5 * 0.98 - 1e-6, $"{p} is {Geometry.DistanceToEdges(lot, p):0.000} m from the boundary");
            }
        }
        // A short edge (0.7 m) at a corner, in line with the long side after it: shifted, it runs back over itself.
        // Trimmed, nothing of it is left nearer the side than 2.5 m.
        List<Pt> corner = [(0, 0), (20, 0), (20, 12), (19.3, 12), (0, 11.9), (0, 0)];
        foreach (var p in Geometry.Setback(corner, 2.5).SelectMany(s => s))
        {
            Assert.True(Geometry.DistanceToEdges(corner, p) >= 2.5 * 0.98 - 1e-6, $"{p} is {Geometry.DistanceToEdges(corner, p):0.000} m from the boundary");
        }
        // A plain rectangle: one line all round, its corners where the shifted sides meet.
        var plain = Geometry.Setback([(0, 0), (30, 0), (30, 12), (0, 12), (0, 0)], 2.5);
        var line = Assert.Single(plain);
        Assert.Contains((2.5, 2.5), line.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))));
        Assert.Contains((27.5, 9.5), line.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))));
    }

    [Fact]
    public void None_fit_gives_the_smallest_height()
    {
        List<Pt> shed = [(0, 0), (2, 0), (2, 2), (0, 2), (0, 0)];
        Assert.Equal(2.0, Label.Even([(shed, ["43,80"])], [2.8, 2.4, 2.0], 0.9));
    }
}
