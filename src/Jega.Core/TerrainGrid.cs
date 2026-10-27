using System.Globalization;
using Jega.Core.Resources;
using static Jega.Core.Texts;

namespace Jega.Core;

/// <summary>Thrown when the terrain CSV or the edge can't be used; the message is for the user.</summary>
public sealed class TerrainGridException(string message) : Exception(message);

/// <summary>
/// The terrain from SiteFetcher's CSV (or a user's own in the same form): a complete, regular grid
/// of elevations. Everything in mm: X/Y from the site plan's 0,0, Z in DVR90.
/// The convention is fixed: no header, "X,Y,Z" per line, '.' as decimal point, no holes.
/// The line order doesn't matter; the grid is rebuilt from the coordinates.
/// </summary>
public sealed class TerrainGrid
{
    // Coordinates in the CSV are rounded to 0.1 mm, so nodes and spacings are compared with this tolerance.
    private const double Tolerance = 1.0;
    // Denmark lies between about -7 and 171 m; anything outside this is a no-data value or a broken file.
    private const double MinZ = -100_000, MaxZ = 500_000;

    private readonly double[,] _z;  // [row (y), column (x)], row 0 is the southernmost

    public double X0 { get; }
    public double Y0 { get; }
    public double Step { get; }
    public int Nx { get; }
    public int Ny { get; }
    public double MaxX => X0 + (Nx - 1) * Step;
    public double MaxY => Y0 + (Ny - 1) * Step;
    public int Count => Nx * Ny;

    private TerrainGrid(double x0, double y0, double step, double[,] z)
    {
        (X0, Y0, Step, _z) = (x0, y0, step, z);
        (Ny, Nx) = (z.GetLength(0), z.GetLength(1));
    }

    /// <summary>The elevation stored at node (i, j), counted from the south-west corner.</summary>
    public double this[int i, int j] => _z[j, i];

    public static TerrainGrid Load(string path) => Parse(File.ReadAllText(path));

    public static TerrainGrid Parse(string text)
    {
        var points = new List<(double X, double Y, double Z)>();
        var lines = text.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var parts = line.Split(',');
            if (parts.Length != 3 || !TryNumber(parts[0], out var x) || !TryNumber(parts[1], out var y) || !TryNumber(parts[2], out var z))
            {
                throw new TerrainGridException(Fill(Strings.Grid_NotNumbers, n + 1, Shorten(line)));
            }
            if (z < MinZ || z > MaxZ)
            {
                throw new TerrainGridException(Fill(Strings.Grid_BadElevation, n + 1, z / 1000));
            }
            points.Add((x, y, z));
        }
        if (points.Count < 4)
        {
            throw new TerrainGridException(Strings.Grid_TooFew);
        }

        var xs = Axis(points.Select(p => p.X));
        var ys = Axis(points.Select(p => p.Y));
        if (xs.Length < 2 || ys.Length < 2)
        {
            throw new TerrainGridException(Strings.Grid_OnALine);
        }
        var step = xs[1] - xs[0];
        if (!Regular(xs, step) || !Regular(ys, step))
        {
            throw new TerrainGridException(Strings.Grid_NotRegular);
        }
        // Before the grid is made: points scattered over a wide box would ask for far more cells than there are points.
        // Fewer points than cells is holes whatever else is wrong; with as many or more, the duplicate check below
        // leaves exactly one per cell.
        if (points.Count < (long)xs.Length * ys.Length)
        {
            throw new TerrainGridException(Fill(Strings.Grid_Holes, points.Count, xs.Length, ys.Length, (long)xs.Length * ys.Length));
        }

        var grid = new double[ys.Length, xs.Length];
        var seen = new bool[ys.Length, xs.Length];
        foreach (var (x, y, z) in points)
        {
            var i = (int)Math.Round((x - xs[0]) / step);
            var j = (int)Math.Round((y - ys[0]) / step);
            if (Math.Abs(x - (xs[0] + i * step)) > Tolerance || Math.Abs(y - (ys[0] + j * step)) > Tolerance)
            {
                throw new TerrainGridException(Fill(Strings.Grid_OffGrid, x / 1000, y / 1000));
            }
            if (seen[j, i])
            {
                throw new TerrainGridException(Fill(Strings.Grid_Duplicate, x / 1000, y / 1000));
            }
            (grid[j, i], seen[j, i]) = (z, true);
        }
        return new TerrainGrid(xs[0], ys[0], step, grid);
    }

    /// <summary>True if the point is inside the grid's frame (its outermost nodes), where elevations can be found.</summary>
    public bool Contains(double x, double y) =>
        x >= X0 - 1e-6 && x <= MaxX + 1e-6 && y >= Y0 - 1e-6 && y <= MaxY + 1e-6;

    /// <summary>
    /// The terrain elevation at a point, bilinear between the four nodes around it: the standard reading of
    /// grid data, using all four corners and always giving the same result for the same point.
    /// </summary>
    public double ElevationAt(double x, double y)
    {
        if (!Contains(x, y))
        {
            // A caller's slip (Contains first), not the user's: stays in code, as .NET's own argument checks do.
            throw new ArgumentOutOfRangeException(nameof(x), string.Create(CultureInfo.InvariantCulture, $"The point {x / 1000:0.000}, {y / 1000:0.000} m is outside the terrain grid."));
        }
        var fx = (x - X0) / Step;
        var fy = (y - Y0) / Step;
        // On the last row or column the cell before it is used, so the far edge is inside too.
        var i = Math.Clamp((int)Math.Floor(fx), 0, Nx - 2);
        var j = Math.Clamp((int)Math.Floor(fy), 0, Ny - 2);
        var (tx, ty) = (fx - i, fy - j);
        return (_z[j, i] * (1 - tx) + _z[j, i + 1] * tx) * (1 - ty) + (_z[j + 1, i] * (1 - tx) + _z[j + 1, i + 1] * tx) * ty;
    }

    private static bool TryNumber(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    // The distinct values along one axis, sorted; values closer than the tolerance are the same node.
    private static double[] Axis(IEnumerable<double> values)
    {
        var axis = new List<double>();
        foreach (var v in values.Order())
        {
            if (axis.Count == 0 || v - axis[^1] > Tolerance)
            {
                axis.Add(v);
            }
        }
        return [.. axis];
    }

    private static bool Regular(double[] axis, double step) =>
        step > Tolerance && axis.Zip(axis.Skip(1)).All(p => Math.Abs(p.Second - p.First - step) <= Tolerance);

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
