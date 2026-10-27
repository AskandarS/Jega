using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Generate Terrain": a Toposolid bounded by the lot's outline from the site plan DXF (layer GRUND), shaped by
/// points along the outline (every corner and at most every 0.4 m, from the grid) and the grid's nodes inside
/// the lot (every 0.8 m). On its own "Terrain" level where Settings says (the lowest point unless changed), its own type (Config's ToposolidType, 200 mm), heights from
/// the DVR90 of Revit's 0. A new run keeps the Toposolid it made before when nothing it's made from has changed,
/// moves it up or down when only Revit 0's DVR90 has, and else replaces it; other Toposolids are left alone.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class LotTerrainCommand : JegaCommand
{
    private const string Role = "LotTerrain";

    protected override string Button => Strings.Ribbon_GenerateTerrain;

    // The Toposolid is placed by the frame.
    protected override bool UsesFrame => true;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        var (result, receipt) = Generate(doc, fromButton: true);
        Receipt(receipt);
        return result;
    }

    /// <summary>
    /// The terrain's points on the lot that can go on a level (Settings, "Relative to a level"), DVR90 mm, by
    /// ProjectOptions.TerrainLevels, as Generate Terrain finds them (LotTerrain.LevelAt). Those not there (no house,
    /// the address point off the grid) are left out; empty without a terrain or a lot outline.
    /// </summary>
    public static Dictionary<string, double> TerrainPoints(Document doc)
    {
        var points = new Dictionary<string, double>();
        if (!StoredTerrain.TryGet(doc, out _, out var grid, out _) || LotOutline.Find(doc) is not { } corners)
        {
            return points;
        }
        var placement = Placement.Get(doc);
        var settings = ProjectStore.ReadOptions(doc);
        var ring = Ring(doc, corners, placement);
        var holes = Holes(doc, placement);
        try
        {
            var terrain = LotTerrain.Compute(grid, ring, settings.InteriorStepMm, settings.EdgeStepMm, holes);
            var house = ProjectStore.ReadSite(doc)?.HouseGroundMm;
            foreach (var at in ProjectOptions.TerrainLevels)
            {
                if (LotTerrain.LevelAt(at, terrain, grid, ring, house, holes, Placement.AddressMm(doc)) is { } mm)
                {
                    points[at] = mm;
                }
            }
        }
        catch (TerrainGridException)
        {
            // The lot off the terrain: nothing to put on a level.
        }
        return points;
    }

    // The lot in the CSV's terms (mm from the project's origin): the lot Load from Address kept, as it is, so what the
    // Toposolid is made from, and its fingerprint (SiteInputs), don't change with where the site is. Taken into the model
    // and back, a corner on a half millimetre rounded the other way after a turn, and the Toposolid was made again with
    // what was done to it lost. No kept lot: the outline found (an import's), taken back.
    private static List<Point2> Ring(Document doc, List<XYZ> corners, Placement placement) =>
        ProjectStore.ReadSite(doc) is { Lot.Count: >= 3 } site ? site.Lot : corners.Select(placement.ToCsv).ToList();

    // The lot's holes in the CSV's terms (mm from the project's origin), as the outline (LotOutline.Holes).
    private static List<IReadOnlyList<Point2>> Holes(Document doc, Placement placement) =>
        LotOutline.Holes(doc).Select(h => (IReadOnlyList<Point2>)h.Select(placement.ToCsv).ToList()).ToList();

    /// <summary>True when the model has a Toposolid made by Generate Terrain (Refresh makes it again).</summary>
    public static bool Exists(Document doc) => ProjectStore.Marked(doc, Role, typeof(Toposolid)).Count > 0;

    /// <summary>
    /// Makes (or makes again) the Toposolid, in its own transaction; the button and Refresh both use it. Returns how
    /// it went and its receipt (Refresh shows the result line). The lot's holes (parcels inside it that aren't part of
    /// it) are left out of it; a Toposolid Revit refuses with them is made again without ($withHoles false), and said so.
    /// </summary>
    internal static (Result Result, ReceiptText Receipt) Generate(Document doc, bool withHoles = true, bool fromButton = false)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (!StoredTerrain.TryGet(doc, out _, out var grid, out var problem))
        {
            return (Result.Cancelled, new ReceiptText(problem) { Warning = true });
        }
        if (LotOutline.Find(doc) is not { } corners)
        {
            return (Result.Cancelled, new ReceiptText(Strings.Generate_NoLot)
            {
                Steps = [Fill(Strings.Generate_NoLotStep, LotOutline.Layer), Strings.Generate_AgainStep],
                Warning = true,
            });
        }
        if (DefaultType(doc) is not { } type)
        {
            return (Result.Cancelled, new ReceiptText(Strings.Generate_NoType)
            {
                Steps = [Strings.Generate_NoTypeStep, Strings.Generate_AgainStep],
                Warning = true,
            });
        }

        var placement = Placement.Get(doc);
        var settings = ProjectStore.ReadOptions(doc);
        LotTerrainResult terrain;
        var ring = Ring(doc, corners, placement);
        var holes = withHoles ? Holes(doc, placement) : [];
        try
        {
            terrain = LotTerrain.Compute(grid, ring, settings.InteriorStepMm, settings.EdgeStepMm, holes);
        }
        catch (TerrainGridException e)
        {
            // The lot and the terrain are both placed by the site's anchor (LotOutline.Find), so where the import sits
            // doesn't matter: off the terrain is always a terrain too small for the lot.
            return (Result.Cancelled, new ReceiptText(Strings.Generate_OffTerrain)
            {
                Lines = [Strings.Generate_OffTerrainWhy],
                Details = [e.Message],
                Warning = true,
            });
        }

        // Terrain at DVR90 z sits at Revit Z = z - the DVR90 of Revit's 0.
        var (dvr90, fromSurvey) = Dvr90.AtRevitZero(doc);
        List<XYZ> Model(IEnumerable<Point3> pts) => pts.Select(p => placement.Point(p.X, p.Y, Units.FeetFromMm(p.Z - dvr90))).ToList();
        var edge = Model(terrain.Edge);
        var inside = Model(terrain.Inside);
        var points = edge.Concat(inside).ToList();
        var lowest = points.Min(p => p.Z);
        // The Terrain level: where Settings says (the lowest point unless changed). The Toposolid is always made on it
        // at the lowest point, where it starts, and the level then moved up to its place with the Toposolid left where
        // it is (LevelTo): a Toposolid made on a level above its lowest point fails as "too thin".
        // Not there (no house on the lot, the address point off the grid): the lowest, and said so.
        var chosen = LotTerrain.LevelAt(settings.TerrainLevelAt, terrain, grid, ring, ProjectStore.ReadSite(doc)?.HouseGroundMm, holes, Placement.AddressMm(doc));
        var levelFellBack = chosen is null;
        var levelDvr90 = chosen ?? terrain.All.Min(p => p.Z);
        var levelZ = Units.FeetFromMm(levelDvr90 - dvr90);

        // The Toposolid made before is kept when nothing it's made from has changed, moved and turned along when only the
        // site's place has (the import moved or turned, Rotate Project North), and only moved up or down when just
        // Revit 0's DVR90 has: deleting it would take the spot elevations, dimensions and tags on it along.
        var siteInputs = SiteInputs(ring, terrain, settings.ToposolidThicknessMm);
        var place = PlaceOf(placement);
        var inputs = $"{siteInputs}@{place}";
        if (KeepExisting(doc, fromButton, siteInputs, place, inputs, placement, edge, dvr90, levelZ, out var edited) is { } keptBefore)
        {
            return keptBefore;
        }
        var replaced = Exists(doc);
        // What deleting the old one takes along: only those are worth a warning (most Toposolids have none). Spot
        // elevations and dimensions, tags, its subdivisions (Toposolids of their own) and families hosted on it; not
        // the old one itself.
        var lost = ProjectStore.Marked(doc, Role, typeof(Toposolid))
            .Sum(old => old.GetDependentElements(new ElementMulticlassFilter(
                [typeof(Dimension), typeof(IndependentTag), typeof(Toposolid), typeof(FamilyInstance)])).Count(id => id != old.Id));
        if (AskBeforeRemaking(edited, lost, fromButton) is { } keptOld)
        {
            return (Result.Succeeded, keptOld);
        }

        if (Rebuild(doc, type, settings, corners, holes, placement, points, edge, lowest, levelZ, inputs, dvr90, out var made) is { } failed)
        {
            return failed;
        }
        (var level, type, var shift, var worst, var measured, var warnings) = made!;
        // How long a Toposolid takes on a lot this size, for a report on a slow one.
        JegaLog.Note($"Toposolid made: {edge.Count} edge points, {terrain.Edge.Count + terrain.Inside.Count} points, {clock.ElapsedMilliseconds} ms");

        var z = terrain.All.Select(p => p.Z).ToList();
        // The headline: the level and which point of the terrain it's on. A failed check, or tags lost, comes before it.
        var generated = Fill(Generated(levelFellBack ? ProjectOptions.LevelLowest : settings.TerrainLevelAt), level.Name, Units.Level(levelDvr90));
        var lostLine = lost > 0 ? Fill(Strings.Generate_MadeAgainLost, lost) : null;
        var shown = new List<string?>
        {
            measured == 0 ? lostLine : null,
            measured == 0 || lost > 0 ? generated : null,
            lost > 0 ? Strings.Generate_AddAgain : null,
            edited ? Strings.Generate_ShapeLost : null,
            !levelFellBack ? null
                : settings.TerrainLevelAt == ProjectOptions.LevelHouse ? Strings.Generate_LevelFellBackHouse : Strings.Generate_LevelFellBackAddress,
            // Absolute levels: the way to relative ones, as a line, not a step (as Load from Address). Not "the Toposolid
            // sits x m above a floor drawn at 0": nobody said the floor is drawn there.
            Math.Abs(dvr90) < 1 ? Receipts.FloorLine(doc) : null,
        };
        return (Result.Succeeded, new ReceiptText(measured == 0 ? Strings.Generate_NotChecked : lostLine ?? generated)
        {
            Lines = shown,
            Details =
            [
                Fill(Strings.Generate_TerrainRange, Units.Level(z.Min()), Units.Level(z.Max())),
                Fill(Strings.Generate_Points, terrain.Edge.Count, terrain.Inside.Count, Units.Metres(settings.InteriorStepMm)),
                holes.Count > 0 ? Fill(Strings.Generate_HolesLeftOut, holes.Count) : null,
                Fill(Strings.Generate_Type, type.Name),
                Dvr90.Describe(doc, dvr90, fromSurvey),
                measured == 0 ? null : Fill(Strings.Generate_Checked, measured, edge.Count, Units.MmFromFeet(worst)),
                shift != 0 ? Fill(Strings.Generate_Shifted, Units.Metres(Units.MmFromFeet(-shift))) : null,
                replaced ? Strings.Generate_WhyAgain : null,
                warnings.Messages.Count > 0 ? Fill(Strings.Generate_RevitWarnings, string.Join(" ", warnings.Messages)) : null,
            ],
            Warning = lost > 0 || measured == 0,
        });
    }

    // The Toposolid made before, if there is one: the result to give when it is kept (as it is, moved along with the
    // site, or up or down), null when it is made again (and why, to the log). $edited: its shape changed by hand since
    // Jega made or moved it (a point added or moved: Modify Sub Elements).
    private static (Result Result, ReceiptText Receipt)? KeepExisting(Document doc, bool fromButton, string siteInputs, string place,
        string inputs, Placement placement, List<XYZ> edge, double dvr90, double levelZ, out bool edited)
    {
        edited = false;
        if (ProjectStore.Marked(doc, Role, typeof(Toposolid)).FirstOrDefault() is Toposolid existing)
        {
            // Recorded (Record): the same inputs, or made again; its shape after a "#". None recorded: made again.
            var record = ProjectStore.ReadBuilt(existing);
            (string Inputs, double Dvr90Mm)? built = record is { } r ? (r.Inputs.Split('#')[0], r.Dvr90Mm) : null;
            var shapeWas = record?.Inputs.Split('#') is [_, var s] ? s : null;
            edited = shapeWas is not null && ShapeChanged(Shape(existing), shapeWas);
            // Why it is made again, if it is: to the log, so a Toposolid that loses what was done to it can be traced.
            var why = built is null ? "no inputs recorded" : null;
            // The same terrain at another place: along with the site, unless Revit took it there already.
            if (built is { } was && was.Inputs.Split('@') is [var wasSite, var wasPlace])
            {
                if (wasSite != siteInputs)
                {
                    why = "what it is made from changed (lot, terrain, spacing or thickness)";
                }
                else if (wasPlace != place)
                {
                    if (Follow(doc, existing, wasPlace, placement, edge, inputs, was.Dvr90Mm, shapeWas, out var followProblem))
                    {
                        built = (inputs, was.Dvr90Mm);
                    }
                    else
                    {
                        why = $"couldn't follow the site from {wasPlace} to {place}: {followProblem}";
                    }
                }
            }
            var builtDvr90 = built is { } same && same.Inputs == inputs ? same.Dvr90Mm : (double?)null;
            // The button pressed on a Toposolid changed by hand: made again from the terrain, asked first (below), as it is
            // the only way back from those changes (Revit's Reset Shape makes it flat). Refresh keeps it.
            if (fromButton && edited)
            {
                why = "Generate Terrain on a shape changed by hand";
            }
            else if (builtDvr90 is { } at && Keep(doc, existing, at, dvr90, levelZ, edge, inputs, shapeWas) is { } kept)
            {
                // Kept with changes made by hand (Refresh): said in its result line, the one Refresh shows, with where it
                // is made again from the terrain, so Refresh keeping it and Generate Terrain asking don't look like two minds.
                return (Result.Succeeded, edited
                    ? kept with { Result = $"{kept.Result} {Fill(Strings.Generate_EditedKept, Strings.Ribbon_GenerateTerrain.Replace('\n', ' '))}" }
                    : kept);
            }
            JegaLog.Note($"Toposolid made again: {why ?? (builtDvr90 is null ? "its inputs recorded in another form" : "couldn't be moved up or down")}");
        }
        return null;
    }

    // Something done to it by hand would be lost: asked first (Ui.Ask). Kept, or the dialog closed: the receipt for the
    // old one staying, and the terrain isn't brought up to date now. Null: made again; nothing on it: unasked, as before.
    private static ReceiptText? AskBeforeRemaking(bool edited, int lost, bool fromButton)
    {
        if (edited || lost > 0)
        {
            var what = string.Join(" ", new[] { edited ? Strings.Generate_AskShapeEdited : null, lost > 0 ? Fill(Strings.Generate_AskLost, lost) : null }
                .OfType<string>());
            var (question, content) = fromButton && edited
                ? (Strings.Generate_AskEdited, Strings.Generate_AskEditedContent)
                : (Strings.Generate_AskRemake, Strings.Generate_AskRemakeContent);
            if (Ask(question, Fill(content, what),
                    [(Strings.Generate_Remake, Strings.Generate_RemakeNote), (Strings.Generate_KeepOld, Strings.Generate_KeepOldNote)]) != 0)
            {
                return new ReceiptText(Strings.Generate_KeptOld)
                {
                    Lines = [Fill(Strings.Generate_KeptOldNext, Strings.Ribbon_GenerateTerrain.Replace('\n', ' '))],
                };
            }
        }
        return null;
    }

    // What Rebuild made, for the receipt: the Terrain level, the Toposolid's type, the shift Revit's heights needed (feet),
    // the largest difference found at the edge (feet) and at how many points, and Revit's warnings.
    private sealed record Made(Level Level, ToposolidType Type, double Shift, double Worst, int Measured, WarningCollector Warnings);

    // The Toposolid made again, in its own transaction, the one before deleted. Null when it went ($made); else the
    // result to give (Revit refused it: made over the whole lot when it had holes, or failed).
    private static (Result Result, ReceiptText Receipt)? Rebuild(Document doc, ToposolidType type, ProjectOptions settings, List<XYZ> corners,
        List<IReadOnlyList<Point2>> holes, Placement placement, List<XYZ> points, List<XYZ> edge, double lowest, double levelZ, string inputs,
        double dvr90, out Made? made)
    {
        made = null;
        Level level;
        var warnings = new WarningCollector();
        double shift = 0, worst;
        int measured;
        using (var t = new Transaction(doc, Strings.Undo_GenerateTerrain))
        {
            var options = t.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(warnings);
            t.SetFailureHandlingOptions(options);
            t.Start();
            // The one before goes; its workset and phases go to the new one.
            var before = ProjectStore.Marked(doc, Role, typeof(Toposolid));
            var inherited = Inherited.From(before.FirstOrDefault());
            doc.Delete(before.Select(e => e.Id).ToList());
            // Copies the user made of it stay (ProjectStore.Copies): said, so one that went missing can be traced.
            JegaLog.Note($"Toposolid: {before.Count} of Jega's deleted, {ProjectStore.Copies(doc, Role, typeof(Toposolid))} copies left alone");
            try
            {
                // Made the way Revit's own "Create from import" does it: the lot's outline as the boundary and
                // all the points, in one call. On its own level at the lowest point, so no point is below where
                // it starts (a Toposolid's bottom is flat, one type thickness below that: anything lower fails as
                // "too thin"), with its own thin type, so the earth under it is 200 mm, not down to some level.
                level = TerrainLevel(doc, lowest);
                type = TerrainType(doc, type, settings.ToposolidThicknessMm);
                doc.Regenerate();
                var loop = LotOutline.Loop(doc, corners, level.ProjectElevation);
                // The holes as more loops of the same profile, so Revit leaves them out (no point lies inside them).
                var holeLoops = holes.Select(h => LotOutline.Loop(doc, h.Select(p => placement.Point(p.X, p.Y, 0)).ToList(), level.ProjectElevation)).ToList();
                Toposolid Build(double dz)
                {
                    var solid = Toposolid.Create(doc, [loop, .. holeLoops], points.Select(p => new XYZ(p.X, p.Y, p.Z + dz)).ToList(), type.Id, level.Id);
                    doc.Regenerate();
                    return solid;
                }
                // The heights are checked against Core at every edge point. If they are all off by the same
                // amount, Revit read the Z from somewhere else than Revit's 0: built again once, corrected.
                var solid = Build(0);
                var (median, _, _) = Deviation(solid, edge);
                if (Math.Abs(median) > Units.FeetFromMm(1))
                {
                    shift = -median;
                    doc.Delete(solid.Id);
                    solid = Build(shift);
                }
                if (Math.Abs(levelZ - lowest) > Units.FeetFromMm(0.5))
                {
                    LevelTo(doc, solid, levelZ, edge);
                }
                (_, worst, measured) = Deviation(solid, edge);
                ProjectStore.Mark(solid, Role, own: true);
                inherited?.To(solid);
                Record(solid, inputs, dvr90);
            }
            catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                t.RollBack();
                if (holes.Count > 0)
                {
                    // Over the whole lot instead: said first, as it isn't what the lot is. Worded for both made and
                    // kept (the one without holes, from a run before), which the line under it tells apart.
                    var (again, receipt) = Generate(doc, withHoles: false);
                    return again != Result.Succeeded ? (again, receipt) : (again, receipt with
                    {
                        Result = Fill(Strings.Generate_WholeLot, holes.Count),
                        Lines = [receipt.Result, .. receipt.Lines],
                        Steps = [Strings.Generate_CutByHand, .. receipt.Steps],
                        Details = [Fill(Strings.Receipt_Revit, e.Message), .. receipt.Details],
                        Warning = true,
                    });
                }
                return (Result.Failed, new ReceiptText(Strings.Generate_Failed)
                {
                    Lines = [Strings.Generate_FailedWhy],
                    Details = [Fill(Strings.Receipt_Revit, e.Message)],
                    Warning = true,
                });
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                return (Result.Failed, new ReceiptText(Strings.Generate_RevitStopped) { Warning = true });
            }
        }
        made = new Made(level, type, shift, worst, measured, warnings);
        return null;
    }

    /// <summary>
    /// The Toposolid made before, from the same inputs: left as it is if Revit 0's DVR90 is the same, else moved up
    /// or down by the difference, with its level, in its own transaction. Null if it couldn't be moved (then it is
    /// made again).
    /// </summary>
    private static ReceiptText? Keep(Document doc, Toposolid solid, double builtDvr90, double dvr90,
        double levelZ, List<XYZ> edge, string inputs, string? shape)
    {
        var moveMm = builtDvr90 - dvr90;
        var kept = new ReceiptText(Strings.Generate_Kept) { Lines = [Strings.Generate_Stay] };
        // Only the level's place changed (Settings, "Terrain level at"): moved below, the Toposolid left where it is.
        var levelInPlace = ProjectStore.Marked(doc, TerrainLevelRole, typeof(Level)).FirstOrDefault() is not Level current
            || Math.Abs(current.ProjectElevation - levelZ) < Units.FeetFromMm(0.5);
        if (Math.Abs(moveMm) < 0.5 && levelInPlace)
        {
            return kept;
        }
        double worst;
        int measured;
        using (var t = new Transaction(doc, Strings.Undo_MoveTerrain))
        {
            t.Start();
            try
            {
                LevelTo(doc, solid, levelZ, edge);
                (_, worst, measured) = Deviation(solid, edge);
                if (measured == 0 || worst > Units.FeetFromMm(20))
                {
                    t.RollBack();
                    return null;
                }
                Record(solid, inputs, dvr90, shape);
            }
            catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                t.RollBack();
                return null;
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                return null;
            }
        }
        return new ReceiptText(Math.Abs(moveMm) < 0.5 ? Strings.Generate_KeptLevelMoved
            : Fill(moveMm > 0 ? Strings.Generate_MovedUp : Strings.Generate_MovedDown, Units.Metres(Math.Abs(moveMm))))
        {
            Lines = [Strings.Generate_Stay],
            Details =
            [
                Fill(Strings.Generate_ZeroWas, Units.Level(builtDvr90), Units.Level(dvr90)),
                Fill(Strings.Generate_Checked, measured, edge.Count, Units.MmFromFeet(worst)),
            ],
        };
    }

    /// <summary>
    /// The same terrain at another place (the site moved or turned since it was made): the Toposolid moved and turned
    /// with it, rigidly, in its own transaction, so what hangs on it (spot elevations, tags) goes along instead of being
    /// deleted with it. Already in place (Revit's Rotate Project North took it along): only recorded. False when it
    /// couldn't be (then it is made again).
    /// </summary>
    private static bool Follow(Document doc, Toposolid solid, string wasPlace, Placement placement, List<XYZ> edge, string inputs, double builtDvr90,
        string? shape, out string problem)
    {
        problem = "";
        if (ParsePlace(wasPlace) is not var (wx, wy, wa))
        {
            problem = "the recorded place can't be read";
            return false;
        }
        using var t = new Transaction(doc, Strings.Undo_MoveTerrain);
        t.Start();
        try
        {
            var before = OnVertex(solid, edge);
            if (!InPlace(solid, edge))
            {
                // Each point was at origin + R(old) s and goes to origin' + R(new) s: turned about the old origin by the
                // difference, then moved to the new one.
                var from = new XYZ(Units.FeetFromMm(wx), Units.FeetFromMm(wy), 0);
                var (nx, ny) = placement.OriginMm;
                var turn = Angles.TurnDegrees(Angles.ToRadians(placement.AngleToTrueNorth), Angles.ToRadians(wa));
                if (Math.Abs(turn) >= 0.001)
                {
                    ElementTransformUtils.RotateElement(doc, solid.Id, Line.CreateBound(from, from + XYZ.BasisZ), Angles.ToRadians(turn));
                }
                ElementTransformUtils.MoveElement(doc, solid.Id, new XYZ(Units.FeetFromMm(nx), Units.FeetFromMm(ny), 0) - from);
                doc.Regenerate();
            }
            if (!InPlace(solid, edge))
            {
                problem = $"edge points on a vertex: {before} of {edge.Count} before, {OnVertex(solid, edge)} after the turn and move";
                t.RollBack();
                return false;
            }
            Record(solid, inputs, builtDvr90, shape);
        }
        catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            problem = e.Message;
            t.RollBack();
            return false;
        }
        if (t.Commit() != TransactionStatus.Committed)
        {
            problem = "Revit didn't accept the move";
            return false;
        }
        return true;
    }

    // What Jega made the Toposolid from, recorded on it (ProjectStore.WriteBuilt) with its shape after a "#": $shape,
    // the one recorded before when it is only moved or turned (Keep, Follow), so a change made by hand since is still
    // told apart at the next run; else its shape now.
    private static void Record(Toposolid solid, string inputs, double dvr90, string? shape = null) =>
        ProjectStore.WriteBuilt(solid, $"{inputs}#{shape ?? Shape(solid)}", dvr90);

    // The Toposolid's shape: how many points it has and their heights above its lowest, to the centimetre, in order.
    // The same when Jega moves or turns it, or moves it up or down; another when a point is added or moved by hand.
    // Kept as the heights, not a hash: a turn (Rotate Project North) leaves float noise in them, and a height on a half
    // centimetre then rounds the other way; ShapeChanged allows that (else Work Direction read as "changed by hand").
    private static string Shape(Toposolid solid)
    {
        var zs = solid.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Select(v => Units.MmFromFeet(v.Position.Z)).ToList();
        if (zs.Count == 0)
        {
            return "0";
        }
        var low = zs.Min();
        return $"{zs.Count}:{string.Join(",", zs.Select(z => (long)Math.Round((z - low) / 10)).Order())}";
    }

    // Whether the shape $now differs from the one recorded, $was, by more than a turn's float noise: another number of
    // points, or a height more than a centimetre off. A shape recorded before as a hash is compared as one. Said in
    // jega.log when it does, so a false "changed by hand" can be traced.
    private static bool ShapeChanged(string now, string was)
    {
        if (now.Split(':') is not [var countNow, var heightsNow] || was.Split(':') is not [var countWas, var heightsWas] || countNow != countWas)
        {
            return now != was;
        }
        if (!heightsWas.Contains(','))
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(heightsNow)))[..16];
            return hash != heightsWas;
        }
        var a = heightsNow.Split(',').Select(long.Parse).ToList();
        var b = heightsWas.Split(',').Select(long.Parse).ToList();
        var worst = a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
        if (worst > 1)
        {
            JegaLog.Note($"Toposolid shape changed: {countNow} points, a height {worst} cm off what was recorded.");
        }
        return worst > 1;
    }

    // On the plan where the terrain says: most edge points (they are its vertices, as made) on a vertex within 10 mm.
    // Not Deviation: it measures heights only, and a site moved off its vertices leaves it none to measure (a median
    // of 0), which would pass. Its height is Keep's.
    private static bool InPlace(Toposolid solid, List<XYZ> edge) => OnVertex(solid, edge) is var on && on >= 3 && on * 2 >= edge.Count;

    // How many edge points lie on one of the Toposolid's vertices, within 10 mm on the plan.
    private static int OnVertex(Toposolid solid, List<XYZ> edge)
    {
        var vertices = new Vertices(solid);
        return edge.Count(p => vertices.Nearest(p, Units.FeetFromMm(10)) is not null);
    }

    // The Toposolid's vertices in 1-foot cells on the plan, so the one nearest an edge point is looked for among its
    // neighbours only: Deviation and OnVertex compared every edge point with every vertex, seconds on a large lot
    // (4,000 × 250,000), several times a run. The same answer, as both look only a few mm around the point.
    private sealed class Vertices
    {
        private const double Cell = 1;
        private readonly Dictionary<(long, long), List<XYZ>> _cells = [];

        public Vertices(Toposolid solid)
        {
            foreach (var v in solid.GetSlabShapeEditor().SlabShapeVertices.Cast<SlabShapeVertex>().Select(v => v.Position))
            {
                var key = Key(v.X, v.Y);
                if (!_cells.TryGetValue(key, out var cell))
                {
                    _cells[key] = cell = [];
                }
                cell.Add(v);
            }
        }

        /// <summary>The vertex nearest $p on the plan, within $radius (feet); null if none is.</summary>
        public XYZ? Nearest(XYZ p, double radius)
        {
            XYZ? nearest = null;
            var best = double.MaxValue;
            var (i0, j0) = Key(p.X - radius, p.Y - radius);
            var (i1, j1) = Key(p.X + radius, p.Y + radius);
            for (var i = i0; i <= i1; i++)
            {
                for (var j = j0; j <= j1; j++)
                {
                    if (!_cells.TryGetValue((i, j), out var cell))
                    {
                        continue;
                    }
                    foreach (var v in cell)
                    {
                        var d = Units.Flat(v, p);
                        if (d <= radius && d < best)
                        {
                            (nearest, best) = (v, d);
                        }
                    }
                }
            }
            return nearest;
        }

        private static (long, long) Key(double x, double y) => ((long)Math.Floor(x / Cell), (long)Math.Floor(y / Cell));
    }

    // Where the site was when the Toposolid was made: its 0,0 in model mm and the angle to true north
    // (degrees), invariant text, after the "@" of its recorded inputs.
    private static string PlaceOf(Placement placement)
    {
        var (x, y) = placement.OriginMm;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round(x)},{Math.Round(y)},{Math.Round(placement.AngleToTrueNorth, 3)}");
    }

    private static (double X, double Y, double Angle)? ParsePlace(string text)
    {
        var parts = text.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return parts.Length == 3 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out var x)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out var y)
            && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out var a) ? (x, y, a) : null;
    }

    /// <summary>
    /// A fingerprint of what the Toposolid is made from, wherever the site is: the outline and the points in site mm
    /// (from the site's 0,0, true north up), their DVR90 heights and the thickness. Recorded with the site's place
    /// (PlaceOf) after an "@", so a site moved or turned is told from another terrain. Revit 0's DVR90 is left out: a
    /// change of it alone moves the Toposolid. $ring: the lot as Ring gives it, the same wherever the site is.
    /// </summary>
    private static string SiteInputs(List<Point2> ring, LotTerrainResult terrain, int thicknessMm)
    {
        static string Mm(double mm) => Math.Round(mm).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text = new System.Text.StringBuilder();
        foreach (var c in ring)
        {
            text.Append(Mm(c.X)).Append(',').Append(Mm(c.Y)).Append(';');
        }
        text.Append('|');
        foreach (var p in terrain.All)
        {
            text.Append(Mm(p.X)).Append(',').Append(Mm(p.Y)).Append(',').Append(Mm(p.Z)).Append(';');
        }
        text.Append('|').Append(thicknessMm);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal const string TerrainLevelRole = "TerrainLevel";

    /// <summary>
    /// Inside a transaction: the Terrain level to Z (feet from Revit's 0) with the Toposolid left where the terrain
    /// says. The level first, and a Toposolid on it goes along; then it is moved back by what it's off along the
    /// edge (its offset from the level, so its points stay as made). Also a Toposolid not on the level gets moved.
    /// </summary>
    private static void LevelTo(Document doc, Toposolid solid, double z, List<XYZ> edge)
    {
        TerrainLevel(doc, z);
        doc.Regenerate();
        var (median, _, _) = Deviation(solid, edge);
        if (Math.Abs(median) > Units.FeetFromMm(1))
        {
            ElementTransformUtils.MoveElement(doc, solid.Id, new XYZ(0, 0, -median));
            doc.Regenerate();
        }
    }

    /// <summary>The receipt's headline for Settings' "Terrain level at": a whole sentence each, {0} the level, {1} its height.</summary>
    private static string Generated(string at) => at switch
    {
        ProjectOptions.LevelHighest => Strings.Generate_MadeHighest,
        ProjectOptions.LevelAverage => Strings.Generate_MadeAverage,
        ProjectOptions.LevelAddress => Strings.Generate_MadeAddress,
        ProjectOptions.LevelHouse => Strings.Generate_MadeHouse,
        _ => Strings.Generate_MadeLowest,
    };

    /// <summary>
    /// Jega's "Terrain" level at Z (feet from Revit's 0), inside a transaction: the one made before, moved,
    /// so views and the Toposolid stay on it; else a new one, named "Terrain" if that name is free, else "Jega Terrain".
    /// </summary>
    private static Level TerrainLevel(Document doc, double z)
    {
        // Dressed once, when made; after that it's the user's: a plan deleted or heads hidden stay so.
        var dress = false;
        if (ProjectStore.Marked(doc, TerrainLevelRole, typeof(Level)).FirstOrDefault() is not Level level)
        {
            level = Level.Create(doc, z);
            var names = new FilteredElementCollector(doc).OfClass(typeof(Level)).Select(l => l.Name).ToHashSet();
            level.Name = names.Contains(Config.Current.Revit.TerrainLevel) ? Config.Current.Revit.TerrainLevelTaken : Config.Current.Revit.TerrainLevel;
            ProjectStore.Mark(level, TerrainLevelRole, own: true);
            dress = true;
        }
        // Elevation is measured from the project's elevation base, which may not be Revit's 0: corrected by the
        // difference to ProjectElevation, which always is.
        level.Elevation += z - level.ProjectElevation;
        if (dress)
        {
            Dress(doc, level);
        }
        return level;
    }

    /// <summary>
    /// Inside a transaction: the Terrain level made like the project's own. A level from Level.Create has the default
    /// level type and no head in the views already there, and no plan. It gets the type most of the other levels
    /// have, a plan of its own (of a "Site Plan" type, else the first floor plan type, named as the level), and in every section and
    /// elevation its heads as the other levels there have theirs (both ends where there's none to copy). Revit
    /// refusing any of it leaves that part as it was.
    /// </summary>
    private static void Dress(Document doc, Level level)
    {
        var others = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(l => l.Id != level.Id).ToList();
        if (others.GroupBy(l => l.GetTypeId()).MaxBy(g => g.Count())?.Key is { } typeId && typeId != level.GetTypeId())
        {
            level.ChangeTypeId(typeId);
        }
        if (!HasPlan(doc, level))
        {
            var types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();
            // Site plans are floor plans to the API: the type called "Site Plan" if the template has one.
            var plans = types.Where(t => t.ViewFamily == ViewFamily.FloorPlan).ToList();
            if ((plans.FirstOrDefault(t => t.Name.Contains(Config.Current.Revit.TerrainPlanType, StringComparison.OrdinalIgnoreCase)) ?? plans.FirstOrDefault()) is { } type)
            {
                try
                {
                    var plan = ViewPlan.Create(doc, type.Id, level.Id);
                    plan.Name = level.Name;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // The name taken by another plan: Revit's own name kept.
                }
            }
        }
        doc.Regenerate();
        foreach (var view in SectionsAndElevations(doc))
        {
            try
            {
                if (!level.CanBeVisibleInView(view))
                {
                    continue;
                }
                var like = others.FirstOrDefault(o => o.CanBeVisibleInView(view));
                foreach (var end in new[] { DatumEnds.End0, DatumEnds.End1 })
                {
                    if (like?.IsBubbleVisibleInView(end, view) ?? true)
                    {
                        level.ShowBubbleInView(end, view);
                    }
                    else
                    {
                        level.HideBubbleInView(end, view);
                    }
                }
            }
            catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // This view's heads left as Revit has them.
            }
        }
    }

    private static bool HasPlan(Document doc, Level level) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Any(v => !v.IsTemplate && v.GenLevel?.Id == level.Id);

    private static IEnumerable<View> SectionsAndElevations(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v.ViewType is ViewType.Section or ViewType.Elevation);

    /// <summary>
    /// Jega's own Toposolid type, inside a transaction: made once as a copy of the default with one layer, in
    /// the material of the default's thickest layer, as thick as the Settings say (200 mm unless changed). Its
    /// thickness follows the Settings on every run; its material, once made, is left as set in Edit Type.
    /// </summary>
    private static ToposolidType TerrainType(Document doc, ToposolidType source, int thicknessMm)
    {
        var name = Config.Current.Revit.ToposolidType;
        var type = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().FirstOrDefault(t => t.Name == name)
                   ?? (ToposolidType)source.Duplicate(name);
        // The type's own structure, cut down to its thickest layer at the thickness: a new structure from scratch
        // has settings (end caps) a Toposolid refuses.
        if (type.GetCompoundStructure() is { } structure && structure.GetLayers().MaxBy(l => l.Width) is { } layer
            && (structure.GetLayers().Count != 1 || Math.Abs(Units.MmFromFeet(layer.Width) - thicknessMm) > 0.5))
        {
            layer.Width = Units.FeetFromMm(thicknessMm);
            structure.SetLayers([layer]);
            type.SetCompoundStructure(structure);
        }
        return type;
    }

    /// <summary>
    /// The Toposolid's height minus the asked height at the edge points that are one of its vertices (within
    /// 1 mm on the plan), in feet: the median, the largest, and how many were measured.
    /// </summary>
    private static (double Median, double Worst, int Count) Deviation(Toposolid solid, List<XYZ> edge)
    {
        var vertices = new Vertices(solid);
        var d = edge.Select(p => (p, v: vertices.Nearest(p, Units.FeetFromMm(1))))
            .Where(x => x.v is not null)
            .Select(x => x.v!.Z - x.p.Z).Order().ToList();
        return d.Count == 0 ? (0, 0, 0) : (d[d.Count / 2], d.Max(Math.Abs), d.Count);
    }

    // Revit's warnings (overlaps, lines slightly off axis) taken out of its dialog and listed in the summary,
    // each kind once. Errors are left to Revit.
    private sealed class WarningCollector : IFailuresPreprocessor
    {
        public HashSet<string> Messages { get; } = [];

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failures)
        {
            foreach (var f in failures.GetFailureMessages().Where(f => f.GetSeverity() == FailureSeverity.Warning))
            {
                Messages.Add(f.GetDescriptionText());
                failures.DeleteWarning(f);
            }
            return FailureProcessingResult.Continue;
        }
    }

    // The project's default Toposolid type, or else the first one by name.
    private static ToposolidType? DefaultType(Document doc) =>
        doc.GetElement(doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_Toposolid))) as ToposolidType
        ?? new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>().OrderBy(t => t.Name).FirstOrDefault();
}
