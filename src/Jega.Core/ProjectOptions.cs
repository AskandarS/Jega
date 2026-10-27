using System.Text.Json;
using System.Text.Json.Serialization;
using Jega.Core.Resources;

namespace Jega.Core;

/// <summary>
/// The project's own Jega settings (the Settings window), kept in the model as JSON so they travel with the .rvt and
/// new ones can be added without changing Revit's storage layout. A setting missing from the stored JSON (written by
/// an older Jega) takes its default, so an old project opens as before. The defaults give the project real
/// coordinates (ETRS89/UTM32) at the first Load from Address; Manual georeference is the way out, and each automation
/// can be switched off for an office's own way of working.
/// </summary>
public sealed record ProjectOptions
{
    /// <summary>
    /// Where the site's 0,0 (the project's origin) is put in the model (Settings > Georeference): the Survey
    /// Point, the Project Base Point, the internal origin, or its real ETRS89/UTM32 place in the shared coordinates;
    /// turned to the project's true north. Then the site follows the site plan import wherever it's moved and turned
    /// (Placement). Real coordinates by default, the project georeferenced at the first Load from Address.
    /// </summary>
    public string SiteAnchor { get; init; } = AnchorShared;

    public const string AnchorSurvey = "survey", AnchorBase = "base", AnchorOrigin = "origin", AnchorShared = "shared";

    public static readonly string[] SiteAnchors = [AnchorSurvey, AnchorBase, AnchorOrigin, AnchorShared];

    // "OrientFromSiteplan", "ImportLeads" (Import Sets True North), "KeepImportOnAnchor" (Refresh
    // places the import by the real coordinates always) and "SiteReference" (the lot's centre on SiteAnchor: the
    // drawing's 0,0 is the project's origin) are gone; models that stored them are read as ever, the names passed over.

    /// <summary>
    /// Where Generate Terrain puts the "Terrain" level, from the terrain on the lot (LotTerrain.LevelAt): its lowest
    /// point (where the Toposolid starts), its highest, the average, the address point or the highest along the house.
    /// </summary>
    public string TerrainLevelAt { get; init; } = LevelLowest;

    public const string LevelLowest = "lowest", LevelHighest = "highest", LevelAverage = "average", LevelAddress = "address", LevelHouse = "house";

    public static readonly string[] TerrainLevels = [LevelLowest, LevelHighest, LevelAverage, LevelAddress, LevelHouse];

    /// <summary>The Toposolid's thickness under the terrain, mm (Jega's own type).</summary>
    public int ToposolidThicknessMm { get; init; } = Config.Current.Revit.ToposolidThicknessMm;

    /// <summary>The Toposolid's points inside the lot, mm apart (on the 0.4 m terrain grid).</summary>
    public int InteriorStepMm { get; init; } = Config.Current.Terrain.InteriorStepMm;

    /// <summary>The Toposolid's points along the lot's outline, at most this far apart, mm (and at every corner).</summary>
    public int EdgeStepMm { get; init; } = Config.Current.Terrain.EdgeStepMm;

    /// <summary>
    /// The elevation cross's width on paper, mm: Settings sets the "Jega Cross" family's Size with it (as Edit Type → Size
    /// does), and it is the family's first Size when it is loaded.
    /// </summary>
    public double LevelCrossMm { get; init; } = Config.Current.Revit.CrossMm;

    /// <summary>
    /// The terrain line labels' text height on paper, mm: Settings sets the labels' text type's Text Size with it (as
    /// Edit Type does), and it is the type's size when it is made.
    /// </summary>
    public double LabelTextMm { get; init; } = Config.Current.Revit.LabelTextMm;

    /// <summary>
    /// Settings > Elevations, "Terrain point on a level": the level (its UniqueId) a point of the terrain was put on;
    /// empty = not used (absolute, or the floor's level typed). Revit 0's DVR90 is worked out from it at OK and kept as
    /// a number (FloorDvr90Mm or the Survey Point's elevation), so the levels never move by themselves when the
    /// terrain is fetched again; Settings says when it no longer fits.
    /// </summary>
    public string ZeroLevel { get; init; } = "";

    /// <summary>With ZeroLevel: which point of the terrain is on it, as TerrainLevelAt (LotTerrain.LevelAt).</summary>
    public string ZeroTerrainAt { get; init; } = LevelLowest;

