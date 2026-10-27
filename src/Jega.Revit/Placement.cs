using Autodesk.Revit.DB;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// Where the site's points go in the model: the anchor everything Jega draws hangs on. The CSV, the lot and the
/// site plan DXF have 0,0 at the project's origin (the address point rounded) and true north up ("site" coordinates, mm).
///
/// The site follows the site plan import: move and turn it, then Refresh, and the terrain follows its address cross
/// and north. Get reads the place Remember kept at the last Refresh or Load from Address (the frame), never the
/// import live: so everything moves together at Refresh (frame, Toposolid, levels, one Undo), and stays put if the
/// import is deleted. Before there is a frame, and where Load from Address starts a new address, the site's 0,0
/// goes where Settings > Georeference says (Landing, ProjectOptions.SiteAnchor): the Survey Point, the Project
/// Base Point, the internal origin, or its real place in the shared coordinates (ETRS89/UTM32; on the Survey Point
/// when they aren't). A new address lands turned to the project's true north. True north comes of the shared
/// coordinates; there are no true-north buttons.
/// </summary>
internal sealed class Placement
{
    /// <summary>The ProjectStore mark on the site plan import the "dxf" anchor follows.</summary>
    public const string SiteplanRole = "Siteplan";

    private readonly XYZ _origin;          // the site's 0,0 in internal coordinates, at Z 0
    private readonly Transform _rotation;  // true north → internal, rotation only

    /// <summary>The angle from project north to the site's north, degrees counter-clockwise: this placement's, which is
    /// the project's true north for FromProject.</summary>
    public double AngleToTrueNorth => Angles.Normalize360(Angles.ToDegrees(Transforms.NorthAngle(_rotation)));

    /// <summary>The site's 0,0 from the internal origin, mm (x, y).</summary>
    public (double X, double Y) OriginMm => (Units.MmFromFeet(_origin.X), Units.MmFromFeet(_origin.Y));

    private Placement(XYZ origin, Transform rotation) => (_origin, _rotation) = (origin, rotation);

    /// <summary>The site's place: the site plan's as of the last Refresh, else where its 0,0 lands (see the class).</summary>
    public static Placement Get(Document doc) =>
        ProjectStore.ReadFrame(doc) is { } frame
            ? new Placement(new XYZ(frame.X, frame.Y, 0), Transform.CreateRotation(XYZ.BasisZ, frame.Angle))
            : Landing(doc);

    /// <summary>
    /// Where Settings > Georeference puts the site: its 0,0 on the point in Revit (RevitAnchor), turned to the project's
    /// true north, or $turn (radians, as the frame's) to keep the site's own turn (an anchor changed).
    /// </summary>
    public static Placement Landing(Document doc, double? turn = null)
    {
        var anchor = RevitAnchor(doc);
        return new Placement(anchor._origin, turn is { } a ? Transform.CreateRotation(XYZ.BasisZ, a) : anchor._rotation);
    }

