using System.Windows.Media;
using Autodesk.Revit.UI;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>
/// The Jega tab, its panels in the order of the work: Site (fetch it, keep it lined up, the work direction and the
/// view's north), Model (what's made from it), Terrain Elevations (Terrain Line, alone), Jega (the add-in itself). Load Terrain CSV File, rarely used, is
/// tucked away under Load from Address (a split button). Names and tooltips are in Strings (Resources/Strings.resx), in
/// the active project's language (ProjectLanguage), said again when it changes (Relabel).
/// </summary>
public class App : IExternalApplication
{
    // Each panel and button with the texts it shows, so a change of language can say them again.
    private static readonly List<(RibbonPanel Panel, Func<string> Title)> Panels = [];
    private static readonly List<(RibbonItem Item, Func<string> Text, Func<string>? Tip, Func<string>? More)> Items = [];

    // A failure here (the config, the ribbon) goes to the log, and Revit says the add-in didn't load; never out into
    // Revit's own startup.
    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            return Startup(app);
        }
        catch (Exception ex)
        {
            JegaLog.Write(ex);
            return Result.Failed;
        }
    }

    private static Result Startup(UIControlledApplication app)
    {
        // The default language until a project is open; then the active project's (ViewActivated below).
        ProjectLanguage.Use(Core.Config.Current.DefaultLanguage, ribbon: false);
        JegaLog.Revit = $"{app.ControlledApplication.SubVersionNumber} ({app.ControlledApplication.VersionBuild})";

        const string tab = "Jega";
        app.CreateRibbonTab(tab);
        var assembly = typeof(App).Assembly.Location;
        RibbonPanel Panel(Func<string> title)
        {
            var panel = app.CreateRibbonPanel(tab, title());
            Panels.Add((panel, title));
            return panel;
        }
        void Button<T>(Func<PushButtonData, RibbonItem> into, string name, Func<string> text, Func<string> tip,
            Func<int, ImageSource> icon, Func<string>? more = null) =>
            Items.Add((into(Data<T>(name, text, tip, icon, more)), text, tip, more));
        PushButtonData Data<T>(string name, Func<string> text, Func<string> tip, Func<int, ImageSource> icon, Func<string>? more = null)
        {
            var data = new PushButtonData(name, text(), assembly, typeof(T).FullName) { ToolTip = tip(), LargeImage = icon(32), Image = icon(16) };
            // Greyed out but in a project (ProjectOnly; WritableProject, the same for now: see there); About is there
            // always, with or without one.
            var availability = typeof(T) == typeof(AboutCommand) ? typeof(Always)
                : typeof(T) == typeof(ExportCommand) ? typeof(ProjectOnly)
                : typeof(T) == typeof(ViewNorthCommand) || typeof(T) == typeof(WorkDirectionCommand) ? typeof(PlanViewOnly)
                : typeof(WritableProject);
            data.AvailabilityClassName = availability.FullName;
            if (more is not null)
            {
                data.LongDescription = more();
            }
            return data;
        }

        // Site: where every project starts, and Refresh, used all along while the site plan is turned into place.
        var site = Panel(() => Strings.Ribbon_Site);
        var load = (SplitButton)site.AddItem(new SplitButtonData("Load", Strings.Ribbon_LoadFromAddress));
        Items.Add((load, () => Strings.Ribbon_LoadFromAddress, null, null));
        load.IsSynchronizedWithCurrentItem = false;  // Load from Address stays on the button; Load Terrain CSV File is in its list
        Button<AddressCommand>(load.AddPushButton, "LoadFromAddress", () => Strings.Ribbon_LoadFromAddress, () => Strings.Ribbon_LoadFromAddressTip, px => Icons.Address(px));
        Button<UpdateTerrainCommand>(load.AddPushButton, "UpdateTerrain", () => Strings.Ribbon_LoadTerrain, () => Strings.Ribbon_LoadTerrainTip, px => Icons.UpdateTerrain(px));
        Button<RefreshCommand>(site.AddItem, "Refresh", () => Strings.Ribbon_Refresh, () => Strings.Ribbon_RefreshTip, px => Icons.Refresh(px));
        // The site plan's layers beside Refresh: Settings on its Layers tab, as close as a family's switches.
        Button<LayersCommand>(site.AddItem, "Layers", () => Strings.Ribbon_Layers, () => Strings.Ribbon_LayersTip, px => Icons.Layers(px));

        // True north comes of the shared coordinates, so no panel turns it. Beside Layers two small ones stacked:
        // the working direction (Revit's Rotate Project North, the whole model turned while the site plan stays on its
        // coordinates) and the active plan view between True North and Project North (both greyed outside plan views).
        (PushButtonData Data, Func<string> Text, Func<string> Tip)[] small =
        [
            (Data<WorkDirectionCommand>("WorkDirection", () => Strings.Ribbon_WorkDirection, () => Strings.Ribbon_WorkDirectionTip, px => Icons.WorkDirection(px)), () => Strings.Ribbon_WorkDirection, () => Strings.Ribbon_WorkDirectionTip),
            (Data<ViewNorthCommand>("ViewNorth", () => Strings.Ribbon_ViewNorth, () => Strings.Ribbon_ViewNorthTip, px => Icons.ViewNorth(px)), () => Strings.Ribbon_ViewNorth, () => Strings.Ribbon_ViewNorthTip),
        ];
        var stacked = site.AddStackedItems(small[0].Data, small[1].Data);
        for (var i = 0; i < stacked.Count; i++)
        {
            Items.Add((stacked[i], small[i].Text, small[i].Tip, null));
        }

        // Model: what's made from the site's lot and surroundings: the Toposolid and the lot boundary on it, the buildings
        // around it.
        var model = Panel(() => Strings.Ribbon_Model);
        Button<LotTerrainCommand>(model.AddItem, "LotTerrain", () => Strings.Ribbon_GenerateTerrain, () => Strings.Ribbon_GenerateTerrainTip, px => Icons.LotTerrain(px));
        Button<LotEdgeCommand>(model.AddItem, "LotEdge", () => Strings.Ribbon_LotBoundary, () => Strings.Ribbon_LotBoundaryTip, px => Icons.LotEdge(px));
        Button<ContextCommand>(model.AddItem, "Context", () => Strings.Ribbon_Context, () => Strings.Ribbon_ContextTip, px => Icons.Context(px));

        // Terrain Elevations: Terrain Line alone, so it stands out. Not "Annotate": it is a live tool (elevations, falls, slopes,
        // HIGH / LOW, following every change) and would read as mere labels there; the long tooltip says what it can do.
        var levels = Panel(() => Strings.Ribbon_TerrainLevels);
        Button<TerrainLineCommand>(levels.AddItem, "TerrainLine", () => Strings.Ribbon_TerrainLine, () => Strings.Ribbon_TerrainLineTip, px => Icons.TerrainLine(px),
            () => Strings.Ribbon_TerrainLineLong);

        // Jega: Settings, Export and About, all large (Export small went unseen, About in the slide-out too).
        var jega = Panel(() => Strings.Ribbon_Jega);
        Button<SettingsCommand>(jega.AddItem, "Settings", () => Strings.Ribbon_Settings, () => Strings.Ribbon_SettingsTip, px => Icons.Settings(px));
        Button<ExportCommand>(jega.AddItem, "Export", () => Strings.Ribbon_Export, () => Strings.Ribbon_ExportTip, px => Icons.Export(px));
        Button<AboutCommand>(jega.AddItem, "About", () => Strings.Ribbon_About, () => Strings.Ribbon_AboutTip, px => Icons.About(px));

        // Jega speaks the active project's language: a view of another project brings its own. Never a failure out
        // into Revit on a view switch: the language stays, and the next command applies it again.
        app.ViewActivated += (_, e) =>
        {
            try
            {
                ProjectLanguage.Apply(e.Document);
            }
            catch (Exception ex)
            {
                JegaLog.Write(ex);
            }
        };
        TerrainLineUpdater.Register(app.ActiveAddInId);
        return Result.Succeeded;
    }

    // The updater taken back, as Revit asks of an add-in; a failure goes to the log, never into Revit's shutdown.
    public Result OnShutdown(UIControlledApplication app)
    {
        try
        {
            TerrainLineUpdater.Unregister(app.ActiveAddInId);
        }
        catch (Exception ex)
        {
            JegaLog.Write(ex);
        }
        return Result.Succeeded;
    }

    /// <summary>Says every panel and button again in the language now spoken (ProjectLanguage).</summary>
    public static void Relabel()
    {
        foreach (var (panel, title) in Panels)
        {
            panel.Title = title();
        }
        foreach (var (item, text, tip, more) in Items)
        {
            item.ItemText = text();
            if (tip is not null)
            {
                item.ToolTip = tip();
            }
            if (more is not null)
            {
                item.LongDescription = more();
            }
        }
    }
}
