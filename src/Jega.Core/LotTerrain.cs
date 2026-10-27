namespace Jega.Core;

/// <summary>A terrain point, mm: X/Y from the site's 0,0, Z in DVR90.</summary>
public readonly record struct Point3(double X, double Y, double Z);

/// <summary>The points for the lot's Toposolid: along its outline, and grid nodes inside it.</summary>
public sealed record LotTerrainResult(IReadOnlyList<Point3> Edge, IReadOnlyList<Point3> Inside)
{
    public IEnumerable<Point3> All => Edge.Concat(Inside);
}

/// <summary>
/// The terrain over the lot, for a Toposolid bounded by the lot's outline (Generate Terrain). The edge
/// gets a point at every corner and at most every 0.4 m between them, interpolated from the grid, so edge and
/// surface meet. Inside, the grid's own nodes, thinned to every interior step, with their values as they are.
/// Stops if any part of the outline is outside the grid.
/// </summary>
public static class LotTerrain
{
    /// <param name="interiorStep">The points inside the lot, mm apart; null = Config's (every second node of the 0.4 m grid).</param>
    /// <param name="edgeStep">The points along the outline, at most mm apart; null = Config's.</param>
    /// <param name="holes">Parcels inside the lot that aren't part of it:
    /// their outlines get edge points as the lot's, and no node inside them (nor too near them) is used.</param>
    public static LotTerrainResult Compute(TerrainGrid grid, IReadOnlyList<Point2> ring,
        double? interiorStep = null, double? edgeStep = null, IReadOnlyList<IReadOnlyList<Point2>>? holes = null)
    {
        var inner = interiorStep ?? Config.Current.Terrain.InteriorStepMm;
        var along = edgeStep ?? Config.Current.Terrain.EdgeStepMm;
        if (!(inner > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(interiorStep), "The spacing inside the lot must be above 0.");
        }
        var holeRings = (holes ?? []).Select(Open).Where(h => h.Count >= 3).ToList();
        var edge = new[] { ring }.Concat(holeRings)
            .SelectMany(r => EdgeLevels.Compute(grid, r, 0, along).Points.Select(p => new Point3(p.X, p.Y, p.Terrain))).ToList();
        var corners = Open(ring);

        // A node closer to the edge than half a grid step is left out: the edge points already cover it, and
        // points almost on the boundary are what makes a Toposolid fail.
        var clearance = grid.Step / 2;
        var every = Math.Max(1, (int)Math.Round(inner / grid.Step));
        var inside = new List<Point3>();
        for (var j = 0; j < grid.Ny; j += every)
        {
            for (var i = 0; i < grid.Nx; i += every)
            {
                var p = new Point2(grid.X0 + i * grid.Step, grid.Y0 + j * grid.Step);
                if (Inside(corners, p) && DistanceToEdge(corners, p) >= clearance
                    && holeRings.All(h => !Inside(h, p) && DistanceToEdge(h, p) >= clearance))
                {
                    inside.Add(new Point3(p.X, p.Y, grid[i, j]));
                }
            }
        }
        return new LotTerrainResult(edge, inside);
    }

    // A ring without its closing point.
    private static List<Point2> Open(IReadOnlyList<Point2> ring) => ring.Count > 1 && ring[0] == ring[^1] ? ring.Take(ring.Count - 1).ToList() : ring.ToList();

    /// <summary>
    /// The "Terrain" level's height in DVR90 (mm), as Settings' "Terrain point for the Terrain level" says (ProjectOptions.TerrainLevelAt):
    /// the lowest or highest of the Toposolid's points, the average of every grid node on the lot (each 0.4 m cell
    /// counts once, so it's the lot's mean, not weighted to the edge), the terrain at the address point ($addressMm in
    /// site coordinates: their 0,0 is the project's origin, the address point rounded, up to 0.7 m off it; 0,0 when
    /// not given), or the highest terrain along the house (Load from Address). Null when that isn't there: no house on
    /// the lot, or the address point off the grid; the caller falls back to the lowest.
    /// </summary>
    public static double? LevelAt(string at, LotTerrainResult terrain, TerrainGrid grid, IReadOnlyList<Point2> ring, double? houseGroundMm,
        IReadOnlyList<IReadOnlyList<Point2>>? holes = null, Point2? addressMm = null)
    {
        switch (at)
        {
            case ProjectOptions.LevelHighest:
                return terrain.All.Max(p => p.Z);
            case ProjectOptions.LevelAverage:
                // The lot without its holes, as Compute.
                var corners = Open(ring);
                var holeRings = (holes ?? []).Select(Open).Where(h => h.Count >= 3).ToList();
                var nodes = new List<double>();
                for (var j = 0; j < grid.Ny; j++)
                {
                    for (var i = 0; i < grid.Nx; i++)
                    {
                        var node = new Point2(grid.X0 + i * grid.Step, grid.Y0 + j * grid.Step);
                        if (Inside(corners, node) && !holeRings.Any(h => Inside(h, node)))
                        {
                            nodes.Add(grid[i, j]);
                        }
                    }
                }
                // A lot smaller than a cell has no node inside: its outline's points instead.
                return nodes.Count > 0 ? nodes.Average() : terrain.All.Average(p => p.Z);
            case ProjectOptions.LevelAddress:
                var (ax, ay) = addressMm is { } a ? (a.X, a.Y) : (0d, 0d);
                return grid.Contains(ax, ay) ? grid.ElevationAt(ax, ay) : null;
            case ProjectOptions.LevelHouse:
                return houseGroundMm;
            default:
                return terrain.All.Min(p => p.Z);
        }
    }

    // Even-odd rule: a ray to the east crosses the outline an odd number of times from inside.
    private static bool Inside(List<Point2> ring, Point2 p)
    {
        var inside = false;
        for (int k = 0, prev = ring.Count - 1; k < ring.Count; prev = k++)
        {
            var (a, b) = (ring[prev], ring[k]);
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static double DistanceToEdge(List<Point2> ring, Point2 p) =>
        ring.Select((a, k) => DistanceToSegment(p, a, ring[(k + 1) % ring.Count])).Min();

    private static double DistanceToSegment(Point2 p, Point2 a, Point2 b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var lengthSq = dx * dx + dy * dy;
        var t = lengthSq == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        var (ex, ey) = (p.X - (a.X + t * dx), p.Y - (a.Y + t * dy));
        return Math.Sqrt(ex * ex + ey * ey);
    }
}