    // Linked Revit models or linked CAD files (Jega's own site plan is imported, not linked); not a CAD file linked into
    // a drafting view (a detail), which nothing places by the shared coordinates. One linked into a plan only (a
    // surveyor's DWG, "Current view only") still counts.
    public static bool HasLinks(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount() > 0
        || new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
            .Any(i => i.IsLinked && !(i.ViewSpecific && doc.GetElement(i.OwnerViewId) is ViewDrafting));

    // The point in Revit the site's 0,0 goes on (Settings > Georeference), turned to the project's true north; that far
    // off it when a neighbour address kept its true offset (HoldHere). $raw: the point itself, without it.
    private static Placement RevitAnchor(Document doc, bool raw = false)
    {
        var project = FromProject(doc);
        var options = ProjectStore.ReadOptions(doc);
        var anchor = options.SiteAnchor switch
        {
            Core.ProjectOptions.AnchorShared => Shared(doc) ?? OnBase(doc),
            Core.ProjectOptions.AnchorBase => OnBase(doc),
            Core.ProjectOptions.AnchorOrigin => new Placement(XYZ.Zero, project._rotation),
            _ => project,
        };
        // Real coordinates place every address at its real place already.
        return !raw && options.SiteAnchor != Core.ProjectOptions.AnchorShared && ProjectStore.ReadAnchorOffset(doc) is var (x, y)
            ? new Placement(anchor._origin + new XYZ(x, y, 0), anchor._rotation)
            : anchor;
    }

    /// <summary>
    /// Inside a transaction, after the site is written (Load from Address, a new address kept where it was): $site's
    /// 0,0, where it lands, is where "Keep the site plan in place" holds it from now on, so a neighbour lies its true
    /// offset from the first address after Refresh too. Forgotten when that is the anchor itself (within 1 mm), and
    /// under real coordinates.
    /// </summary>
    public static void HoldHere(Document doc, Placement site)
    {
        var held = site._origin - RevitAnchor(doc, raw: true)._origin;
        ProjectStore.WriteAnchorOffset(doc,
            ProjectStore.ReadOptions(doc).SiteAnchor == Core.ProjectOptions.AnchorShared || Units.MmFromFeet(Flat(held).GetLength()) < 1
                ? null : (held.X, held.Y));
    }

    /// <summary>Inside a transaction: the site's 0,0 held on the Revit anchor itself again (a new address started
    /// afresh, the anchor changed in Settings).</summary>
    public static void ForgetHold(Document doc) => ProjectStore.WriteAnchorOffset(doc, null);

    /// <summary>
    /// Where the site plan import's 0,0 is in the model (Z 0). Every reading of the import's place goes through here
    /// (Remember, MoveSiteTo).
    /// </summary>
    public static XYZ AddressPointOf(ImportInstance import) => Flat(import.GetTotalTransform().OfPoint(XYZ.Zero));

    /// <summary>
    /// The site plan import's north (radians counter-clockwise from project north, as Transforms.NorthAngle gives an
    /// import's): its own turn in the model plus the turn its drawing was given (ProjectStore.ReadDrawnTurn: a plan
    /// drawn in the project's working direction lies unturned in the model with its north that far round). Every
    /// reading of the import's turn goes through here (Remember, Stale, MoveSiteTo, the redraws), as AddressPointOf
    /// for its place.
    /// </summary>
    public static double NorthOf(ImportInstance import) =>
        Transforms.NorthAngle(import.GetTotalTransform()) + Angles.ToRadians(ProjectStore.ReadDrawnTurn(import));

    /// <summary>The site with its 0,0 at $origin (Z dropped), turned $angle (radians, as an import's NorthAngle).</summary>
    public static Placement At(XYZ origin, double angle) => new(Flat(origin), Transform.CreateRotation(XYZ.BasisZ, angle));

    /// <summary>What lands where, for the messages: "the address point on the Survey Point" … (Place_*).</summary>
    public static string Describe(Core.ProjectOptions options) => options.SiteAnchor switch
    {
        Core.ProjectOptions.AnchorShared => Strings.Place_Real,
        Core.ProjectOptions.AnchorBase => Strings.Place_AddressOnBase,
        Core.ProjectOptions.AnchorOrigin => Strings.Place_AddressOnOrigin,
        _ => Strings.Place_AddressOnSurvey,
    };

    private static XYZ Flat(XYZ p) => new(p.X, p.Y, 0);

    /// <summary>
    /// The site's 0,0, the project's origin, at its real ETRS89/UTM32 easting and
    /// northing in the shared coordinates; null without a site from Load from Address, or when that lands over 10 km
    /// from the internal origin: then the shared coordinates aren't UTM32 (not georeferenced, or System 34), and Revit's
    /// geometry would be too far out anyway.
    /// </summary>
    public static Placement? Shared(Document doc)
    {
        if (Origin(doc) is not var (e, n))
        {
            return null;
        }
        var origin = AtShared(doc, e, n);
        return origin.GetLength() > Units.FeetFromMm(10_000_000) ? null : new Placement(origin, FromProject(doc)._rotation);
    }

    // The model point (Z 0) whose shared coordinates are $e, $n (UTM32, m): their offset from the internal origin's own,
    // turned into the model. Never from the Survey Point: Revit moves it out to the shared 0,0 (some 6,240 km off with
    // UTM32) when the coordinates are set (a new address placed by it landed that far).
    private static XYZ AtShared(Document doc, double e, double n)
    {
        var zero = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        var offset = new XYZ(Units.FeetFromMm(e * 1000) - zero.EastWest, Units.FeetFromMm(n * 1000) - zero.NorthSouth, 0);
        return FromProject(doc)._rotation.OfVector(offset);
    }

    /// <summary>The project has shared coordinates of its own (a template's, a surveyor's): the internal origin isn't at
    /// E/N 0/0 (Revit's default). True north alone doesn't count: Georeference keeps it.</summary>
    public static bool HasCoordinates(Document doc) =>
        doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero) is var p
        && (Math.Abs(p.EastWest) > Units.FeetFromMm(1) || Math.Abs(p.NorthSouth) > Units.FeetFromMm(1));

