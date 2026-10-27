using Jega.Core.Resources;
using static Jega.Core.Texts;

namespace Jega.Core;

/// <summary>
/// The fall along one piece of a terrain line, from its start to its end: Fall is the end's terrain minus the
/// start's, so negative means the piece runs downhill in the drawn direction. Length along the piece, mm.
/// </summary>
public readonly record struct PieceFall(int Piece, double Length, double Fall)
{
    /// <summary>Fall over length, in ‰, as falls are given on Danish drawings; positive uphill in the drawn direction.</summary>
    public double SlopePermille => Length > 0 ? Fall / Length * 1000 : 0;
}

/// <summary>The levels along a terrain line: a level at every vertex, the fall of every piece, highest and lowest.</summary>
public sealed record LineLevelsResult(
    IReadOnlyList<LevelPoint> Vertices, IReadOnlyList<PieceFall> Falls, bool Closed, double Offset,
    LevelPoint Highest, LevelPoint Lowest);

/// <summary>
/// The terrain along a line drawn on the plan (Terrain Line): open or closed, made of pieces
/// end to end. A straight piece is its two ends; an arc comes in as its tessellation, so only its ends are
/// vertices. Levels at every vertex, the fall of every piece, and the highest and lowest point, found by
/// sampling at most every 0.4 m: they may lie between vertices (Corner is then null). Stops if any part of the
/// line is outside the grid.
/// </summary>
public static class LineLevels
{
    // Ends closer than this are the same point: Revit's own joins are exact, this only absorbs float noise.
    private const double JoinTolerance = 1.0;

    public static LineLevelsResult Compute(TerrainGrid grid, IReadOnlyList<IReadOnlyList<Point2>> pieces, double offset = 0,
        double step = EdgeSampler.DefaultStep)
    {
        if (pieces.Count == 0 || pieces.Any(p => p.Count < 2))
        {
            throw new ArgumentException("A terrain line needs at least one piece of two points.", nameof(pieces));
        }
        if (!(step > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The sampling distance must be above 0.");
        }
        var closed = Distance(pieces[0][0], pieces[^1][^1]) <= JoinTolerance;

        // Samples along every piece: each piece's start is a vertex, and an open line's very end is one too.
        var samples = new List<(double Distance, Point2 P, int? Vertex)>();
        var distance = 0.0;
        var lengths = new List<double>();
        for (var k = 0; k < pieces.Count; k++)
        {
            var piece = pieces[k];
            var length = 0.0;
            for (var s = 0; s < piece.Count - 1; s++)
            {
                var (a, b) = (piece[s], piece[s + 1]);
                var d = Distance(a, b);
                var n = Math.Max(1, (int)Math.Ceiling(d / step - 1e-9));
                for (var t = 0; t < n; t++)
                {
                    var f = (double)t / n;
                    samples.Add((distance + length + d * f, new Point2(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f), s == 0 && t == 0 ? k : null));
                }
                length += d;
            }
            lengths.Add(length);
            distance += length;
        }
        if (!closed)
        {
            samples.Add((distance, pieces[^1][^1], pieces.Count));
        }

        var outside = samples.Count(p => !grid.Contains(p.P.X, p.P.Y));
        if (outside > 0)
        {
            throw new TerrainGridException(outside == samples.Count
                ? Strings.Line_Outside
                : Fill(Strings.Line_Partly, outside, samples.Count));
        }
        var points = samples.Select(p =>
        {
            var terrain = grid.ElevationAt(p.P.X, p.P.Y);
            return new LevelPoint(p.Distance, p.P.X, p.P.Y, p.Vertex, terrain, terrain + offset);
        }).ToList();

        var vertices = points.Where(p => p.Corner is not null).ToList();
        var falls = Enumerable.Range(0, pieces.Count)
            .Select(k => new PieceFall(k, lengths[k], vertices[(k + 1) % vertices.Count].Terrain - vertices[k].Terrain))
            .ToList();
        return new LineLevelsResult(vertices, falls, closed, offset, points.MaxBy(p => p.Terrain), points.MinBy(p => p.Terrain));
    }

    private static double Distance(Point2 a, Point2 b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
