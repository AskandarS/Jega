using Autodesk.Revit.DB;
using Jega.Core;

namespace Jega.Revit;

/// <summary>
/// The terrain grid's frame as a dashed rectangle of detail lines (line style "Jega Grid"): where levels
/// can be looked up. A new run replaces the old frame in every view that had one, plus the active view if it's a
/// plan (a section or 3D view has no plan to draw it in).
/// </summary>
internal static class GridFrameWriter
{
    public const string Role = "GridFrame";

    /// <summary>Inside a transaction. Returns how many views got the frame.</summary>
    public static int Draw(Document doc, View active, TerrainGrid grid, Placement placement)
    {
        var old = ProjectStore.Marked(doc, Role, typeof(CurveElement));
        // A shared model: a view whose frame someone else holds (TerrainLines.Held) keeps it as it is, not drawn again;
        // deleting it would refuse the whole transaction.
        var held = old.Where(e => TerrainLines.Held(doc, e.Id) is not null).Select(e => e.OwnerViewId).ToHashSet();
        old = old.Where(e => !held.Contains(e.OwnerViewId)).ToList();
        var views = old.Select(e => e.OwnerViewId).Concat(active is ViewPlan ? new[] { active.Id } : []).Distinct()
            .Where(id => !held.Contains(id)).ToList();
        doc.Delete(old.Select(e => e.Id).ToList());

        var style = LineStyles.GridFrame(doc);
        var drawn = 0;
        foreach (var id in views)
        {
            if (doc.GetElement(id) is not ViewPlan view)
            {
                continue;
            }
            // Detail lines lie in the view's plane: at its level's height above the internal origin.
            var z = view.GenLevel?.ProjectElevation ?? 0;
            XYZ[] c =
            [
                placement.Point(grid.X0, grid.Y0, z), placement.Point(grid.MaxX, grid.Y0, z),
                placement.Point(grid.MaxX, grid.MaxY, z), placement.Point(grid.X0, grid.MaxY, z),
            ];
            for (var k = 0; k < 4; k++)
            {
                var line = doc.Create.NewDetailCurve(view, Line.CreateBound(c[k], c[(k + 1) % 4]));
                line.LineStyle = style;
                ProjectStore.Mark(line, Role, own: true);
            }
            drawn++;
        }
        return drawn;
    }
}
