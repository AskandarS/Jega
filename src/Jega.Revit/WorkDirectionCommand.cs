using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Work Direction": Revit's own Rotate Project North (its dialog, its order of choices), started from the Site panel.
/// Project North is set as the user likes: the whole model turns, as AutoCAD's UCS or ArchiCAD's rotated view would,
/// and the coordinates and true north stay. Revit does
/// the turning, of everything, views and annotation too; Jega has no API for it and turns nothing itself. Revit runs
/// it after this command has ended, so Jega waits for it (AfterRotate) and Refreshes once the project has turned: the
/// site plan's paper straight again, the terrain frame and the rest along. Revit runs it only in a view set to Project
/// North; in a True North view it did nothing at all, so that is said instead.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class WorkDirectionCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_WorkDirection;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        if (doc.ActiveView.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH)?.AsInteger() == ViewNorthCommand.TrueNorth)
        {
            Receipt(new ReceiptText(Strings.WorkDirection_TrueNorth));
            return Result.Cancelled;
        }
        data.Application.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.RotateProjectNorth));
        Arm(data.Application, doc);
        return Result.Succeeded;
    }

    // Armed by Run: the document and its project's angle to true north (degrees) when Rotate Project North was posted.
    // Null when nothing is awaited.
    private static (Document Doc, double North)? _armed;

    // Waits for Revit to be idle with the project turned (AfterRotate).
    private static void Arm(UIApplication app, Document doc)
    {
        if (_armed is null)
        {
            app.Idling += AfterRotate;
        }
        _armed = (doc, Placement.FromProject(doc).AngleToTrueNorth);
    }

    /// <summary>Stops waiting: a Jega command ran (a rotation that was cancelled is not awaited for ever), or the
    /// document changed, or the Refresh ran.</summary>
    internal static void Disarm(UIApplication app)
    {
        if (_armed is not null)
        {
            app.Idling -= AfterRotate;
            _armed = null;
        }
    }

    // Revit idle after the posted Rotate Project North: once the project has turned (by 0.01° or more), Refresh as the
    // button would, its own Undo, quiet unless it has a problem to tell (as JegaCommand's Refresh first). Not turned yet
    // (Revit still in its picks) or not at all (Esc): wait on, until the next Jega command.
    private static void AfterRotate(object? sender, IdlingEventArgs e)
    {
        if (sender is not UIApplication app || _armed is not var (doc, north))
        {
            return;
        }
        if (app.ActiveUIDocument?.Document is not { } active || !active.Equals(doc))
        {
            Disarm(app);
            return;
        }
        var now = Placement.FromProject(doc).AngleToTrueNorth;
        if (Math.Abs(Angles.TurnDegrees(Angles.ToRadians(now), Angles.ToRadians(north))) < 0.01)
        {
            return;
        }
        Disarm(app);
        var title = Ui.Title;
        try
        {
            ProjectLanguage.Apply(doc);
            Ui.Title = Ui.OneLine(Strings.Ribbon_Refresh);
            // Cancelled by the user (no receipt): nothing to say; Refresh, or a command that Refreshes first, asks again.
            var (result, receipt) = RefreshCommand.Run(doc);
            if (receipt is not null && (result == Result.Failed || (result == Result.Succeeded && receipt.Warning)))
            {
                Ui.Receipt(receipt);
            }
        }
        catch (Exception ex)
        {
            JegaLog.Write(ex);
            Ui.Failed(ex);
        }
        finally
        {
            Ui.Title = title;
        }
    }
}
