using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Draw Lot Boundary": a filled region of its own type "Jega Lot Boundary" exactly on the lot's outline from the site
/// plan DXF (layer GRUND), in the active plan view, then its elevations. Its corners are the DXF's lot corners, so the
/// elevations match the DXF's spot elevations (KOTER) there. A parcel inside the lot that isn't part of it is a hole in
/// the region; the elevations window measures the outer outline only. A new run replaces the region it made before.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class LotEdgeCommand : JegaCommand
{
    private const string Role = "LotEdge";

    protected override string Button => Strings.Ribbon_LotBoundary;

    // The lot's outline is placed by the frame (LotOutline.Find).
    protected override bool UsesFrame => true;

    /// <summary>
    /// True when the lot boundary drawn before no longer sits on the lot: the site moved or turned since (rotate and
    /// Refresh, or Settings' anchor), so no point of it is on a lot corner now. Every side of it starts on one when drawn.
    /// </summary>
    public static bool Stale(Document doc)
    {
        if (ProjectStore.Marked(doc, Role, typeof(FilledRegion)).FirstOrDefault() is not FilledRegion region || LotOutline.Find(doc) is not { } corners)
        {
            return false;
        }
        var tolerance = Units.FeetFromMm(5);
        return !region.GetBoundaries().SelectMany(loop => loop).Select(side => side.GetEndPoint(0))
            .Any(p => corners.Any(c => Math.Abs(c.X - p.X) < tolerance && Math.Abs(c.Y - p.Y) < tolerance));
    }

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        if (doc.ActiveView is not ViewPlan view || view.GenLevel is null)
        {
            Receipt(new ReceiptText(Strings.LotEdge_OpenPlan) { Warning = true });
            return Result.Cancelled;
        }
        if (!StoredTerrain.TryGet(doc, out var stored, out var grid, out var problem))
        {
            Receipt(new ReceiptText(problem) { Warning = true });
            return Result.Cancelled;
        }
        if (LotOutline.Find(doc) is not { } corners)
        {
            Receipt(new ReceiptText(Strings.LotEdge_NoLot)
            {
                Steps = [Fill(Strings.LotEdge_NoLotStep, LotOutline.Layer), Strings.LotEdge_AgainStep],
                Warning = true,
            });
            return Result.Cancelled;
        }

        FilledRegionType type;
        var withoutHole = false;
        using (var t = new Transaction(doc, Strings.Undo_LotBoundary))
        {
            t.Start();
            try
            {
                type = EdgeRegions.FindType(doc, EdgeRegions.LotTypeName) ?? EdgeRegions.CreateType(doc, EdgeRegions.LotTypeName);
                doc.Delete(ProjectStore.Marked(doc, Role, typeof(FilledRegion)).Select(e => e.Id).ToList());
                // The region lies in the view's plane; heights come from the terrain, never from it.
                var z = view.GenLevel.ProjectElevation;
                var loop = LotOutline.Loop(doc, corners, z);
                // The parcels inside the lot that aren't part of it as loops of the
                // same region, so Revit leaves them out as holes. One Revit refuses (e.g. on the lot's own boundary):
                // the outline alone, and said so.
                var holes = LotOutline.Holes(doc).Select(h => LotOutline.Loop(doc, h, z)).ToList();
                FilledRegion region;
                try
                {
                    region = FilledRegion.Create(doc, type.Id, view.Id, [loop, .. holes]);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException) when (holes.Count > 0)
                {
                    region = FilledRegion.Create(doc, type.Id, view.Id, [loop]);
                    withoutHole = true;
                }
                region.SetLineStyleId(LineStyles.Edge(doc).Id);
                ProjectStore.Mark(region, Role, own: true);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException e)
            {
                // E.g. a lot outline that touches itself: Revit refuses the region.
                t.RollBack();
                Receipt(new ReceiptText(Strings.LotEdge_Failed)
                {
                    Lines = [Strings.LotEdge_FailedWhy],
                    Details = [Fill(Strings.Receipt_Revit, e.Message)],
                    Warning = true,
                });
                return Result.Failed;
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                Receipt(new ReceiptText(Strings.LotEdge_RevitStopped) { Warning = true });
                return Result.Failed;
            }
        }
        if (withoutHole)
        {
            Receipt(new ReceiptText(Strings.LotEdge_WithoutHole) { Steps = [Strings.LotEdge_CutByHand], Warning = true });
        }
        return ShowLevels(data, doc, stored, grid, type);
    }

    /// <summary>The levels window for the lot boundary; the offset it closes with is kept for the project.</summary>
    private static Result ShowLevels(ExternalCommandData data, Document doc, ProjectSettings stored, TerrainGrid grid, FilledRegionType type)
    {
        var edges = EdgeRegions.Find(doc, type, Placement.Get(doc));
        if (edges.Count == 0)
        {
            Receipt(new ReceiptText(Strings.LotEdge_NothingToMeasure) { Steps = [Strings.LotEdge_AgainStep], Warning = true });
            return Result.Cancelled;
        }


        var window = new ResultWindow(grid, edges, stored.LastOffsetMm, stored.FileName);
        new WindowInteropHelper(window) { Owner = data.Application.MainWindowHandle };
        window.ShowDialog();

        // The offset is kept as the project's last used value.
        if (window.Offset != stored.LastOffsetMm)
        {
            using var t = new Transaction(doc, Strings.Undo_Offset);
            t.Start();
            var labelProblem = ProjectStore.WriteOffset(doc, window.Offset);
            if (t.Commit() != TransactionStatus.Committed)
            {
                Receipt(new ReceiptText(Strings.LotEdge_OffsetNotKept) { Warning = true });
            }
            else if (labelProblem is not null)
            {
                Receipt(new ReceiptText(labelProblem) { Warning = true });
            }
        }
        return Result.Succeeded;
    }
}
