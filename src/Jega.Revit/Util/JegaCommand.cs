using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>
/// Every Jega button but About (which needs no document): the project's language first (ARCHITECTURE invariant 19), a Refresh when
/// the command places by the frame and the site plan moved since (UsesFrame), then the command's Run; a failure it didn't foresee goes to the log (JegaLog) and a plain message (Ui.Failed), never out
/// into Revit's own dialog. Esc in one of Revit's picks is not a failure. Either way Cancelled: a transaction left
/// open is rolled back as it is disposed.
/// </summary>
public abstract class JegaCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        // The title before: "Jega", or the command that opened this one (Settings from Keys.FetcherOrAsk), which goes on after.
        var title = Ui.Title;
        // A Work Direction still waited for (its rotation cancelled): no longer, so Refresh never runs unasked later.
        WorkDirectionCommand.Disarm(data.Application);
        try
        {
            var doc = data.Application.ActiveUIDocument.Document;
            ProjectLanguage.Apply(doc);
            Ui.Title = Ui.OneLine(Button);
            // A read-only file (opened read-only): the command writes in memory, but a save would fail (Revit's own
            // freeze, the same for any edit). Said once per model, not blocked, before the first writing command.
            if (Writes && doc.IsReadOnlyFile && _warnedReadOnly.Add(doc))
            {
                Ui.Receipt(new ReceiptText(Strings.ReadOnly_Warning) { Warning = true });
            }
            // The site plan moved or turned since the last Refresh (a new work direction, a hand move): Refreshed first,
            // so the command places by where the import is now, not the frame before. Stopped by Revit: said, and
            // Cancelled as below, not into Revit's own dialog.
            if (UsesFrame && Placement.Stale(doc) && !RefreshFirst(doc))
            {
                return Result.Cancelled;
            }
            return Run(data, doc);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception e)
        {
            JegaLog.Write(e);
            Ui.Failed(e);
            return Result.Cancelled;
        }
        finally
        {
            Ui.Title = title;
        }
    }

    /// <summary>The command's button text (Strings.Ribbon_*), in the project's language: the title of its dialogs (Ui.Title).</summary>
    protected abstract string Button { get; }

    /// <summary>Whether the command writes in the model: all but Export (which only reads it and writes its own files).</summary>
    protected virtual bool Writes => true;

    /// <summary>Whether the command places by the site's frame (Placement.Get), so a site plan moved or turned since the
    /// last Refresh is Refreshed first. Not Refresh, Load from Address or the north buttons, which read the import themselves.</summary>
    protected virtual bool UsesFrame => false;

    // Refresh before the command: quiet when it went well (its own Undo, "Refresh"); its receipt, said to be first, when
    // it has a problem to tell. False when Revit stopped it, or the user cancelled it (no receipt): the command doesn't run
    // on the frame before. Cancelled (no terrain to refresh) changed nothing, and the command says what it needs itself.
    private static bool RefreshFirst(Document doc)
    {
        var (result, receipt) = RefreshCommand.Run(doc);
        if (receipt is null)
        {
            return false;
        }
        if (result == Result.Failed || (result == Result.Succeeded && receipt.Warning))
        {
            var first = ProjectStore.ReadOptions(doc).SiteAnchor == Core.ProjectOptions.AnchorShared ? Strings.Refresh_FirstShared : Strings.Refresh_First;
            Ui.Receipt(receipt with { Lines = [first, .. receipt.Lines] });
        }
        return result != Result.Failed;
    }

    // The read-only files warned about this session (by reference: a reopened model is a new one, and is warned again).
    private static readonly HashSet<Document> _warnedReadOnly = [];

    /// <summary>The command, in the project's language; $doc the active one.</summary>
    protected abstract Result Run(ExternalCommandData data, Document doc);
}
