namespace Jega.Core;

/// <summary>
/// The numbers, names and looks Jega's functions use, in one place: line styles, text sizes, the site plan's layers,
/// margins, defaults. For development now (change a value here, not in the function); later read from a JSON file
/// set as Current, so an office can change them without a new build. Every value here is today's default.
///
/// Not here, as they are facts rather than choices: the height model's 0.4 m cells (HeightCells.Size) and the
/// sampling that follows them (EdgeSampler.DefaultStep), the services' URLs and limits (SiteFetcher), tolerances.
/// </summary>
public sealed record Config
{
    /// <summary>The config every function reads. Today's defaults; replaced as a whole (e.g. from JSON) at start-up.</summary>
    public static Config Current { get; set; } = new();

    public RevitLook Revit { get; init; } = new();
    public SiteplanLook Siteplan { get; init; } = new();
    public FetchSettings Fetch { get; init; } = new();
    public TerrainDefaults Terrain { get; init; } = new();

    /// <summary>
    /// The languages Settings offers (Language): each a name and a culture, which picks the texts (Strings.resx, or
    /// Strings.&lt;culture&gt;.resx when there is one) and how numbers are shown, on the screen and on the drawing.
    /// Another language is a line here and a resx beside each Strings.resx. Settings' fields read a number back with a
    /// comma or a point: a culture with another minus (U+2212, e.g. sv-SE) or a space as thousands separator needs
    /// that reading taught first.
    /// </summary>
    public IReadOnlyList<LanguageChoice> Languages { get; init; } = [new("Dansk", "da-DK"), new("English", "en-GB")];

    /// <summary>The language of a project that hasn't chosen one.</summary>
    public string DefaultLanguage { get; init; } = "da-DK";
}

/// <summary>A language Settings offers: its name in the list, and its culture (e.g. "da-DK").</summary>
public sealed record LanguageChoice(string Name, string Culture);

/// <summary>A colour, 0–255 each.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>A Revit line style (a subcategory of Lines): made once with these, then the user's (Line Styles).</summary>
/// <param name="Weight">Revit's line weight number, 1–16.</param>
/// <param name="Dashed">The first line pattern called "Dash…" in the model; solid if there is none.</param>
public sealed record LineStyle(string Name, Rgb Colour, int Weight, bool Dashed);

/// <summary>What Jega makes in Revit: its line styles, text, families, types and levels.</summary>
public sealed record RevitLook
{
    /// <summary>The terrain grid's frame: grey, thin, dashed.</summary>
    public LineStyle GridFrame { get; init; } = new("Jega Grid", new(90, 99, 94), 1, true);

    /// <summary>The lot boundary's outline (Draw Lot Boundary): blue, heavy, solid.</summary>
    public LineStyle Edge { get; init; } = new("Jega Edge", new(47, 108, 216), 4, false);

    /// <summary>The terrain lines with falls and HIGH / LOW: orange, medium, solid.</summary>
    /// <remarks>
    /// The two terrain line styles' names are a contract with every model, not a choice: a terrain line is any line of
    /// one of them (LineStyles.TerrainLineStyles), so another name here leaves every line drawn so far without its
    /// labels. Never change them, nor let an office's config do it; the colour, weight and dash may.
    /// </remarks>
    public LineStyle TerrainLine { get; init; } = new("Jega Line", new(217, 122, 31), 3, false);

    /// <summary>The terrain lines with only the elevations: as TerrainLine, dashed to tell them apart. Its name is a
    /// contract too (TerrainLine).</summary>
    public LineStyle TerrainLineLevelsOnly { get; init; } = new("Jega Line (elevations only)", new(217, 122, 31), 3, true);

    /// <summary>The terrain line labels' text type and its first size (mm on paper); the project's LabelTextMm (Settings)
    /// changes it, and the type is then named "Jega &lt;size&gt;mm".</summary>
    public string LabelTextType { get; init; } = "Jega 3mm";

    public double LabelTextMm { get; init; } = 3;

    /// <summary>The elevation cross's annotation family; its first Size (mm on paper) is the project's LevelCrossMm.</summary>
    public string CrossFamily { get; init; } = "Jega Cross";

    public double CrossMm { get; init; } = 5;

    /// <summary>The Toposolid's own type, and its thickness under the terrain (mm).</summary>
    public string ToposolidType { get; init; } = "Jega Terrain";

    public int ToposolidThicknessMm { get; init; } = 200;

    /// <summary>The Terrain level's name, and the one used when a level of that name is the project's own.</summary>
    public string TerrainLevel { get; init; } = "Terrain";

    public string TerrainLevelTaken { get; init; } = "Jega Terrain";

    /// <summary>The Terrain level's plan: of the first floor plan type whose name has this in it, else the first.</summary>
    public string TerrainPlanType { get; init; } = "Site";

    /// <summary>The lot edge's filled region type (Draw Lot Boundary).</summary>
    public string LotEdgeType { get; init; } = "Jega Lot Boundary";

    /// <summary>
    /// The line patterns tried, in order, for the site plan's setback line in Revit (its import shows the DXF's
    /// DASHED solid): a pattern of this name, or the first starting with it.
    /// </summary>
    public string[] SetbackPatterns { get; init; } = ["Demolished", "Dash"];
}

