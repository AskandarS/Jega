using Autodesk.Revit.DB;

namespace Jega.Revit;

/// <summary>
/// The lot's outline. From Load from Address it is kept in the model and placed by the site's anchor (Placement),
/// so it sits where the terrain does, as of the last Refresh. Else from an imported site plan DXF:
/// SiteplanDxf draws the lot the address lies on as one closed polyline on its own layer, GRUND, read
/// from the import as placed in the model (a DXF imported by hand).
/// </summary>
internal static class LotOutline
{
    public const string Layer = Core.SiteplanLayers.Lot;

    /// <summary>
    /// The outline's corners in model coordinates (Z dropped), or null if there's no lot. Always placed by the site's
    /// anchor (Placement.Get), so it sits where the terrain does as of the last Refresh, even if the DXF has been
    /// moved since: the lot kept by Load from Address, else the import's lot in the DXF's own coordinates (the marked
    /// site plan, else the largest lot of any import).
    /// </summary>
    public static List<XYZ>? Find(Document doc)
    {
        var placement = Placement.Get(doc);
        if (ProjectStore.ReadSite(doc) is { Lot.Count: >= 3 } site)
        {
            return site.Lot.Select(p => placement.Point(p.X, p.Y, 0)).ToList();
        }
        var imports = Placement.Siteplan(doc) is { } marked
            ? [marked]
            : new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
        var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
        List<XYZ>? best = null;
        foreach (var import in imports)
        {
            if (import.get_Geometry(options) is not { } geometry)
            {
                continue;
            }
            // Back into the DXF's own coordinates (feet from its 0,0, the site's, north up), then placed.
            var toDxf = import.GetTotalTransform().Inverse;
            foreach (var ring in Rings(doc, geometry))
            {
                if (best is null || Math.Abs(Area(ring)) > Math.Abs(Area(best)))
                {
                    best = ring.Select(p => toDxf.OfPoint(p)).Select(p => placement.Point(Units.MmFromFeet(p.X), Units.MmFromFeet(p.Y), 0)).ToList();
                }
            }
        }
        return best;
    }

    /// <summary>
    /// The lot's holes (parcels inside it that aren't part of it), in model coordinates
    /// and placed as Find's outline; empty for most lots. From the surroundings Load from Address kept (the lot's rings
    /// after the first, mm from the project's origin as the outline), else the import's other GRUND rings inside its lot.
    /// </summary>
    public static List<List<XYZ>> Holes(Document doc)
    {
        var placement = Placement.Get(doc);
        if (ProjectStore.ReadSite(doc) is { Lot.Count: >= 3 })
        {
            if (ProjectStore.ReadSurroundings(doc) is not { } json)
            {
                return [];
            }
            Core.Surroundings s;
            try
            {
                s = Core.Surroundings.Parse(json);
            }
            catch (FormatException)
            {
                // Surroundings that can't be read (damaged, or from another Jega): the lot without its holes, rather
                // than Settings, Generate Terrain or Draw Lot Boundary not opening at all. Export and a redraw say why.
                return [];
            }
            // Found by the address point, measured from the project's origin as the outline is (Placement.Origin; from the
            // address point the holes sat up to 0.7 m off the lot).
            var lot = s.Lots.FirstOrDefault(l => Core.Geometry.Inside(l.Rings, s.X, s.Y));
            var (oe, on) = Placement.Origin(doc) ?? (s.X, s.Y);
            return lot is null ? [] : lot.Rings.Skip(1).Where(r => r.Count >= 3)
                .Select(r => Open(r.Select(p => placement.Point(Math.Round((p.X - oe) * 1000, 1), Math.Round((p.Y - on) * 1000, 1), 0)).ToList()))
                .ToList();
        }
        // From an import: the rings on GRUND inside the largest (only the lot itself is drawn on GRUND).
        if (Find(doc) is not { } outer)
        {
            return [];
        }
        var imports = Placement.Siteplan(doc) is { } marked
            ? [marked]
            : new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
        var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
        var holes = new List<List<XYZ>>();
        foreach (var import in imports)
        {
            if (import.get_Geometry(options) is not { } geometry)
            {
                continue;
            }
            var toDxf = import.GetTotalTransform().Inverse;
            foreach (var ring in Rings(doc, geometry))
            {
                var placed = ring.Select(p => toDxf.OfPoint(p)).Select(p => placement.Point(Units.MmFromFeet(p.X), Units.MmFromFeet(p.Y), 0)).ToList();
                // Smaller than the lot, and its middle inside it (a hole's corners may lie on the lot's own boundary).
                var middle = new XYZ(placed.Average(p => p.X), placed.Average(p => p.Y), 0);
                if (Math.Abs(Area(placed)) < Math.Abs(Area(outer)) - 1e-6 && Inside(outer, middle))
                {
                    holes.Add(placed);
                }
            }
        }
        return holes;
    }

