using Jega.Core.Resources;
using static Jega.Core.Texts;

namespace Jega.Core;

/// <summary>One point on the edge with its levels, mm DVR90: the terrain, and the terrain plus the offset.</summary>
public readonly record struct LevelPoint(double Distance, double X, double Y, int? Corner, double Terrain, double Level);

/// <summary>The levels along an edge: every sampled point, its corners, the highest and lowest point.</summary>
public sealed record EdgeLevelsResult(
    IReadOnlyList<LevelPoint> Points, double Offset, double Length, LevelPoint Highest, LevelPoint Lowest)
{
    public IEnumerable<LevelPoint> Corners => Points.Where(p => p.Corner is not null);

    /// <summary>Highest minus lowest terrain along the edge, mm.</summary>
    public double Span => Highest.Terrain - Lowest.Terrain;
}

/// <summary>
/// The levels along the building's edge: the terrain from the grid at each sampled point, plus a fixed offset
/// (0 = terrain, +150 = 150 mm above it, negative for foundations). No plinth rules: judging the plinth and
/// stepping is the architect's. Stops if any part of the edge is outside the grid.
/// </summary>
public static class EdgeLevels
{
    public static EdgeLevelsResult Compute(TerrainGrid grid, IReadOnlyList<Point2> ring, double offset = 0,
        double step = EdgeSampler.DefaultStep)
    {
        if (!double.IsFinite(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The offset must be a number in mm.");
        }
        var edge = EdgeSampler.Sample(ring, step);
        var outside = edge.Count(p => !grid.Contains(p.X, p.Y));
        if (outside > 0)
        {
            throw new TerrainGridException(outside == edge.Count
                ? Strings.Edge_Outside
                : Fill(Strings.Edge_Partly, outside, edge.Count));
        }
        var points = edge.Select(p =>
        {
            var terrain = grid.ElevationAt(p.X, p.Y);
            return new LevelPoint(p.Distance, p.X, p.Y, p.Corner, terrain, terrain + offset);
        }).ToList();
        var highest = points.MaxBy(p => p.Terrain);
        var lowest = points.MinBy(p => p.Terrain);
        return new EdgeLevelsResult(points, offset, EdgeSampler.Perimeter(ring), highest, lowest);
    }
}
