using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Refresh": everything Jega has put in the model, lined up again with the site. In order:
///   1. the site's place read from the site plan import (placed by the shared coordinates first); true north
///      and the Survey Point are never set here,
///   2. the grid frame (in the plans that have it, and the active view if a plan) and the terrain line labels in every
///      view, from the stored terrain,
///   3. the context masses, if shown,
///   4. the Toposolid, if Generate Terrain has made one: kept if nothing it's made from changed, moved up or down
///      if only Revit 0's DVR90 did, made again otherwise (e.g. a new orientation).
/// Only what's already there is made again: Refresh never adds what wasn't switched on. One Undo takes it all back.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class RefreshCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_Refresh;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        var (result, receipt) = Run(doc);
        if (receipt is not null)
        {
            Receipt(receipt);
        }
        return result;
    }

    /// <summary>The refresh itself, for the button and Settings' OK. Returns how it went and its receipt (none when the
    /// user cancelled: nothing was done, nothing to say).</summary>
    internal static (Result Result, ReceiptText? Receipt) Run(Document doc)
    {
        // Any view: the grid frame is remade in the plans that have it (and the active one, if a plan).
        var view = doc.ActiveView;
        if (!StoredTerrain.TryGet(doc, out var stored, out var grid, out var problem))
        {
            return (Result.Cancelled, new ReceiptText(problem) { Warning = true });
        }

        // Problems the user must act on go first; the rest is for the details.
        var problems = new List<string>();
        var lines = new List<string>();
        string? toposolid = null;
        string? movedBack = null;
        string? straightened = null;
        ReceiptText? toposolidProblem = null;
        var noImport = false;
        var reload = false;
        var redrawn = false;

        var options = ProjectStore.ReadOptions(doc);
        // To jega.log: where the import, the frame and true north were before and after, and what each step did, so a
        // Refresh that moves the site (seen in a True North view) can be traced from one report.
        string State()
        {
            var import = Placement.Siteplan(doc);
            var at = import is null ? null : Placement.AddressPointOf(import);
            var frame = ProjectStore.ReadFrame(doc);
            return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"import {(at is null ? "none" : $"{Units.MmFromFeet(at.X):0},{Units.MmFromFeet(at.Y):0} mm, {Angles.ToDegrees(Placement.NorthOf(import!)):0.###}° (drawn {ProjectStore.ReadDrawnTurn(import!):0.###}°)")}; " +
                $"frame {(frame is { } f ? $"{Units.MmFromFeet(f.X):0},{Units.MmFromFeet(f.Y):0} mm, {Angles.ToDegrees(f.Angle):0.###}°" : "none")}; " +
                $"true north {Placement.FromProject(doc).AngleToTrueNorth:0.###}°");
        }
        var before = State();
        // No site plan found where one is used: every import to jega.log, to tell none marked from two or a copy (in a
        // shared model, "isn't in the model" has been seen at two users and went away after a sync).
        if (options.ImportSiteplan && Placement.Siteplan(doc) is null)
        {
            JegaLog.Note(ProjectStore.TraceImports(doc, Placement.SiteplanRole));
        }
        // A site plan deleted (its import gone), not exploded: offer to draw it again from what the model keeps —
        // nothing is fetched, and the frame puts it back where it stood. Asked before the work starts, by the address
        // so the user knows which. Not for a terrain of the user's own, the import switched off, or geometry an
        // explode left behind (a redraw would draw over it; that stays the user's own to tidy).
        var exploded = LotOutline.Exploded(doc);
        var redraw = false;
        if (options.ImportSiteplan && Placement.Siteplan(doc) is null && !exploded && !_declinedRedraw.Contains(doc)
            && ProjectStore.ReadSite(doc) is { AddressE: not null } deleted && stored.FileName == deleted.Address
            && ProjectStore.ReadSurroundings(doc) is not null && ProjectStore.ReadFrame(doc) is not null)
        {
            // Asked once per model per session: a deliberate delete-and-keep (terrain only) isn't nagged at every
            // Refresh. A no is remembered; Load from Address still brings the site plan back on demand. Cancel stops
            // Refresh before anything is done, and is asked again next time.
            if (AskRedraw(deleted.Address) is not { } answer)
            {
                return (Result.Cancelled, null);
            }
            redraw = answer;
            if (!redraw)
            {
                _declinedRedraw.Add(doc);
            }
        }

        if (CrossSymbol.Prepare(doc) is { } crossProblem)
        {
            problems.Add(crossProblem);
        }
        int views;
        using var group = new TransactionGroup(doc, Strings.Undo_Refresh);
        group.Start();
        using (var t = new Transaction(doc, Strings.Undo_Refresh))
        {
            t.Start();
            // The deleted site plan back first, so all that follows (frame, terrain) lines up to it.
            redrawn = redraw && SiteplanRedraw.FromFrame(doc, options);
            // The import placed by the real coordinates first, moved and turned (to the project's true north,
            // which the coordinates set), so the terrain follows it there; the frame follows. Said only when it changed:
            // a site plan moved by hand that jumps back looks like a fault otherwise. Without real coordinates (a
            // surveyor's, System 34) the site follows the import where it lies.
            movedBack = PlaceByCoordinates(doc, options);
            // An import the terrain can't follow (copies, mirrored): said, as the user must act.
            if (Placement.Remember(doc) is { Note: { } note } remembered)
            {
                (remembered.Problem ? problems : lines).Add(note);
            }
            // The drawing's turn no longer the working direction's where the import stands now (Rotate Project North,
            // Work Direction, or Settings' choice changed): drawn again there, so its texts lie straight; the place is kept.
            straightened = SiteplanRedraw.Straighten(doc, options);
            // No import (exploded, deleted or never imported): nothing to follow, the terrain stays at the last place.
            noImport = Placement.Siteplan(doc) is null;
            // Gone after an import (deleted or exploded): the same address brings it back. Not with the import off,
            // or a terrain of the user's own (no address, or a CSV loaded since: the terrain is kept under its file's name).
            reload = noImport && !exploded && options.ImportSiteplan && ProjectStore.ReadSite(doc) is { AddressE: not null } site
                && stored.FileName == site.Address;
            var placement = Placement.Get(doc);
            views = GridFrameWriter.Draw(doc, view, grid, placement);
            if (CrossSymbol.Load(doc) is { } crossLoad)
            {
                problems.Add(crossLoad);
            }
            // Every view made again: what the updater kept from before is answered by now, so only this update's said.
            TerrainLineUpdater.TakeProblem(doc);
            if (TerrainLines.Update(doc) is { } labelProblem)
            {
                problems.Add(labelProblem);
            }
            // The context masses, if shown: made again at the site's place and Revit 0's DVR90 now.
            if (ContextMasses.Shown(doc) && ProjectStore.ReadContext(doc) is { } json)
            {
                try
                {
                    var (made, refused) = ContextMasses.Build(doc, Context.Parse(json), placement, Dvr90.AtRevitZero(doc).Mm);
                    lines.Add(refused > 0 ? Fill(Strings.Refresh_ContextSkipped, made, refused) : Fill(Strings.Refresh_Context, made));
                }
                catch (ContextException e)
                {
                    problems.Add(Strings.Refresh_ContextUnreadable);
                    lines.Add(e.Message);
                }
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                return (Result.Failed, new ReceiptText(Strings.Refresh_RevitStopped) { Warning = true });
            }
        }
        var afterPlace = State();
        // Asked to draw the site plan again, but it couldn't (no plan to draw in, or the stored data wouldn't read:
        // FromFrame logged why): say so, rather than the bare "no site plan". The terrain and the rest still updated.
        if (redraw && !redrawn)
        {
            problems.Add(Strings.Refresh_RedrawFailed);
        }
        var hasToposolid = LotTerrainCommand.Exists(doc);
        if (hasToposolid)
        {
            var (result, receipt) = LotTerrainCommand.Generate(doc);
            // Kept, moved or made again (Generate decides): its result line says which. A failure or a warning (tags
            // lost, over the whole lot, heights unchecked) is a problem here too, with its lines, steps and details.
            if (result != Result.Succeeded || receipt.Warning)
            {
                problems.Add(receipt.Result);
                toposolidProblem = receipt;
            }
            else
            {
                toposolid = receipt.Result;
            }
        }
        group.Assimilate();
        JegaLog.Note($"Refresh in \"{view.Name}\" ({SiteplanRedraw.ViewNorth(view)}); " +
            $"anchor {options.SiteAnchor}\n" +
            $"  before: {before}\n  placed: {afterPlace}\n  after:  {State()}\n" +
            $"  moved back: {movedBack ?? "no"}; straightened: {(straightened is null ? "no" : "yes")}; Toposolid: {toposolid ?? toposolidProblem?.Result ?? "none"}");

        // What's still to do on the way to a Toposolid: Refresh is often the step before (Load from Address's). What can be
        // done next as lines, as Load from Address says them; numbered steps only for what must be done.
        List<string> steps = [.. toposolidProblem?.Steps ?? []];
        var floorLine = Receipts.FloorLine(doc);
        var next = new List<string?>
        {
            Receipts.AbsoluteLevels(doc) && !(toposolidProblem?.Lines.Contains(floorLine) ?? false) ? floorLine : null,
            hasToposolid ? null : Strings.Address_GenerateLine,
        };
        // Refresh doesn't move the lot boundary (a filled region of the user's): left behind when the site moved.
        if (LotEdgeCommand.Stale(doc))
        {
            steps.Add(Strings.Refresh_LotEdgeStep);
        }
        return (Result.Succeeded, new ReceiptText(problems.Count > 0 ? problems[0]
            : noImport && exploded ? Strings.Refresh_Exploded
            : noImport ? Strings.Refresh_NoImport
            : options.SiteAnchor == ProjectOptions.AnchorShared ? Strings.Refresh_DoneShared : Strings.Refresh_Done)
        {
            Lines = [.. problems.Skip(1), problems.Count > 0 ? Strings.Refresh_Rest : null,
                redrawn ? Strings.Refresh_Redrawn : null, reload ? Strings.Refresh_ReloadSameAddress : null,
                movedBack, straightened, toposolid, Receipts.Levels(doc), .. toposolidProblem?.Lines ?? [], .. next],
            Steps = steps,
            Details = [Fill(Strings.Refresh_From, stored.FileName), .. Receipts.Terrain(doc, grid, views), .. lines, .. toposolidProblem?.Details ?? []],
            Warning = problems.Count > 0,
        });
    }

    /// <summary>Inside a transaction (Refresh, Load from Address on the same address): the site plan import moved and
    /// turned onto its real coordinates (Placement.Shared). Returns a line when it moved; null when already there,
    /// without an import, or without real coordinates.</summary>
    internal static string? PlaceByCoordinates(Document doc, ProjectOptions options)
    {
        if (options.SiteAnchor != ProjectOptions.AnchorShared || Placement.Siteplan(doc) is null || Placement.Shared(doc) is not { } real)
        {
            return null;
        }
        Placement.MoveSiteTo(doc, real, Placement.Describe(options), out var changed);
        return changed ? Strings.Placement_BackOnCoordinates : null;
    }

    // The models where the offer to redraw a deleted site plan was declined this session: not asked again (a
    // reopened model is a new one, and is offered again). Load from Address still brings it back on demand.
    private static readonly HashSet<Document> _declinedRedraw = [];

    // Offers to draw a deleted site plan again; $address names it so the user knows which. True draws it again
    // (from what the model keeps, nothing fetched); false refreshes the terrain alone; null for Cancel or Esc.
    private static bool? AskRedraw(string address)
    {
        var dialog = new TaskDialog(Ui.Title)
        {
            MainInstruction = Strings.Refresh_RedrawAsk,
            MainContent = Fill(Strings.Refresh_RedrawAskContent, address),
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Refresh_RedrawYes, Strings.Refresh_RedrawYesNote);
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Refresh_RedrawNo, Strings.Refresh_RedrawNoNote);
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => true,
            TaskDialogResult.CommandLink2 => false,
            _ => null,
        };
    }
}
