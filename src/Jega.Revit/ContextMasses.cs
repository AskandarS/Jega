using Autodesk.Revit.DB;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// The buildings around the lot as plain masses: each outline from the context extruded from its
/// ground to its top, as a DirectShape in Generic Models (visible in every view, unlike the Mass category). Placed like
/// the terrain: by the site's anchor (Placement), DVR90 heights through Revit's 0. The lot's own buildings only with
/// Settings' "Include the buildings on the lot" (off by default: the existing house is modelled by hand; on for a
/// lot with many buildings), commented apart so the one being replaced can be found. A new run
/// replaces the masses made before.
/// </summary>
internal static class ContextMasses
{
    public const string Role = "Context";

    public static bool Shown(Document doc) => ProjectStore.Marked(doc, Role, typeof(DirectShape)).Count > 0;

    /// <summary>Inside a transaction. Returns how many masses were made, and how many outlines Revit refused.</summary>
    public static (int Made, int Refused) Build(Document doc, IReadOnlyList<ContextBuilding> buildings, Placement placement, double dvr90Mm)
    {
        // The workset and phases of each mass before, by where it stood: a new one takes over those of the one on its
        // building, so a building set apart by hand (the house on the lot demolished) stays so and the others don't follow.
        var before = ProjectStore.Marked(doc, Role, typeof(DirectShape))
            .Select(e => (At: e.get_BoundingBox(null) is { } box ? (box.Min + box.Max) / 2 : null, Kept: Inherited.From(e))).ToList();
        Remove(doc);
        var tolerance = doc.Application.ShortCurveTolerance;
        var (made, refused) = (0, 0);
        var withLot = ProjectStore.ReadOptions(doc).ContextIncludesLot;
        foreach (var b in buildings.Where(b => withLot || !b.Own))
        {
            try
            {
                var z = Units.FeetFromMm(b.Base - dvr90Mm);
                // Corners closer than Revit's shortest line are merged, so every edge can be drawn.
                var corners = new List<XYZ>();
                foreach (var p in b.Ring.Select(p => placement.Point(p.X, p.Y, z)))
                {
                    if (corners.Count == 0 || corners[^1].DistanceTo(p) > tolerance)
                    {
                        corners.Add(p);
                    }
                }
                if (corners.Count > 1 && corners[^1].DistanceTo(corners[0]) <= tolerance)
                {
                    corners.RemoveAt(corners.Count - 1);
                }
                if (corners.Count < 3)
                {
                    refused++;
                    continue;
                }
                var loop = new CurveLoop();
                for (var i = 0; i < corners.Count; i++)
                {
                    loop.Append(Line.CreateBound(corners[i], corners[(i + 1) % corners.Count]));
                }
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry([loop], XYZ.BasisZ, Units.FeetFromMm(b.Height));
                var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.SetShape([solid]);
                shape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(
                    Fill(b.Own ? Strings.Context_CommentOnLot : Strings.Context_Comment, Units.Level(b.Top - b.Base)));
                ProjectStore.Mark(shape, Role, own: true);
                var outline = corners.Select(c => (c.X, c.Y)).ToList();
                before.FirstOrDefault(m => m.At is { } at && Geometry.Inside(outline, at.X, at.Y)).Kept?.To(shape);
                made++;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                refused++;  // an outline Revit can't extrude, e.g. one that crosses itself (argument errors included)
            }
        }
        return (made, refused);
    }

    /// <summary>Inside a transaction: the masses go; the stored context stays.</summary>
    public static void Remove(Document doc)
    {
        var old = ProjectStore.Marked(doc, Role, typeof(DirectShape));
        if (old.Count > 0)
        {
            doc.Delete(old.Select(e => e.Id).ToList());
        }
    }
}