    /// <summary>The project's coordinates are this site's: its origin's E/N on the Project Base Point (as Load from Address
    /// or Georeference gave them), within 1 m. Not after a neighbour was placed beside, nor over a template's or a
    /// surveyor's.</summary>
    public static bool OnOwnCoordinates(Document doc) =>
        Origin(doc) is var (e, n) && BaseCoordinates(doc) is var (be, bn) && Math.Abs(e - be) < 1 && Math.Abs(n - bn) < 1;

    /// <summary>The project's coordinates place the address point $e, $n (UTM32, m) within 10 km of the internal origin:
    /// they are UTM32 and this address fits them (Shared would place it).</summary>
    public static bool Fits(Document doc, double e, double n)
    {
        var (oe, on) = ProjectOrigin.Of(e, n);
        return AtShared(doc, oe, on).GetLength() <= Units.FeetFromMm(10_000_000);
    }

    /// <summary>
    /// For the question about a project's own shared coordinates (AddressCommand.ExistingCoordinates), for the site's
    /// origin $origin (UTM32, m): how far (m) the place they give it is from the Project Base Point, which is also how far
    /// links placed by them move if Jega's are set over them (null when they don't place it within 10 km: another
    /// system); and, once a site stands (the frame), how far it moves and turns (m, degrees) to that place ($ToTheirs:
    /// following them; null with $Apart) and onto the Project Base Point ($ToBase: Jega's set over them). Before the
    /// first site both null.
    /// </summary>
    public static (double? Apart, (double M, double Deg)? ToTheirs, (double M, double Deg)? ToBase) Moves(Document doc, (double E, double N) origin)
    {
        var theirs = AtShared(doc, origin.E, origin.N);
        var fits = theirs.GetLength() <= Units.FeetFromMm(10_000_000);
        var onBase = OnBase(doc)._origin;
        double? apart = fits ? Units.MmFromFeet(Units.Flat(theirs, onBase)) / 1000 : null;
        if (ProjectStore.ReadFrame(doc) is not { } frame)
        {
            return (apart, null, null);
        }
        // Either way the site ends with the project's true north (FromProject), from the frame's turn.
        var now = new XYZ(frame.X, frame.Y, 0);
        var turn = Angles.TurnDegrees(Transforms.NorthAngle(FromProject(doc)._rotation), frame.Angle);
        return (apart, fits ? (Units.MmFromFeet(Units.Flat(now, theirs)) / 1000, turn) : null,
            (Units.MmFromFeet(Units.Flat(now, onBase)) / 1000, turn));
    }

