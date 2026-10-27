using System.IO;
using Autodesk.Revit.DB;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// Settings, the language or the layers changed: the site plan drawn again, its texts in the project's language and
/// without the layers left out (Settings > Layers), from what the model keeps (the surroundings and the terrain;
/// nothing is fetched), and imported in place of the one before at the same place and north (its 0,0 where the old
/// one's was). For the language Settings asks first; kept, only its texts are in the language before. Other layers
/// are drawn again unasked, as that is what the change is for. Also the turn the drawing gets (TurnFor: as the view
/// it stands in has its north, so the frame and the texts lie straight there), for Load from Address, Refresh and
/// True / Project North (Straighten) too.
/// </summary>
internal static class SiteplanRedraw
{
    /// <summary>Whether there is a site plan to draw again: a marked import and the surroundings it was drawn from.</summary>
    public static bool Possible(Document doc) => Placement.Siteplan(doc) is not null && ProjectStore.ReadSurroundings(doc) is not null;

    /// <summary>A language's name as Settings lists it (Config's Languages), for the question and the summary.</summary>
    public static string LanguageName(string culture) =>
        Config.Current.Languages.FirstOrDefault(l => Core.Language.Same(l.Culture, Core.Language.Of(culture)))?.Name ?? culture;

    /// <summary>
    /// The turn the site plan is drawn with where the site lands at $target, for $view, the plan it stands in
    /// (SiteplanDxf.Write's turnDeg): with Settings' "paper straight" its frame, texts and legend lie straight in that
    /// view, the lines turn: in Project North the site's angle to true north (the drawing turned, imported unturned); in
    /// True North 0 (north up, imported turned, which Revit shows straight there). Without it 0: the whole plan turns as
    /// one. Export always draws north up.
    /// </summary>
    public static double TurnFor(ProjectOptions options, Placement target, View view) =>
        !options.SiteplanPaperStraight || IsTrueNorth(view) ? 0 : target.AngleToTrueNorth;

