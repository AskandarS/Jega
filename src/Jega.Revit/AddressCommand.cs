using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Load from Address": search an address, fetch its site plan and terrain from the public services with this
/// PC's keys (SiteFetcher), keep the site in the model (nothing beside the project: Export writes the files) (the lot, the house's terrain, the address point), import the DXF where the site's
/// anchor says (Placement: under "dxf" where the last one was, so its turn is kept), line the Survey Point up if
/// Settings says so, and load the terrain (±0.00 is never set here: it is the floor, the user's to enter). One Undo.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AddressCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_LoadFromAddress;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        if (doc.ActiveView is not ViewPlan view || view.GenLevel is null)
        {
            Receipt(new ReceiptText(Strings.Address_OpenPlan) { Warning = true });
            return Result.Cancelled;
        }
        if (Keys.FetcherOrAsk(data) is not { } fetcher)
        {
            return Result.Cancelled;
        }
        var options = ProjectStore.ReadOptions(doc);
        // The search and the fetch happen in the window, off Revit's thread; Revit's part starts when it has closed.
        var window = new AddressWindow(fetcher, options);
        new WindowInteropHelper(window) { Owner = data.Application.MainWindowHandle };
        if (window.ShowDialog() != true || window.Result is not { } fetched)
        {
            return Result.Cancelled;
        }
        var (address, x, y, site, grid) = fetched;

        // Another address than the one loaded before (its point more than 1 m off). The old site plan's place was fitted
        // to its own lot: ask. The context is in mm from the old site's 0,0: it goes, whatever the answer.
        var restart = false;
        var lines = new List<string>();
        // What happened to the coordinates (moved origin, Manual georeference on, fitted): shown, not in the details.
        var info = new List<string>();
        var problems = new List<string>();
        var place = Placement.Describe(options);
        var old = ProjectStore.ReadSite(doc);
        var newAddress = old is { AddressE: { } oldE, AddressN: { } oldN } && Math.Sqrt((oldE - x) * (oldE - x) + (oldN - y) * (oldN - y)) > 1;
        if (newAddress && ProjectStore.ReadFrame(doc) is not null)
        {
            // Real coordinates: beside the old one by them, the project's origin moved here, or (far off) Manual
            // georeference: on the Project Base Point, the coordinates left to the old address.
            var shared = options.SiteAnchor == ProjectOptions.AnchorShared;
            if (shared)
            {
                var away = Math.Sqrt((old!.AddressE!.Value - x) * (old.AddressE.Value - x) + (old.AddressN!.Value - y) * (old.AddressN.Value - y));
                if (NewAddressUtm(old.Address, address, away, Placement.Fits(doc, x, y), Placement.HasLinks(doc)) is not { } chosen)
                {
                    return Result.Cancelled;
                }
                restart = chosen != NewPlace.Beside;
                if (chosen == NewPlace.Manual)
                {
                    // Written with the site (StoreSite).
                    options = options with { SiteAnchor = ProjectOptions.AnchorBase };
                    info.Add(Strings.Placement_ManualOn);
                }
            }
            else if (NewAddress(old!.Address, address, place) is { } answer)
            {
                restart = answer;
            }
            else
            {
                return Result.Cancelled;
            }
            if (restart && !shared)
            {
                lines.Add(Fill(Strings.Address_NewAddress, old.Address, place));
            }
        }
        // Real coordinates, the first site in a project with coordinates of its own (a template's, a surveyor's): Jega sets its
        // own over them, or fits to them: placed by them if this address fits, else Manual georeference.
        var setCoordinates = false;
        if (options.SiteAnchor == ProjectOptions.AnchorShared && ProjectStore.ReadFrame(doc) is null && Placement.HasCoordinates(doc))
        {
            var fits = Placement.Fits(doc, x, y);
            if (ExistingCoordinates(Placement.Moves(doc, ProjectOrigin.Of(x, y)), Placement.HasLinks(doc), Strings.Address_NewCancel, orManual: true) is not { } set)
            {
                return Result.Cancelled;
            }
            setCoordinates = set;
            if (!set && !fits)
            {
                // Written with the site (StoreSite).
                options = options with { SiteAnchor = ProjectOptions.AnchorBase };
                info.Add(Strings.Placement_ManualOn);
            }
            else if (!set)
            {
                info.Add(Strings.Placement_FitsCoordinates);
            }
        }

        // Nothing beside the project (it needn't be saved): the DXF only for Revit's import, from a file of its own in
        // the temp folder, deleted once imported (kept if the import fails, to import by hand). The terrain, the lot and
        // the surroundings are kept in the model; Export writes the DXF and CSV from there.
        var tempDir = Path.Combine(Path.GetTempPath(), "Jega", Guid.NewGuid().ToString("N"));
        var dxfPath = Path.Combine(tempDir, ImportName);
        if (!WriteTempDxf(tempDir, dxfPath, site.Dxf))
        {
            return Result.Cancelled;
        }

        if (CrossSymbol.Prepare(doc) is { } crossProblem)
        {
            problems.Add(crossProblem);
        }
        int views;
        string? importFailed = null;
        var contextCleared = false;
        var georeferenced = false;
        using (var group = new TransactionGroup(doc, Strings.Undo_LoadFromAddress))
        {
            group.Start();
            // Leaving the group without Assimilate (a step failed and said so) undoes the whole load.
            if (StoreSite(doc, address, x, y, site, options, old, newAddress, restart, setCoordinates, lines, info, problems, out contextCleared, out georeferenced) is not { } target)
            {
                return Result.Failed;
            }
            var import = ImportSiteplan(doc, view, options, tempDir, dxfPath, target, site, grid, address, lines, out importFailed);
            if (StoreTerrain(doc, view, address, site, grid, target, import, restart, problems) is not { } drawn)
            {
                return Result.Failed;
            }
            views = drawn;
            group.Assimilate();
        }

        // Numbered steps only for what must be done (the import by hand, a Toposolid of the old terrain); what can be
        // done next is said as lines.
        var steps = new List<string>();
        if (importFailed is not null)
        {
            problems.Insert(0, Fill(Strings.Address_NotImported, address));
            steps.Add(Fill(Strings.Address_ImportByHand, dxfPath));
        }
        var hasToposolid = LotTerrainCommand.Exists(doc);
        if (hasToposolid)
        {
            steps.Add(Strings.Receipt_RefreshToposolidStep);
        }
        // A new address in a project turned with Work Direction keeps the old house's turn (the project's angle to true
        // north stays, whether its origin moved or not): said, with the way to change it, instead of the plain line.
        var keptTurn = Math.IEEERemainder(Placement.FromProject(doc).AngleToTrueNorth, 360);
        var next = new List<string?>
        {
            newAddress && Math.Abs(keptTurn) >= 0.01 ? Fill(Strings.Address_WorkDirectionKept, keptTurn)
                : options.ImportSiteplan ? Strings.Address_WorkDirectionLine : null,
            hasToposolid ? null : Strings.Address_GenerateLine,
            // Relative levels and another address: ±0.00 is the old house's floor, kept as it was; said, as this one's may differ.
            Receipts.AbsoluteLevels(doc) ? Receipts.FloorLine(doc)
                : newAddress ? Fill(Strings.Address_FloorKept, Units.Level(Dvr90.AtRevitZero(doc).Mm), old!.Address) : null,
        };
        // Real coordinates: where the site is in them, the origin on the Project Base Point; or how far from it, when the
        // project had its coordinates before (another address, a surveyor's).
        var onCoordinates = options.SiteAnchor == ProjectOptions.AnchorShared && Placement.Shared(doc) is not null
            && Placement.BaseCoordinates(doc) is var (be, bn) && Placement.Origin(doc) is var (oe, on)
            ? Placement.OnOwnCoordinates(doc) ? Fill(Strings.Address_OnCoordinates, be, bn)
                : Fill(Strings.Address_OnCoordinatesApart, oe, on, Math.Sqrt((oe - be) * (oe - be) + (on - bn) * (on - bn)))
            : null;
        var loaded = Fill(georeferenced ? Strings.Address_LoadedGeoreferenced : Strings.Address_Loaded, address);
        Receipt(new ReceiptText(problems.Count > 0 ? problems[0] : loaded)
        {
            Lines = [.. problems.Skip(1), problems.Count > 0 && importFailed is null ? loaded : null, onCoordinates, .. info,
                contextCleared ? (site.Context is null ? Strings.Address_ContextCleared : Strings.Address_ContextReplaced) : null,
                // Without the surface: no roofs; the neighbours too, unless those of a load before still stand (same address).
                // Without the buildings that says it all (no roofs or neighbours either), so not the surface's too.
                site.NoBuildings ? Strings.Address_NoBuildings
                : site.NoSurface ? (!contextCleared && ProjectStore.ReadContext(doc) is not null ? Strings.Address_NoSurfaceRoofs : Strings.Address_NoSurface) : null,
                site.NoLots ? Strings.Address_NoLots : null,
                site.NoRoads ? Strings.Address_NoRoads : null, site.NoWater ? Strings.Address_NoWater : null,
                site.NoManholes ? Strings.Address_NoManholes : null, site.NoAddresses ? Strings.Address_NoAddresses : null,
                site.NoVegetation ? Strings.Address_NoVegetation : null,
                fetcher.AddressFallbackNote,
                // A layer whose drawing failed: named as Settings > Layers names it.
                site.SkippedLayers is { Count: > 0 } skippedLayers ? Fill(Strings.Address_LayersSkipped,
                    string.Join(", ", skippedLayers.Select(l => Strings.ResourceManager.GetString("Layer_" + l) ?? l))) : null,
                .. next],
            Steps = steps,
            Details = [.. Receipts.Terrain(doc, grid, views), Receipts.Levels(doc), .. lines,
                importFailed is null ? null : Fill(Strings.Address_RevitImport, importFailed)],
            Warning = problems.Count > 0,
            Footer = Receipts.Credit,
        });
        return Result.Succeeded;
    }

    // The DXF for Revit's import, in a folder of its own in the temp folder; false, and said, when it can't be written.
    private static bool WriteTempDxf(string tempDir, string dxfPath, string dxf)
    {
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(dxfPath, dxf);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Receipt(new ReceiptText(Strings.Address_TempFailed)
            {
                Lines = [Strings.Address_TempFailedNext],
                Details = [e.Message],
                Warning = true,
            });
            return false;
        }
    }

    // 1. The site, first: the "shared" anchor places by its address point. Returns where the site plan goes, or null
    // when Revit stopped it (said). $contextCleared: the old address's neighbours were taken out. $setCoordinates: Jega's
    // coordinates set over the project's own (asked). $options' anchor written if it changed (Manual georeference on).
    // $info: what happened to the coordinates, shown in the receipt (the details go in $lines); $georeferenced: the
    // project got its coordinates now (the receipt's headline says so).
    private static Placement? StoreSite(Document doc, string address, double x, double y, SiteFetcher.Site site, ProjectOptions options,
        SiteData? old, bool newAddress, bool restart, bool setCoordinates, List<string> lines, List<string> info, List<string> problems,
        out bool contextCleared, out bool georeferenced)
    {
        contextCleared = false;
        georeferenced = false;
        Placement target;
        using (var t = new Transaction(doc, Strings.Undo_Site))
        {
            t.Start();
            ProjectStore.WriteSite(doc, address, x, y, site.LotMm, site.HouseGroundMm);
            if (ProjectStore.ReadOptions(doc) is var stored && stored.SiteAnchor != options.SiteAnchor)
            {
                ProjectStore.WriteOptions(doc, stored with { SiteAnchor = options.SiteAnchor });
            }
            // The old address's neighbours, shown or kept, would be built round the new lot by Refresh and Context.
            if (newAddress && ProjectStore.ReadContext(doc) is not null)
            {
                ContextMasses.Remove(doc);
                ProjectStore.ClearContext(doc);
                contextCleared = true;
            }
            // The context buildings came with the same calls: kept, so Context Buildings only shows them.
            if (site.Context is { } context)
            {
                ProjectStore.WriteContext(doc, context, "Datafordeler");
            }
            // The import's place now, as Refresh would take it (placed by the real coordinates first). Not for a new
            // address: the site written above is the new one, so its coordinates aren't the old import's.
            if (!restart && !newAddress && RefreshCommand.PlaceByCoordinates(doc, options) is { } placed)
            {
                info.Add(placed);
            }
            if (!restart && Placement.Remember(doc) is { Note: { } note } remembered)
            {
                (remembered.Problem ? problems : lines).Add(note);
            }
            // Real coordinates, a new address that moves the project's origin here: the Project Base Point given its
            // coordinates, so the new site lands on it as the first did (Landing below).
            if (restart && options.SiteAnchor == ProjectOptions.AnchorShared && Placement.MoveOrigin(doc, old) is { } moved)
            {
                info.Add(moved);
            }
            // Real coordinates in a project without them: given from the site (a new address: on the Project Base
            // Point). A project that has them already places a new address at its real place, beside the old one.
            // Failed, or another system: a problem, as the user must act.
            if (options.SiteAnchor == ProjectOptions.AnchorShared && Placement.Georeference(doc, fresh: restart || ProjectStore.ReadFrame(doc) is null, force: setCoordinates) is { } real)
            {
                if (real == Strings.Placement_GeoreferenceFailed || real == Strings.Placement_OtherSystem)
                {
                    problems.Add(real);
                }
                else
                {
                    // Said in the headline and the coordinates line, with the origin's E/N: not again in the details.
                    georeferenced = true;
                }
            }
            // Kept where it was, a new address nearby (a neighbour, the same area): its point lands its true offset
            // from the old one (new − old, turned as the old site plan), so the two lots lie as they do on the map;
            // not its point on the old one's, which shifted the lot by that offset. One far off (another town) lands
            // on the old place, its turn kept: that far out Revit can't place anything (seen from one town to another
            // about 100 km off: nothing loaded, not a word). Real coordinates place it at its real place already.
            var kept = Placement.Get(doc);
            if (restart)
            {
                Placement.ForgetHold(doc);
            }
            // Real coordinates: always at its real place by them (a new address beside the old one, the same one again),
            // never by the frame kept before, which may be stale (an import deleted, an Undo: the same address after Work
            // Direction landed 43° off).
            target = options.SiteAnchor == ProjectOptions.AnchorShared && Placement.Shared(doc) is { } byCoordinates ? byCoordinates
                : restart ? Placement.Landing(doc)
                : newAddress && options.SiteAnchor != ProjectOptions.AnchorShared && old is { AddressE: { } e0, AddressN: { } n0 }
                    && Math.Sqrt((x - e0) * (x - e0) + (y - n0) * (y - n0)) <= NearbyM
                    ? Placement.At(kept.Point((x - e0) * 1000, (y - n0) * 1000, 0), Angles.ToRadians(kept.AngleToTrueNorth))
                    : kept;
            // Kept where it was: "Keep the site plan in place" holds the new address where it lands, not on the anchor.
            if (newAddress && !restart)
            {
                Placement.HoldHere(doc, target);
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                Receipt(RevitStopped);
                return null;
            }
        }
        return target;
    }

    // 2. The site plan where the anchor says, in place of the one before (one to turn). Its own
    // transaction: a failed import leaves the rest ($importFailed says why). Without the import, the temp folder goes.
    // With the paper straight (Settings), drawn for $view's north (SiteplanRedraw.TurnFor; in Project North turned as the
    // site lands), its frame big enough for either north (SiteplanRedraw.Draw): in a turned project the DXF drawn again
    // here from what was fetched ($site's surroundings and $grid, nothing fetched again), over the one Load from Address
    // drew north up and tight. Without, that one, turned as a whole at the import.
    private static ImportInstance? ImportSiteplan(Document doc, ViewPlan view, ProjectOptions options, string tempDir, string dxfPath,
        Placement target, SiteFetcher.Site site, TerrainGrid grid, string address, List<string> lines, out string? importFailed)
    {
        importFailed = null;
        ImportInstance? import = null;
        if (options.ImportSiteplan)
        {
            try
            {
                var turn = SiteplanRedraw.TurnFor(options, target, view);
                if (options.SiteplanPaperStraight && target.AngleToTrueNorth != 0)
                {
                    File.WriteAllText(dxfPath, SiteplanRedraw.Draw(doc, options, site.Surroundings, grid, address, turn, target));
                }
                using var t = new Transaction(doc, Strings.Undo_ImportSitePlan);
                t.Start();
                import = Import(doc, view, dxfPath, target, turn);
                if (t.Commit() != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException(Strings.Address_ImportNotAccepted);
                }
                lines.Add(Fill(Strings.Address_Imported, view.Name));
                TryDelete(tempDir);
            }
            catch (Exception ex)
            {
                import = null;
                importFailed = ex.Message;
            }
        }
        else
        {
            TryDelete(tempDir);
        }
        return import;
    }

    // 3. The anchor's place, the Survey Point and the terrain. Returns how many views got the grid frame, or null when
    // it failed (said): the caller then undoes the whole load (the group).
    private static int? StoreTerrain(Document doc, ViewPlan view, string address, SiteFetcher.Site site, TerrainGrid grid, Placement target,
        ImportInstance? import, bool restart, List<string> problems)
    {
        int views;
        using (var t = new Transaction(doc, Strings.Undo_LoadTerrain))
        {
            t.Start();
            try
            {
                if (import is null && restart)
                {
                    // The old address's site plan, still there: Refresh mustn't follow it back.
                    foreach (var stale in ProjectStore.Marked(doc, Placement.SiteplanRole, typeof(ImportInstance)))
                    {
                        ProjectStore.Unmark(stale);
                    }
                }
                var (ox, oy) = target.OriginMm;
                ProjectStore.WriteFrame(doc, Units.FeetFromMm(ox), Units.FeetFromMm(oy), Angles.ToRadians(target.AngleToTrueNorth));
                ProjectStore.WriteTerrain(doc, site.Csv, address);
                ProjectStore.WriteSurroundings(doc, site.Surroundings);
                views = GridFrameWriter.Draw(doc, view, grid, Placement.Get(doc));
                if (CrossSymbol.Load(doc) is { } crossLoad)
                {
                    problems.Add(crossLoad);
                }
                if (TerrainLines.Update(doc) is { } labelProblem)
                {
                    problems.Add(labelProblem);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException e)
            {
                t.RollBack();
                Receipt(new ReceiptText(Strings.Address_StoreFailed)
                {
                    Lines = [Strings.Address_StoreFailedNext],
                    Details = [Fill(Strings.Receipt_Revit, e.Message)],
                    Warning = true,
                });
                return null;
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                Receipt(RevitStopped);
                return null;
            }
        }
        return views;
    }

    /// <summary>
    /// Inside a transaction. Deletes the site plan before (the marked one, or one from this file whose mark is gone)
    /// with its CAD type, so types don't pile up (never a linked DXF), imports the DXF origin to origin in mm, then
    /// turns it and moves it so its 0,0 is on $target's, and marks it. $drawnTurnDeg: the turn the DXF was drawn with
    /// (SiteplanDxf.Write's turnDeg, a plan in the working direction): the import is turned that much less, so its
    /// north (Placement.NorthOf) is $target's, and kept on it (ProjectStore.WriteDrawnTurn). Also the redraws.
    /// </summary>
    internal static ImportInstance Import(Document doc, ViewPlan view, string dxfPath, Placement target, double drawnTurnDeg = 0)
    {
        // The new one in the workset and phases of the one before.
        var inherited = Inherited.From(ProjectStore.Marked(doc, Placement.SiteplanRole, typeof(ImportInstance)).FirstOrDefault());
        var old = ProjectStore.Marked(doc, Placement.SiteplanRole, typeof(ImportInstance)).Cast<ImportInstance>()
            .Concat(new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                .Where(i => string.Equals(doc.GetElement(i.GetTypeId())?.Name, ImportName, StringComparison.OrdinalIgnoreCase)))
            .Where(i => !i.IsLinked).Select(i => i.GetTypeId()).Distinct().ToList();
        doc.Delete(old);
        var viewOnly = ProjectStore.ReadOptions(doc).ImportViewOnly;
        // Not oriented to the view: in a True North view Revit would turn it by the project's angle first, and the turn
        // below would come on top (a redraw in True North came out off by true north's angle).
        // Nor its lines straightened: Revit by default snaps lines within ~0.1° of an axis, boundaries included.
        var options = new DWGImportOptions
        {
            Placement = ImportPlacement.Origin, Unit = ImportUnit.Millimeter, ThisViewOnly = viewOnly, OrientToView = false,
            AutoCorrectAlmostVHLines = false,
        };
        // In a True North view Revit turns a new import by true north on top of the turn below, unseen by its transform
        // (after Work Direction, a layer change in True North turned the site plan to project north
        // while jega.log read it right). So the view is in Project North while it is imported, and put back after.
        var north = view.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH);
        var inTrueNorth = north is { IsReadOnly: false } && north.AsInteger() == ViewNorthCommand.TrueNorth;
        if (inTrueNorth)
        {
            north!.Set(ViewNorthCommand.ProjectNorth);
            doc.Regenerate();
        }
        ElementId id;
        try
        {
            if (!doc.Import(dxfPath, options, view, out id))
            {
                throw new InvalidOperationException(Strings.Address_DxfNotImported);
            }
        }
        finally
        {
            if (inTrueNorth)
            {
                north!.Set(ViewNorthCommand.TrueNorth);
            }
        }
        if (doc.GetElement(id) is not ImportInstance import)
        {
            throw new InvalidOperationException(Strings.Address_DxfNotImported);
        }
        // Origin to origin may pin it: moved and turned unpinned, then left as Revit had it.
        var pinned = import.Pinned;
        import.Pinned = false;
        // Turned and moved from where it actually landed, not as if it lay at the origin unturned: in a True North view
        // Revit still turned a new import about the internal origin, OrientToView off or not, so
        // a redraw from Layers moved the whole site. Said in jega.log when it did, for a report.
        doc.Regenerate();
        var landed = import.GetTotalTransform();
        var landedAt = landed.OfPoint(XYZ.Zero);
        var landedTurn = Angles.TurnDegrees(Transforms.NorthAngle(landed), 0);
        if (Math.Abs(landedTurn) >= 0.001 || Units.MmFromFeet(Math.Abs(landedAt.X) + Math.Abs(landedAt.Y)) >= 0.01)
        {
            JegaLog.Note($"Site plan import landed turned {landedTurn:0.###}° and {Units.MmFromFeet(Units.Flat(landedAt, XYZ.Zero)) / 1000:0.###} m off the origin in \"{view.Name}\"; put right.");
        }
        var turn = Angles.TurnDegrees(Angles.ToRadians(target.AngleToTrueNorth - drawnTurnDeg), Transforms.NorthAngle(landed));
        if (Math.Abs(turn) > 1e-7)
        {
            ElementTransformUtils.RotateElement(doc, id, Line.CreateBound(landedAt, landedAt + XYZ.BasisZ), Angles.ToRadians(turn));
        }
        // The DXF's 0,0 onto the site's.
        var at = target.Point(0, 0, 0);
        var move = new XYZ(at.X - landedAt.X, at.Y - landedAt.Y, 0);
        if (Units.MmFromFeet(move.GetLength()) > 0.01)
        {
            ElementTransformUtils.MoveElement(doc, id, move);
        }
        import.Pinned = pinned;
        ProjectStore.Mark(import, Placement.SiteplanRole, own: true);
        ProjectStore.WriteDrawnTurn(import, drawnTurnDeg);
        inherited?.To(import);
        // The setback line dashed with Revit's own pattern: its import shows the DXF's DASHED line type solid. The
        // layer is a subcategory of the import's own, so Visibility/Graphics can still change it.
        if (import.Category?.SubCategories is { } layers && layers.Contains(SiteplanLayers.Setback) && LineStyles.DashPattern(doc) is { } dash)
        {
            layers.get_Item(SiteplanLayers.Setback).SetLinePatternId(dash, GraphicsStyleType.Projection);
        }
        // The callers read its transform next: stale until regenerated.
        doc.Regenerate();
        return import;
    }

    /// <summary>The DXF's file name for the import: always the same, so the import's CAD type is too and the one before is found.</summary>
    internal const string ImportName = "Jega siteplan.dxf";

    // A new address this near the old one (m), kept where it was, lands at its true offset from it; farther, on its place.
    // Real coordinates: beyond it the dialog says how far they lie apart; beside is offered while they place it within
    // Placement.Shared's 10 km of the internal origin (Placement.Fits: farther Revit can't place anything).
    private const double NearbyM = 2000;

    // The model is left as it was (the group is rolled back).
    private static ReceiptText RevitStopped => new(Strings.Address_RevitStopped) { Warning = true };

    // The temp folder of one load; one left behind (Revit still reading it) is harmless.
    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A new address in a project with a site plan: true = start where the address point lands, false = keep the old place, null = cancel.</summary>
    private static bool? NewAddress(string before, string now, string place)
    {
        var dialog = new TaskDialog(Ui.Title)
        {
            MainInstruction = Strings.Address_NewAsk,
            MainContent = Fill(Strings.Address_NewAskContent, before, now),
            CommonButtons = TaskDialogCommonButtons.Cancel,
            FooterText = Strings.Address_NewCancel,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Address_NewFresh, Fill(Strings.Address_NewFreshNote, place));
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Address_NewKeep, Strings.Address_NewKeepNote);
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => true,
            TaskDialogResult.CommandLink2 => false,
            _ => null,
        };
    }

    /// <summary>
    /// Real coordinates, a new address $away m from the old one: beside the old one at its real place (offered when $fits: the
    /// coordinates place it within 10 km of the internal origin, Placement.Fits; beyond NearbyM the dialog says how far
    /// apart they lie), the project's origin moved here (the new site lands where the old one was), or, where it
    /// doesn't fit, Manual georeference (on the Project Base Point, not georeferenced); null = cancel. $links: the
    /// project has links, which move with the origin.
    /// </summary>
    private static NewPlace? NewAddressUtm(string before, string now, double away, bool fits, bool links)
    {
        // In m up to NearbyM, beyond in km ("43,1 km"); the same in the text and on the button.
        var distance = away <= NearbyM ? Fill(Strings.Address_DistanceM, away) : Fill(Strings.Address_DistanceKm, away / 1000);
        var content = Fill(Strings.Address_NewAwayContent, before, now, distance);
        var dialog = new TaskDialog(Ui.Title)
        {
            MainInstruction = Strings.Address_NewAsk,
            MainContent = fits && away <= NearbyM ? content
                : $"{content}\n\n{(fits ? Fill(Strings.Address_NewFar, away / 1000) : Strings.Address_NewTooFar)}",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            FooterText = Strings.Address_NewCancel,
        };
        if (fits)
        {
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Address_NewBeside, Fill(Strings.Address_NewBesideNote, distance));
        }
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Address_NewOrigin,
            links ? $"{Strings.Address_NewOriginNote} {Strings.Address_NewOriginLinks}" : Strings.Address_NewOriginNote);
        if (!fits)
        {
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, Strings.Location_Manual, Strings.Address_NewManualNote);
        }
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => NewPlace.Beside,
            TaskDialogResult.CommandLink2 => NewPlace.MoveOrigin,
            TaskDialogResult.CommandLink3 => NewPlace.Manual,
            _ => null,
        };
    }

    // Where a new address goes over real coordinates (NewAddressUtm).
    private enum NewPlace { Beside, MoveOrigin, Manual }

    /// <summary>
    /// Real coordinates, a site in a project with coordinates of its own (the first at Load from Address; Settings leaving Manual
    /// georeference): true = set Jega's over them, false = keep them and follow, null = cancel. $moves (Placement.Moves):
    /// with Apart they are UTM32 and place this address, that far from the Project Base Point (else keeping them turns
    /// Manual georeference on, offered only $orManual); with ToTheirs and ToBase a site stands, and each choice says how
    /// far it moves and turns. $links: the project has links, which move if Jega sets its own. $footer: what Cancel does.
    /// </summary>
    internal static bool? ExistingCoordinates((double? Apart, (double M, double Deg)? ToTheirs, (double M, double Deg)? ToBase) moves,
        bool links, string footer, bool orManual)
    {
        var fits = moves.Apart is not null;
        var dialog = new TaskDialog(Ui.Title)
        {
            MainInstruction = Strings.Address_HasCoordinates,
            MainContent = moves.Apart is { } apart ? Fill(Strings.Address_HasCoordinatesFit, apart) : Strings.Address_HasCoordinatesOther,
            CommonButtons = TaskDialogCommonButtons.Cancel,
            FooterText = footer,
        };
        if (fits || orManual)
        {
            // Where they don't fit, the site doesn't follow them: the link says Manual georeference.
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, fits ? Strings.Address_FitCoordinates : Strings.Location_Manual,
                !fits ? Strings.Address_FitCoordinatesManualNote
                : moves.ToTheirs is var (m, deg) ? Moved(Strings.Address_FitCoordinatesMoveNote, Strings.Address_FitCoordinatesStayNote, m, deg)
                : Fill(Strings.Address_FitCoordinatesNote, moves.Apart!.Value));
        }
        // Set over them: the site onto the Project Base Point; links placed by them move as far as their place for the
        // address is from it (another system's: just that they move; not at all: nothing said).
        var set = moves.ToBase is var (bm, bdeg) ? Moved(Strings.Address_SetCoordinatesMoveNote, Strings.Address_SetCoordinatesStayNote, bm, bdeg)
            : Strings.Address_SetCoordinatesNote;
        var linksMove = moves.Apart is not { } shift ? Strings.Address_NewOriginLinks
            : Math.Abs(shift) >= 0.005 ? Fill(Strings.Address_SetCoordinatesLinks, shift) : null;
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Address_SetCoordinates, links && linksMove is not null ? $"{set} {linksMove}" : set);
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => false,
            TaskDialogResult.CommandLink2 => true,
            _ => null,
        };
    }

    /// <summary>What the window fetched: the address's text and point (UTM32, m), the site and its terrain.</summary>
    private sealed record Fetched(string Address, double X, double Y, SiteFetcher.Site Site, TerrainGrid Grid);

    /// <summary>
    /// The address field with suggestions (AddressSearch): from 2 characters and a 200 ms pause, an
    /// answer for older text thrown away; ↓/↑ and Enter pick, a street fills in and waits for the house number. Load
    /// fetches here, on worker threads with its steps shown, so Revit never freezes; Cancel (or Esc) stops the fetch,
    /// a second one closes. Closes with Result once all is fetched.
    /// </summary>
    private sealed class AddressWindow : Window
    {
        private readonly SiteFetcher _fetcher;
        private readonly ProjectOptions _options;
        private readonly System.Windows.Controls.TextBox _text = Field(410);
        private readonly ListBox _hits = new() { Height = 180, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock _status = Error();
        private readonly Button _load = Button(Strings.Address_Load, 18);
        private readonly DispatcherTimer _pause = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private CancellationTokenSource? _search;
        private CancellationTokenSource? _fetch;
        // The fetcher's note that the field has left Adressevælger (SiteFetcher.AddressFallbackNote) is said once.
        private bool _fallbackSaid;

        public Fetched? Result { get; private set; }

        public AddressWindow(SiteFetcher fetcher, ProjectOptions options)
        {
            _fetcher = fetcher;
            _options = options;
            Title = Strings.Address_WindowTitle;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _pause.Tick += (_, _) =>
            {
                _pause.Stop();
                _ = Search();
            };
            _text.TextChanged += (_, _) =>
            {
                _pause.Stop();
                if (_text.Text.Trim().Length < 2)
                {
                    _search?.Cancel();
                    _hits.Items.Clear();
                    Say("");
                }
                else
                {
                    _pause.Start();
                }
            };
            _text.PreviewKeyDown += Keys;
            _hits.MouseDoubleClick += (_, _) => Choose();
            _load.IsDefault = true;
            _load.Click += (_, _) => Choose();
            var cancel = Button(Strings.Address_Cancel, 14);
            cancel.IsCancel = true;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(_load);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = Strings.Address_Field, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(_text);
            root.Children.Add(_hits);
            root.Children.Add(_status);
            root.Children.Add(buttons);
            Content = root;
            Loaded += (_, _) =>
            {
                _text.Focus();
                _ = Task.Run(_fetcher.WarmUp);  // the connection ready by the second character
            };
            // While fetching, Cancel, Esc and the window's X stop the fetch and keep the window: nothing half loaded.
            Closing += (_, e) =>
            {
                if (_fetch is not null)
                {
                    _fetch.Cancel();
                    e.Cancel = true;
                    return;
                }
                _pause.Stop();
                _search?.Cancel();
            };
        }

        private void Say(string text, bool error = false)
        {
            _status.Text = text;
            _status.Foreground = error ? Brushes.Firebrick : Brushes.DimGray;
        }

        private void Keys(object sender, KeyEventArgs e)
        {
            var n = _hits.Items.Count;
            switch (e.Key)
            {
                case Key.Down when n > 0:
                    _hits.SelectedIndex = Math.Min(_hits.SelectedIndex + 1, n - 1);
                    break;
                case Key.Up when n > 0:
                    _hits.SelectedIndex = Math.Max(_hits.SelectedIndex - 1, 0);
                    break;
                case Key.Enter:
                    Choose();
                    break;
                default:
                    return;
            }
            if (_hits.SelectedItem is { } item)
            {
                _hits.ScrollIntoView(item);
            }
            e.Handled = true;
        }

        private async Task Search()
        {
            _search?.Cancel();
            var cts = _search = new CancellationTokenSource();
            var text = _text.Text.Trim();
            try
            {
                var found = await Task.Run(() => _fetcher.SearchAddress(text, cts.Token), cts.Token);
                // Only what's still in the field.
                if (cts.IsCancellationRequested || _text.Text.Trim() != text)
                {
                    return;
                }
                _hits.Items.Clear();
                foreach (var s in found)
                {
                    _hits.Items.Add(new ListBoxItem { Content = s.Text, Tag = s });
                }
                _hits.SelectedIndex = found.Count > 0 ? 0 : -1;
                if (_fetcher.AddressFallbackNote is { } note && !_fallbackSaid)
                {
                    _fallbackSaid = true;
                    Say(note);
                }
                else
                {
                    Say(found.Count == 0 ? Strings.Address_NoneFound : "");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (!cts.IsCancellationRequested && e is not OperationCanceledException)
            {
                Say(e is FetchException ? e.Message : Fill(Strings.Address_SearchFailed, e.GetType().Name, e.Message), true);
            }
        }

        /// <summary>The selected suggestion: a street fills the field and waits for the number; an address loads.</summary>
        private void Choose()
        {
            if (_fetch is not null)
            {
                return;
            }
            if (_hits.SelectedItem is not ListBoxItem { Tag: AddressSearch.Suggestion s })
            {
                Say(Strings.Address_Pick);
                return;
            }
            if (s.Id is null)
            {
                _text.Text = s.Fill;
                _text.CaretIndex = s.Caret;
                _text.Focus();
                return;
            }
            _ = Load(s.Id);
        }

        private async Task Load(string id)
        {
            _pause.Stop();
            _search?.Cancel();
            var cts = _fetch = new CancellationTokenSource();
            _text.IsEnabled = _hits.IsEnabled = _load.IsEnabled = false;
            // Reported from the worker, shown here; a late one after a cancel is dropped.
            var progress = new Progress<string>(step =>
            {
                if (_fetch == cts)
                {
                    Say(step);
                }
            });
            try
            {
                Say(Strings.Address_StepAddress);
                var (address, x, y) = await Task.Run(() => _fetcher.AddressPoint(id, cts.Token), cts.Token);
                Say(Strings.Address_StepLot);
                var lot = await Task.Run(() => _fetcher.Lot(x, y, cts.Token), cts.Token);
                // A lot larger than one call of the height model (about 150 m): the user chooses the grid, or stops here.
                var cell = HeightCells.Size;
                if (SiteFetcher.PlanTerrain(lot, cell).Parts > 1)
                {
                    if (GridChoice(lot) is not { } chosen)
                    {
                        throw new OperationCanceledException();
                    }
                    cell = chosen;
                }
                // The site measured from the project's origin, the address point rounded (Placement.Origin).
                var site = await Task.Run(() => _fetcher.Fetch(x, y, address, _options.SiteplanMarginM,
                    _options.SetbackM, progress, cts.Token, lot, cell, _options.HiddenLayerList, _options.RoofLabels,
                    ProjectOrigin.Of(x, y)), cts.Token);
                var grid = await Task.Run(() => TerrainGrid.Parse(site.Csv), cts.Token);
                Result = new Fetched(address, x, y, site, grid);
                _fetch = null;
                DialogResult = true;
                return;
            }
            catch (OperationCanceledException)
            {
                Say(Strings.Address_Cancelled);
            }
            catch (Exception e) when (e is FetchException or TerrainGridException)
            {
                Say(Fill(Strings.Address_NothingLoaded, e.Message), true);
            }
            catch (Exception e)
            {
                // Never left fetching: Closing would refuse every close and Revit's modal window could never go.
                Say(Fill(Strings.Address_Unexpected, e.GetType().Name, e.Message), true);
            }
            _fetch = null;
            _text.IsEnabled = _hits.IsEnabled = _load.IsEnabled = true;
            _text.Focus();
        }

        /// <summary>
        /// A lot larger than one call: its size, and the grids that fit (the finest first, at most three), to choose
        /// from. Null = cancel. None fits (over about 2 km): a FetchException saying so.
        /// </summary>
        private static double? GridChoice(List<(double X, double Y)> lot)
        {
            var (w, h) = (lot.Max(p => p.X) - lot.Min(p => p.X), lot.Max(p => p.Y) - lot.Min(p => p.Y));
            var plans = SiteFetcher.Cells.Select(c => SiteFetcher.PlanTerrain(lot, c)).Where(p => p.Fits).Take(3).ToList();
            if (plans.Count == 0)
            {
                throw new FetchException(Fill(Strings.Address_LotTooLarge, w, h, SiteFetcher.MaxPoints));
            }
            var dialog = new TaskDialog(Ui.Title)
            {
                MainInstruction = Fill(Strings.Address_LargeLot, w, h),
                MainContent = Strings.Address_LargeLotContent,
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            var links = new[] { TaskDialogCommandLinkId.CommandLink1, TaskDialogCommandLinkId.CommandLink2, TaskDialogCommandLinkId.CommandLink3 };
            for (var k = 0; k < plans.Count; k++)
            {
                var p = plans[k];
                dialog.AddCommandLink(links[k],
                    Fill(k == 0 && p.Cell == HeightCells.Size ? Strings.Address_GridOwn : Strings.Address_Grid, p.Cell),
                    Fill(p.Cell == HeightCells.Size ? Strings.Address_GridPoints : Strings.Address_GridResampled, p.Points, p.Parts));
            }
            var answer = dialog.Show();
            var index = Array.FindIndex(links, l => (int)l == (int)answer);
            return index >= 0 && index < plans.Count ? plans[index].Cell : null;
        }
    }
}