    /// <summary>The Project Base Point's easting and northing in the shared coordinates, m: the origin's with real coordinates.</summary>
    public static (double E, double N) BaseCoordinates(Document doc) =>
        doc.ActiveProjectLocation.GetProjectPosition(BasePoint.GetProjectBasePoint(doc).Position) is var p
            ? (Units.MmFromFeet(p.EastWest) / 1000, Units.MmFromFeet(p.NorthSouth) / 1000) : default;

    // The Project Base Point (Z 0), true north as the project's: where a new project's origin goes.
    private static Placement OnBase(Document doc) => new(Flat(BasePoint.GetProjectBasePoint(doc).Position), FromProject(doc)._rotation);

    /// <summary>The project's origin (Core.ProjectOrigin; UTM32, m), the site's 0,0: the CSV, the lot and the
    /// site plan are measured from it (SiteFetcher.Fetch). The address point rounded; null without a site from Load
    /// from Address.</summary>
    public static (double E, double N)? Origin(Document doc) =>
        ProjectStore.ReadSite(doc) is { AddressE: { } e, AddressN: { } n } ? ProjectOrigin.Of(e, n) : null;

    /// <summary>The address point in site coordinates (mm from the origin, up to 0.7 m off it); null without a site.</summary>
    public static Core.Point2? AddressMm(Document doc) =>
        ProjectStore.ReadSite(doc) is { AddressE: { } e, AddressN: { } n } && ProjectOrigin.Of(e, n) is var (oe, on)
            ? new Core.Point2((e - oe) * 1000, (n - on) * 1000) : null;

    /// <summary>
    /// Inside a transaction, under "real coordinates": a project whose shared coordinates aren't UTM32 gets them from
    /// the site, so Shared has a place to land on. The site's 0,0 where the site is now (Get; $fresh: on the Project
    /// Base Point, a new address) is given its real easting and northing, and true north the site's; the elevation
    /// (Revit 0's DVR90) is kept, and nothing in the model moves. Only over local shared coordinates (the site's 0,0
    /// within 10 km of their 0,0: Revit's default, or the Survey Point the site plan put there); others (System 34, a
    /// surveyor's) are left alone. Null when already UTM32 or without an address. Revit's sign for the angle isn't
    /// taken on trust: the one where the project's north then agrees is kept, else nothing changes. $force (the user
    /// chose to set Jega's over the project's own): set whatever the project had.
    /// </summary>
    public static string? Georeference(Document doc, bool fresh = false, bool force = false)
    {
        if ((!force && Shared(doc) is not null) || ProjectStore.ReadSite(doc) is not { AddressE: { } e, AddressN: { } n } || Origin(doc) is not var (oe, on))
        {
            return null;
        }
        var site = fresh ? OnBase(doc) : Get(doc);
        var location = doc.ActiveProjectLocation;
        var kept = location.GetProjectPosition(XYZ.Zero);
        var now = location.GetProjectPosition(site._origin);
        if (!force && !Local(now))
        {
            return Strings.Placement_OtherSystem;
        }
        // The site's 0,0 is the project's origin (Core.ProjectOrigin), given its easting and northing; a new site
        // (fresh) has it on the Project Base Point, so the shared coordinates read as round numbers there.
        var north = site._rotation.OfVector(XYZ.BasisY);
        var phi = Transforms.NorthAngle(site._rotation);
        foreach (var angle in new[] { now.Angle, phi, -phi })
        {
            location.SetProjectPosition(site._origin, new ProjectPosition(Units.FeetFromMm(oe * 1000), Units.FeetFromMm(on * 1000), now.Elevation, angle));
            doc.Regenerate();
            if (FromProject(doc)._rotation.OfVector(XYZ.BasisY).DotProduct(north) > Math.Cos(Angles.ToRadians(0.01))
                && Shared(doc) is { } real && Units.Flat(real._origin, site._origin) < Units.FeetFromMm(1))
            {
                // The project's own kept when Jega's were set over them ($force, the first time), so leaving real
                // coordinates can put them back. Not Revit's default in a new project: there is nothing to put back, and
                // Manual georeference would otherwise offer to undo the georeference.
                if (force && ProjectStore.ReadCoordinatesBefore(doc) is null)
                {
                    ProjectStore.WriteCoordinatesBefore(doc, (kept.EastWest, kept.NorthSouth, kept.Angle));
                }
                return Fill(Strings.Placement_Georeferenced, e, n);
            }
        }
        location.SetProjectPosition(XYZ.Zero, kept);
        doc.Regenerate();
        return Strings.Placement_GeoreferenceFailed;
    }