    /// <summary>Whether a plan view is set to True North (its Orientation).</summary>
    public static bool IsTrueNorth(View view) =>
        view.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH)?.AsInteger() == ViewNorthCommand.TrueNorth;

    /// <summary>The site plan DXF from what the model keeps ($json: the surroundings Load from Address fetched, $grid:
    /// the terrain), the same drawing Load from Address makes with $options, turned $turnDeg (TurnFor) for the site
    /// at $target: with the paper straight its frame big enough for the lot both north up and turned the site's angle
    /// to true north, so it keeps its frame and scale when its view switches between True and Project North; without,
    /// round the lot north up.</summary>
    public static string Draw(Document doc, ProjectOptions options, string json, TerrainGrid? grid, string address, double turnDeg,
        Placement target)
    {
        var surroundings = Surroundings.Parse(json);
        return surroundings.SitePlan(grid, address, Core.Language.Current, Math.Min(options.SiteplanMarginM, surroundings.Margin), options.SetbackM,
            options.HiddenLayerList, options.RoofLabels, Placement.Origin(doc), turnDeg, options.SiteplanPaperStraight ? target.AngleToTrueNorth : 0);
    }

    // As Load from Address: the DXF from a file of its own in the temp folder, imported at $target (the import's
    // 0,0 and north; $turnDeg: what the drawing was turned), the file deleted once imported.
    private static ImportInstance ImportDrawing(Document doc, ViewPlan view, string dxf, Placement target, double turnDeg)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Jega", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, AddressCommand.ImportName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, dxf);
        try
        {
            return AddressCommand.Import(doc, view, path, target, turnDeg);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Inside a transaction (Refresh after Remember, True / Project North): the site plan drawn again when its
    /// drawing's turn no longer fits the view it stands in (Unfitting): the working direction changed (Rotate Project
    /// North or Work Direction took the import along, so its texts lie turned), or the view's orientation did. Nothing is
    /// fetched; the site's place and north are kept, so the frame and the terrain stay. Returns a line for the receipt,
    /// or null when there was nothing to do, or it couldn't be drawn (logged; the command goes on).
    /// </summary>
    public static string? Straighten(Document doc, ProjectOptions options)
    {
        if (Placement.Siteplan(doc) is not { } old || Unfitting(doc, options, old) is not var (view, target, wanted)
            || ProjectStore.ReadSurroundings(doc) is not { } json)
        {
            return null;
        }
        var drawn = ProjectStore.ReadDrawnTurn(old);
        try
        {
            var grid = StoredTerrain.TryGet(doc, out _, out var g, out _) ? g : null;
            var address = ProjectStore.ReadSite(doc)?.Address ?? doc.Title;
            var was = Trace(doc, old);
            var made = ImportDrawing(doc, view, Draw(doc, options, json, grid, address, wanted, target), target, wanted);
            JegaLog.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Straightened in \"{view.Name}\" ({ViewNorth(view)}): drawn {drawn:0.###}° → {wanted:0.###}°; old {was}; new {Trace(doc, made)}"));
            return Strings.Refresh_Straightened;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or IOException or UnauthorizedAccessException
                                      or Autodesk.Revit.Exceptions.ApplicationException)
        {
            JegaLog.Write(e);
            return null;
        }
    }

    /// <summary>
    /// The site plan $old's view, the site's place and the turn its drawing should have for that view (TurnFor), when
    /// that isn't the turn it was drawn with (by 0.01° or more); null when it fits, or when it couldn't be drawn again
    /// anyway (no site plan imported in Settings, no surroundings kept, no plan to draw in). Straighten draws it again;
    /// Placement.Stale counts it, so the next command that places by the frame Refreshes first.
    /// </summary>
    internal static (ViewPlan View, Placement Target, double Turn)? Unfitting(Document doc, ProjectOptions options, ImportInstance old)
    {
        if (!options.ImportSiteplan || ProjectStore.ReadSurroundings(doc) is null || View(doc, old) is not { } view)
        {
            return null;
        }
        var target = Placement.At(Placement.AddressPointOf(old), Placement.NorthOf(old));
        var wanted = TurnFor(options, target, view);
        return Math.Abs(Angles.TurnDegrees(Angles.ToRadians(wanted), Angles.ToRadians(ProjectStore.ReadDrawnTurn(old)))) < 0.01
            ? null : (view, target, wanted);
    }

    /// <summary>
    /// Inside a transaction, after the new options are written and their language spoken (ProjectLanguage).
    /// $language: the language changed; $layers: the layers or the roof levels did (drawn with options.HiddenLayers left
    /// out and options.RoofLabels, as always). Returns the lines for the summary, one for each that changed.
    /// </summary>
    public static IReadOnlyList<string> Run(Document doc, ProjectOptions options, bool language, bool layers)
    {
        if (Placement.Siteplan(doc) is not { } old || ProjectStore.ReadSurroundings(doc) is not { } json)
        {
            return [Strings.Redraw_Nothing];
        }
        if (View(doc, old) is not { } view)
        {
            return [Strings.Redraw_NoView];
        }
        var grid = StoredTerrain.TryGet(doc, out _, out var g, out _) ? g : null;
        var address = ProjectStore.ReadSite(doc)?.Address ?? doc.Title;
        // Where the old one is: its 0,0 and its north (its drawing's turn included); drawn as the new options say, for
        // the view it stands in.
        var target = Placement.At(Placement.AddressPointOf(old), Placement.NorthOf(old));
        var turn = TurnFor(options, target, view);
        var was = Trace(doc, old);
        var made = ImportDrawing(doc, view, Draw(doc, options, json, grid, address, turn, target), target, turn);
        // To jega.log: the old import, the target and the new one, to find a redraw that comes out turned (in a True
        // North view, from true north to project north).
        JegaLog.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Redraw in \"{view.Name}\" ({ViewNorth(view)}): old {was}; target {target.AngleToTrueNorth:0.###}°, drawn {turn:0.###}°; new {Trace(doc, made)}; true north {Placement.FromProject(doc).AngleToTrueNorth:0.###}°"));
        if (!language)
        {
            return [Strings.Redraw_DoneLayers];
        }
        var done = Fill(Strings.Redraw_DoneLanguage, LanguageName(options.Language));
        return layers ? [done, Strings.Redraw_DoneLayers] : [done];
    }

    /// <summary>
    /// Inside a transaction (Refresh): a site plan that was deleted drawn again from what the model keeps, with no
    /// import left to line up to — put on the frame where it last stood (ProjectStore.ReadFrame) instead, in a floor
    /// plan (the active one if it is). Nothing is fetched. Returns false when there is nothing to draw from (no
    /// surroundings or frame), no plan to draw in, or the redraw itself failed (logged): Refresh carries on with the
    /// terrain either way, and says a redraw was asked for but couldn't happen.
    /// </summary>
    public static bool FromFrame(Document doc, ProjectOptions options)
    {
        if (ProjectStore.ReadSurroundings(doc) is not { } json || ProjectStore.ReadFrame(doc) is not { } frame
            || PlanView(doc) is not { } view)
        {
            return false;
        }
        try
        {
            var grid = StoredTerrain.TryGet(doc, out _, out var g, out _) ? g : null;
            var address = ProjectStore.ReadSite(doc)?.Address ?? doc.Title;
            // Where it stood: the frame (the site's 0,0 and its turn as of the last Refresh or Load), the place the
            // deleted import had and the terrain still holds — so it comes back lined up with everything else. With real
            // coordinates: by them, which Refresh then lines the rest up to; the frame may be stale (an Undo).
            var target = options.SiteAnchor == ProjectOptions.AnchorShared && Placement.Shared(doc) is { } byCoordinates ? byCoordinates
                : Placement.At(new XYZ(frame.X, frame.Y, 0), frame.Angle);
            var turn = TurnFor(options, target, view);
            ImportDrawing(doc, view, Draw(doc, options, json, grid, address, turn, target), target, turn);
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or IOException or UnauthorizedAccessException
                                      or Autodesk.Revit.Exceptions.ApplicationException)
        {
            // A redraw that won't go (surroundings that won't read, Revit refusing the import — its own exceptions are
            // ApplicationException, not System) must not topple the whole Refresh: the terrain and the rest still
            // update, as SettingsCommand's redraw does too. RefreshCommand reports it couldn't be drawn.
            JegaLog.Write(e);
            return false;
        }
    }

    /// <summary>An import's 0,0 (mm) and turn (degrees), for jega.log; with its basic transform's turn
    /// (GetTransform) beside the total one, to find a redraw in True North that shows turned while the total reads right.</summary>
    internal static string Trace(Document doc, ImportInstance import) =>
        Placement.AddressPointOf(import) is var at
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{Units.MmFromFeet(at.X):0},{Units.MmFromFeet(at.Y):0} mm, {Angles.ToDegrees(Transforms.NorthAngle(import.GetTotalTransform())):0.###}° " +
                $"(basic {Angles.ToDegrees(Transforms.NorthAngle(import.GetTransform())):0.###}°, view-specific {import.ViewSpecific})")
            : "";

    /// <summary>A plan view's orientation, for jega.log.</summary>
    internal static string ViewNorth(View view) => view.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH)?.AsInteger() is { } north
        ? north == ViewNorthCommand.TrueNorth ? "True North" : "Project North" : "no plan";

    // A floor plan to draw a deleted site plan in (no old import to go by): the active one if it is a plan on a
    // level, else any floor plan.
    private static ViewPlan? PlanView(Document doc)
    {
        if (doc.ActiveView is ViewPlan { GenLevel: not null } active)
        {
            return active;
        }
        return new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel is not null);
    }

    // The plan to import in, read before the old import is deleted: the old one's own view if it shows in one only
    // (Settings' "Only in that view"; not wherever Settings was opened from), else a floor plan on its level (the
    // active one if it is), else the active plan, else any floor plan. Also the view its turn is for (TurnFor).
    internal static ViewPlan? View(Document doc, ImportInstance old)
    {
        if (old.ViewSpecific && doc.GetElement(old.OwnerViewId) is ViewPlan { GenLevel: not null } owner)
        {
            return owner;
        }
        var active = doc.ActiveView is ViewPlan { GenLevel: not null } a ? a : null;
        if (active?.GenLevel.Id == old.LevelId)
        {
            return active;
        }
        var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel is not null).ToList();
        return plans.FirstOrDefault(v => v.GenLevel.Id == old.LevelId) ?? active ?? plans.FirstOrDefault();
    }
}
