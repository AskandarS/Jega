using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "True / Project North": the active plan view's Orientation switched to the other one, and the site plan drawn
/// again for it when it stands in this view (SiteplanRedraw.Straighten: its frame and texts straight, the lines turned).
/// Silent: the view turning on the screen says enough. Only a view template holding the orientation is said, as
/// nothing would be seen to happen. The only thing in Jega that touches a view's orientation. True north comes of the
/// shared coordinates, so nothing else changes here. One Undo.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ViewNorthCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_ViewNorth;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        if (doc.ActiveView is not ViewPlan plan || plan.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH) is not { } orientation)
        {
            return Result.Cancelled;
        }
        if (orientation.IsReadOnly)
        {
            Receipt(new ReceiptText(Strings.North_ViewLocked));
            return Result.Cancelled;
        }
        using var t = new Transaction(doc, Strings.Undo_ViewNorth);
        t.Start();
        try
        {
            orientation.Set(orientation.AsInteger() == TrueNorth ? ProjectNorth : TrueNorth);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            t.RollBack();
            Receipt(new ReceiptText(Strings.North_ViewLocked));
            return Result.Cancelled;
        }
        // The site plan drawn for the view's new north, if it stands in this view (else it is mended when its own view is
        // used): silent too, the plan turning on the screen says it; a failure is logged and the switch stays. The
        // terrain line labels are laid out by the view's up and right (HIGH / LOW above, falls readable): made again in
        // this view for its new north, in the same Undo, so none stands upside down and no Refresh is needed.
        doc.Regenerate();
        SiteplanRedraw.Straighten(doc, ProjectStore.ReadOptions(doc));
        var labelProblem = TerrainLines.Update(doc, new HashSet<ElementId> { plan.Id });
        if (t.Commit() != TransactionStatus.Committed)
        {
            return Result.Failed;
        }
        // Silent, unless the labels couldn't be made again.
        if (labelProblem is { } shown)
        {
            Receipt(new ReceiptText(shown) { Warning = true });
        }
        return Result.Succeeded;
    }

    // PLAN_VIEW_NORTH, a plan view's Orientation: 0 Project North, 1 True North.
    internal const int ProjectNorth = 0, TrueNorth = 1;
}
