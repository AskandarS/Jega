using Pt = (double X, double Y);

namespace Jega.Core;

/// <summary>Plane geometry for the drawing, in metres: inside, clipping rings and lines, centres, offset rings.</summary>
public static class Geometry
{
    /// <summary>True if the point is inside the ring (ray casting).</summary>
    public static bool Inside(List<Pt> ring, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (a, b) = (ring[i], ring[j]);
            if (a.Y > y != b.Y > y && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>True if the point is on the parcel: inside its outline and not in a hole, another parcel inside it.
    /// Even-odd over all the parcel's rings.</summary>
    public static bool Inside(List<List<Pt>> rings, double x, double y) => rings.Count(r => Inside(r, x, y)) % 2 == 1;

    /// <summary>The parcel's area: its outline less its holes (a ring inside an odd number of the others is a hole).</summary>
    public static double Area(List<List<Pt>> rings) =>
        rings.Sum(r => (rings.Count(o => o != r && Inside(o, r[0].X, r[0].Y)) % 2 == 0 ? 1 : -1) * Math.Abs(Area(r)));

    /// <summary>The middle of a ring (vertex average; the last point repeats the first).</summary>
    public static Pt Centre(List<Pt> ring)
    {
        var pts = ring.Take(Math.Max(1, ring.Count - 1)).ToList();
        return (pts.Average(p => p.X), pts.Average(p => p.Y));
    }

    /// <summary>
    /// A point inside the ring for its label: the middle (Centre) when that lies inside, as for an ordinary lot; else
    /// (a bent strip such as a road parcel, whose middle falls in the bend, outside it) the point of a 40 × 40 grid over
    /// the ring that lies inside it and farthest from its edges: the middle of the road.
    /// </summary>
    public static Pt LabelPoint(List<Pt> ring)
    {
        var centre = Centre(ring);
        if (Inside(ring, centre.X, centre.Y))
        {
            return centre;
        }
        var (minX, minY, maxX, maxY) = (ring.Min(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.X), ring.Max(p => p.Y));
        var (best, bestDistance) = (centre, -1.0);
        for (var i = 0; i <= 40; i++)
        {
            for (var j = 0; j <= 40; j++)
            {
                Pt p = (minX + (maxX - minX) * i / 40, minY + (maxY - minY) * j / 40);
                if (Inside(ring, p.X, p.Y) && DistanceToEdges(ring, p) is var d && d > bestDistance)
                {
                    (best, bestDistance) = (p, d);
                }
            }
        }
        return best;
    }

    /// <summary>The shortest distance from a point to the ring's edges.</summary>
    public static double DistanceToEdges(List<Pt> ring, Pt p)
    {
        var best = double.MaxValue;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (a, b) = (ring[j], ring[i]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var t = dx == 0 && dy == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
            var (qx, qy) = (a.X + t * dx - p.X, a.Y + t * dy - p.Y);
            best = Math.Min(best, Math.Sqrt(qx * qx + qy * qy));
        }
        return best;
    }

    /// <summary>
    /// The lot's centre (where SiteplanDxf asks if a lot lies inside another): its centre of area, or, when that falls outside (an L or a bent
    /// strip), the point inside farthest from its edges (LabelPoint). Any unit; the ring closed or open.
    /// </summary>
    public static Pt LotCentre(List<Pt> ring)
    {
        var closed = ring.Count > 0 && ring[0] != ring[^1] ? [.. ring, ring[0]] : ring;
        var a = Area(closed);
        if (Math.Abs(a) < 1e-9)
        {
            return Centre(closed);
        }
        var (cx, cy) = (0.0, 0.0);
        for (var i = 0; i + 1 < closed.Count; i++)
        {
            var (p, q) = (closed[i], closed[i + 1]);
            var cross = p.X * q.Y - q.X * p.Y;
            (cx, cy) = (cx + (p.X + q.X) * cross, cy + (p.Y + q.Y) * cross);
        }
        (cx, cy) = (cx / (6 * a), cy / (6 * a));
        return Inside(closed, cx, cy) ? (cx, cy) : LabelPoint(closed);
    }

    /// <summary>The signed area (shoelace): positive when counter-clockwise.</summary>
    public static double Area(List<Pt> ring)
    {
        var area = 0.0;
        for (var i = 0; i + 1 < ring.Count; i++)
        {
            area += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
        }
        return area / 2;
    }

    /// <summary>
    /// The ring moved $d inwards (the setback line): each edge shifted along its inward normal, neighbours joined where
    /// they meet; a very sharp corner is cut off. A closed ring; a guide only.
    /// </summary>
    public static List<Pt> OffsetRing(List<Pt> ring, double d)
    {
        var pts = ring.Count > 1 && ring[0] == ring[^1] ? ring.SkipLast(1).ToList() : ring;
        var n = pts.Count;
        if (n < 3)
        {
            return [];
        }
        var area = 0.0;
        for (var i = 0; i < n; i++)
        {
            area += pts[i].X * pts[(i + 1) % n].Y - pts[(i + 1) % n].X * pts[i].Y;
        }
        var side = area > 0 ? 1 : -1;
        var edges = new List<(Pt Start, Pt Dir, Pt End, Pt Corner)>();
        for (var i = 0; i < n; i++)
        {
            var (a, b) = (pts[i], pts[(i + 1) % n]);
            var len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (len < 1e-6)
            {
                continue;
            }
            var (ux, uy) = ((b.X - a.X) / len, (b.Y - a.Y) / len);
            var (nx, ny) = (-uy * side, ux * side);
            edges.Add(((a.X + nx * d, a.Y + ny * d), (ux, uy), (b.X + nx * d, b.Y + ny * d), b));
        }
        var output = new List<Pt>();
        var m = edges.Count;
        for (var i = 0; i < m; i++)
        {
            var (p1, u1, end1, corner) = edges[(i + m - 1) % m];
            var (p2, u2, _, _) = edges[i];
            var cross = u1.X * u2.Y - u1.Y * u2.X;
            if (Math.Abs(cross) < 1e-9)
            {
                output.Add(p2);  // straight on: the shifted edges meet end to start
                continue;
            }
            var t = ((p2.X - p1.X) * u2.Y - (p2.Y - p1.Y) * u2.X) / cross;
            Pt q = (p1.X + u1.X * t, p1.Y + u1.Y * t);
            if (Math.Sqrt((q.X - corner.X) * (q.X - corner.X) + (q.Y - corner.Y) * (q.Y - corner.Y)) > 4 * d)
            {
                output.Add(end1);  // too sharp: cut the corner off
                output.Add(p2);
            }
            else
            {
                output.Add(q);
            }
        }
        output.Add(output[0]);
        return output;
    }

    /// <summary>
    /// The setback line $d inside the ring, trimmed: OffsetRing, of which only the stretches on the lot and at least
    /// $d from its boundary are kept, as open lines in order. At a notch or a short edge the shifted edges overshoot,
    /// loop or run back over themselves (seen on Munkemøllestræde 3, Odense, at 2.5, 3 and 4 m); trimmed, they end
    /// where the line comes nearer the boundary than $d, and a part of the lot narrower than 2 × $d has none. Each
    /// segment is tried in steps of $d / 20 and cut where it passes the limit (to a millimetre), not where segments
    /// cross: those meet only near each other, not on, at the corners that go wrong.
    /// </summary>
    public static List<List<Pt>> Setback(List<Pt> ring, double d)
    {
        var raw = OffsetRing(ring, d);
        bool Ok(Pt p) => Inside(ring, p.X, p.Y) && DistanceToEdges(ring, p) >= d * 0.995;
        static double Distance(Pt p, Pt q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        var pieces = new List<List<Pt>>();
        List<Pt>? cur = null;
        foreach (var (a, b) in raw.Zip(raw.Skip(1)))
        {
            var len = Distance(a, b);
            if (len < 1e-6)
            {
                continue;
            }
            Pt At(double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            // Where Ok changes between $t0 (as it is there) and $t1, to within a millimetre.
            double Limit(double t0, double t1)
            {
                var at0 = Ok(At(t0));
                while ((t1 - t0) * len > 0.001)
                {
                    var m = (t0 + t1) / 2;
                    (t0, t1) = Ok(At(m)) == at0 ? (m, t1) : (t0, m);
                }
                return (t0 + t1) / 2;
            }
            // A kept stretch from $t0 to $t1, joined to the one before when it goes on from its end. One shorter than
            // $d / 20 is a sliver at a corner (the stub of a short edge), left out so the corner is filleted clean.
            void Keep(double t0, double t1)
            {
                if ((t1 - t0) * len < Math.Max(0.01, d / 20))
                {
                    return;
                }
                var (p, q) = (At(t0), At(t1));
                if (cur is null || Distance(cur[^1], p) > 0.001)
                {
                    cur = [p];
                    pieces.Add(cur);
                }
                cur.Add(q);
            }
            var n = Math.Max(1, (int)Math.Ceiling(len / (d / 20)));
            var (was, wasOk) = (0.0, Ok(a));
            double? from = wasOk ? 0 : null;
            for (var i = 1; i <= n; i++)
            {
                var t = (double)i / n;
                var ok = Ok(At(t));
                if (ok != wasOk)
                {
                    var limit = Limit(was, t);
                    if (ok)
                    {
                        from = limit;
                    }
                    else if (from is { } f)
                    {
                        Keep(f, limit);
                        from = null;
                    }
                }
                (was, wasOk) = (t, ok);
            }
            if (from is { } start)
            {
                Keep(start, 1);
            }
        }
        // Then, as AutoCAD's Fillet with radius 0: where one piece ends near where the next begins (a corner, cut on both
        // sides at the limit), both are carried on to where their lines meet and made one, so the corner is sharp and
        // closed. Round from the last to the first too: the ring began mid-piece.
        Pt Corner(Pt a, Pt b, Pt c, Pt e) =>
            Meet(a, b, c, e) is { } m && Distance(m, b) <= d / 2 && Distance(m, c) <= d / 2 ? m : b;
        for (var joined = true; joined && pieces.Count > 1;)
        {
            joined = false;
            for (var i = 0; i < pieces.Count; i++)
            {
                var j = (i + 1) % pieces.Count;
                var (p, q) = (pieces[i], pieces[j]);
                if (Distance(p[^1], q[0]) <= d / 5)
                {
                    p[^1] = Corner(p[^2], p[^1], q[0], q[1]);
                    p.AddRange(q.Skip(1));
                    pieces.RemoveAt(j);
                    joined = true;
                    break;
                }
            }
        }
        // One piece all round whose ends nearly meet: closed at their corner.
        if (pieces is [var all] && all.Count > 3 && Distance(all[^1], all[0]) is var gap && gap > 1e-9 && gap <= d / 5)
        {
            all[0] = all[^1] = Corner(all[^2], all[^1], all[0], all[1]);
        }
        return pieces;

        // Where the lines through a-b and c-e meet; null when they're parallel.
        static Pt? Meet(Pt a, Pt b, Pt c, Pt e)
        {
            var (rx, ry, sx, sy) = (b.X - a.X, b.Y - a.Y, e.X - c.X, e.Y - c.Y);
            var den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12)
            {
                return null;
            }
            var t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
            return (a.X + t * rx, a.Y + t * ry);
        }
    }

    /// <summary>
    /// The ring's least box turned along one of its edges (a house's own box): its middle, its long side's direction
    /// in degrees, turned to read from below or the right (Label.Upright), and its long and short side. Any unit.
    /// </summary>
    public static (Pt Middle, double AngleDeg, double Long, double Short) LongBox(List<Pt> ring)
    {
        var (area, mx, my, ux, uy, a, b) = (double.MaxValue, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0);
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (dx, dy) = (ring[i].X - ring[j].X, ring[i].Y - ring[j].Y);
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
            {
                continue;
            }
            var (ex, ey) = (dx / len, dy / len);
            var us = ring.Select(p => p.X * ex + p.Y * ey).ToList();   // along the edge
            var vs = ring.Select(p => -p.X * ey + p.Y * ex).ToList();  // across it
            var (w, h) = (us.Max() - us.Min(), vs.Max() - vs.Min());
            if (w * h < area)
            {
                var (mu, mv) = ((us.Max() + us.Min()) / 2, (vs.Max() + vs.Min()) / 2);
                (area, mx, my, ux, uy, a, b) = (w * h, mu * ex - mv * ey, mu * ey + mv * ex, ex, ey, w, h);
            }
        }
        // The long side: along the edge, or across it.
        var (lx, ly, longSide, shortSide) = a >= b ? (ux, uy, a, b) : (-uy, ux, b, a);
        return ((mx, my), Label.Upright(Math.Atan2(ly, lx) * 180 / Math.PI), longSide, shortSide);
    }

    /// <summary>
    /// A closed ring (its first point repeated or not) less its edges that run along one of $others' (the edge's middle
    /// within $tol of it): the runs left, as open lines (none when all of it is shared). Null when nothing is shared, so
    /// the ring can stay one closed line. Matriklen's parcels share their corners, so a shared stretch is whole edges.
    /// </summary>
    public static List<List<Pt>>? Unshared(List<Pt> ring, List<List<Pt>> others, double tol)
    {
        var pts = ring.Count > 1 && ring[0] == ring[^1] ? ring.SkipLast(1).ToList() : ring;
        bool Shared(int i) => (pts[i], pts[(i + 1) % pts.Count]) is var (a, b)
            && others.Any(o => DistanceToEdges(o, ((a.X + b.X) / 2, (a.Y + b.Y) / 2)) <= tol);
        var start = Enumerable.Range(0, pts.Count).FirstOrDefault(Shared, -1);
        if (start < 0)
        {
            return null;
        }
        // Round from the shared edge, so no run wraps past the ring's first point.
        var pieces = new List<List<Pt>>();
        List<Pt>? cur = null;
        for (var k = 1; k <= pts.Count; k++)
        {
            var i = (start + k) % pts.Count;
            if (Shared(i))
            {
                cur = null;
                continue;
            }
            if (cur is null)
            {
                cur = [pts[i]];
                pieces.Add(cur);
            }
            cur.Add(pts[(i + 1) % pts.Count]);
        }
        return pieces;
    }

    /// <summary>A closed ring cut to the box (Sutherland–Hodgman). Empty if it lies outside.</summary>
    public static List<Pt> ClipRing(List<Pt> ring, (double X1, double Y1, double X2, double Y2) box)
    {
        var (x1, y1, x2, y2) = box;
        var edges = new (Func<Pt, bool> Inside, Func<Pt, Pt, Pt> Cut)[]
        {
            (p => p.X >= x1, (a, b) => (x1, a.Y + (b.Y - a.Y) * (x1 - a.X) / (b.X - a.X))),
            (p => p.X <= x2, (a, b) => (x2, a.Y + (b.Y - a.Y) * (x2 - a.X) / (b.X - a.X))),
            (p => p.Y >= y1, (a, b) => (a.X + (b.X - a.X) * (y1 - a.Y) / (b.Y - a.Y), y1)),
            (p => p.Y <= y2, (a, b) => (a.X + (b.X - a.X) * (y2 - a.Y) / (b.Y - a.Y), y2)),
        };
        var pts = ring;
        foreach (var (inside, cut) in edges)
        {
            var input = pts;
            pts = [];
            for (var i = 0; i < input.Count; i++)
            {
                var (cur, prev) = (input[i], input[(i + input.Count - 1) % input.Count]);
                if (inside(cur))
                {
                    if (!inside(prev))
                    {
                        pts.Add(cut(prev, cur));
                    }
                    pts.Add(cur);
                }
                else if (inside(prev))
                {
                    pts.Add(cut(prev, cur));
                }
            }
            if (pts.Count == 0)
            {
                return [];
            }
        }
        return pts;
    }

    /// <summary>An open line cut to the box: the pieces inside (Liang–Barsky per segment).</summary>
    public static List<List<Pt>> ClipLine(List<Pt> line, (double X1, double Y1, double X2, double Y2) box)
    {
        var (x1, y1, x2, y2) = box;
        var pieces = new List<List<Pt>>();
        var cur = new List<Pt>();
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var (a, b) = (line[i], line[i + 1]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var (t0, t1, ok) = (0.0, 1.0, true);
            foreach (var (p, q) in new[] { (-dx, a.X - x1), (dx, x2 - a.X), (-dy, a.Y - y1), (dy, y2 - a.Y) })
            {
                if (p == 0)
                {
                    ok &= q >= 0;
                    continue;
                }
                var t = q / p;
                if (p < 0)
                {
                    t0 = Math.Max(t0, t);
                }
                else
                {
                    t1 = Math.Min(t1, t);
                }
            }
            if (!ok || t0 > t1)
            {
                if (cur.Count > 0)
                {
                    pieces.Add(cur);
                    cur = [];
                }
                continue;
            }
            if (cur.Count == 0)
            {
                cur.Add((a.X + t0 * dx, a.Y + t0 * dy));
            }
            cur.Add((a.X + t1 * dx, a.Y + t1 * dy));
            if (t1 < 1.0)  // left the box: this piece ends here
            {
                pieces.Add(cur);
                cur = [];
            }
        }
        if (cur.Count > 1)
        {
            pieces.Add(cur);
        }
        return pieces;
    }
}
