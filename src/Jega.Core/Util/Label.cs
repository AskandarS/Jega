using Pt = (double X, double Y);

namespace Jega.Core;

/// <summary>
/// Text fitted into a shape on the drawing (a roof's levels in its building): a block of lines turned along the
/// shape's long side (Geometry.LongBox), read from below or the right, where it and a margin lie inside the shape; and
/// one height for many such blocks, so the drawing is even. Any unit (the drawing's metres).
/// </summary>
public static class Label
{
    /// <summary>A text's width per character, as a share of its height (a little wider than Revit's average, so a
    /// fitted text stays inside).</summary>
    public const double CharWidth = 0.6;

    /// <summary>From one line's middle to the next, as a share of the height.</summary>
    public const double LineStep = 1.25;

    /// <summary>The margin kept round a block inside its shape, as a share of the height.</summary>
    public const double Margin = 0.1;

    /// <summary>A block of $Lines, $Height tall, turned $AngleDeg (counter-clockwise from east) about its middle.</summary>
    public sealed record Block(double X, double Y, double AngleDeg, double Height, IReadOnlyList<string> Lines)
    {
        /// <summary>Each line's middle and text, the first on top.</summary>
        public IEnumerable<(double X, double Y, string Text)> Placed()
        {
            var a = AngleDeg * Math.PI / 180;
            var (nx, ny) = (-Math.Sin(a), Math.Cos(a));
            for (var i = 0; i < Lines.Count; i++)
            {
                var o = ((Lines.Count - 1) / 2.0 - i) * LineStep * Height;
                yield return (X + nx * o, Y + ny * o, Lines[i]);
            }
        }
    }

    /// <summary>
    /// The places in $ring where $lines at $height fit with a margin round them ($margin; null = Margin × the height)
    /// and clear of $avoid (the buildings on a lot, its holes), best first: the middle of its box, then a $steps ×
    /// $steps grid over the box inside it, farthest from its edges first (an L, whose box's middle is in the bend or
    /// whose arms are narrow). $along: turned along the ring's long side (Geometry.LongBox); else level. None: they
    /// don't fit.
    /// </summary>
    public static IEnumerable<Block> Spots(List<Pt> ring, IReadOnlyList<string> lines, double height,
        IReadOnlyList<List<Pt>>? avoid = null, bool along = true, double? margin = null, int steps = 9)
    {
        var (middle, angle, longSide, shortSide) = along ? Geometry.LongBox(ring) : Level(ring);
        var pad = margin ?? Margin * height;
        var hw = lines.Max(s => s.Length) * height * CharWidth / 2 + pad;
        var hh = ((lines.Count - 1) * LineStep * height + height) / 2 + pad;
        if (2 * hw > longSide || 2 * hh > shortSide)
        {
            yield break;
        }
        avoid ??= [];
        var a = angle * Math.PI / 180;
        var (ux, uy, nx, ny) = (Math.Cos(a), Math.Sin(a), -Math.Sin(a), Math.Cos(a));
        var half = steps / 2;
        var grid = Enumerable.Range(-half, steps).SelectMany(i => Enumerable.Range(-half, steps).Select(j => (Pt)(
                middle.X + ux * i * longSide / (steps + 1) + nx * j * shortSide / (steps + 1),
                middle.Y + uy * i * longSide / (steps + 1) + ny * j * shortSide / (steps + 1))))
            .Where(p => Geometry.Inside(ring, p.X, p.Y)).OrderByDescending(p => Geometry.DistanceToEdges(ring, p));
        // The block's edge and middle line, a line height apart or closer along it, so a narrow building between two
        // points isn't missed; and no corner of what's avoided inside it (a shed smaller than that).
        var nu = Math.Max(4, (int)Math.Ceiling(2 * hw / height));
        var samples = Enumerable.Range(0, nu + 1).SelectMany(i => new[] { -1.0, 0, 1 }.Select(fv => (fu: -1 + 2.0 * i / nu, fv))).ToList();
        foreach (var (mx, my) in new[] { middle }.Concat(grid))
        {
            bool Clear(double px, double py) => Geometry.Inside(ring, px, py) && !avoid.Any(r => Geometry.Inside(r, px, py));
            bool InBlock(Pt p) => Math.Abs((p.X - mx) * ux + (p.Y - my) * uy) <= hw && Math.Abs((p.X - mx) * nx + (p.Y - my) * ny) <= hh;
            if (samples.All(f => Clear(mx + ux * f.fu * hw + nx * f.fv * hh, my + uy * f.fu * hw + ny * f.fv * hh))
                && !avoid.Any(r => r.Any(InBlock)))
            {
                yield return new Block(mx, my, angle, height, lines);
            }
        }
    }

    /// <summary>A text's box, level round the turned text ($rotDeg), CharWidth wide per character, from its middle or
    /// ($left) its left end, grown by $pad on every side.</summary>
    public static (double X1, double Y1, double X2, double Y2) Bounds(double x, double y, double height, string text, double rotDeg, bool left, double pad)
    {
        var (w, a) = (text.Length * height * CharWidth, rotDeg * Math.PI / 180);
        var (cos, sin) = (Math.Cos(a), Math.Sin(a));
        var (cx, cy) = left ? (x + cos * w / 2, y + sin * w / 2) : (x, y);
        var hx = Math.Abs(cos) * w / 2 + Math.Abs(sin) * height / 2 + pad;
        var hy = Math.Abs(sin) * w / 2 + Math.Abs(cos) * height / 2 + pad;
        return (cx - hx, cy - hy, cx + hx, cy + hy);
    }

    /// <summary>Two boxes (Bounds) overlap.</summary>
    public static bool Overlap((double X1, double Y1, double X2, double Y2) a, (double X1, double Y1, double X2, double Y2) b) =>
        a.X1 < b.X2 && b.X1 < a.X2 && a.Y1 < b.Y2 && b.Y1 < a.Y2;

    /// <summary>A text angle in degrees turned to read from below or from the right (-90 &lt; angle &lt;= 90).</summary>
    public static double Upright(double angle) => angle > 90 ? angle - 180 : angle <= -90 ? angle + 180 : angle;

    /// <summary>A long, narrow ring (a road parcel): its long side over three times its short side, so its text goes
    /// along it.</summary>
    public static bool Strip(List<Pt> ring) => Geometry.LongBox(ring) is var (_, _, l, s) && l > 3 * s;

    // The ring's level box: its middle, angle 0, and its width and height (as LongBox, not turned).
    private static (Pt Middle, double AngleDeg, double Long, double Short) Level(List<Pt> ring)
    {
        var (x1, x2, y1, y2) = (ring.Min(p => p.X), ring.Max(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.Y));
        return (((x1 + x2) / 2, (y1 + y2) / 2), 0, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// One height for all the $items (a shape and its lines each), so the drawing is even: the first of $heights
    /// (largest first) at which at least $share of them fit; none does: the smallest.
    /// </summary>
    public static double Even(IReadOnlyList<(List<Pt> Ring, IReadOnlyList<string> Lines)> items, IReadOnlyList<double> heights, double share)
    {
        foreach (var h in heights)
        {
            if (items.Count(i => Spots(i.Ring, i.Lines, h).Any()) >= share * items.Count)
            {
                return h;
            }
        }
        return heights[^1];
    }
}
