using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;
using ComboBox = System.Windows.Controls.ComboBox;
using Hyperlink = System.Windows.Documents.Hyperlink;
using Run = System.Windows.Documents.Run;
using TextBox = System.Windows.Controls.TextBox;

namespace Jega.Revit;

// Settings' window, in a file of its own: the five tabs, built in code, and what OK checks before it closes.
public partial class SettingsCommand
{
    private sealed class SettingsWindow : Window
    {
        private readonly ProjectOptions _start;
        private readonly MachineOptions _machine;
        private readonly string _startKey;
        private readonly PasswordBox _key = new() { Width = 250, Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBox _token = Field(250);
        private readonly TextBlock _test = Note();
        private readonly TextBox _dvr90 = Field(), _offset = Field(), _thickness = Field(), _interior = Field(), _edge = Field(), _cross = Field(), _labelText = Field();
        private readonly TextBox _margin = Field(), _setback = Field();

        // Georeference: Jega georeferences the project (AnchorShared), unless "Manual georeference" opens the anchor
        // (where in Revit the site goes), the coordinates then left alone. The Project Base Point first, and the default:
        // once georeferenced, Revit has moved the Survey Point out to the shared 0,0, some 6,240 km off.
        private readonly CheckBox _manual = new() { Content = Wrapped(Strings.Location_Manual) };
        private readonly ComboBox _anchor = Choice((ProjectOptions.AnchorBase, Strings.Location_AnchorBase), (ProjectOptions.AnchorSurvey, Strings.Location_AnchorSurvey),
            (ProjectOptions.AnchorOrigin, Strings.Location_AnchorOrigin));

        // Elevations: what Revit's 0 is.
        private readonly RadioButton _absolute = new() { Content = Wrapped(Strings.Levels_Absolute), GroupName = "levels" };
        private readonly RadioButton _relative = new() { Content = Wrapped(Strings.Levels_Relative), GroupName = "levels", Margin = new Thickness(0, 4, 0, 0) };
        private readonly RadioButton _onLevel = new() { Content = Wrapped(Strings.Levels_OnLevel), GroupName = "levels", Margin = new Thickness(0, 8, 0, 0) };
        // Filled in LevelsPage: only the terrain points there are, and the project's levels.
        private ComboBox _zeroAt = new(), _zeroLevel = new();
        private IReadOnlyDictionary<string, double> _terrainPoints = new Dictionary<string, double>();
        private IReadOnlyList<LevelChoice> _levels = [];
        // Revit 0's DVR90 when the window opened, and whether it was set from a terrain point on a level then. That stays
        // as it is unless the user chooses again (_touched) or presses "Use" on the amber line (_applyNow).
        private double _startDvr90Mm;
        private bool _startedOnLevel, _touched, _applyNow;

        private bool KeepOnLevel => _startedOnLevel && !_touched && !_applyNow;
        private readonly ComboBox _levelAt = Choice((ProjectOptions.LevelLowest, Strings.Levels_AtLowest), (ProjectOptions.LevelHighest, Strings.Levels_AtHighest),
            (ProjectOptions.LevelAverage, Strings.Levels_AtAverage), (ProjectOptions.LevelAddress, Strings.Levels_AtAddress), (ProjectOptions.LevelHouse, Strings.Levels_AtHouse));

        private readonly ComboBox _language = Choice(Core.Config.Current.Languages.Select(l => (l.Culture, l.Name)).ToArray());

        private readonly CheckBox _import = new() { Content = Wrapped(Strings.Plan_Import) };
        private readonly CheckBox _viewOnly = new() { Content = Wrapped(Strings.Plan_ViewOnly) };
        private readonly CheckBox _paperStraight = new() { Content = Wrapped(Strings.Plan_PaperStraight) };
        private readonly CheckBox _contextLot = new() { Content = Wrapped(Strings.Plan_ContextLot) };
        // Layers: a box per layer that can be left out (not SiteplanDxf.FixedLayers), in Config's order; ticked = drawn.
        private readonly List<(string Name, CheckBox Box)> _layers = [];
        // Under the roof levels' box: which buildings get one, and how.
        private readonly TextBox _roofArea = Field();
        private readonly RadioButton _roofRidge = new() { Content = Wrapped(Strings.Layers_RoofRidge), GroupName = "roof" };
        private readonly RadioButton _roofBoth = new() { Content = Wrapped(Strings.Layers_RoofBoth), GroupName = "roof", Margin = new Thickness(16, 0, 0, 0) };
        private readonly CheckBox _roofSkip = new() { Content = Wrapped(Strings.Layers_RoofSkip), Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock _error = Error();

        public MachineOptions Machine { get; private set; }
        public ProjectOptions Options { get; private set; }
        public double Dvr90Mm { get; private set; }
        public int? OffsetMm { get; private set; }

        // $floorInJega: relative elevations are kept in Jega, not in the Survey Point's elevation (Manual georeference, or links: SettingsCommand);
        // $surveyPointMm: that elevation, which may still hold a floor set before.
        public SettingsWindow(ProjectOptions options, double dvr90Mm, bool floorInJega, double surveyPointMm, int? offsetMm, double trueNorth, MachineOptions machine,
            double? houseGroundMm, string? siteStatus, IReadOnlyList<string> warnings, bool keyMissing, string? georeference,
            IReadOnlyDictionary<string, double> terrainPoints, IReadOnlyList<LevelChoice> levels, bool onLayers)
        {
            (_terrainPoints, _levels) = (terrainPoints, levels);
            (_start, _machine, Options, Dvr90Mm, OffsetMm, Machine) = (options, machine, options, dvr90Mm, offsetMm, machine);
            _startKey = Secret.Unprotect(machine.DatafordelerKeyProtected);
            Title = Strings.Settings_Title;
            Width = 640;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var location = LocationPage(options, trueNorth, siteStatus, georeference);
            var levelsPage = LevelsPage(options, dvr90Mm, floorInJega, surveyPointMm, offsetMm, houseGroundMm);
            var plan = PlanPage(options);
            var layers = LayersPage(options);
            var access = DataPage(machine);

            // One height for every tab, so the window doesn't jump.
            var tabs = new TabControl { Height = 360 };
            foreach (var (header, page) in new[] { (Strings.Settings_TabLocation, location), (Strings.Settings_TabLevels, levelsPage), (Strings.Settings_TabPlan, plan),
                (Strings.Settings_TabLayers, layers), (Strings.Settings_TabData, access) })
            {
                tabs.Items.Add(new TabItem { Header = header, Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            }
            // The ribbon's Layers: straight to them (the key isn't needed to draw). First run: nothing else works without
            // the key, so start there (the last tab).
            if (onLayers)
            {
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => ((ScrollViewer)t.Content).Content == layers);
            }
            else if (keyMissing)
            {
                tabs.SelectedIndex = tabs.Items.Count - 1;
            }

            // The project's language, for the whole window: top right, level with the tabs (ProjectLanguage, at OK).
            Select(_language, Core.Language.Of(options.Language));
            _language.Width = 110;
            _language.HorizontalAlignment = HorizontalAlignment.Right;
            _language.VerticalAlignment = VerticalAlignment.Top;
            _language.ToolTip = Strings.Settings_LanguageTip;
            var top = new System.Windows.Controls.Grid();
            top.Children.Add(tabs);
            top.Children.Add(_language);

            var root = new StackPanel { Margin = new Thickness(12) };
            if (warnings.Count > 0)
            {
                root.Children.Add(Banner(string.Join("\n", warnings)));
            }
            root.Children.Add(top);
            root.Children.Add(_error);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            // Another language chosen: said by OK, as the window itself stays in the one it opened in.
            var languageAtOk = Note(Strings.Settings_LanguageAtOk);
            languageAtOk.Margin = new Thickness(0, 0, 12, 0);
            languageAtOk.VerticalAlignment = VerticalAlignment.Center;
            languageAtOk.Visibility = System.Windows.Visibility.Collapsed;
            var startLanguage = Core.Language.Of(options.Language);
            _language.SelectionChanged += (_, _) => languageAtOk.Visibility = Core.Language.Same(Value(_language), startLanguage)
                ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            bar.Children.Add(languageAtOk);
            var ok = new Button { Content = Strings.Settings_Ok, Padding = new Thickness(24, 4, 24, 4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (_, _) => Accept();
            bar.Children.Add(ok);
            bar.Children.Add(new Button { Content = Strings.Settings_Cancel, Padding = new Thickness(14, 4, 14, 4), IsCancel = true });
            root.Children.Add(bar);
            Content = root;
        }

        // Location: which point of the site goes where in Revit; then the site follows the site plan import.
        // $georeference: what choosing real coordinates will do here (Placement.GeoreferencePreview), null = just move.
        private StackPanel LocationPage(ProjectOptions options, double trueNorth, string? siteStatus, string? georeference)
        {
            var page = Page(Strings.Location_About);
            // Off (the default): Jega georeferences; on, the anchors below show.
            _manual.IsChecked = options.SiteAnchor != ProjectOptions.AnchorShared;
            page.Children.Add(_manual);
            var manualNote = Note();
            page.Children.Add(manualNote);
            var anchors = new StackPanel();
            Select(_anchor, options.SiteAnchor);
            anchors.Children.Add(Row(Strings.Location_AnchorInRevit, _anchor, null, top: 8));
            anchors.Children.Add(Note(Strings.Location_AnchorInRevitNote));
            page.Children.Add(anchors);
            // Only when the site plan was moved by hand off its place; in amber what OK will do.
            if (siteStatus is not null)
            {
                page.Children.Add(Note(siteStatus, top: 8));
            }
            var change = Note();
            change.Foreground = Brushes.DarkOrange;
            page.Children.Add(change);
            // True north: from the coordinates (Jega's georeference), or the project's (Manual).
            var north = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            page.Children.Add(Row(Strings.Location_TrueNorth, north, null, top: 12));
            var howToFit = Note(Strings.Location_HowToFit, top: 10);
            page.Children.Add(howToFit);

            void Show()
            {
                var chosen = options with { SiteAnchor = Anchor() };
                var shared = chosen.SiteAnchor == ProjectOptions.AnchorShared;
                // In amber, what OK will do: move the site plan.
                change.Text = chosen.SiteAnchor != options.SiteAnchor
                    ? shared && georeference is not null ? georeference : Fill(Strings.Location_WillMove, Placement.Describe(chosen))
                    : "";
                manualNote.Text = shared ? Strings.Location_AutoNote : Strings.Location_ManualNote;
                north.Text = Fill(shared ? Strings.Location_NorthShared : Strings.Location_NorthProject, trueNorth);
                var manual = shared ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                anchors.Visibility = manual;
                howToFit.Visibility = manual;
            }
            Show();
            _manual.Checked += (_, _) => Show();
            _manual.Unchecked += (_, _) => Show();
            _anchor.SelectionChanged += (_, _) => Show();
            return page;
        }

        // Elevations: what Revit's 0 is, and the terrain lines.
        private StackPanel LevelsPage(ProjectOptions options, double dvr90Mm, bool floorInJega, double surveyPointMm, int? offsetMm, double? houseGroundMm)
        {
            var page = Page(Strings.Levels_About);
            page.Children.Add(_absolute);
            // Relative, and to its right where the floor's elevation goes: the Survey Point's Z, or Jega (links); what
            // that does, on the "?".
            var relativeRow = new StackPanel { Orientation = Orientation.Horizontal };
            relativeRow.Children.Add(_relative);
            relativeRow.Children.Add(new TextBlock
            {
                // Kept in Jega while the Survey Point still holds a floor set before: said, so its elevations aren't taken for DVR90.
                Text = !floorInJega ? Strings.Levels_RelativeSurvey
                    : Math.Abs(surveyPointMm) >= 1 ? Fill(Strings.Levels_RelativeKeptSurvey, Units.Level(surveyPointMm)) : Strings.Levels_RelativeKept,
                Foreground = Brushes.DimGray, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 4, 0, 0),
            });
            var surveyHelp = Help(floorInJega ? Strings.Levels_RelativeKeptTip : Strings.Levels_RelativeSurveyTip);
            surveyHelp.Margin = new Thickness(6, 4, 0, 0);
            relativeRow.Children.Add(surveyHelp);
            page.Children.Add(relativeRow);
            // A terrain point on a level: chosen when Settings set it last time and the level and a terrain are still
            // there; else the number it gave stays as the floor's level (relative) and the amber line says so.
            var unavailable = _terrainPoints.Count == 0 ? Strings.Levels_OnLevelNoTerrain : _levels.Count == 0 ? Strings.Levels_OnLevelNoLevels : null;
            var onLevel = options.ZeroLevel.Length > 0 && _levels.Any(l => l.Id == options.ZeroLevel) && unavailable is null;
            (_startDvr90Mm, _startedOnLevel) = (dvr90Mm, onLevel);
            var relative = Math.Abs(dvr90Mm) >= 1;
            (onLevel ? _onLevel : relative ? _relative : _absolute).IsChecked = true;
            // The level stays in the field while Absolute is chosen (greyed), so switching back doesn't lose it.
            _dvr90.Text = relative ? Units.Level(dvr90Mm) : "";
            _dvr90.ToolTip = Strings.Levels_FloorTip;
            var floorRow = Row(Strings.Levels_Floor, _dvr90, Strings.Levels_FloorUnit);
            floorRow.Margin = new Thickness(20, 6, 0, 2);
            page.Children.Add(floorRow);
            // The highest terrain along the house (Load from Address): the floor that just meets the ground. Only fills
            // the field; the user decides (e.g. adds 0.15). None without a house on the lot, or a terrain of one's own.
            var fromHouse = houseGroundMm is { } g ? Button(Fill(Strings.Levels_UseHouse, Units.Level(g))) : null;
            if (fromHouse is not null)
            {
                fromHouse.HorizontalAlignment = HorizontalAlignment.Left;
                fromHouse.Click += (_, _) => _dvr90.Text = Units.Level(houseGroundMm!.Value);
                page.Children.Add(Indented(fromHouse));
            }

            // Relative to a level: a point of the terrain on one of the project's levels; Revit's 0 worked out from them.
            page.Children.Add(_onLevel);
            _zeroAt = Choices(new (string, string)[]
                {
                    (ProjectOptions.LevelLowest, Strings.Levels_AtLowest), (ProjectOptions.LevelHighest, Strings.Levels_AtHighest),
                    (ProjectOptions.LevelAverage, Strings.Levels_AtAverage), (ProjectOptions.LevelAddress, Strings.Levels_AtAddress),
                    (ProjectOptions.LevelHouse, Strings.Levels_AtHouse),
                }
                .Where(p => _terrainPoints.ContainsKey(p.Item1)).Select(p => (p.Item1, Fill(Strings.Levels_OnLevelPoint, p.Item2, Units.Level(_terrainPoints[p.Item1])))));
            _zeroLevel = Choices(_levels.Select(l => (l.Id, Fill(Strings.Levels_OnLevelLevel, l.Name, Units.Level(l.Mm)))));
            Pick(_zeroAt, options.ZeroTerrainAt);
            // The level asked for last time, else the one nearest Revit's 0 (usually the ground floor).
            Pick(_zeroLevel, onLevel ? options.ZeroLevel : _levels.MinBy(l => Math.Abs(l.Mm))?.Id);
            var onLevelRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 6, 0, 2) };
            onLevelRow.Children.Add(_zeroAt);
            onLevelRow.Children.Add(new TextBlock { Text = Strings.Levels_OnLevelOn, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            onLevelRow.Children.Add(_zeroLevel);
            page.Children.Add(onLevelRow);
            var result = new TextBlock { Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 2) };
            page.Children.Add(result);
            // In amber: the terrain or the level moved since it was set, its terrain point is no longer there, or the
            // level or the terrain is gone. Nothing changes until the user says so: "Use" applies the new height.
            var drift = new TextBlock { Foreground = Brushes.DarkOrange, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            // The amber line on its own, so it wraps; "Use" under it.
            var driftRow = new StackPanel { Margin = new Thickness(20, 2, 0, 2) };
            var use = Button("");
            use.HorizontalAlignment = HorizontalAlignment.Left;
            use.Margin = new Thickness(0, 4, 0, 0);
            use.Click += (_, _) =>
            {
                _applyNow = true;
                Enable();
            };
            driftRow.Children.Add(drift);
            driftRow.Children.Add(use);
            page.Children.Add(driftRow);
            _onLevel.IsEnabled = unavailable is null;

            // Only with a terrain to move; in line with the line above it (without one, that line says to load it).
            if (_terrainPoints.Count > 0)
            {
                var moves = Note(Strings.Levels_MovesTerrain);
                moves.Margin = new Thickness(20, 2, 0, 2);
                page.Children.Add(moves);
            }
            void Enable()
            {
                _dvr90.IsEnabled = _relative.IsChecked == true;
                if (fromHouse is not null)
                {
                    fromHouse.IsEnabled = _dvr90.IsEnabled;
                }
                _zeroAt.IsEnabled = _zeroLevel.IsEnabled = _onLevel.IsChecked == true && unavailable is null;
                // What OK gives: the height kept, or the one worked out from the choice.
                var gives = _onLevel.IsChecked == true && KeepOnLevel ? dvr90Mm : OnLevelMm();
                result.Text = unavailable ?? (gives is { } mm
                    ? Fill(Strings.Levels_OnLevelResult, Units.Level(mm)) + " " + Strings.Levels_OnLevelNote
                    : Strings.Levels_OnLevelNote);
                drift.Text = "";
                use.Visibility = System.Windows.Visibility.Collapsed;
                if (options.ZeroLevel.Length > 0 && !onLevel)
                {
                    drift.Text = Strings.Levels_OnLevelGone;
                }
                else if (_onLevel.IsChecked == true && KeepOnLevel && !_terrainPoints.ContainsKey(options.ZeroTerrainAt))
                {
                    drift.Text = Fill(Strings.Levels_OnLevelPointGone, Dvr90.TerrainPoint(options.ZeroTerrainAt), Units.Level(dvr90Mm));
                }
                else if (_onLevel.IsChecked == true && KeepOnLevel && OnLevelMm() is { } now && Math.Abs(now - dvr90Mm) >= 1)
                {
                    drift.Text = Fill(Strings.Levels_OnLevelDrift, Units.Level(dvr90Mm), Units.Level(now));
                    use.Content = Fill(Strings.Levels_OnLevelUse, Units.Level(now));
                    use.Visibility = System.Windows.Visibility.Visible;
                }
                // Empty, an empty line's height would still stand between the lines above and below.
                driftRow.Visibility = drift.Text.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            Enable();
            _absolute.Checked += (_, _) => Enable();
            _relative.Checked += (_, _) => Enable();
            _onLevel.Checked += (_, _) => Enable();
            // Chosen again: worked out from the choice at OK (no longer the height kept).
            _zeroAt.SelectionChanged += (_, _) =>
            {
                _touched = true;
                Enable();
            };
            _zeroLevel.SelectionChanged += (_, _) =>
            {
                _touched = true;
                Enable();
            };

            page.Children.Add(Section(Strings.Levels_TerrainLines));
            _offset.Text = offsetMm?.ToString(CultureInfo.InvariantCulture) ?? "";
            _offset.IsEnabled = offsetMm is not null;
            page.Children.Add(Row(Strings.Levels_Offset, _offset, offsetMm is null ? Strings.Levels_OffsetLater : Strings.Levels_OffsetNote));
            _cross.Text = options.LevelCrossMm.ToString("0.##", Units.Numbers);
            _cross.ToolTip = Fill(Strings.Levels_CrossTip, CrossSymbol.Name);
            page.Children.Add(Row(Strings.Levels_Cross, _cross, Strings.Levels_CrossUnit));
            _labelText.Text = options.LabelTextMm.ToString("0.##", Units.Numbers);
            _labelText.ToolTip = Strings.Levels_LabelTextTip;
            page.Children.Add(Row(Strings.Levels_LabelText, _labelText, Strings.Levels_LabelTextUnit));
            return page;
        }

        // A narrower Choice for the row "[terrain point] on [level]"; empty, it stays empty (nothing to choose).
        private static ComboBox Choices(IEnumerable<(string Value, string Label)> items)
        {
            var list = items.ToArray();
            var box = list.Length > 0 ? Choice(list) : new ComboBox();
            box.Width = 220;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            return box;
        }

        // Selects $value if it's there, else leaves the first.
        private static void Pick(ComboBox box, string? value)
        {
            if (box.Items.Count > 0 && value is not null)
            {
                Select(box, value);
            }
        }

        // The DVR90 of Revit's 0 the chosen terrain point on the chosen level gives, mm; null without both.
        private double? OnLevelMm()
        {
            if (_zeroAt.SelectedItem is null || _zeroLevel.SelectedItem is null
                || !_terrainPoints.TryGetValue(Value(_zeroAt), out var terrain) || _levels.FirstOrDefault(l => l.Id == Value(_zeroLevel)) is not { } level)
            {
                return null;
            }
            return ProjectOptions.Dvr90OnLevel(terrain, level.Mm);
        }

        // Site plan & terrain: the DXF Load from Address makes, and the Toposolid.
        private StackPanel PlanPage(ProjectOptions options)
        {
            var page = Page(Strings.Plan_About);
            page.Children.Add(Section(Strings.Plan_SitePlan, first: true));
            _import.IsChecked = options.ImportSiteplan;
            page.Children.Add(_import);
            // Its note and "only in that view" under the import they belong to, a grey line down from its box.
            var importNote = Note(Strings.Plan_ImportNote);
            importNote.Margin = new Thickness(0, 0, 0, 2);
            _viewOnly.IsChecked = options.ImportViewOnly;
            _viewOnly.Margin = new Thickness(0, 4, 0, 0);
            // The paper straight in its view, only the map turning; or the whole plan turned as one. Its note under it.
            _paperStraight.IsChecked = options.SiteplanPaperStraight;
            _paperStraight.Margin = new Thickness(0, 4, 0, 0);
            var paperNote = Note(Strings.Plan_PaperStraightNote);
            paperNote.Margin = new Thickness(20, 2, 0, 2);
            _viewOnly.IsEnabled = _paperStraight.IsEnabled = _import.IsChecked == true;
            _import.Checked += (_, _) => _viewOnly.IsEnabled = _paperStraight.IsEnabled = true;
            _import.Unchecked += (_, _) => _viewOnly.IsEnabled = _paperStraight.IsEnabled = false;
            page.Children.Add(UnderBox(importNote, _viewOnly, _paperStraight, paperNote));
            _margin.Text = options.SiteplanMarginM.ToString(Units.Numbers);
            page.Children.Add(Row(Strings.Plan_Margin, _margin, Strings.Plan_MarginUnit, top: 8));
            _setback.Text = options.SetbackM.ToString(Units.Numbers);
            page.Children.Add(Row(Strings.Plan_Setback, _setback, Strings.Plan_SetbackUnit));
            // What the plan shows is switched with Revit's own Visibility/Graphics (each kind on its own DXF layer).

            page.Children.Add(Section(Strings.Plan_Toposolid));
            _thickness.Text = options.ToposolidThicknessMm.ToString(CultureInfo.InvariantCulture);
            page.Children.Add(Row(Strings.Plan_Thickness, _thickness, Strings.Plan_ThicknessUnit));
            // Where Generate Terrain puts its "Terrain" level: about the Toposolid, not Revit's 0 (the Elevations tab).
            Select(_levelAt, options.TerrainLevelAt);
            page.Children.Add(Row(Strings.Plan_TerrainLevelAt, _levelAt, null));
            page.Children.Add(Note(Strings.Plan_TerrainLevelNote));

            // Context: the lot's own buildings too (already fetched; OK's Refresh makes the masses again if shown).
            page.Children.Add(Section(Strings.Plan_Context));
            _contextLot.IsChecked = options.ContextIncludesLot;
            page.Children.Add(_contextLot);
            page.Children.Add(new TextBlock { Text = Strings.Plan_ContextLotNote, Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 2) });

            // Point spacing in mm, as building is drawn; last on the page, under the context.
            _interior.Text = options.InteriorStepMm.ToString(CultureInfo.InvariantCulture);
            _edge.Text = options.EdgeStepMm.ToString(CultureInfo.InvariantCulture);
            var advanced = new StackPanel();
            advanced.Children.Add(Row(Strings.Plan_SpacingInside, _interior, Strings.Plan_SpacingInsideUnit, top: 6));
            advanced.Children.Add(Row(Strings.Plan_SpacingEdge, _edge, Strings.Plan_SpacingEdgeUnit));
            page.Children.Add(new Expander { Header = Strings.Settings_Advanced, Content = advanced, Margin = new Thickness(0, 10, 0, 0) });
            return page;
        }

        // The layers turned off first, at the top: GeoDanmark's many small symbols, which weigh most on a large lot. Only
        // the window's order; the DXF keeps Config's.
        private static readonly string[] FirstLayers = [SiteplanLayers.Trees, SiteplanLayers.Hedges, SiteplanLayers.Manholes, SiteplanLayers.Gullies];

        // Layers: a box per layer of the site plan (Config's, FirstLayers first), the ones it can't do without ticked and
        // greyed after the rest. Each named in words and by the layer's own name, which Visibility/Graphics lists.
        private StackPanel LayersPage(ProjectOptions options)
        {
            var page = Page(Strings.Layers_About);
            var hidden = options.HiddenLayerList;
            var names = Core.Config.Current.Siteplan.Layers.Select(l => l.Name)
                .OrderBy(n => Array.IndexOf(FirstLayers, n) is var i and >= 0 ? i : FirstLayers.Length).ToList();
            foreach (var name in names.Where(n => !SiteplanDxf.FixedLayers.Contains(n)).Concat(names.Where(n => SiteplanDxf.FixedLayers.Contains(n))))
            {
                var fixedLayer = SiteplanDxf.FixedLayers.Contains(name);
                var box = new CheckBox
                {
                    Content = Wrapped(Strings.ResourceManager.GetString("Layer_" + name, Strings.Culture) ?? name),
                    IsChecked = fixedLayer || !hidden.Contains(name), IsEnabled = !fixedLayer, Margin = new Thickness(0, 2, 0, 2),
                };
                page.Children.Add(box);
                if (!fixedLayer)
                {
                    _layers.Add((name, box));
                }
                if (name == SiteplanLayers.RoofLevels)
                {
                    page.Children.Add(RoofChoices(options, box));
                }
            }
            var fixedNote = Note(Strings.Layers_FixedNote);
            fixedNote.Margin = new Thickness(0, 6, 0, 2);
            page.Children.Add(fixedNote);
            var viewNote = Note(Strings.Layers_ViewNote);
            viewNote.Margin = new Thickness(0, 2, 0, 2);
            page.Children.Add(viewNote);
            return page;
        }

        // Under the roof levels' $box, a grey line down from it: the least area, ridge / eaves or the ridge alone, and
        // whether one with no room is left out. Greyed while the layer is off. Changed, OK draws the plan again from
        // what the model keeps, as for the layers.
        private Border RoofChoices(ProjectOptions options, CheckBox box)
        {
            _roofArea.Text = options.RoofMinAreaM2.ToString(Units.Numbers);
            (options.RoofRidgeOnly ? _roofRidge : _roofBoth).IsChecked = true;
            _roofSkip.IsChecked = options.RoofSkipCrowded;
            var show = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            show.Children.Add(_roofRidge);
            show.Children.Add(_roofBoth);
            var choices = UnderBox(Row(Strings.Layers_RoofMinArea, _roofArea, Strings.Layers_RoofMinAreaUnit), Note(Strings.Layers_RoofMinAreaNote), show, _roofSkip);
            choices.IsEnabled = box.IsChecked == true;
            box.Checked += (_, _) => choices.IsEnabled = true;
            box.Unchecked += (_, _) => choices.IsEnabled = false;
            return choices;
        }

        // Data access: the keys, on this PC, never in the project (a .rvt gets sent on). The token is for the day
        // Adressevælger wants one.
        private StackPanel DataPage(MachineOptions machine)
        {
            var page = Page(Strings.Data_About);
            _key.Password = _startKey;
            var test = Button(Strings.Data_Test);
            test.Click += async (_, _) => await Test(test);
            var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
            keyRow.Children.Add(_key);
            keyRow.Children.Add(test);
            page.Children.Add(Row(Strings.Data_Key, keyRow, null));
            page.Children.Add(Note(Strings.Data_KeyHelp));
            // The first wall for a new user: the place to get a key, one click away.
            var link = new Hyperlink(new Run(Strings.Data_KeyLink));
            link.Click += (_, _) => Keys.OpenKeyPage();
            page.Children.Add(new TextBlock(link) { FontSize = 11, Margin = new Thickness(LabelWidth, 0, 0, 2) });
            page.Children.Add(_test);
            _token.Text = machine.AddressToken;
            var advanced = new StackPanel();
            advanced.Children.Add(Row(Strings.Data_Token, _token, null, top: 6));
            advanced.Children.Add(Note(Fill(Strings.Data_TokenNote, SiteFetcher.DefaultAddressToken)));
            page.Children.Add(new Expander { Header = Strings.Settings_Advanced, Content = advanced, Margin = new Thickness(0, 10, 0, 0) });
            return page;
        }

        // Where the site goes: Jega's georeference, or with "Manual georeference" the anchor chosen.
        private string Anchor() => _manual.IsChecked == true ? Value(_anchor) : ProjectOptions.AnchorShared;

        // A tab's page: its rows, with what it is about in grey on top.
        private static StackPanel Page(string about)
        {
            var page = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            page.Children.Add(new TextBlock { Text = about, Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            return page;
        }

        // What belongs to the check box above it ($items: its note, a choice under it), a grey line down from its box.
        private static Border UnderBox(params UIElement[] items)
        {
            var group = new StackPanel();
            foreach (var item in items)
            {
                group.Children.Add(item);
            }
            return new Border
            {
                BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0),
                Margin = new Thickness(6, 2, 0, 2), Child = group,
            };
        }

        // A check box's or radio button's text, wrapped to the page's width (a plain string runs out of the window).
        private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

        // A warning above the tabs (settings that couldn't be read, a key this user can't read).
        private static Border Banner(string text) => new()
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF4, 0xCE)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xB4, 0x2B)),
            BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        };

        // One small call to each service with the key and token as typed (not yet saved).
        // Awaited off Revit's thread, so Revit never freezes while the services answer (up to 30 s each).
        private async Task Test(Button button)
        {
            button.IsEnabled = false;
            _test.Text = Strings.Data_Testing;
            _test.Foreground = Brushes.DimGray;
            try
            {
                var fetcher = new SiteFetcher(_key.Password, _token.Text);
                var results = await Task.Run(fetcher.Test);
                _test.Text = string.Join("\n", results.Select(r => r.Problem is not null ? Fill(Strings.Data_TestFailed, r.Service, r.Problem)
                    : r.Via is not null ? Fill(Strings.Data_TestOkVia, r.Service, r.Via) : Fill(Strings.Data_TestOk, r.Service)));
                _test.Foreground = results.All(r => r.Problem is null) ? Brushes.DarkGreen : Brushes.Firebrick;
            }
            catch (Exception e)
            {
                _test.Text = Fill(Strings.Data_TestError, e.GetType().Name, e.Message);
                _test.Foreground = Brushes.Firebrick;
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void Accept()
        {
            var problems = new List<string>();
            // $name: the field's label, so the user finds it.
            int Int(TextBox box, string name)
            {
                if (int.TryParse(box.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                {
                    return n;
                }
                problems.Add(Fill(Strings.Check_WholeNumber, name));
                return 0;
            }
            double Number(TextBox box, string name, string? problem = null)
            {
                // Either mark as the decimal one (2,5 or 2.5 in both languages), but not the other language's mark
                // before three last digits: 1,000 in English or 1.000 in Danish is a thousand to whoever typed it, not 1.
                var text = box.Text.Trim();
                var other = Units.Numbers.NumberFormat.NumberDecimalSeparator == "," ? '.' : ',';
                var at = text.IndexOf(other);
                var thousands = at >= 0 && text.Length - at == 4 && text[(at + 1)..].All(char.IsDigit);
                if (!thousands && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    return v;
                }
                problems.Add(problem ?? Fill(Strings.Check_Number, name));
                return 0;
            }
            // The field as shown (2 decimals) keeps the exact height it was shown from: a level set from the terrain
            // (whole mm) isn't moved a few mm by an OK that didn't touch it.
            var dvr90Mm = _relative.IsChecked != true ? 0
                : Math.Abs(_startDvr90Mm) >= 1 && _dvr90.Text.Trim() == Units.Level(_startDvr90Mm) ? _startDvr90Mm
                : Number(_dvr90, Strings.Levels_Floor, Strings.Levels_FloorNumber) * 1000;
            // 0 m DVR90 would be absolute levels again, and Relative gone at the next opening: said here instead.
            if (_relative.IsChecked == true && _dvr90.Text.Trim().Length > 0 && Math.Abs(dvr90Mm) < 1)
            {
                problems.Add(Strings.Levels_FloorNotZero);
            }
            // A terrain point on a level: Revit's 0 worked out now and kept as a number, as a typed floor level is. Set
            // before and nothing touched: kept as it is, even if the terrain has moved since (only "Use" applies that).
            var onLevel = _onLevel.IsChecked == true;
            var keepOnLevel = onLevel && KeepOnLevel;
            double? onLevelMm = !onLevel ? null : keepOnLevel ? _startDvr90Mm : OnLevelMm();
            if (onLevel && onLevelMm is null)
            {
                problems.Add(Strings.Levels_OnLevelChoose);
            }
            int? offset = _offset.IsEnabled ? Int(_offset, Strings.Levels_Offset) : null;
            var options = _start with
            {
                SiteAnchor = Anchor(),
                TerrainLevelAt = Value(_levelAt),
                LevelCrossMm = Number(_cross, Strings.Levels_Cross),
                LabelTextMm = Number(_labelText, Strings.Levels_LabelText),
                ZeroLevel = keepOnLevel ? _start.ZeroLevel : onLevelMm is not null ? Value(_zeroLevel) : "",
                ZeroTerrainAt = keepOnLevel ? _start.ZeroTerrainAt : onLevelMm is not null ? Value(_zeroAt) : _start.ZeroTerrainAt,

                ImportSiteplan = _import.IsChecked == true,
                ImportViewOnly = _viewOnly.IsChecked == true,
                SiteplanPaperStraight = _paperStraight.IsChecked == true,
                ContextIncludesLot = _contextLot.IsChecked == true,
                SiteplanMarginM = Number(_margin, Strings.Plan_Margin),
                SetbackM = Number(_setback, Strings.Plan_Setback),
                ToposolidThicknessMm = Int(_thickness, Strings.Plan_Thickness),
                InteriorStepMm = Int(_interior, Strings.Plan_SpacingInside),
                EdgeStepMm = Int(_edge, Strings.Plan_SpacingEdge),
                Language = Value(_language),
                // In Config's order, so the same boxes give the same text and Settings sees no change.
                HiddenLayers = string.Join(",", _layers.Where(l => l.Box.IsChecked != true).Select(l => l.Name)),
                RoofMinAreaM2 = Number(_roofArea, Strings.Layers_RoofMinArea),
                RoofRidgeOnly = _roofRidge.IsChecked == true,
                RoofSkipCrowded = _roofSkip.IsChecked == true,
            };
            problems.AddRange(options.Problems());
            if (problems.Count > 0)
            {
                _error.Text = string.Join("\n", problems.Distinct());
                return;
            }
            (Options, Dvr90Mm, OffsetMm) = (options, keepOnLevel ? _startDvr90Mm : Math.Round(onLevelMm ?? dvr90Mm), offset);
            // The key protected again only when it changed: DPAPI gives a new text each time, which would rewrite the file for nothing.
            var key = _key.Password.Trim();
            Machine = _machine with
            {
                DatafordelerKeyProtected = key == _startKey ? _machine.DatafordelerKeyProtected : Secret.Protect(key),
                AddressToken = _token.Text.Trim(),
            };
            DialogResult = true;
        }
    }
}