    /// <summary>
    /// The DVR90 of Revit's 0, mm, kept here instead of in the Survey Point's elevation (Dvr90.AtRevitZero).
    /// Each time Revit's 0 changes in Settings it is decided again: here with Manual georeference (the coordinates
    /// aren't Jega's), or when the project has links and the user keeps it in Jega (asked); else null, the height
    /// georeferenced in the Survey Point's elevation. Null (the default) = the Survey Point's elevation.
    /// </summary>
    public double? FloorDvr90Mm { get; init; }

    /// <summary>The DVR90 of Revit's 0 that puts terrain at $terrainMm (DVR90) on a level $levelMm above Revit's 0.</summary>
    public static double Dvr90OnLevel(double terrainMm, double levelMm) => terrainMm - levelMm;

    /// <summary>The site plan shows the lot plus this much on every side, m.</summary>
    public double SiteplanMarginM { get; init; } = Config.Current.Siteplan.MarginM;

    /// <summary>The dashed line this far inside the lot, m (BR18's 2.5 m); 0 = none.</summary>
    public double SetbackM { get; init; } = Config.Current.Siteplan.SetbackM;

    /// <summary>Context makes the buildings on the lot itself too (commented apart); off, only those around it, as the
    /// existing house is usually modelled by hand. Fetched either way: switching needs no new fetch.</summary>
    public bool ContextIncludesLot { get; init; }

    /// <summary>
    /// The project's language (Settings, a culture from Config's Languages): Jega's windows and receipts, the ribbon,
    /// and what it writes on the drawing, numbers too. Kept in the project, not on the PC, so the labels in a shared
    /// model don't change language with whoever edits a line.
    /// </summary>
    public string Language { get; init; } = Config.Current.DefaultLanguage;

    /// <summary>Load from Address imports the site plan DXF; off, nothing is imported (Export writes the DXF).</summary>
    public bool ImportSiteplan { get; init; } = true;

    /// <summary>The imported site plan shows in the active plan view only (Revit's "Current view only"); off, in every
    /// view, upper floors and 3D too.</summary>
    public bool ImportViewOnly { get; init; } = true;

    /// <summary>
    /// The site plan's paper (frame, title, notes, legend, scale bar) lies straight in the view it stands in, True or
    /// Project North, and only the map inside turns (SiteplanRedraw.TurnFor). Off: the whole plan turns as one, drawn
    /// north up and turned when imported, its paper aslant in a turned Project North view. Export always writes north up.
    /// </summary>
    public bool SiteplanPaperStraight { get; init; } = true;

    /// <summary>
    /// The site plan's layers left out of the drawing (Settings > Layers; names from Config's Siteplan.Layers), e.g.
    /// "KOTER,SKELMAAL": Load from Address, the redraw and Export draw without them, their legend rows too, and
    /// Settings draws an import already there again at OK. Empty (the default) = every layer, so a layer added to
    /// Config later shows; a name Config doesn't have is passed over, and SiteplanDxf.FixedLayers are always drawn.
    /// Which of the drawn layers a view shows is still Revit's Visibility/Graphics. One text, the names
    /// comma-separated in Config's order (HiddenLayerList), not a list: a list in a record compares by reference, and
    /// Settings tells a change by comparing the options.
    /// </summary>
    public string HiddenLayers { get; init; } = "";

    /// <summary>HiddenLayers as names.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> HiddenLayerList => HiddenLayers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// These settings with HiddenLayers as Settings writes them: Config's names in Config's order, without
    /// SiteplanDxf.FixedLayers or a name Config doesn't have. So layers stored in another form (another order, spaces,
    /// by another Jega or under an office's Config) aren't taken for a change when OK is pressed untouched.
    /// </summary>
    public ProjectOptions WithLayersAsSettingsWrites()
    {
        var hidden = HiddenLayerList;
        return this with
        {
            HiddenLayers = string.Join(",", Config.Current.Siteplan.Layers.Select(l => l.Name)
                .Where(n => hidden.Contains(n) && !SiteplanDxf.FixedLayers.Contains(n))),
        };
    }

    /// <summary>Roof levels only on buildings of at least this many m² (Settings > Layers); 0 = on every one.</summary>
    public double RoofMinAreaM2 { get; init; } = Config.Current.Siteplan.RoofMinAreaM2;

