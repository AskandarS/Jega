using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using Hyperlink = System.Windows.Documents.Hyperlink;
using Run = System.Windows.Documents.Run;
using TextBox = System.Windows.Controls.TextBox;

namespace Jega.Revit;

/// <summary>
/// "Settings": Jega's config in one window, five tabs by what the user asks. Location: which point of the site goes
/// where in Revit (the two anchors). Elevations: what Revit's 0 is, the terrain lines. Site plan &amp; terrain: the DXF
/// Load from Address makes, the Toposolid and its Terrain level, the neighbouring buildings. Layers: which of the
/// site plan's layers are drawn. Data access: the user's Datafordeler key, protected
/// (Secret), with Test; under Advanced, Adressevælger's token (its own file on this PC, never in the project, as a
/// .rvt gets sent on). The project's settings are kept in the model, so a colleague gets the same. OK saves and, when
/// something of the project's changed, Refreshes, so what's built follows at once: one Undo for both. Every text is in
/// Strings (Resources/Strings.resx), ready for a translation.
/// </summary>
[Transaction(TransactionMode.Manual)]
public partial class SettingsCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_Settings;

    /// <summary>Opens on the Layers tab (LayersCommand, the ribbon's Layers); else on the first, or Data access without a key.</summary>
    protected virtual bool OnLayers => false;

    // The window's terrain points (TerrainPoints) and the site plan's status are read from the frame.
    protected override bool UsesFrame => true;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        var stored = ProjectStore.Read(doc);
        var (dvr90, fromSurvey) = Dvr90.AtRevitZero(doc);
        // The floor's elevation goes in the Survey Point's (the height georeferenced too: Revit's elevations from
        // the Survey Point show DVR90, from the Project Base Point the height above the floor), unless it is kept in Jega
        // (ProjectOptions.FloorDvr90Mm): with Manual georeference (the coordinates aren't Jega's), or asked at OK when
        // links would move up or down (FloorInJega).
        var floorInJega = !fromSurvey || ProjectStore.ReadOptions(doc).SiteAnchor != ProjectOptions.AnchorShared;
        var machine = MachineOptions.Load(out var machineProblem);
        var options = ProjectStore.ReadOptions(doc, out var optionsUnreadable);
        // Shown in the window, on top: it shows the defaults, and OK saves them over what couldn't be read.
        var warnings = new List<string>();
        if (optionsUnreadable)
        {
            warnings.Add(Strings.Settings_WarnOptionsUnreadable);
        }
        if (machineProblem is not null)
        {
            warnings.Add(Fill(Strings.Settings_WarnMachineUnreadable, machineProblem));
        }
        else if (!Secret.TryUnprotect(machine.DatafordelerKeyProtected, out _))
        {
            warnings.Add(Strings.Settings_WarnKeyUnreadable);
        }
        var keyMissing = string.IsNullOrWhiteSpace(Secret.Unprotect(machine.DatafordelerKeyProtected));
        // The cross's size as it is now: the family's Size (Edit Type may have changed it), else the stored one.
        var crossMm = CrossSymbol.SizeMm(doc);
        // The same for the labels' text: the type's Text Size, to the 2 decimals the field shows (else an odd size
        // such as 3/32" would count as changed at every OK).
        var textMm = Math.Round(TerrainLines.TextSizeMm(doc), 2);
        // The layers in the form the window writes them, so an untouched OK isn't a change (and a redraw).
        var start = options.WithLayersAsSettingsWrites() with { LevelCrossMm = crossMm, LabelTextMm = textMm };
        // "Terrain point on a level": the terrain's points and the project's levels (not Jega's Terrain level, which moves
        // with the terrain), lowest first.
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .Where(l => !ProjectStore.Is(l, LotTerrainCommand.TerrainLevelRole))
            .OrderBy(l => l.ProjectElevation)
            .Select(l => new LevelChoice(l.UniqueId, l.Name, Units.MmFromFeet(l.ProjectElevation))).ToList();
        var window = new SettingsWindow(start, dvr90, floorInJega, Dvr90.SurveyPointMm(doc), stored?.LastOffsetMm, Placement.FromProject(doc).AngleToTrueNorth, machine,
            ProjectStore.ReadSite(doc)?.HouseGroundMm, Placement.Status(doc), warnings, keyMissing,
            Placement.GeoreferencePreview(doc), LotTerrainCommand.TerrainPoints(doc), levels, OnLayers);
        new WindowInteropHelper(window) { Owner = data.Application.MainWindowHandle };
        if (window.ShowDialog() != true)
        {
            return Result.Cancelled;
        }
        // What's built follows only when something of the project's changed: the key alone needs no Refresh.
        var projectChanged = window.Options != start || Math.Abs(window.Dvr90Mm - dvr90) >= 1 || (window.OffsetMm is { } o && o != stored?.LastOffsetMm);

        // Another language changes the site plan's texts: asked first whether to draw it again (Cancel: nothing is
        // saved). Other layers: drawn again unasked, as that is what the change is for, and in the new language too, if
        // that changed as well.
        // What the drawing is drawn with: its layers, roof levels, setback line and margin round the lot (the legend and
        // notes follow), all drawn again from what the model keeps, nothing fetched.
        var layers = window.Options.HiddenLayers != start.HiddenLayers || window.Options.RoofLabels != start.RoofLabels
            || window.Options.SetbackM != start.SetbackM || window.Options.SiteplanMarginM != start.SiteplanMarginM
            || window.Options.SiteplanPaperStraight != start.SiteplanPaperStraight;
        var redraw = layers && SiteplanRedraw.Possible(doc);
        // Only what the drawing is drawn with changed (Layers): drawing the import again is all there is to do, so no
        // Refresh after it, and a layer can't make the Toposolid again or move the site.
        var drawingOnly = layers && Math.Abs(window.Dvr90Mm - dvr90) < 1 && (window.OffsetMm is not { } changed || changed == stored?.LastOffsetMm)
            && window.Options with
            {
                HiddenLayers = start.HiddenLayers, RoofMinAreaM2 = start.RoofMinAreaM2, RoofRidgeOnly = start.RoofRidgeOnly,
                RoofSkipCrowded = start.RoofSkipCrowded, SetbackM = start.SetbackM, SiteplanMarginM = start.SiteplanMarginM,
                SiteplanPaperStraight = start.SiteplanPaperStraight,
            } == start;
        var language = Core.Language.Of(window.Options.Language) != Core.Language.Of(options.Language);
        if (language && !redraw && SiteplanRedraw.Possible(doc))
        {
            if (AskRedraw(window.Options) is not { } answer)
            {
                return Result.Cancelled;
            }
            redraw = answer;
        }

        // Back to real coordinates from Manual georeference, in a project whose coordinates aren't this site's (another
        // address's, after a far one was loaded with Manual georeference; a template's; a surveyor's): asked as Load from
        // Address asks, follow them or set Jega's (Cancel: nothing is saved). Where they don't fit, set Jega's only:
        // following them would be Manual georeference again.
        var setCoordinates = false;
        if (options.SiteAnchor != ProjectOptions.AnchorShared && window.Options.SiteAnchor == ProjectOptions.AnchorShared
            && ProjectStore.ReadFrame(doc) is not null && Placement.Origin(doc) is not null
            && Placement.HasCoordinates(doc) && !Placement.OnOwnCoordinates(doc))
        {
            if (AddressCommand.ExistingCoordinates(Placement.Moves(doc, Placement.Origin(doc)!.Value), Placement.HasLinks(doc), Strings.Settings_CancelLosesAll, orManual: false) is not { } set)
            {
                return Result.Cancelled;
            }
            setCoordinates = set;
        }

        // Leaving real coordinates that Jega gave the project: asked whether to put its own from before back (Cancel:
        // nothing is saved). Real ones it had already (a surveyor's) are never touched, so nothing to ask.
        var putBack = false;
        if (options.SiteAnchor == ProjectOptions.AnchorShared && window.Options.SiteAnchor != ProjectOptions.AnchorShared
            && ProjectStore.ReadCoordinatesBefore(doc) is not null)
        {
            if (AskPutBack(doc, ProjectStore.ReadCoordinatesBefore(doc)!.Value) is not { } back)
            {
                return Result.Cancelled;
            }
            putBack = back;
        }

        // Revit's 0 at another height: in the Survey Point's elevation, unless Manual georeference keeps the project's
        // coordinates as they are, or links placed by them would move up or down and the user keeps it in Jega instead.
        var newDvr90 = Math.Abs(window.Dvr90Mm - dvr90) >= 1;
        var inJega = window.Options.SiteAnchor != ProjectOptions.AnchorShared;
        if (newDvr90 && !inJega && Placement.HasLinks(doc))
        {
            if (AskFloorWithLinks(window.Dvr90Mm - Dvr90.SurveyPointMm(doc)) is not { } kept)
            {
                return Result.Cancelled;
            }
            inJega = kept;
        }

        var notes = new List<string>();
        // What failed and needs the user: first in the receipt, with the warning icon.
        var problems = new List<string>();
        // The chosen language from here on, so all OK says and writes (labels, receipts, the ribbon) is in it; back to
        // the one spoken before unless the settings are saved (Keep below).
        using var spoken = ProjectLanguage.Try(window.Options.Language);
        // The settings on this PC: their own file, not the project (and not undone with it).
        if (window.Machine != machine)
        {
            try
            {
                window.Machine.Save();
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                problems.Add(Fill(Strings.Settings_MachineSaveFailed, MachineOptions.DefaultPath, ex.Message));
            }
        }
        if (!projectChanged)
        {
            ShowSaved(problems, notes, null);
            return Result.Succeeded;
        }
        // The cross at another size: a parametric one just gets its Size (below); a fixed one (or none yet) is made
        // again at it now, outside the transaction (a family document can't be made inside one), and loaded below.
        var newCross = Math.Abs(window.Options.LevelCrossMm - crossMm) > 1e-6;
        var newText = Math.Abs(window.Options.LabelTextMm - textMm) > 1e-6;
        var parametric = CrossSymbol.Parametric(doc);
        if (newCross && !parametric && CrossSymbol.Prepare(doc, window.Options.LevelCrossMm) is { } crossNote)
        {
            problems.Add(crossNote);
        }
        // One Undo for OK: the settings and the Refresh that applies them, so an Undo never leaves the settings saved and
        // what's built as before (the terrain moved back with Relative elevations still ticked).
        using var group = new TransactionGroup(doc, Strings.Undo_Settings);
        group.Start();
        using (var t = new Transaction(doc, Strings.Undo_Settings))
        {
            t.Start();
            // In Jega: the floor's elevation stored; in the Survey Point: none stored, so Dvr90 reads the Survey Point.
            ProjectStore.WriteOptions(doc, !newDvr90 ? window.Options
                : window.Options with { FloorDvr90Mm = inJega ? window.Dvr90Mm : null });
            // The site plan in its new language or layers first, where the old one was; then onto the anchor below, as any.
            if (redraw)
            {
                try
                {
                    notes.AddRange(SiteplanRedraw.Run(doc, window.Options, language, layers));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or System.IO.IOException
                                               or FormatException)
                {
                    problems.Add(Fill(Strings.Redraw_Failed, ex.Message));
                }
            }
            else if (layers)
            {
                // No import to draw again (none, or exploded): said, as OK changes nothing to be seen.
                notes.Add(Strings.Layers_Later);
            }
            // An anchor changed: the site plan (and the site with it) moves there now.
            if (window.Options.SiteAnchor != options.SiteAnchor && ProjectStore.ReadFrame(doc) is not null)
            {
                MoveToNewAnchor(doc, options, window.Options, putBack, setCoordinates, notes);
            }
            if (newCross && parametric)
            {
                CrossSymbol.SetSizeMm(doc, window.Options.LevelCrossMm);
            }
            else if (newCross && CrossSymbol.Load(doc, window.Options.LevelCrossMm) is { } crossLoad)
            {
                problems.Add(crossLoad);
            }
            if (newText)
            {
                TerrainLines.SetTextSizeMm(doc, window.Options.LabelTextMm);
            }
            if (newCross || newText)
            {
                // A fixed cross loaded again took its crosses with it; the texts' distances from the line follow their size.
                TerrainLines.Update(doc);
            }
            if (newDvr90)
            {
                SetRevitZero(doc, window.Dvr90Mm, inJega, window.Options, levels, notes);
            }
            if (window.OffsetMm is { } offset && ProjectStore.WriteOffset(doc, offset) is { } labelProblem)
            {
                problems.Add(labelProblem);
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                group.RollBack();
                spoken.Dispose();  // not saved: the language spoken before already, so this receipt is in it
                Receipt(new ReceiptText(Strings.Settings_NotSaved) { Warning = true });
                return Result.Failed;
            }
        }
        // A redrawn import once committed, for jega.log: Revit could turn it after the transaction (SiteplanRedraw's note
        // has it before).
        if (redraw && Placement.Siteplan(doc) is { } redrawn)
        {
            JegaLog.Note($"Redraw committed: {SiteplanRedraw.Trace(doc, redrawn)}");
        }
        spoken.Keep();
        // OK applies: Refresh now, in the same Undo. No terrain yet, or only the drawing changed: saved, nothing more to
        // apply.
        ReceiptText? applied = null;
        if (!drawingOnly && StoredTerrain.TryGet(doc, out _, out _, out _))
        {
            // Refresh cancelled (its question about a deleted site plan): the settings stay saved, said not applied.
            var (refreshed, receipt) = RefreshCommand.Run(doc);
            applied = receipt is null ? new ReceiptText(Fill(Strings.Settings_NotApplied, Strings.Refresh_Cancelled)) { Warning = true }
                : refreshed == Result.Succeeded ? receipt : receipt with { Result = Fill(Strings.Settings_NotApplied, receipt.Result), Warning = true };
        }
        group.Assimilate();
        ShowSaved(problems, notes, applied);
        return Result.Succeeded;
    }

    // The language changed: draw the site plan again? True, false, or null for Cancel (nothing saved).
    private static bool? AskRedraw(ProjectOptions chosen)
    {
        var name = SiteplanRedraw.LanguageName(chosen.Language);
        var ask = new TaskDialog(Ui.Title)
        {
            MainInstruction = Fill(Strings.Redraw_AskLanguage, name),
            MainContent = Strings.Redraw_AskLanguageContent,
            CommonButtons = TaskDialogCommonButtons.Cancel,
            FooterText = Strings.Settings_CancelLosesAll,
        };
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Redraw_Yes, Fill(Strings.Redraw_YesNoteLanguage, name));
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Redraw_No, Strings.Redraw_NoNoteLanguage);
        return ask.Show() switch
        {
            TaskDialogResult.CommandLink1 => true,
            TaskDialogResult.CommandLink2 => false,
            _ => null,
        };
    }

    // Revit's 0 at another height in a project with links: raising the Survey Point moves those placed by the shared
    // coordinates $deltaMm up (or down). True = keep it in Jega instead, false = move them, null = Cancel (nothing saved).
    private static bool? AskFloorWithLinks(double deltaMm)
    {
        var ask = new TaskDialog(Ui.Title)
        {
            MainInstruction = Strings.Levels_LinksAsk,
            MainContent = Fill(Strings.Levels_LinksAskContent, Units.Level(Math.Abs(deltaMm)), deltaMm > 0 ? Strings.Levels_LinksDown : Strings.Levels_LinksUp),
            CommonButtons = TaskDialogCommonButtons.Cancel,
            FooterText = Strings.Settings_CancelLosesAll,
        };
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Levels_LinksSurvey, Strings.Levels_LinksSurveyNote);
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Levels_LinksJega, Strings.Levels_LinksJegaNote);
        return ask.Show() switch
        {
            TaskDialogResult.CommandLink1 => false,
            TaskDialogResult.CommandLink2 => true,
            _ => null,
        };
    }

    // Leaving real coordinates Jega gave the project: put its own from before back? True, false, or null for Cancel.
    // Leaving real coordinates Jega set over the project's own ($before): true = put those back, false = keep the
    // address's, null = cancel. Said in numbers: how far the project's coordinates move back and turn (at Revit's 0),
    // which links placed by them follow.
    private static bool? AskPutBack(Document doc, (double EastWest, double NorthSouth, double Angle) before)
    {
        var now = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        var (de, dn) = (before.EastWest - now.EastWest, before.NorthSouth - now.NorthSouth);
        var metres = Units.MmFromFeet(Math.Sqrt(de * de + dn * dn)) / 1000;
        var degrees = Angles.TurnDegrees(before.Angle, now.Angle);
        // What shows as 0 left out, as Ui.Movement: "ligger 10,00 m derfra", not "… og er drejet 0,0°".
        var turned = Math.Abs(degrees) >= 0.05;
        var content = Math.Abs(metres) >= 0.005 || !turned
            ? Fill(turned ? Strings.Coordinates_AskContent : Strings.Coordinates_AskContentMoved, metres, degrees)
            : Fill(Strings.Coordinates_AskContentTurned, degrees);
        var ask = new TaskDialog(Ui.Title)
        {
            MainInstruction = Strings.Coordinates_Ask, MainContent = content,
            CommonButtons = TaskDialogCommonButtons.Cancel, FooterText = Strings.Settings_CancelLosesAll,
        };
        var links = Placement.HasLinks(doc) && Ui.Movement(metres, degrees) is { } go ? $" {Fill(Strings.Coordinates_BackLinks, go)}" : "";
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Coordinates_Back, Strings.Coordinates_BackNote + links);
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Coordinates_Leave, Strings.Coordinates_LeaveNote);
        return ask.Show() switch
        {
            TaskDialogResult.CommandLink1 => true,
            TaskDialogResult.CommandLink2 => false,
            _ => null,
        };
    }

    // Inside the settings' transaction: an anchor changed, so the site plan (and the site with it) moves there now.
    // $options: the settings before; $chosen: the window's; $setCoordinates: Jega's set over the project's own (asked).
    private static void MoveToNewAnchor(Document doc, ProjectOptions options, ProjectOptions chosen, bool putBack, bool setCoordinates, List<string> notes)
    {
        // Real coordinates in a project without them, or Jega's set over its own: given from the site, so it has
        // somewhere to land: on the Project Base Point (fresh), as at Load from Address, where the origin always is.
        if (chosen.SiteAnchor == ProjectOptions.AnchorShared && Placement.Georeference(doc, fresh: true, force: setCoordinates) is { } real)
        {
            notes.Add(real);
        }
        // Leaving them: the project's own from before back first (true north with them), so the site lands by those.
        if (putBack && ProjectStore.ReadCoordinatesBefore(doc) is { } own)
        {
            var location = doc.ActiveProjectLocation;
            var now = location.GetProjectPosition(XYZ.Zero);
            location.SetProjectPosition(XYZ.Zero, new ProjectPosition(own.EastWest, own.NorthSouth, now.Elevation, own.Angle));
            ProjectStore.WriteCoordinatesBefore(doc, null);
            doc.Regenerate();
            notes.Add(Strings.Location_CoordinatesPutBack);
        }
        // Left as they are: the project's own from here on, so a surveyor's acquired later isn't taken for Jega's.
        else if (!putBack && options.SiteAnchor == ProjectOptions.AnchorShared && chosen.SiteAnchor != ProjectOptions.AnchorShared
                 && ProjectStore.ReadCoordinatesBefore(doc) is not null)
        {
            ProjectStore.WriteCoordinatesBefore(doc, null);
        }
        // Moved, its turn kept: only the true-north buttons turn the site. Real coordinates are the exception,
        // as their north is UTM's.
        var turn = chosen.SiteAnchor == ProjectOptions.AnchorShared ? null : ProjectStore.ReadFrame(doc)?.Angle;
        // A new anchor: the site plan on it, a neighbour's offset from the old one forgotten.
        Placement.ForgetHold(doc);
        notes.Add(Placement.MoveSiteTo(doc, Placement.Landing(doc, turn), Placement.Describe(chosen)));
    }

    // Inside the settings' transaction: Revit's 0 at another DVR90 height. Kept in Jega: written with the options already.
    // Else the Survey Point's elevation: Revit 0's height in the shared coordinates, the rest kept.
    private static void SetRevitZero(Document doc, double dvr90Mm, bool floorInJega, ProjectOptions chosen, List<LevelChoice> levels, List<string> notes)
    {
        if (!floorInJega)
        {
            var location = doc.ActiveProjectLocation;
            var p = location.GetProjectPosition(XYZ.Zero);
            location.SetProjectPosition(XYZ.Zero, new ProjectPosition(p.EastWest, p.NorthSouth, Units.FeetFromMm(dvr90Mm), p.Angle));
        }
        notes.Add(levels.FirstOrDefault(l => l.Id == chosen.ZeroLevel) is { } on
            ? Fill(Strings.Settings_NowOnLevel, Dvr90.TerrainPoint(chosen.ZeroTerrainAt), on.Name, Units.Level(dvr90Mm))
            : Math.Abs(dvr90Mm) < 1 ? Strings.Settings_NowAbsolute : Fill(Strings.Settings_NowRelative, Units.Level(dvr90Mm)));
    }

    /// <summary>
    /// Settings' receipt: what the settings changed, then Refresh's ($refresh, null when it didn't run). Problems first,
    /// the settings' own before Refresh's; nothing at all when nothing is worth saying.
    /// </summary>
    private static void ShowSaved(List<string> problems, List<string> notes, ReceiptText? refresh)
    {
        List<string> all = [.. problems, .. refresh is { Warning: true } ? [refresh.Result] : Array.Empty<string>()];
        if (all.Count == 0 && notes.Count == 0 && refresh is null)
        {
            return;
        }
        var done = refresh is null ? Strings.Settings_Saved : Strings.Settings_SavedAndRefreshed;
        Receipt(new ReceiptText(all.Count > 0 ? all[0] : done)
        {
            Lines = [all.Count > 0 ? Strings.Settings_Saved : null, .. all.Skip(1), .. notes, .. refresh?.Lines ?? []],
            Steps = refresh?.Steps ?? [],
            Details = refresh?.Details ?? [],
            Warning = all.Count > 0,
        });
    }

    /// <summary>A level for "Relative to a level": its UniqueId, name and height above Revit's 0, mm.</summary>
    private sealed record LevelChoice(string Id, string Name, double Mm);
}
