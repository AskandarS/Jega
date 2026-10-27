namespace Jega.Core;

/// <summary>
/// The site plan's DXF layers, by name: the same in every language, as the drawing is Danish. SiteplanDxf draws on
/// them, Config gives their colours and order, the Revit side finds the import's by them, and Settings > Layers shows
/// each under its own text (Layer_&lt;name&gt; in Strings.resx).
/// </summary>
public static class SiteplanLayers
{
    /// <summary>The lot itself; LotOutline reads it back from the import.</summary>
    public const string Lot = "GRUND";

    /// <summary>The neighbours' boundaries.</summary>
    public const string Neighbours = "SKEL";

    /// <summary>The buildings on the lot.</summary>
    public const string Buildings = "BYGNINGER";

    /// <summary>The buildings around it.</summary>
    public const string NeighbourBuildings = "BYGNINGER_NABO";

    public const string RoadEdges = "VEJKANT";

    public const string PathEdges = "STI";

    /// <summary>The parcel numbers, the legend's words, the title and the notes.</summary>
    public const string Text = "TEKST";

    /// <summary>The cross at the drawing's 0,0; with a project origin apart from the address point, a small cross at the
    /// address point.</summary>
    public const string AddressPoint = "ADRESSEPUNKT";

    /// <summary>The cross at the project's origin, the drawing's 0,0, when that isn't the address point.</summary>
    public const string Origin = "NULPUNKT";

    /// <summary>The frame, the north arrow and the scale bar.</summary>
    public const string Frame = "RAMME";

    /// <summary>The lot's boundary lengths.</summary>
    public const string Lengths = "SKELMAAL";

    /// <summary>The setback line inside the lot, dashed.</summary>
    public const string Setback = "AFSTAND_SKEL";

    /// <summary>The spot elevations at the lot's corners.</summary>
    public const string Levels = "KOTER";

    /// <summary>The roofs' ridge (and eaves) on the lot.</summary>
    public const string RoofLevels = "TAGKOTER";

    public const string Water = "VAND";

    /// <summary>GeoDanmark's manhole covers (Brønddæksel) round the lot, a symbol and the cover's level. What is under
    /// a cover (sewer, water, heating …) GeoDanmark doesn't say.</summary>
    public const string Manholes = "BROENDE";

    /// <summary>GeoDanmark's road gullies (Nedløbsrist), a symbol and the grate's level.</summary>
    public const string Gullies = "RISTE";

    /// <summary>The neighbours' house numbers (Danmarks Adresseregister), each in the building its address belongs to,
    /// else at its address point.</summary>
    public const string HouseNumbers = "HUSNUMRE";

    /// <summary>The street names (Danmarks Adresseregister), along the register's own name line for each street.</summary>
    public const string StreetNames = "VEJNAVNE";

    /// <summary>GeoDanmark's trees (Træ, a point: the prominent ones seen from the air), tree groups and woods
    /// (Trægruppe, Skov: outlines).</summary>
    public const string Trees = "TRAEER";

    /// <summary>GeoDanmark's hedges and fences (Hegn), lines.</summary>
    public const string Hedges = "HEGN";
}