    /// <summary>
    /// Inside a transaction, after the new site is written: Load from Address with a new address that moves the
    /// project's origin here. The Project Base Point given the new origin's easting and northing, true north
    /// and the elevation kept, so the new site lands on it as the first one did: the origin is always the Project Base
    /// Point, also after a neighbour placed beside (else the new site landed on the old one's place, a neighbour's, and
    /// the two came apart). Links placed by the shared coordinates move with it (asked before). Only when the old origin
    /// is in this model's coordinates (within 10 km of the internal origin); others (System 34, a surveyor's) are left
    /// alone, and said. Null without a site.
    /// </summary>
    public static string? MoveOrigin(Document doc, SiteData? old)
    {
        if (Origin(doc) is not var (e, n))
        {
            return null;
        }
        if (old is not { AddressE: { } oldE, AddressN: { } oldN })
        {
            return Strings.Placement_OriginNotOurs;
        }
        var (e0, n0) = ProjectOrigin.Of(oldE, oldN);
        if (AtShared(doc, e0, n0).GetLength() > Units.FeetFromMm(10_000_000))
        {
            return Strings.Placement_OriginNotOurs;
        }
        var at = OnBase(doc)._origin;
        var location = doc.ActiveProjectLocation;
        var now = location.GetProjectPosition(at);
        location.SetProjectPosition(at, new ProjectPosition(Units.FeetFromMm(e * 1000), Units.FeetFromMm(n * 1000), now.Elevation, now.Angle));
        doc.Regenerate();
        // The E/N stand in the receipt's line on the origin just above; said once.
        return Strings.Placement_OriginMoved;
    }

    /// <summary>For Settings, before OK: what choosing real coordinates will do to a project without them (Georeference);
    /// null when it has them already (the site plan just moves there; coordinates not the site's own are asked about at
    /// OK, SettingsCommand) or nothing is loaded.</summary>
    public static string? GeoreferencePreview(Document doc)
    {
        if (Shared(doc) is not null || ProjectStore.ReadFrame(doc) is null || Origin(doc) is not var (e, n) || HasCoordinates(doc))
        {
            return null;
        }
        // The origin's E/N, on the Project Base Point (SettingsCommand georeferences there).
        return Fill(Strings.Location_WillGeoreference, e, n);
    }

    // Local shared coordinates: the point within 10 km of their 0,0 (Revit's default, or the Survey Point the site plan
    // put on the address point), not another system's.
    private static bool Local(ProjectPosition p) => Math.Sqrt(p.EastWest * p.EastWest + p.NorthSouth * p.NorthSouth) <= Units.FeetFromMm(10_000_000);

    /// <summary>The Survey Point as the site's 0,0, true north as the project's.</summary>
    public static Placement FromProject(Document doc)
    {
        var location = doc.ActiveProjectLocation;
        // GetTotalTransform() turns the CSV's true-north directions into the model's. Its inverse turns them the
        // wrong way: tried, the frame came out 26° off, twice the 13° to true north.
        var toInternal = location.GetTotalTransform();
        var rotation = Transform.Identity;
        rotation.BasisX = toInternal.BasisX;
        rotation.BasisY = toInternal.BasisY;
        rotation.BasisZ = toInternal.BasisZ;
        var survey = BasePoint.GetSurveyPoint(doc).Position;
        return new Placement(new XYZ(survey.X, survey.Y, 0), rotation);
    }