    /// <summary>Roof levels as the ridge alone (the default: ridge over eaves is cramped in a house at 1:2000); off,
    /// the ridge over the eaves where they fit, else the ridge alone.</summary>
    public bool RoofRidgeOnly { get; init; } = true;

    /// <summary>A roof level with no clear place near its building's middle is left out; off, it goes on the middle
    /// over the other texts.</summary>
    public bool RoofSkipCrowded { get; init; } = true;

    /// <summary>The three roof level settings, for the drawing.</summary>
    [JsonIgnore]
    public SiteplanDxf.RoofLabels RoofLabels => new(RoofMinAreaM2, RoofRidgeOnly, RoofSkipCrowded);

    // Models from before the Layers tab hold seven "Show…" settings in their JSON; read, they are passed over.

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>The settings, and the Jega that wrote them ("WrittenBy", Build.Version; passed over when read).</summary>
    public string ToJson()
    {
        var json = JsonSerializer.SerializeToNode(this, Json)!.AsObject();
        json["WrittenBy"] = Build.Version;
        return json.ToJsonString(Json);
    }

    /// <summary>From the stored JSON; null, empty or unreadable gives the defaults (never fails a command).</summary>
    public static ProjectOptions FromJson(string? json) => FromJson(json, out _);

    /// <summary>As FromJson; $unreadable: settings were stored but couldn't be read, so these are the defaults.</summary>
    public static ProjectOptions FromJson(string? json, out bool unreadable)
    {
        unreadable = false;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProjectOptions();
        }
        try
        {
            var o = JsonSerializer.Deserialize<ProjectOptions>(json, Json) ?? new ProjectOptions();
            // A setting written as null is read as null whatever its type says (System.Text.Json): its default, as when
            // missing. Numbers and switches can't be: null for one is a JsonException.
            var d = new ProjectOptions();
            return o with
            {
                SiteAnchor = o.SiteAnchor ?? d.SiteAnchor,
                TerrainLevelAt = o.TerrainLevelAt ?? d.TerrainLevelAt,
                ZeroLevel = o.ZeroLevel ?? d.ZeroLevel,
                ZeroTerrainAt = o.ZeroTerrainAt ?? d.ZeroTerrainAt,
                Language = o.Language ?? d.Language,
                HiddenLayers = o.HiddenLayers ?? d.HiddenLayers,
            };
        }
        catch (JsonException)
        {
            unreadable = true;
            return new ProjectOptions();
        }
    }

    /// <summary>What's wrong with these settings, for the window; empty when they can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        // Named as the Settings window's labels, so the user finds the field.
        if (ToposolidThicknessMm is < 10 or > 5000)
        {
            problems.Add(Strings.Options_Thickness);
        }
        if (InteriorStepMm is < 400 or > 10000)
        {
            problems.Add(Strings.Options_InteriorStep);
        }
        if (EdgeStepMm is < 100 or > 10000)
        {
            problems.Add(Strings.Options_EdgeStep);
        }

        if (LevelCrossMm is < 0.5 or > 10)
        {
            problems.Add(Strings.Options_CrossSize);
        }
        if (LabelTextMm is < 1 or > 10)
        {
            problems.Add(Strings.Options_LabelText);
        }
        if (!TerrainLevels.Contains(ZeroTerrainAt))
        {
            problems.Add(Strings.Options_ZeroTerrainAt);
        }
        if (SiteplanMarginM is < 0 or > 100)
        {
            problems.Add(Strings.Options_Margin);
        }
        if (SetbackM is < 0 or > 20)
        {
            problems.Add(Strings.Options_Setback);
        }
        if (RoofMinAreaM2 is < 0 or > 10000)
        {
            problems.Add(Strings.Options_RoofMinArea);
        }
        if (!SiteAnchors.Contains(SiteAnchor))
        {
            problems.Add(Strings.Options_SiteAnchor);
        }
        if (!TerrainLevels.Contains(TerrainLevelAt))
        {
            problems.Add(Strings.Options_TerrainLevelAt);
        }
        // Offered at all: not Language.Of, which turns any other into the default.
        if (!Config.Current.Languages.Any(l => Core.Language.Same(l.Culture, Language)))
        {
            problems.Add(Texts.Fill(Strings.Options_Language, string.Join(", ", Config.Current.Languages.Select(l => l.Name))));
        }
        return problems;
    }
}