/// <summary>A site plan layer and its DXF colour number.</summary>
public sealed record DxfLayer(string Name, int Colour);

/// <summary>The site plan DXF: its layers, the setback line, the plot scales.</summary>
public sealed record SiteplanLook
{
    /// <summary>
    /// Each layer's DXF colour (1 red, 3 green, 4 cyan, 5 blue, 6 magenta, 7 black/white, 8 dark grey, 9 light grey, 30 orange,
    /// 94 dark green: the paths, kin to the roads' green; 150 dark blue: the manhole covers, apart from the water's
    /// cyan; 92 green: the gullies, kin to the roads they sit in).
    /// The neighbours' boundaries are light grey (9), so they differ from the
    /// neighbours' buildings (8) and the legend can tell them apart. The roof levels blue as the buildings they stand
    /// in, apart from the terrain's (orange). In this order in the file.
    /// </summary>
    public DxfLayer[] Layers { get; init; } =
    [
        new(SiteplanLayers.Neighbours, 9), new(SiteplanLayers.Buildings, 5), new(SiteplanLayers.NeighbourBuildings, 8),
        new(SiteplanLayers.RoadEdges, 3), new(SiteplanLayers.PathEdges, 94), new(SiteplanLayers.Text, 7), new(SiteplanLayers.AddressPoint, 1),
        new(SiteplanLayers.Origin, 6), new(SiteplanLayers.Frame, 7), new(SiteplanLayers.Lengths, 1), new(SiteplanLayers.Setback, 6), new(SiteplanLayers.Levels, 30),
        new(SiteplanLayers.RoofLevels, 5), new(SiteplanLayers.Water, 4), new(SiteplanLayers.Manholes, 150), new(SiteplanLayers.Gullies, 92),
        new(SiteplanLayers.HouseNumbers, 8), new(SiteplanLayers.StreetNames, 7), new(SiteplanLayers.Trees, 84), new(SiteplanLayers.Hedges, 42),
        new(SiteplanLayers.Lot, 1),
    ];

    /// <summary>The plan shows the lot plus this much on every side, m (the project's own is in Settings).</summary>
    public double MarginM { get; init; } = 10.0;

    /// <summary>The dashed line this far inside the lot, m (BR18's 2.5 m); 0 = none (the project's own is in Settings).</summary>
    public double SetbackM { get; init; } = 2.5;

    /// <summary>A building on the lot smaller than this gets no roof level (sheds, carports, bin stores), m² (the
    /// project's own is in Settings > Layers).</summary>
    public double RoofMinAreaM2 { get; init; } = 25.0;

    /// <summary>The setback line's dash and gap on paper, mm, at the plot scale.</summary>
    public double SetbackDashMm { get; init; } = 3.0;

    public double SetbackGapMm { get; init; } = 1.5;

    /// <summary>The plot scales to choose from: the smallest where the frame (lot + margin) fits A3.</summary>
    public int[] Scales { get; init; } = [100, 200, 500, 1000, 2000, 5000];
}

/// <summary>How much is fetched round the lot.</summary>
public sealed record FetchSettings
{
    /// <summary>The terrain CSV: the lot plus this, m.</summary>
    public double TerrainMarginM { get; init; } = 5.0;

    /// <summary>
    /// The lots, buildings and road edges, this far round the lot (m), whatever the site plan shows: kept in the model,
    /// so Settings can show any area up to it without fetching again.
    /// </summary>
    public double SurroundingsMarginM { get; init; } = 50.0;

    /// <summary>The context's buildings: the lot plus this, m.</summary>
    public double ContextMarginM { get; init; } = 40.0;

    /// <summary>A context building lower than this above the ground is left out (a terrace, a ruin, noise), m.</summary>
    public double ContextMinHeightM { get; init; } = 1.5;

    /// <summary>The eaves are read from the roof's cells this close to the outline, m: GeoDanmark's outline is the roof's edge.</summary>
    public double EaveBandM { get; init; } = 1.0;

    /// <summary>The terrain grids a large lot can choose from, m (the service resamples).</summary>
    public double[] Cells { get; init; } = [0.4, 0.8, 1.2, 1.6, 2.0];

    /// <summary>Calls at a time for a terrain in several parts.</summary>
    public int ParallelParts { get; init; } = 4;

    /// <summary>
    /// The most cells per side in one call to Danmarks Højdemodel: the service's limit, not a choice (160 m at 0.4 m).
    /// A larger terrain is fetched in parts this size.
    /// </summary>
    public int MaxCellsPerCall { get; init; } = 400;

    /// <summary>
    /// The most terrain points fetched (1,000 × 1,000): the CSV is kept in the model (about 30 MB at this size) and read
    /// on every terrain line edit. A larger lot gets a coarser grid, or none.
    /// </summary>
    public long MaxPoints { get; init; } = 1_000_000;
}

/// <summary>The terrain's defaults (the project's own are in Settings).</summary>
public sealed record TerrainDefaults
{
    /// <summary>The Toposolid's points inside the lot, mm apart (on the 0.4 m grid).</summary>
    public int InteriorStepMm { get; init; } = 800;

    /// <summary>The Toposolid's points along the outline, at most this far apart, mm (and at every corner).</summary>
    public int EdgeStepMm { get; init; } = 400;
}