    /// <summary>The site plan import the "dxf" anchor follows: the one marked, if exactly one is.</summary>
    public static ImportInstance? Siteplan(Document doc) =>
        ProjectStore.Marked(doc, SiteplanRole, typeof(ImportInstance)) is [ImportInstance one] ? one : null;

    /// <summary>
    /// Inside a transaction (Refresh, Load from Address). Keeps the site plan import's place in the model: Get
    /// reads it until the next time. An import without the mark (a site plan DXF imported by hand) gets it here
    /// if it is the only one with the lot on it. Returns a line for the receipt when the place moved, or, as a Problem
    /// (the terrain didn't follow, the user must act), when it couldn't be kept.
    /// </summary>
    public static (string? Note, bool Problem) Remember(Document doc)
    {
        var marked = ProjectStore.Marked(doc, SiteplanRole, typeof(ImportInstance)).Cast<ImportInstance>().ToList();
        if (marked.Count > 1)
        {
            return (Fill(Strings.Placement_SeveralMarked, marked.Count), true);
        }
        var import = marked.FirstOrDefault();
        if (import is null)
        {
            var found = LotOutline.SiteplanImports(doc);
            if (found is not [var only])
            {
                return found.Count == 0 ? (null, false)
                    : (Fill(Strings.Placement_SeveralWithLot, found.Count, LotOutline.Layer), true);
            }
            ProjectStore.Mark(only, SiteplanRole, own: true);
            import = only;
        }
        var t = import.GetTotalTransform();
        if (Transforms.IsMirrored(t))
        {
            return (Strings.Placement_Mirrored, true);
        }
        // Where the terrain was: the last frame, or (the first time) where the site's 0,0 lands.
        var b = Get(doc);
        var angle = NorthOf(import);
        var at = AddressPointOf(import);
        ProjectStore.WriteFrame(doc, at.X, at.Y, angle);
        var moved = Units.MmFromFeet(Units.Flat(at, new XYZ(Units.FeetFromMm(b.OriginMm.X), Units.FeetFromMm(b.OriginMm.Y), 0))) / 1000;
        var turned = Angles.TurnDegrees(angle, Angles.ToRadians(b.AngleToTrueNorth));
        return (moved < 0.001 && Math.Abs(turned) < 0.01 ? null : Fill(Strings.Placement_Follows, moved, turned), false);
    }

    /// <summary>
    /// Whether the site plan import has moved or turned since the frame was kept (a new work direction, a hand move):
    /// what Remember would change, by its tolerances; or its drawing's turn no longer fits the view it stands in (its
    /// orientation switched in Revit's own properties: SiteplanRedraw.Unfitting), which Refresh's Straighten mends. False
    /// without a frame, without exactly one marked import, or mirrored (Refresh can't follow those either). Cheap, no
    /// geometry: JegaCommand asks it before every command that places by the frame, and Refreshes first when it's true.
    /// </summary>
    public static bool Stale(Document doc)
    {
        if (ProjectStore.ReadFrame(doc) is not { } frame || Siteplan(doc) is not { } import)
        {
            return false;
        }
        var t = import.GetTotalTransform();
        if (Transforms.IsMirrored(t))
        {
            return false;
        }
        var moved = Units.MmFromFeet(Units.Flat(AddressPointOf(import), new XYZ(frame.X, frame.Y, 0))) / 1000;
        var turned = Angles.TurnDegrees(NorthOf(import), frame.Angle);
        return moved >= 0.001 || Math.Abs(turned) >= 0.01 || SiteplanRedraw.Unfitting(doc, ProjectStore.ReadOptions(doc), import) is not null;
    }

