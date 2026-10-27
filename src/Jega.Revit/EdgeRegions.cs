using Autodesk.Revit.DB;
using Jega.Core;

namespace Jega.Revit;

/// <summary>One drawn edge: the filled region it came from and its outline in the CSV's terms (mm, true north).</summary>
internal sealed record DrawnEdge(ElementId Region, string ViewName, IReadOnlyList<Point2> Ring);

/// <summary>
/// The lot boundary's filled regions (type "Jega Lot Boundary", from Draw Lot Boundary), in any view. Each region is one
/// edge; only its outer outline is used, holes are ignored.
/// </summary>
internal static class EdgeRegions
{
    /// <summary>The type Draw Lot Boundary draws with.</summary>
    public static string LotTypeName => Config.Current.Revit.LotEdgeType;

    public static FilledRegionType? FindType(Document doc, string name) =>
        new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
            .FirstOrDefault(t => t.Name == name);

    /// <summary>
    /// Makes the type, inside a transaction: a copy of the model's first filled region type with no fill, so
    /// only the outline shows and nothing under it is hidden.
    /// </summary>
    public static FilledRegionType CreateType(Document doc, string name)
    {
        var source = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().First();
        var type = (FilledRegionType)source.Duplicate(name);
        type.ForegroundPatternId = ElementId.InvalidElementId;
        type.BackgroundPatternId = ElementId.InvalidElementId;
        type.IsMasking = false;
        return type;
    }

    public static List<DrawnEdge> Find(Document doc, FilledRegionType type, Placement placement) =>
        new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)).Cast<FilledRegion>()
            .Where(r => r.GetTypeId() == type.Id)
            .Select(r => new DrawnEdge(r.Id, (doc.GetElement(r.OwnerViewId) as View)?.Name ?? "", Outline(r, placement)))
            .Where(e => e.Ring.Count >= 3)
            .ToList();

    // The outer loop (the largest), as a polygon: straight sides give their corners, arcs and splines are
    // split into Revit's own short pieces. Each point is turned into the CSV's terms.
    private static List<Point2> Outline(FilledRegion region, Placement placement)
    {
        var rings = region.GetBoundaries().Select(loop =>
            loop.SelectMany(curve => curve.Tessellate().SkipLast(1)).Select(placement.ToCsv).ToList()).ToList();
        return rings.MaxBy(r => Math.Abs(Area(r))) ?? [];
    }

    private static double Area(List<Point2> ring) =>
        ring.Select((p, k) => p.X * ring[(k + 1) % ring.Count].Y - ring[(k + 1) % ring.Count].X * p.Y).Sum() / 2;
}