    // A ring without its closing point (the first again at the end).
    private static List<XYZ> Open(List<XYZ> ring)
    {
        if (ring.Count > 1 && ring[0].IsAlmostEqualTo(ring[^1]))
        {
            ring.RemoveAt(ring.Count - 1);
        }
        return ring;
    }

    // Even-odd rule on the plan.
    private static bool Inside(List<XYZ> ring, XYZ p)
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

    /// <summary>
    /// Whether the site plan is left in the model as exploded lines: no import to find, but curves on the GRUND
    /// and RAMME layers (their line styles' categories; every Jega plan has both), which a redraw would draw over. So
    /// Refresh leaves an exploded plan alone, and offers to draw one again only when it was truly deleted. An office's
    /// own line style "GRUND" alone isn't taken for it.
    /// </summary>
    public static bool Exploded(Document doc)
    {
        var layers = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Lines).WhereElementIsNotElementType()
            .OfType<CurveElement>().Select(c => c.LineStyle?.Id).OfType<ElementId>().Distinct()
            .Select(id => (doc.GetElement(id) as GraphicsStyle)?.GraphicsStyleCategory?.Name).ToList();
        return new[] { Layer, Core.SiteplanLayers.Frame }.All(l => layers.Contains(l, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The imports that are a site plan Jega drew: imported, not linked (a surveyor's or a colleague's DWG), named as
    /// Jega names it ("Jega siteplan.dxf", Export's "&lt;name&gt;-siteplan.dxf"), and with the lot on the GRUND layer.
    /// Another office's drawing with a GRUND layer isn't taken for it.
    /// </summary>
    public static List<ImportInstance> SiteplanImports(Document doc)
    {
        var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
        return new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
            .Where(i => !i.IsLinked && doc.GetElement(i.GetTypeId())?.Name is { } name
                && name.Contains("siteplan", StringComparison.OrdinalIgnoreCase))
            .Where(i => i.get_Geometry(options) is { } g && Rings(doc, g).Any())
            .ToList();
    }

    /// <summary>The outline as a closed loop at height z (feet), dropping sides too short for Revit.</summary>
    public static CurveLoop Loop(Document doc, List<XYZ> corners, double z)
    {
        var loop = new CurveLoop();
        for (var k = 0; k < corners.Count; k++)
        {
            var (a, b) = (corners[k], corners[(k + 1) % corners.Count]);
            if (a.DistanceTo(b) > doc.Application.ShortCurveTolerance)
            {
                loop.Append(Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
            }
        }
        return loop;
    }

    // Every polyline on the GRUND layer, in model coordinates: the instance geometry is already transformed.
    private static IEnumerable<List<XYZ>> Rings(Document doc, GeometryElement geometry)
    {
        foreach (var obj in geometry)
        {
            if (obj is GeometryInstance gi)
            {
                foreach (var ring in Rings(doc, gi.GetInstanceGeometry()))
                {
                    yield return ring;
                }
            }
            else if (obj is PolyLine pl && IsLayer(doc, pl.GraphicsStyleId))
            {
                var pts = pl.GetCoordinates().Select(p => new XYZ(p.X, p.Y, 0)).ToList();
                if (pts.Count > 1 && pts[0].IsAlmostEqualTo(pts[^1]))
                {
                    pts.RemoveAt(pts.Count - 1);
                }
                if (pts.Count >= 3)
                {
                    yield return pts;
                }
            }
        }
    }

    private static bool IsLayer(Document doc, ElementId styleId) =>
        doc.GetElement(styleId) is GraphicsStyle style && string.Equals(style.GraphicsStyleCategory?.Name, Layer, StringComparison.OrdinalIgnoreCase);

    private static double Area(List<XYZ> ring) =>
        ring.Select((p, k) => p.X * ring[(k + 1) % ring.Count].Y - ring[(k + 1) % ring.Count].X * p.Y).Sum() / 2;
}