    /// <summary>For Settings: how far the site plan was moved by hand from its landing point; null when it is on it, or
    /// nothing is loaded (the choices above say where it is).</summary>
    public static string? Status(Document doc)
    {
        if (ProjectStore.ReadFrame(doc) is not { } frame)
        {
            return null;
        }
        var landing = Landing(doc);
        var metres = Math.Sqrt(Math.Pow(Units.MmFromFeet(frame.X) - landing.OriginMm.X, 2) + Math.Pow(Units.MmFromFeet(frame.Y) - landing.OriginMm.Y, 2)) / 1000;
        var turned = Angles.TurnDegrees(frame.Angle, Angles.ToRadians(landing.AngleToTrueNorth));
        var options = ProjectStore.ReadOptions(doc);
        return metres < 0.001 && Math.Abs(turned) < 0.01
            ? null
            : options.SiteAnchor == Core.ProjectOptions.AnchorShared
                ? Fill(Strings.Placement_MovedByHandShared, metres, turned)
                : Fill(Strings.Placement_MovedByHand, metres, turned, Describe(options));
    }

    /// <summary>
    /// Inside a transaction (Settings, an anchor changed): the site plan import moved and turned so its address cross
    /// is on $target, and the site's place set there, so everything follows at Refresh. Without an import only the
    /// place. Returns a line for the summary; $place says what lands where (Describe).
    /// </summary>
    public static string MoveSiteTo(Document doc, Placement target, string place) => MoveSiteTo(doc, target, place, out _);

    /// <summary>As MoveSiteTo; $changed: the import was moved or turned.</summary>
    public static string MoveSiteTo(Document doc, Placement target, string place, out bool changed)
    {
        var angle = Angles.ToRadians(target.AngleToTrueNorth);
        var import = Siteplan(doc);
        changed = false;
        if (import is not null)
        {
            // Unpinned to move, then left as it was (as Load from Address does).
            var pinned = import.Pinned;
            import.Pinned = false;
            var t = import.GetTotalTransform();
            // Turned by what its north (drawing's turn included) is off the target's.
            var north = NorthOf(import);
            var turn = angle - north;
            if (Math.Abs(Angles.TurnDegrees(angle, north)) >= 0.01)
            {
                ElementTransformUtils.RotateElement(doc, import.Id, Line.CreateBound(t.Origin, t.Origin + XYZ.BasisZ), turn);
                changed = true;
                // AddressPointOf reads the turned transform: stale until regenerated.
                doc.Regenerate();
            }
            var move = target._origin - AddressPointOf(import);
            if (Units.MmFromFeet(move.GetLength()) >= 1)
            {
                ElementTransformUtils.MoveElement(doc, import.Id, move);
                changed = true;
            }
            import.Pinned = pinned;
        }
        ProjectStore.WriteFrame(doc, target._origin.X, target._origin.Y, angle);
        return import is null
            ? Fill(Strings.Placement_NoImportToMove, place)
            : changed
                ? Fill(Strings.Placement_Moved, place)
                : Fill(Strings.Placement_Stays, place);
    }

    /// <summary>A site point (mm from the site's 0,0, true north) in the model at height z (feet).</summary>
    public XYZ Point(double xMm, double yMm, double zFeet) =>
        _origin + _rotation.OfVector(new XYZ(Units.FeetFromMm(xMm), Units.FeetFromMm(yMm), 0)) + new XYZ(0, 0, zFeet);

    /// <summary>The other way: a model point's plan position in site terms (mm from the site's 0,0, true north).
    /// Its Z is ignored: heights come from the terrain, never from the drawn geometry.</summary>
    public Core.Point2 ToCsv(XYZ p)
    {
        var v = _rotation.Inverse.OfVector(new XYZ(p.X - _origin.X, p.Y - _origin.Y, 0));
        return new Core.Point2(Units.MmFromFeet(v.X), Units.MmFromFeet(v.Y));
    }
}
