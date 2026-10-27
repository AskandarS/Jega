namespace Jega.Core;

/// <summary>A point on the plan, in mm from the site's 0,0.</summary>
public readonly record struct Point2(double X, double Y);

/// <summary>A point on the edge: its distance along the edge from the first corner, and the corner it is, if any.</summary>
public readonly record struct EdgePoint(double Distance, double X, double Y, int? Corner);

/// <summary>
/// Points along a closed edge (the building's outline): every corner, and between them equal steps no longer
/// than the sampling distance. Arcs come in already split into short straight pieces (the Revit side does that).
/// </summary>
public static class EdgeSampler
{
    /// <summary>The sampling distance along the edge, mm (0.4 m, the grid's spacing).</summary>
    public const double DefaultStep = 400;

    /// <param name="ring">The corners in order; the last may repeat the first.</param>
    /// <param name="step">Longest distance between two points, mm.</param>
    public static List<EdgePoint> Sample(IReadOnlyList<Point2> ring, double step = DefaultStep)
    {
        if (!(step > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The sampling distance must be above 0.");
        }
        var corners = Corners(ring);
        var points = new List<EdgePoint>();
        var distance = 0.0;
        for (var k = 0; k < corners.Count; k++)
        {
            var (a, b) = (corners[k], corners[(k + 1) % corners.Count]);
            var length = Distance(a, b);
            // The small margin keeps a side of exactly n steps from getting an extra piece through float noise.
            var n = Math.Max(1, (int)Math.Ceiling(length / step - 1e-9));
            for (var t = 0; t < n; t++)
            {
                var f = (double)t / n;
                points.Add(new EdgePoint(distance + length * f, a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, t == 0 ? k : null));
            }
            distance += length;
        }
        return points;
    }

    /// <summary>The length of the closed edge, mm.</summary>
    public static double Perimeter(IReadOnlyList<Point2> ring)
    {
        var corners = Corners(ring);
        return corners.Select((a, k) => Distance(a, corners[(k + 1) % corners.Count])).Sum();
    }

    private static List<Point2> Corners(IReadOnlyList<Point2> ring)
    {
        var corners = ring.Count > 1 && ring[0] == ring[^1] ? ring.Take(ring.Count - 1).ToList() : ring.ToList();
        if (corners.Count < 3)
        {
            throw new ArgumentException("The edge needs at least three corners.", nameof(ring));
        }
        return corners;
    }

    private static double Distance(Point2 a, Point2 b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
