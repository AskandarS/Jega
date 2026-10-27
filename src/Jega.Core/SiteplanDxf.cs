using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jega.Core.Resources;
using Layer = Jega.Core.SiteplanLayers;
using Pt = (double X, double Y);

namespace Jega.Core;

/// <summary>
/// The site plan as DXF R12 for Revit, the window being the lot + 10 m: the lot (GRUND) with its lengths (SKELMAAL) and the setback line (AFSTAND_SKEL), the neighbours
/// (SKEL), the buildings on the lot (BYGNINGER) and around it (BYGNINGER_NABO), the road edges (VEJKANT) and path edges (STI), spot
/// elevations at the corners (KOTER), the roofs' ridge and eaves on the lot (TAGKOTER), the manhole covers (BROENDE) and
/// road gullies (RISTE) with their levels, the neighbours' house numbers (HUSNUMRE) and the street names (VEJNAVNE), the parcel numbers, the address
/// cross, frame, north arrow, scale bar and the notes under it. Texts and symbols are sized for printing at the
/// drawing's scale on A3. Not here (they need BBR): the buildings' names. 1:1 in mm, 0,0 at
/// the project's origin (or, without one, the address point), true north up, or turned to the project's working
/// direction ($turnDeg: everything turned before it is drawn, so the frame, the texts and the legend lie straight in
/// Project North and the north arrow turns instead). LotOutline reads the lot from GRUND, Placement the turn of the import.
/// Its texts and numbers are in the language it is given (Drawing_* in Strings.resx, Danish in Strings.da.resx); the
/// layers' names are the same in every language.
/// </summary>
public static class SiteplanDxf
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // The layers' colours, the plot scales, the lot's share of the window and the setback line: Config.Siteplan.

    /// <summary>
    /// The layers always drawn, whatever ProjectOptions.HiddenLayers says: the lot (LotOutline also tells an exploded
    /// plan by it), the frame with the north arrow and scale bar, the texts, which carry the legend's words, the title
    /// and the notes as well as the parcel numbers, and the project's origin (the key to the coordinates the note
    /// gives).
    /// </summary>
    public static readonly string[] FixedLayers = [Layer.Lot, Layer.Frame, Layer.Text, Layer.Origin];

    // GeoDanmark's Vejkant types drawn on STI: the paths' edges (the others seen: Kørevej, Internvej and Cykelsti
    // langs vej, which stay on VEJKANT).
    private static readonly string[] PathTypes = ["Sti, diverse", "Hovedsti"];

    /// <summary>
    /// Which roofs get a roof level (ProjectOptions.RoofLabels): buildings of at least $MinAreaM2, as ridge / eaves or
    /// the ridge alone, and one with no clear place left out ($SkipCrowded) or put on its building's middle anyway.
    /// </summary>
    public sealed record RoofLabels(double MinAreaM2, bool RidgeOnly, bool SkipCrowded)
    {
        /// <summary>The defaults: Config's least area, the ridge alone (ridge over eaves is cramped in a house at
        /// 1:2000), crowded ones left out.</summary>
        public static RoofLabels Default => new(Config.Current.Siteplan.RoofMinAreaM2, true, true);
    }

    // The paper window (mm, half width and height on A3 landscape), which the texts' sizes are set by.
    private static readonly (double W, double H) WindowMm = (190, 125);

    /// <summary>The drawing for the address point ($x, $y), cut to $box (m); $dtm gives the spot elevations;
    /// $setbackM null = Config's; $water: GeoDanmark's water of every kind, all on VAND. $address goes in the title; $language: the drawing's texts
    /// and numbers (the project's, Language.Current, from the callers in Jega). $fetchedUtc: when the data was fetched,
    /// the credit's date; null = today. $roofs: the roofs on the lot (SiteFetcher.Roofs); null = none. $hiddenLayers:
    /// the layers left out (ProjectOptions.HiddenLayers), as if there were nothing to draw on them, so their legend
    /// rows and notes go and the other texts don't keep clear of them; FixedLayers are drawn anyway. $roofLabels: which
    /// roofs get a level, and how (ProjectOptions.RoofLabels); null = RoofLabels.Default. $origin: the project's origin
    /// (ProjectOrigin; UTM32, m), the drawing's 0,0 with its cross on NULPUNKT and a small cross at the address point;
    /// null, or the address point itself, = the drawing as without it. $manholes, $gullies: GeoDanmark's manhole
    /// covers and road gullies, points with their level ("z"), on BROENDE and RISTE; null = none. $addresses: the house
    /// numbers round the lot with their streets (SiteFetcher.Addresses), on HUSNUMRE and VEJNAVNE; null = none.
    /// $trees (points), $treeGroups (tree groups and woods, outlines), $hedges (lines): GeoDanmark's, on TRAEER and
    /// HEGN; null = none. A layer that can't be drawn is left out (WriteWithReport says which); the plan comes anyway.
    /// $turnDeg: the drawing turned counter-clockwise by this about its 0,0 (the project's angle to true north), so it
    /// lies in Project North when imported unturned: the window follows the turned lot, the north arrow turns the same
    /// way, and a note says so; 0 = true north up. $fitTurnDeg: a turn the frame must hold the lot at too (the project's
    /// angle to true north): the frame is made big enough for the lot north up, turned $turnDeg and turned $fitTurnDeg,
    /// so a plan drawn again for its view's north (True or Project North) keeps its frame and its scale; 0 with $turnDeg
    /// 0 = the frame round the lot as it lies.</summary>
    public static string Write(List<SiteFetcher.Feature> lots, List<SiteFetcher.Feature> buildings, List<SiteFetcher.Feature> roads,
        SiteFetcher.Dtm? dtm, (double X1, double Y1, double X2, double Y2) box, double x, double y, string address, CultureInfo language,
        double? setbackDistanceM = null, List<SiteFetcher.Feature>? water = null,
        DateTime? fetchedUtc = null, List<SiteFetcher.RoofLevels>? roofs = null, IReadOnlyCollection<string>? hiddenLayers = null,
        RoofLabels? roofLabels = null, (double E, double N)? origin = null,
        List<SiteFetcher.Feature>? manholes = null, List<SiteFetcher.Feature>? gullies = null, List<SiteFetcher.HouseNumber>? addresses = null,
        List<SiteFetcher.Feature>? trees = null, List<SiteFetcher.Feature>? treeGroups = null, List<SiteFetcher.Feature>? hedges = null,
        double turnDeg = 0, double fitTurnDeg = 0) =>
        WriteWithReport(lots, buildings, roads, dtm, box, x, y, address, language, setbackDistanceM, water, fetchedUtc, roofs, hiddenLayers,
            roofLabels, origin, manholes, gullies, addresses, trees, treeGroups, hedges, turnDeg, fitTurnDeg).Dxf;

    /// <summary>Write, and the layers left out because their drawing failed (SiteplanLayers names; the receipt says
    /// which). The lot, the frame, the texts and the notes can't be left out: without them there is no plan.</summary>
    public static (string Dxf, IReadOnlyList<string> SkippedLayers) WriteWithReport(List<SiteFetcher.Feature> lots, List<SiteFetcher.Feature> buildings,
        List<SiteFetcher.Feature> roads, SiteFetcher.Dtm? dtm, (double X1, double Y1, double X2, double Y2) box, double x, double y, string address,
        CultureInfo language, double? setbackDistanceM = null, List<SiteFetcher.Feature>? water = null,
        DateTime? fetchedUtc = null, List<SiteFetcher.RoofLevels>? roofs = null, IReadOnlyCollection<string>? hiddenLayers = null,
        RoofLabels? roofLabels = null, (double E, double N)? origin = null,
        List<SiteFetcher.Feature>? manholes = null, List<SiteFetcher.Feature>? gullies = null, List<SiteFetcher.HouseNumber>? addresses = null,
        List<SiteFetcher.Feature>? trees = null, List<SiteFetcher.Feature>? treeGroups = null, List<SiteFetcher.Feature>? hedges = null,
        double turnDeg = 0, double fitTurnDeg = 0)
    {
        // The flow, in this order:
        // 1. What can't move: the lines (lots, buildings, roads, water, setback, hedges, outlines) and the point symbols
        //    (covers, gullies, trees), each symbol marking the box it takes.
        // 2. The texts, the most important first, each kept clear of every symbol and of the texts placed before it:
        //    the boundary lengths and the parcel numbers (with the lots), the roof levels, the spot elevations, the
        //    street names, the house numbers, the covers' and gullies' levels, and last the own lot's label, which gives
        //    way to all.
        // 3. The frame, the notes, the legend and the scale bar, outside the plan.
        // Every optional layer's step runs on its own (Step): a failure in it leaves that layer out and is reported.
        var d = new Drawing(lots, buildings, roads, dtm, box, x, y, address, language, setbackDistanceM, water, fetchedUtc,
            roofs, hiddenLayers, roofLabels, origin, manholes, gullies, addresses, trees, treeGroups, hedges, turnDeg, fitTurnDeg);
        d.Lots();
        d.Step(Layer.Buildings, d.Buildings);
        d.Step(Layer.RoadEdges, d.RoadEdges);
        d.Step(Layer.Water, d.Water);
        d.Step(Layer.Setback, d.SetbackLine);
        d.Step(Layer.Hedges, d.Hedges);
        d.Step(Layer.Trees, d.Trees);
        d.Step(Layer.Manholes, d.Manholes);
        d.Step(Layer.Gullies, d.Gullies);
        d.Step(Layer.RoofLevels, d.Roofs);
        d.Step(Layer.Levels, d.SpotElevations);
        d.Step(Layer.StreetNames, d.StreetNames);
        d.Step(Layer.HouseNumbers, d.HouseNumbers);
        d.Step(Layer.Manholes, d.CoverLevels);
        d.OwnLotLabel();
        d.FrameAndNorthArrow();
        d.Notes();
        d.Legend();
        d.ScaleBar();
        d.WriteNotes();
        return (d.Finish(), d.Skipped);
    }

    /// <summary>
    /// Write's drawing as it is made: what it is drawn from, the lines and texts so far, and what one step leaves for a
    /// later one (the own lot's label, the buildings on the lot, how many roofs and spot elevations were shown, the
    /// legend's place). The fields are named as Write's locals were, so each step reads as it did there.
    /// </summary>
    private sealed class Drawing
    {
        private readonly List<SiteFetcher.Feature> lots;
        private readonly List<SiteFetcher.Feature> buildings;
        private readonly List<SiteFetcher.Feature> roads;
        private readonly SiteFetcher.Dtm? dtm;
        private readonly (double X1, double Y1, double X2, double Y2) box;
        private readonly double x;
        private readonly double y;
        private readonly string address;
        private readonly CultureInfo language;
        private readonly List<SiteFetcher.Feature>? water;
        private readonly DateTime? fetchedUtc;
        private readonly List<SiteFetcher.RoofLevels>? roofs;
        private readonly List<SiteFetcher.Feature>? manholes;
        private readonly List<SiteFetcher.Feature>? gullies;
        private readonly List<SiteFetcher.HouseNumber>? addresses;
        private readonly List<SiteFetcher.Feature>? trees;
        private readonly List<SiteFetcher.Feature>? treeGroups;
        private readonly List<SiteFetcher.Feature>? hedges;

        private readonly RoofLabels roofLook;
        private readonly SiteplanLook look;
        private readonly double setbackM;
        private readonly HashSet<string> hidden;
        private readonly SiteFetcher.Feature? ownLot;
        private readonly List<Pt>? own;
        private readonly bool apart;
        private readonly double ox;
        private readonly double oy;
        private readonly int scale;
        // The turn (degrees, counter-clockwise about 0,0) everything was given before the drawing, and what stays
        // unturned: the address point as the notes give it (UTM32) and the own lot as the height model sees it.
        private readonly double turn;
        private readonly double geoX;
        private readonly double geoY;
        private readonly SiteFetcher.Feature? ownLotGeo;

        private List<(List<Pt> Pts, string Layer, bool Closed)> lines = [];
        private List<(double X, double Y, double H, string S, string Layer, double Rot, bool Left)> texts = [];
        // The boxes the point symbols take (covers, gullies, trees): every text keeps clear of them.
        private List<(double X1, double Y1, double X2, double Y2)> marks = [];
        // How much each layer got on the plan (not the legend): the legend's rows and the notes follow it.
        private readonly Dictionary<string, int> drawn = [];
        private readonly List<string> skipped = [];

        // Left by one step for a later one.
        private (double X, double Y, string Label, List<Pt> Visible)? ownLabel;
        private readonly List<List<Pt>> ownBuildings = [];
        private double roofHeight;
        private string? streetSample;
        private readonly List<(double X, double Y, string Layer, string Level)> pointLevels = [];
        private List<(string S, double PaperMm)> notes = [];
        private double left;
        private double row;
        private double sy;

        public Drawing(List<SiteFetcher.Feature> lots, List<SiteFetcher.Feature> buildings, List<SiteFetcher.Feature> roads,
            SiteFetcher.Dtm? dtm, (double X1, double Y1, double X2, double Y2) box, double x, double y, string address, CultureInfo language,
            double? setbackDistanceM, List<SiteFetcher.Feature>? water, DateTime? fetchedUtc,
            List<SiteFetcher.RoofLevels>? roofs, IReadOnlyCollection<string>? hiddenLayers, RoofLabels? roofLabels,
            (double E, double N)? origin, List<SiteFetcher.Feature>? manholes, List<SiteFetcher.Feature>? gullies,
            List<SiteFetcher.HouseNumber>? addresses, List<SiteFetcher.Feature>? trees, List<SiteFetcher.Feature>? treeGroups,
            List<SiteFetcher.Feature>? hedges, double turnDeg, double fitTurnDeg)
        {
            (this.lots, this.buildings, this.roads, this.dtm, this.box, this.x, this.y) = (lots, buildings, roads, dtm, box, x, y);
            (this.address, this.language, this.water, this.fetchedUtc, this.roofs) = (address, language, water, fetchedUtc, roofs);
            (this.manholes, this.gullies, this.addresses) = (manholes, gullies, addresses);
            (this.trees, this.treeGroups, this.hedges) = (trees, treeGroups, hedges);
            roofLook = roofLabels ?? RoofLabels.Default;
            look = Config.Current.Siteplan;
            setbackM = setbackDistanceM ?? look.SetbackM;
            hidden = (hiddenLayers ?? []).Except(FixedLayers).ToHashSet();
            // The drawing's 0,0: the project's origin, or the address point (UTM32, m). An origin apart from the address
            // point has its own layer; at it, the drawing is the one without an origin, so NULPUNKT is left out (not in
            // the layer table either).
            apart = origin is { } o && (o.E, o.N) != (x, y);
            if (!apart)
            {
                hidden.Add(Layer.Origin);
            }
            (ox, oy) = origin ?? (x, y);
            (geoX, geoY) = (x, y);
            ownLotGeo = lots.FirstOrDefault(l => Geometry.Inside(l.Rings, x, y));
            turn = turnDeg;
            // The margin the given window has round the lot (round the address point without a lot).
            var margin = ownLotGeo is null ? (box.X2 - box.X1) / 2 : box.X2 - ownLotGeo.Rings[0].Max(p => p.X);
            if (turn != 0)
            {
                // Everything turned about 0,0 before it is drawn (Turn), the address point with it; the window is the
                // turned lot's plus that margin.
                this.lots = lots.Select(Turned).ToList();
                this.buildings = buildings.Select(Turned).ToList();
                this.roads = roads.Select(Turned).ToList();
                this.water = water?.Select(Turned).ToList();
                this.manholes = manholes?.Select(Turned).ToList();
                this.gullies = gullies?.Select(Turned).ToList();
                this.trees = trees?.Select(Turned).ToList();
                this.treeGroups = treeGroups?.Select(Turned).ToList();
                this.hedges = hedges?.Select(Turned).ToList();
                this.roofs = roofs?.Select(r => Turn((r.X, r.Y)) is var t ? new SiteFetcher.RoofLevels(t.X, t.Y, r.Ridge, r.Eave) : r).ToList();
                this.addresses = addresses?.Select(a => Turn((a.X, a.Y)) is var t
                    ? a with { X = t.X, Y = t.Y, StreetLine = a.StreetLine.Select(l => l.Select(Turn).ToList()).ToList() } : a).ToList();
                (this.x, this.y) = Turn((x, y));
                ownLot = this.lots.FirstOrDefault(l => Geometry.Inside(l.Rings, this.x, this.y));
                this.box = Surroundings.Window(ownLot?.Rings[0], this.x, this.y, margin);
            }
            else
            {
                ownLot = ownLotGeo;
            }
            // The frame big enough for the lot north up and turned $fitTurnDeg as well as as drawn, round the drawn
            // window's middle: drawn again for True or Project North, the plan keeps its frame and scale.
            if (ownLotGeo is { } lotGeo && (turn != 0 || fitTurnDeg != 0))
            {
                var (w, h) = (this.box.X2 - this.box.X1, this.box.Y2 - this.box.Y1);
                foreach (var deg in new[] { 0, fitTurnDeg })
                {
                    var ring = lotGeo.Rings[0].Select(p => Spin(p, deg, (ox, oy))).ToList();
                    w = Math.Max(w, ring.Max(p => p.X) - ring.Min(p => p.X) + 2 * margin);
                    h = Math.Max(h, ring.Max(p => p.Y) - ring.Min(p => p.Y) + 2 * margin);
                }
                var (cx, cy) = ((this.box.X1 + this.box.X2) / 2, (this.box.Y1 + this.box.Y2) / 2);
                this.box = (cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
            }
            own = ownLot?.Rings[0];  // its outline; OnLot also leaves out its holes
            scale = Scale(this.box);
        }

        // $p turned $deg counter-clockwise about $c.
        private static Pt Spin(Pt p, double deg, Pt c)
        {
            var (s, co) = Math.SinCos(deg * Math.PI / 180);
            var (dx, dy) = (p.X - c.X, p.Y - c.Y);
            return (c.X + dx * co - dy * s, c.Y + dx * s + dy * co);
        }

        // A point as drawn (turned about 0,0), and back to where it is on the ground.
        private Pt Turn(Pt p) => Spin(p, turn, (ox, oy));

        private Pt Geo(Pt p) => Spin(p, -turn, (ox, oy));

        private SiteFetcher.Feature Turned(SiteFetcher.Feature f) => new(f.Rings.Select(r => r.Select(Turn).ToList()).ToList(), f.Props);

        // The terrain at a point of the drawing: the height model lies true north up, so the turn is undone first.
        private double? Z(double px, double py) => dtm is null ? null : Geo((px, py)) is var g ? SiteFetcher.Elevation(dtm, g.X, g.Y) : null;

        // A text of the drawing in its language, with its values filled in by it (Drawing_* keys).
        private string T(string key, params object?[] args) => string.Format(language, Strings.ResourceManager.GetString(key, language) ?? key, args);

        private bool Shown(string layer) => !hidden.Contains(layer);

        private void Poly(List<Pt> pts, string layer, bool closed) => lines.Add((pts, layer, closed));

        private void Text(double tx, double ty, double h, string s, string layer = Layer.Text, double rot = 0, bool left = false) => texts.Add((tx, ty, h, s, layer, rot, left));

        /// <summary>The layers left out because their step failed.</summary>
        public IReadOnlyList<string> Skipped => skipped;

        // One more thing drawn on $layer, on the plan.
        private void Drawn(string layer) => drawn[layer] = drawn.GetValueOrDefault(layer) + 1;

        private int DrawnOn(string layer) => drawn.GetValueOrDefault(layer);

        // A symbol's box on the plan: texts keep clear of it.
        private void Mark(double cx, double cy, double half) => marks.Add((cx - half, cy - half, cx + half, cy + half));

        // Whether a text would stand clear of every symbol and of the texts so far, with $pad round it.
        private bool Clear((double X, double Y, double H, string S, string Layer, double Rot, bool Left) t, double pad)
        {
            var b = Bounds(t, pad);
            return !marks.Any(m => Overlap(m, b)) && !texts.Any(o => Overlap(Bounds(o, 0), b));
        }

        // The first of $candidates (best first) that stands clear, added to the texts; false if none does.
        private bool Place(IEnumerable<(double X, double Y, double H, string S, string Layer, double Rot, bool Left)> candidates, double pad)
        {
            foreach (var t in candidates)
            {
                if (Clear(t, pad))
                {
                    texts.Add(t);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// One optional layer's step on its own: if it fails, what it drew is taken away again, the layer is left out
        /// and named in Skipped, and the drawing goes on. The required steps (the lot, the frame, the notes) don't go
        /// through here: without them there is no plan.
        /// </summary>
        public void Step(string layer, Action draw)
        {
            var (l, t, m) = (lines.Count, texts.Count, marks.Count);
            var before = new Dictionary<string, int>(drawn);
            try
            {
                draw();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                lines.RemoveRange(l, lines.Count - l);
                texts.RemoveRange(t, texts.Count - t);
                marks.RemoveRange(m, marks.Count - m);
                drawn.Clear();
                foreach (var (k, v) in before)
                {
                    drawn[k] = v;
                }
                if (!skipped.Contains(layer))
                {
                    skipped.Add(layer);
                }
            }
        }

        private bool OnLot(double px, double py) => ownLot is not null && Geometry.Inside(ownLot.Rings, px, py);

        private double P(double paperMm) => paperMm * scale / 1000;  // paper millimetres → metres on the ground

        // The lots in the window: the own lot and the neighbours' boundaries, the boundary lengths, the parcel numbers.
        public void Lots()
        {
            foreach (var lot in lots)
            {
                var mine = lot == ownLot;
                foreach (var ring in lot.Rings)
                {
                    if (Geometry.ClipRing(ring, box) is not { Count: > 0 } c)
                    {
                        continue;
                    }
                    // A neighbour's boundary without the stretches it shares with the own lot: Revit ignores the DXF's
                    // order, so the grey would otherwise be drawn over the red.
                    if (mine || ownLot is null || Geometry.Unshared(c, ownLot.Rings, 0.01) is not { } parts)
                    {
                        Poly(c, mine ? Layer.Lot : Layer.Neighbours, true);
                    }
                    else
                    {
                        parts.ForEach(p => Poly(p, Layer.Neighbours, false));
                    }
                }
                if (mine && Shown(Layer.Lengths))
                {
                    LotDimensions(lot.Rings[0], box, P, language, (tx, ty, h, s, layer, rot) => Text(tx, ty, h, s, layer, rot));
                }
                // The label inside the visible part, fitted by Label: level, or along a road parcel (Label.Strip); with
                // no room, in its middle or, for a bent road parcel, along its middle (Geometry.LabelPoint), as before.
                // The own lot's waits until its buildings are known.
                if (Geometry.ClipRing(lot.Rings[0], box) is { Count: > 0 } visible)
                {
                    var (cx, cy) = Geometry.LabelPoint([.. visible, visible[0]]);
                    var number = lot.Props.GetValueOrDefault("matrikelnummer", "");
                    if (mine)
                    {
                        var area = double.TryParse(lot.Props.GetValueOrDefault("registreretAreal"), NumberStyles.Float, Inv, out var a) ? a : 0;
                        ownLabel = (cx, cy, T(nameof(Strings.Drawing_ParcelArea), number, area), visible);
                    }
                    else if (Label.Spots(visible, [number], P(1.4), along: Label.Strip(visible)).FirstOrDefault() is { } spot)
                    {
                        Text(spot.X, spot.Y, P(1.4), number, rot: spot.AngleDeg);
                    }
                    else
                    {
                        Text(cx, cy, P(1.4), number);
                    }
                }
            }
        }

        // The buildings, on the lot or around it.
        public void Buildings()
        {
            foreach (var b in buildings)
            {
                var (cx, cy) = Geometry.Centre(b.Rings[0]);
                var onLot = OnLot(cx, cy);
                foreach (var ring in b.Rings)
                {
                    if (Geometry.ClipRing(ring, box) is { Count: > 0 } c)
                    {
                        Poly(c, onLot ? Layer.Buildings : Layer.NeighbourBuildings, true);
                        Drawn(onLot ? Layer.Buildings : Layer.NeighbourBuildings);
                    }
                }
                if (onLot)
                {
                    ownBuildings.Add(b.Rings[0]);
                }
            }
        }

        // GeoDanmark's hedges and fences: lines on HEGN, cut to the window.
        public void Hedges()
        {
            foreach (var piece in ((Shown(Layer.Hedges) ? hedges : null) ?? []).SelectMany(h => h.Rings).SelectMany(l => Geometry.ClipLine(l, box)))
            {
                Poly(piece, Layer.Hedges, false);
                Drawn(Layer.Hedges);
            }
        }

        // GeoDanmark's trees: a tree seen from the air is a circle (2 mm on paper) with a dot, marked so texts keep
        // clear; a tree group or wood its outline, both on TRAEER.
        public void Trees()
        {
            if (!Shown(Layer.Trees))
            {
                return;
            }
            foreach (var (px, py) in (trees ?? []).Where(f => f.Rings.Count > 0 && f.Rings[0].Count > 0).Select(f => f.Rings[0][0]))
            {
                if (px < box.X1 || px > box.X2 || py < box.Y1 || py > box.Y2)
                {
                    continue;
                }
                TreeSymbol(px, py);
                Drawn(Layer.Trees);
            }
            foreach (var ring in (treeGroups ?? []).SelectMany(g => g.Rings))
            {
                if (ring.Count > 3 && Geometry.ClipRing(ring, box) is { Count: > 0 } c)
                {
                    Poly(c, Layer.Trees, true);
                    Drawn(Layer.Trees);
                }
            }
        }

        private void TreeSymbol(double cx, double cy)
        {
            var r = P(1.0);
            Poly([.. Enumerable.Range(0, 12).Select(i => (cx + r * Math.Cos(i * Math.PI / 6), cy + r * Math.Sin(i * Math.PI / 6)))], Layer.Trees, true);
            var d = P(0.3);
            Poly([(cx - d, cy), (cx + d, cy)], Layer.Trees, false);
            Poly([(cx, cy - d), (cx, cy + d)], Layer.Trees, false);
            Mark(cx, cy, r);
        }

        // The roofs on the lot: the numbers only, fitted into their building by Label: along its long side, read from
        // below or the right, the ridge over the eaves, or the ridge alone (RoofLabels, or where the two don't fit); the
        // legend says which is which. One height for them all (Label.Even): the largest from 1.4 mm on paper down to
        // 1.0 mm at which nine in ten fit, so the plan is even. No room in the building, clear of the texts so far: left
        // out, or (RoofLabels) on one line at its middle, moved up or down in steps of 3 mm on paper
        // to the first place clear. A building under RoofLabels' least area gets none. A roof is its building's by its
        // middle (SiteFetcher.Roofs puts it there), so the building comes from the kept ones, nothing fetched; one not
        // found (none kept) goes on the one line. Before the spot elevations, so their levels keep clear of these.
        public void Roofs()
        {
            var kept = buildings.Select(b => (Ring: b.Rings[0], Centre: Geometry.Centre(b.Rings[0]))).ToList();
            List<Pt>? RingOf(SiteFetcher.RoofLevels r) => kept.Where(k => Math.Abs(k.Centre.X - r.X) + Math.Abs(k.Centre.Y - r.Y) < 0.05)
                .Select(k => k.Ring).FirstOrDefault();
            var roofItems = ((Shown(Layer.RoofLevels) ? roofs : null) ?? [])
                .Where(r => r.X >= box.X1 && r.X <= box.X2 && r.Y >= box.Y1 && r.Y <= box.Y2)
                .Select(r => (Roof: r, Ring: RingOf(r)))
                .Where(i => i.Ring is null || Math.Abs(Geometry.Area(i.Ring)) >= roofLook.MinAreaM2)
                .Select(i => (i.Roof, i.Ring, Numbers: (IReadOnlyList<string>)(i.Roof.Eave is { } e && !roofLook.RidgeOnly
                    ? [T(nameof(Strings.Drawing_RoofRidge), i.Roof.Ridge), T(nameof(Strings.Drawing_RoofRidge), e)]
                    : [T(nameof(Strings.Drawing_RoofRidge), i.Roof.Ridge)])))
                .ToList();
            roofHeight = Label.Even([.. roofItems.Where(i => i.Ring is not null).Select(i => (i.Ring!, i.Numbers))],
                [P(1.4), P(1.3), P(1.2), P(1.1), P(1.0)], 0.9);
            // The first place in the building clear of the texts so far, but those in other buildings (which can't meet
            // it, and whose turned boxes would only stand in the way): both lines, else the ridge alone.
            List<(double, double, double, string, string, double, bool)>? InBuilding(List<Pt> ring, IReadOnlyList<string> numbers)
            {
                var here = texts.Where(o => !kept.Any(k => k.Ring != ring && Geometry.Inside(k.Ring, o.X, o.Y))).ToList();
                IReadOnlyList<string>[] tries = numbers.Count > 1 ? [numbers, [numbers[0]]] : [numbers];
                foreach (var tryNumbers in tries)
                {
                    foreach (var block in Label.Spots(ring, tryNumbers, roofHeight))
                    {
                        var placed = block.Placed().Select(p => (p.X, p.Y, block.Height, p.Text, Layer.RoofLevels, block.AngleDeg, false)).ToList();
                        if (!placed.Any(t => here.Any(o => Overlap(Bounds(o, 0), Bounds(t, 0))) || marks.Any(m => Overlap(m, Bounds(t, 0)))))
                        {
                            return placed;
                        }
                    }
                }
                return null;
            }
            foreach (var (r, ring, numbers) in roofItems)
            {
                if (ring is not null && InBuilding(ring, numbers) is { } inside)
                {
                    texts.AddRange(inside);
                    Drawn(Layer.RoofLevels);
                    continue;
                }
                if (ring is not null && roofLook.SkipCrowded)
                {
                    continue;
                }
                var words = numbers.Count > 1 ? T(nameof(Strings.Drawing_RoofBoth), r.Ridge, r.Eave) : numbers[0];
                (double, double, double, string, string, double, bool) At(double dy) => (r.X, r.Y + P(dy), roofHeight, words, Layer.RoofLevels, 0.0, false);
                if (Place(new[] { 0.0, 3, -3, 6, -6, 9, -9 }.Select(At), P(0.3)))
                {
                    Drawn(Layer.RoofLevels);
                }
                else if (!roofLook.SkipCrowded)
                {
                    texts.Add(At(0));
                    Drawn(Layer.RoofLevels);
                }
            }
        }

        // Spot elevations: terrain (DVR90) at the lot's and its buildings' corners, a small x and the level beside it.
        // A corner within 8 mm on paper of one already marked is skipped, so close corners don't pile up. The level goes
        // up right of the x, else in the first of the other three corners round it clear of the texts so far (the
        // boundary lengths, parcel numbers, levels); with none clear, the x alone.
        public void SpotElevations()
        {
            if (dtm is not null && own is not null && Shown(Layer.Levels))
            {
                var marked = new List<Pt>();
                foreach (var (kx, ky) in own.SkipLast(1).Concat(ownBuildings.SelectMany(r => r.SkipLast(1))))
                {
                    if (kx < box.X1 || kx > box.X2 || ky < box.Y1 || ky > box.Y2 || marked.Any(m => Math.Sqrt((kx - m.X) * (kx - m.X) + (ky - m.Y) * (ky - m.Y)) < P(8)))
                    {
                        continue;
                    }
                    if (Z(kx, ky) is not { } z)
                    {
                        continue;
                    }
                    marked.Add((kx, ky));
                    var d = P(0.8);
                    Poly([(kx - d, ky - d), (kx + d, ky + d)], Layer.Levels, false);
                    Poly([(kx - d, ky + d), (kx + d, ky - d)], Layer.Levels, false);
                    var level = z.ToString("N2", language);
                    var dx = P(1.2) + P(1.4) * CharWidth * level.Length / 2;
                    Place(new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) }.Select(c => (kx + c.Item1 * dx, ky + c.Item2 * P(1.6), P(1.4), level, Layer.Levels, 0.0, false)), P(0.3));
                    Drawn(Layer.Levels);
                }
            }
        }

        // The house numbers round the lot (Danmarks Adresseregister), each in the GeoDanmark building its address
        // belongs to (by the building's id), fitted by Label as the roofs are: one height for all (Label.Even), along
        // the building's long side, clear of the texts so far (the roof levels on the lot, the spot elevations, the
        // parcel numbers). Several numbers in one building are one text ("12, 14", or "2-16" for more than three). No
        // building, or no room in it: the number at its address point, moved up or down to the first place clear, else
        // left out. The drawing's own address is left out: the title has it. Within the window only.
        public void HouseNumbers()
        {
            if (!Shown(Layer.HouseNumbers) || addresses is null)
            {
                return;
            }
            bool InBox(double px, double py) => px >= box.X1 && px <= box.X2 && py >= box.Y1 && py <= box.Y2;
            var shown = addresses.Where(a => InBox(a.X, a.Y) && (Math.Abs(a.X - x) > 0.5 || Math.Abs(a.Y - y) > 0.5)).ToList();
            var byId = buildings.Where(b => b.Props.GetValueOrDefault("id.lokalId") is { Length: > 0 })
                .GroupBy(b => b.Props["id.lokalId"]).ToDictionary(g => g.Key, g => g.First().Rings[0]);
            // The numbers of a building, in one text: sorted by the number, then its letter.
            static string Joined(IEnumerable<string> numbers)
            {
                var sorted = numbers.Distinct().OrderBy(n => int.TryParse(new string(n.TakeWhile(char.IsDigit).ToArray()), out var i) ? i : int.MaxValue)
                    .ThenBy(n => n, StringComparer.Ordinal).ToList();
                return sorted.Count > 3 ? $"{sorted[0]}-{sorted[^1]}" : string.Join(", ", sorted);
            }
            var inBuildings = shown.Where(a => a.BuildingId is not null && byId.ContainsKey(a.BuildingId))
                .GroupBy(a => a.BuildingId!).Select(g => (Ring: byId[g.Key], Text: Joined(g.Select(a => a.Number)), Points: g.ToList())).ToList();
            var height = Label.Even([.. inBuildings.Select(i => (i.Ring, (IReadOnlyList<string>)[i.Text]))],
                [P(1.4), P(1.3), P(1.2), P(1.1), P(1.0)], 0.9);
            var loose = shown.Where(a => a.BuildingId is null || !byId.ContainsKey(a.BuildingId)).Select(a => (a.X, a.Y, Text: a.Number)).ToList();
            foreach (var (ring, text, points) in inBuildings)
            {
                var block = Label.Spots(ring, [text], height).FirstOrDefault(b => b.Placed().All(p =>
                    Clear((p.X, p.Y, b.Height, p.Text, Layer.HouseNumbers, b.AngleDeg, false), P(0.3))));
                if (block is null)
                {
                    // No room in the building: each number at its own point.
                    loose.AddRange(points.Select(a => (a.X, a.Y, Text: a.Number)));
                    continue;
                }
                foreach (var p in block.Placed())
                {
                    Text(p.X, p.Y, block.Height, p.Text, Layer.HouseNumbers, block.AngleDeg);
                }
                foreach (var _ in points)
                {
                    Drawn(Layer.HouseNumbers);
                }
            }
            foreach (var (px, py, text) in loose)
            {
                if (Place(new[] { 0.0, 1.5, -1.5, 3, -3 }.Select(dy => (px, py + P(dy), P(1.2), text, Layer.HouseNumbers, 0.0, false)), P(0.3)))
                {
                    Drawn(Layer.HouseNumbers);
                }
            }
        }

        // The street names (Danmarks Adresseregister), each once, along the register's own name line for the street:
        // the longest piece of it in the window, the name at its middle (else a third or two thirds along), turned
        // along it and read from below or the right, clear of the texts so far; no room, or no line in the window:
        // left out.
        public void StreetNames()
        {
            if (!Shown(Layer.StreetNames) || addresses is null)
            {
                return;
            }
            foreach (var street in addresses.Where(a => a.Street.Length > 0).GroupBy(a => a.Street))
            {
                var pieces = street.First().StreetLine.SelectMany(l => Geometry.ClipLine(l, box)).Where(p => p.Count > 1).ToList();
                if (pieces.Count == 0)
                {
                    continue;
                }
                var piece = pieces.OrderByDescending(Length).First();
                var total = Length(piece);
                var name = street.Key;
                if (Place(new[] { 0.5, 0.35, 0.65, 0.2, 0.8 }.Select(fraction =>
                    {
                        var (px, py, angle) = Along(piece, fraction * total);
                        return (px, py, P(1.8), name, Layer.StreetNames, Label.Upright(angle), false);
                    }), P(0.3)))
                {
                    Drawn(Layer.StreetNames);
                    streetSample ??= name;
                }
            }
        }

        // The covers' and gullies' levels, last of the plan's texts (the least important), beside their symbols as the
        // spot elevations' are: up right, else the first of the other corners clear; none clear, the symbol alone.
        public void CoverLevels()
        {
            foreach (var (px, py, layer, level) in pointLevels)
            {
                var dx = P(1.2) + P(1.2) * CharWidth * level.Length / 2;
                Place(new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) }.Select(c => (px + c.Item1 * dx, py + c.Item2 * P(1.4), P(1.2), level, layer, 0.0, false)), P(0.3));
            }
        }

        private static double Length(List<Pt> line) =>
            line.Zip(line.Skip(1), (a, b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y))).Sum();

        // The point $distance along $line from its start, and the line's direction there (degrees, counter-clockwise from east).
        private static (double X, double Y, double AngleDeg) Along(List<Pt> line, double distance)
        {
            var left = distance;
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var (a, b) = (line[i], line[i + 1]);
                var len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (left <= len || i + 2 == line.Count)
                {
                    var f = len > 0 ? Math.Clamp(left / len, 0, 1) : 0;
                    return (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
                }
                left -= len;
            }
            return (line[0].X, line[0].Y, 0);
        }

        // The own lot's label, last, so it keeps clear of every number: fitted by Label into its visible part, level or
        // along a road parcel, clear of its holes (the parcels inside it) and of the texts so far; on a building is fine.
        // No room: the middle (Geometry.LabelPoint), as before.
        public void OwnLotLabel()
        {
            if (ownLabel is { } start && own is not null)
            {
                bool Clear(Label.Block b) => !b.Placed().Any(p => texts.Any(o =>
                    Overlap(Bounds(o, 0), Bounds((p.X, p.Y, b.Height, p.Text, Layer.Text, b.AngleDeg, false), P(0.5)))));
                var spot = Label.Spots(start.Visible, [start.Label], P(2.5), [.. ownLot!.Rings.Skip(1)], Label.Strip(start.Visible))
                    .FirstOrDefault(b => Clear(b) && !b.Placed().Any(p => marks.Any(m => Overlap(m, Bounds((p.X, p.Y, b.Height, p.Text, Layer.Text, b.AngleDeg, false), P(0.5))))));
                Text(spot?.X ?? start.X, spot?.Y ?? start.Y, P(2.5), start.Label, rot: spot?.AngleDeg ?? 0);
            }
        }

        // The setback line: the boundary moved inwards and trimmed where it would overshoot (Geometry.Setback), dashed,
        // as open lines cut to the window. No label on the plan: the legend under the frame says what it is. No point
        // repeated at a line's end: a zero-length last segment made Revit lose the side next to it. In Revit the layer
        // gets a dashed line pattern from Jega (AddressCommand), as Revit's import shows the DXF's DASHED solid.
        public void SetbackLine()
        {
            if (own is not null && setbackM > 0 && Shown(Layer.Setback))
            {
                foreach (var piece in Geometry.Setback(own, setbackM).SelectMany(s => Geometry.ClipLine(s, box)))
                {
                    Poly(piece, Layer.Setback, false);
                    Drawn(Layer.Setback);
                }
            }
        }

        // Road edges, the paths' on their own layer (GeoDanmark's Vejkant type; a cycle track along a road stays a road's).
        public void RoadEdges()
        {
            foreach (var road in roads)
            {
                var path = PathTypes.Contains(road.Props.GetValueOrDefault("type", ""));
                foreach (var line in road.Rings)
                {
                    foreach (var piece in Geometry.ClipLine(line, box))
                    {
                        Poly(piece, path ? Layer.PathEdges : Layer.RoadEdges, false);
                        Drawn(path ? Layer.PathEdges : Layer.RoadEdges);
                    }
                }
            }
        }

        // Water: a lake's or basin's outline is a closed ring, a stream or ditch a line.
        public void Water()
        {
            foreach (var line in ((Shown(Layer.Water) ? water : null) ?? []).SelectMany(w => w.Rings))
            {
                if (line.Count > 3 && line[0] == line[^1])
                {
                    if (Geometry.ClipRing(line, box) is { Count: > 0 } c)
                    {
                        Poly(c, Layer.Water, true);
                        Drawn(Layer.Water);
                    }
                }
                else
                {
                    foreach (var piece in Geometry.ClipLine(line, box))
                    {
                        Poly(piece, Layer.Water, false);
                        Drawn(Layer.Water);
                    }
                }
            }
        }

        // GeoDanmark's manhole covers and road gullies in the window: a cover is a small circle (1.6 mm on paper), a gully
        // a small square (1.2 mm), each marked so texts keep clear of it; its level (from GeoDanmark's 3D point; a point
        // without "z" gets the symbol alone) is placed later, after the more important texts (CoverLevels).
        public void Manholes() => Points(Shown(Layer.Manholes) ? manholes : null, Layer.Manholes, Circle);

        public void Gullies() => Points(Shown(Layer.Gullies) ? gullies : null, Layer.Gullies, Square);

        // A circle of 1.6 mm on paper as a closed polyline (R12 has CIRCLE, but a polyline keeps the layer's look in
        // every reader and in the legend's sample).
        private void Circle(double cx, double cy, string layer)
        {
            var r = P(0.8);
            Poly([.. Enumerable.Range(0, 12).Select(i => (cx + r * Math.Cos(i * Math.PI / 6), cy + r * Math.Sin(i * Math.PI / 6)))], layer, true);
            Mark(cx, cy, r);
        }

        private void Square(double cx, double cy, string layer)
        {
            var d = P(0.6);
            Poly([(cx - d, cy - d), (cx + d, cy - d), (cx + d, cy + d), (cx - d, cy + d)], layer, true);
            Mark(cx, cy, d);
        }

        // The points of one layer in the window: their symbols now, their levels kept for CoverLevels.
        private void Points(List<SiteFetcher.Feature>? points, string layer, Action<double, double, string> symbol)
        {
            foreach (var (px, py, feature) in (points ?? []).Where(f => f.Rings.Count > 0 && f.Rings[0].Count > 0)
                         .Select(f => (f.Rings[0][0].X, f.Rings[0][0].Y, f)))
            {
                if (px < box.X1 || px > box.X2 || py < box.Y1 || py > box.Y2)
                {
                    continue;
                }
                symbol(px, py, layer);
                Drawn(layer);
                if (double.TryParse(feature.Props.GetValueOrDefault("z"), NumberStyles.Float, Inv, out var z))
                {
                    pointLevels.Add((px, py, layer, z.ToString("N2", language)));
                }
            }
        }

        // Frame, the cross at 0,0 (4 mm on paper) and the address point's (2 mm) when apart from it, north arrow (12 mm)
        // in the top right corner.
        public void FrameAndNorthArrow()
        {
            Poly([(box.X1, box.Y1), (box.X2, box.Y1), (box.X2, box.Y2), (box.X1, box.Y2)], Layer.Frame, true);
            bool InBox(double px, double py) => px > box.X1 && px < box.X2 && py > box.Y1 && py < box.Y2;
            // The cross at the drawing's 0,0.
            if (InBox(ox, oy))
            {
                Cross(ox, oy, apart ? Layer.Origin : Layer.AddressPoint);
            }
            if (apart && InBox(x, y))
            {
                Cross(x, y, Layer.AddressPoint);
            }
            var (ax, ay) = (box.X2 - P(12), box.Y2 - P(22));
            // The arrow points to true north: straight up, or turned with the drawing, its N read upright.
            Pt A(double dx, double dy) => Spin((ax + dx, ay + dy), turn, (ax, ay));
            Poly([A(-P(3), 0), A(0, P(12)), A(P(3), 0)], Layer.Frame, true);
            var n = A(0, P(14));
            Text(n.X, n.Y, P(4.0), "N", Layer.Frame, Label.Upright(turn));
        }

        // A cross on $layer, on the plan and in the legend: 4 mm on paper at the drawing's 0,0, 2 mm at the address
        // point apart from it.
        private void Cross(double cx, double cy, string layer)
        {
            var c2 = P((apart && layer == Layer.AddressPoint ? 2.0 : 4.0) / 2);
            Poly([(cx - c2, cy), (cx + c2, cy)], layer, false);
            Poly([(cx, cy - c2), (cx, cy + c2)], layer, false);
        }

        // The title and notes under the drawing, left-aligned on the frame's left edge: centred,
        // the long lines ran out past the frame on both sides. Written after the legend, wrapped (WriteNotes).
        public void Notes()
        {
            var z0 = Z(x, y);
            notes = new List<(string S, double PaperMm)>
            {
                (T(nameof(Strings.Drawing_Title), address), 4.0),
                (apart ? T(nameof(Strings.Drawing_OriginProject), ox, oy, geoX, geoY)
                    : T(nameof(Strings.Drawing_OriginAddress), geoX, geoY), 2.0),
                (T(nameof(Strings.Drawing_Credit), fetchedUtc?.ToLocalTime() ?? DateTime.Now), 2.0),
            };
            void Note(string s) => notes.Add((s, 2.0));
            if (turn != 0)
            {
                // The turn as -180 to 180: "turned -12°" reads better than "348°" for a project turned a little clockwise.
                Note(T(nameof(Strings.Drawing_Turned), Math.IEEERemainder(turn, 360)));
            }
            // The terrain, said plainly: the lot's lowest and highest (every cell of the height model on the lot), and the
            // level at the address point, which often lies
            // inside the house, where the height model only interpolates (on one test lot 9.99 inside the house, the lot 9.73 to 10.21).
            if (dtm is not null && ownLotGeo is not null && LotRange(dtm, ownLotGeo.Rings) is var (low, high))
            {
                // The difference of the two as written (rounded as N2 rounds them), so it adds up on the drawing.
                static double Written(double v) => double.Parse(v.ToString("F2", Inv), Inv);
                Note(T(nameof(Strings.Drawing_TerrainLot), low, high, Written(high) - Written(low)));
            }
            if (z0 is { } zz)
            {
                var inside = ownBuildings.Any(r => Geometry.Inside(r, x, y));
                Note(T(inside ? nameof(Strings.Drawing_TerrainAddressInBuilding) : nameof(Strings.Drawing_TerrainAddress), zz));
            }
            if (DrawnOn(Layer.RoofLevels) > 0)
            {
                Note(T(roofLook.RidgeOnly ? nameof(Strings.Drawing_NoteRoofRidge) : nameof(Strings.Drawing_NoteRoof)));
            }
            if (Shown(Layer.Buildings) || Shown(Layer.NeighbourBuildings))
            {
                Note(T(nameof(Strings.Drawing_Buildings)));
            }
            if (DrawnOn(Layer.Manholes) + DrawnOn(Layer.Gullies) > 0)
            {
                Note(T(nameof(Strings.Drawing_NoteManholes)));
            }
            if (DrawnOn(Layer.HouseNumbers) + DrawnOn(Layer.StreetNames) > 0)
            {
                Note(T(nameof(Strings.Drawing_NoteAddresses)));
            }
            if (DrawnOn(Layer.Trees) + DrawnOn(Layer.Hedges) > 0)
            {
                Note(T(nameof(Strings.Drawing_NoteVegetation)));
            }
            // The lot's area twice: registered in Matriklen (the legal figure) and as drawn, so the map's inaccuracy shows.
            if (own is not null && double.TryParse(ownLot!.Props.GetValueOrDefault("registreretAreal"), NumberStyles.Float, Inv, out var registered) && registered > 0)
            {
                var drawn = Geometry.Area(ownLot.Rings);
                var dev = (drawn - registered) / registered * 100;
                // The parcels in its holes, by name, so the drawing says what's left out.
                var inner = lots.Where(l => l != ownLot && Geometry.LotCentre(l.Rings[0]) is var (ix, iy) && Geometry.Inside(own!, ix, iy) && !OnLot(ix, iy))
                    .Select(l => T(nameof(Strings.Drawing_ParcelArea), l.Props.GetValueOrDefault("matrikelnummer", ""),
                        double.TryParse(l.Props.GetValueOrDefault("registreretAreal"), NumberStyles.Float, Inv, out var ia) ? ia : Geometry.Area(l.Rings)))
                    .ToList();
                var number = ownLot.Props.GetValueOrDefault("matrikelnummer", "");
                Note(inner.Count switch
                {
                    0 => T(nameof(Strings.Drawing_LotArea), number, registered, drawn, dev),
                    1 => T(nameof(Strings.Drawing_LotAreaInnerOne), number, registered, drawn, dev, inner[0]),
                    _ => T(nameof(Strings.Drawing_LotAreaInnerMany), number, registered, drawn, dev,
                        T(nameof(Strings.Drawing_ListAnd), string.Join(", ", inner.SkipLast(1)), inner[^1])),
                });
            }
        }

        // Right of the frame, level with its top: the legend in groups (a heading, then per row a sample 12 mm wide and
        // its text in a straight column after it), then the scale bar (about 50 mm on paper, a round length in quarters:
        // two hatched, one open). Placed from the frame and the scale only, so a larger window (more than 10 m round the
        // lot) takes the column along. Headings and texts left-aligned, so the column lines up.
        public void Legend()
        {
            left = box.X2 + P(10);
            row = box.Y2 - P(4);
            Text(left, row, P(2.0), T(nameof(Strings.Drawing_Legend)), left: true);
            row -= P(3);
            // Kind: "line" (a sample of the layer's line), "text" (the sample is a text as on the plan), "level", "cross",
            // "manhole", "gully", "tree", "roof". Always: whether the row is on the plan at all ($always: the lot, the
            // neighbours' boundaries, the parcel numbers and the crosses are the plan's bones; the others show only when
            // something was drawn on their layer).
            var (boundaries, ownAndNeighbours, points) = (T(nameof(Strings.Drawing_GroupBoundaries)), T(nameof(Strings.Drawing_GroupBuildings)), T(nameof(Strings.Drawing_GroupPoints)));
            var (roadsGroup, vegetation) = (T(nameof(Strings.Drawing_GroupRoads)), T(nameof(Strings.Drawing_GroupVegetation)));
            var legend = new List<(string Group, string Kind, string Layer, string Sample, string Text, bool Always)>
            {
                (boundaries, "line", Layer.Lot, "", T(nameof(Strings.Drawing_RowLot)), true),
                (boundaries, "line", Layer.Neighbours, "", T(nameof(Strings.Drawing_RowNeighbourLots)), true),
                (boundaries, "line", Layer.Setback, "", T(nameof(Strings.Drawing_RowSetback), setbackM), false),
                (boundaries, "text", Layer.Lengths, 21.99.ToString("N2", language), T(nameof(Strings.Drawing_RowLengths)), true),
                (boundaries, "text", Layer.Text, ownLot?.Props.GetValueOrDefault("matrikelnummer", "") is { Length: > 0 } ownNumber ? ownNumber : "12a", T(nameof(Strings.Drawing_RowParcelNumber)), true),
                (ownAndNeighbours, "line", Layer.Buildings, "", T(nameof(Strings.Drawing_RowOwnBuildings)), true),
                (ownAndNeighbours, "line", Layer.NeighbourBuildings, "", T(nameof(Strings.Drawing_RowNeighbourBuildings)), true),
                (ownAndNeighbours, "text", Layer.HouseNumbers, "12", T(nameof(Strings.Drawing_RowHouseNumber)), false),
                (roadsGroup, "line", Layer.RoadEdges, "", T(nameof(Strings.Drawing_RowRoadEdge)), true),
                (roadsGroup, "line", Layer.PathEdges, "", T(nameof(Strings.Drawing_RowPath)), false),
                (roadsGroup, "text", Layer.StreetNames, streetSample ?? "", T(nameof(Strings.Drawing_RowStreetName)), false),
                (T(nameof(Strings.Drawing_GroupWater)), "line", Layer.Water, "", T(nameof(Strings.Drawing_RowWater)), false),
                (vegetation, "tree", Layer.Trees, "", T(nameof(Strings.Drawing_RowTrees)), false),
                (vegetation, "line", Layer.Hedges, "", T(nameof(Strings.Drawing_RowHedges)), false),
                (points, "level", Layer.Levels, 9.92.ToString("N2", language), T(nameof(Strings.Drawing_RowElevation)), false),
                (points, "manhole", Layer.Manholes, 2.27.ToString("N2", language), T(nameof(Strings.Drawing_RowManhole)), false),
                (points, "gully", Layer.Gullies, 2.15.ToString("N2", language), T(nameof(Strings.Drawing_RowGully)), false),
                roofLook.RidgeOnly
                    ? (points, "roof", Layer.RoofLevels, T(nameof(Strings.Drawing_RoofRidge), 14.37), T(nameof(Strings.Drawing_RowRoofRidge)), false)
                    : (points, "roof", Layer.RoofLevels, T(nameof(Strings.Drawing_RoofRidge), 14.37) + "\n" + T(nameof(Strings.Drawing_RoofRidge), 11.2),
                        T(nameof(Strings.Drawing_RowRoof)), false),
                (points, "cross", Layer.Origin, "", T(nameof(Strings.Drawing_RowOriginProject)), true),
                (points, "cross", Layer.AddressPoint, "", T(apart ? nameof(Strings.Drawing_RowAddressPoint)
                    : nameof(Strings.Drawing_RowOriginAddress)), true),
            };
            string? group = null;
            foreach (var (g, kind, layer, sample, text, always) in legend)
            {
                if (!Shown(layer) || (!always && DrawnOn(layer) == 0))
                {
                    continue;  // left out, or nothing of it on this plan
                }
                if (g != group)
                {
                    row -= P(5);
                    Text(left, row, P(1.8), g, left: true);
                    group = g;
                }
                row -= P(4.5);
                var mid = left + P(6);
                switch (kind)
                {
                    case "level":
                        var d = P(0.8);
                        Poly([(left + d, row - d), (left + 3 * d, row + d)], layer, false);
                        Poly([(left + d, row + d), (left + 3 * d, row - d)], layer, false);
                        Text(left + 4 * d + P(0.8), row, P(1.4), sample, layer, left: true);
                        break;
                    case "cross":
                        Cross(mid, row, layer);
                        break;
                    case "manhole":
                        Circle(left + P(1.6), row, layer);
                        Text(left + P(4.0), row, P(1.2), sample, layer, left: true);
                        break;
                    case "gully":
                        Square(left + P(1.6), row, layer);
                        Text(left + P(4.0), row, P(1.2), sample, layer, left: true);
                        break;
                    case "tree":
                        TreeSymbol(left + P(1.6), row);
                        Poly([(left + P(5), row), (left + P(12), row)], layer, false);
                        break;
                    case "text":
                        Text(mid, row, P(1.4), sample, layer);
                        break;
                    case "roof":
                        // As on the plan: the ridge over the eaves (the sample's two lines), or the ridge alone.
                        var roofLines = sample.Split('\n');
                        for (var i = 0; i < roofLines.Length; i++)
                        {
                            Text(mid, row + ((roofLines.Length - 1) / 2.0 - i) * Label.LineStep * roofHeight, roofHeight, roofLines[i], layer);
                        }
                        break;
                    default:
                        Poly([(left, row), (left + P(12), row)], layer, false);
                        break;
                }
                Text(left + P(16), row, P(2.0), text, left: true);
            }
        }

        // The scale and the scale bar under the legend.
        public void ScaleBar()
        {
            var scaleTop = row - P(9);
            Text(left, scaleTop, P(2.0), T(nameof(Strings.Drawing_Scale), scale), left: true);
            var len = new[] { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500 }.Where(n => n <= P(50)).DefaultIfEmpty(1).Max();
            (var sx, sy, var bh) = (left, scaleTop - P(10), P(2.0));
            foreach (var (f0, f1, filled) in new[] { (0.0, 0.25, true), (0.25, 0.5, false), (0.5, 1.0, true) })
            {
                Poly([(sx + f0 * len, sy), (sx + f1 * len, sy), (sx + f1 * len, sy + bh), (sx + f0 * len, sy + bh)], Layer.Frame, true);
                for (var h = f0 * len + P(1); filled && h < f1 * len; h += P(1))
                {
                    Poly([(sx + h, sy), (sx + h, sy + bh)], Layer.Frame, false);
                }
            }
            foreach (var f in new[] { 0, 0.25, 0.5, 1.0 })
            {
                Text(sx + f * len, sy + bh + P(2.5), P(2.0), (f * len).ToString(language), Layer.Frame);
            }
            Text(sx + len + P(3), sy + bh / 2, P(2.0), "m", Layer.Frame, left: true);
        }

        // The notes, wrapped to the frame's width, so a long line doesn't run under the legend and the scale bar; also
        // under the legend's column when that ends above the frame's bottom.
        public void WriteNotes()
        {
            var notesWidth = box.X2 - box.X1 + (sy > box.Y1 ? P(10) + P(LegendMm) : 0);
            var ny = box.Y1 - P(6);
            foreach (var (s, h) in notes)
            {
                foreach (var line in Wrap(s, (int)(notesWidth / (P(h) * CharWidth))))
                {
                    ny -= P(h / 2);
                    Text(box.X1, ny, P(h), line, left: true);
                    ny -= P(h / 2 + 2);
                }
            }
        }

        // The layers left out that weren't skipped above (the neighbours' boundaries, the buildings, the road and path edges,
        // the cross): taken out here. Then the own lot last, on top of the neighbours' boundaries that share its edges.
        public string Finish()
        {
            lines = [.. lines.Where(l => Shown(l.Layer))];
            texts = [.. texts.Where(t => Shown(t.Layer))];
            lines = [.. lines.Where(l => l.Layer != Layer.Lot), .. lines.Where(l => l.Layer == Layer.Lot)];
            return File(lines, texts, ox, oy, (P(look.SetbackDashMm), P(look.SetbackGapMm)), hidden);
        }
    }

    // The smallest plot scale where the frame (the lot and the margin round it) fits the A3 window; the largest
    // (1:5000) when none fits, as a lot up to about 2 km across can be loaded. Not the lot at most 40 % of the
    // window: Jega's frame is the lot + the margin, not the window, so a lot just too big for 1:2000 got
    // 1:5000 and texts 7 m tall on a 150 m frame.
    private static int Scale((double X1, double Y1, double X2, double Y2) box)
    {
        var (w, h) = (box.X2 - box.X1, box.Y2 - box.Y1);
        var scales = Config.Current.Siteplan.Scales;
        return scales.Where(s => w <= 2 * WindowMm.W * s / 1000 && h <= 2 * WindowMm.H * s / 1000)
            .DefaultIfEmpty(scales[^1]).First();
    }

    // A text's width per character, as a share of its height (Label's, so a wrapped line stays inside), and the
    // legend column's width on paper (its sample, text and the scale bar), mm.
    private const double CharWidth = Label.CharWidth;
    private const double LegendMm = 70;

    // A text of the drawing's box (m), grown by $pad on every side (Label.Bounds).
    private static (double X1, double Y1, double X2, double Y2) Bounds((double X, double Y, double H, string S, string Layer, double Rot, bool Left) t, double pad) =>
        Label.Bounds(t.X, t.Y, t.H, t.S, t.Rot, t.Left, pad);

    private static bool Overlap((double X1, double Y1, double X2, double Y2) a, (double X1, double Y1, double X2, double Y2) b) => Label.Overlap(a, b);

    // The text cut into lines of at most $chars characters, at spaces; a word longer than that gets a line of its own.
    private static IEnumerable<string> Wrap(string text, int chars)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > Math.Max(chars, 1))
            {
                yield return line;
                line = "";
            }
            line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0)
        {
            yield return line;
        }
    }

    // The boundary lengths (SKELMAAL): each boundary's length along it, just outside the lot, never upside down; under 8 mm on paper or
    // outside the window skipped.
    private static void LotDimensions(List<Pt> ring, (double X1, double Y1, double X2, double Y2) box, Func<double, double> p,
        CultureInfo language, Action<double, double, double, string, string, double> text)
    {
        var side = Geometry.Area(ring) > 0 ? 1 : -1;  // counter-clockwise: the inside is on the left
        for (var i = 0; i + 1 < ring.Count; i++)
        {
            var (a, b) = (ring[i], ring[i + 1]);
            var len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            var (mx, my) = ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            if (len < p(8) || mx < box.X1 || mx > box.X2 || my < box.Y1 || my > box.Y2)
            {
                continue;
            }
            var (nx, ny) = (-(b.Y - a.Y) / len * side, (b.X - a.X) / len * side);  // into the lot
            var angle = Label.Upright(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            text(mx - nx * p(1.4), my - ny * p(1.4), p(1.4), len.ToString("N2", language), Layer.Lengths, angle);
        }
    }

    // The lowest and highest terrain on the lot: every cell of the height model whose centre lies on it (not in a hole).
    private static (double Low, double High)? LotRange(SiteFetcher.Dtm dtm, List<List<Pt>> lot)
    {
        var zs = new List<double>();
        for (var j = 0; j < dtm.H; j++)
        {
            for (var i = 0; i < dtm.W; i++)
            {
                double z = dtm.Z[j * dtm.W + i];
                if (HeightCells.Valid(z) && HeightCells.Centre(dtm, i, j) is var (cx, cy) && Geometry.Inside(lot, cx, cy))
                {
                    zs.Add(z);
                }
            }
        }
        return zs.Count > 0 ? (zs.Min(), zs.Max()) : null;
    }

    // The whole file: every table AutoCAD writes in R12. Revit's CAD import failed
    // on a file with only LTYPE and LAYER ("An internal error has occurred"). $dash: the setback line's
    // dash and gap (m). $hidden: the layers left out, not in the layer table either, so the import has no empty
    // layer of them for Visibility/Graphics to list.
    private static string File(List<(List<Pt> Pts, string Layer, bool Closed)> lines, List<(double X, double Y, double H, string S, string Layer, double Rot, bool Left)> texts,
        double ox, double oy, (double Dash, double Gap) dash, IReadOnlySet<string> hidden)
    {
        static string F(double v) => v.ToString("0.000", Inv);
        string X(double v) => F((v - ox) * 1000);
        string Y(double v) => F((v - oy) * 1000);

        var e = new List<string>();
        foreach (var (pts, layer, closed) in lines)
        {
            e.Add($"0\nPOLYLINE\n8\n{layer}\n66\n1\n70\n{(closed ? 1 : 0)}\n10\n0.0\n20\n0.0\n30\n0.0");
            e.AddRange(pts.Select(p => $"0\nVERTEX\n8\n{layer}\n10\n{X(p.X)}\n20\n{Y(p.Y)}\n30\n0.0"));
            e.Add($"0\nSEQEND\n8\n{layer}");
        }
        foreach (var (tx, ty, h, s, layer, rot, left) in texts)
        {
            // R12 is not UTF-8: æøå as \U+ escapes, which AutoCAD and most readers understand. Aligned on the point
            // (11/21): centred (72 = 1) or from the left (72 = 0), vertically in the middle (73 = 2).
            var text = Regex.Replace(s, @"[^\x20-\x7E]", m => $"\\U+{(int)m.Value[0]:X4}");
            e.Add($"0\nTEXT\n8\n{layer}\n10\n{X(tx)}\n20\n{Y(ty)}\n30\n0.0\n40\n{F(h * 1000)}\n1\n{text}\n50\n{rot.ToString("0.00", Inv)}\n72\n{(left ? 0 : 1)}\n73\n2\n11\n{X(tx)}\n21\n{Y(ty)}\n31\n0.0");
        }

        var all = lines.SelectMany(l => l.Pts).Concat(texts.Select(t => (t.X, t.Y))).ToList();
        var (minX, minY) = ((all.Min(p => p.X) - ox) * 1000, (all.Min(p => p.Y) - oy) * 1000);
        var (maxX, maxY) = ((all.Max(p => p.X) - ox) * 1000, (all.Max(p => p.Y) - oy) * 1000);
        string Xy(int c, double x, double y) => $"1{c}\n{F(x)}\n2{c}\n{F(y)}";
        string Var(string name, string value) => $"9\n${name}\n{value}\n";
        var header = Var("ACADVER", "1\nAC1009") + Var("DWGCODEPAGE", "3\nANSI_1252")
            + Var("INSBASE", Xy(0, 0, 0) + "\n30\n0.000") + Var("EXTMIN", Xy(0, minX, minY) + "\n30\n0.000") + Var("EXTMAX", Xy(0, maxX, maxY) + "\n30\n0.000")
            + Var("LIMMIN", Xy(0, minX, minY)) + Var("LIMMAX", Xy(0, maxX, maxY))
            + Var("CLAYER", "8\n0") + Var("TEXTSTYLE", "7\nSTANDARD") + Var("CELTYPE", "6\nBYLAYER");

        static string Table(string name, IReadOnlyCollection<string> entries) =>
            $"0\nTABLE\n2\n{name}\n70\n{entries.Count}\n" + string.Concat(entries.Select(x => $"0\n{name}\n{x}\n")) + "0\nENDTAB\n";
        // Opens zoomed to the drawing: the active viewport centred on it, a little taller than it.
        var (width, height) = (Math.Max(maxX - minX, 1), Math.Max(maxY - minY, 1));
        var vport = $"2\n*ACTIVE\n70\n0\n{Xy(0, 0, 0)}\n{Xy(1, 1, 1)}\n{Xy(2, (minX + maxX) / 2, (minY + maxY) / 2)}\n{Xy(3, 0, 0)}\n{Xy(4, 1, 1)}\n"
            + $"{Xy(5, 0, 0)}\n16\n0.0\n26\n0.0\n36\n1.0\n17\n0.0\n27\n0.0\n37\n0.0\n"
            + $"40\n{F(Math.Max(height, width / 1.5) * 1.1)}\n41\n1.5\n42\n50.0\n43\n0.0\n44\n0.0\n50\n0.0\n51\n0.0\n"
            + "71\n0\n72\n100\n73\n1\n74\n3\n75\n0\n76\n0\n77\n0\n78\n0";
        var (dashMm, gapMm) = (dash.Dash * 1000, dash.Gap * 1000);
        string[] ltypes =
        [
            "2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0.0",
            $"2\nDASHED\n70\n0\n3\n__ __ __\n72\n65\n73\n2\n40\n{F(dashMm + gapMm)}\n49\n{F(dashMm)}\n49\n{F(-gapMm)}",
        ];
        var layers = Config.Current.Siteplan.Layers.Where(l => !hidden.Contains(l.Name)).Prepend(new DxfLayer("0", 7))
            .Select(l => $"2\n{l.Name}\n70\n0\n62\n{l.Colour}\n6\n{(l.Name == Layer.Setback ? "DASHED" : "CONTINUOUS")}").ToList();
        var style = "2\nSTANDARD\n70\n0\n40\n0.0\n41\n1.0\n50\n0.0\n71\n0\n42\n2.5\n3\ntxt\n4\n";

        return new StringBuilder()
            .Append($"0\nSECTION\n2\nHEADER\n{header}0\nENDSEC\n")
            .Append("0\nSECTION\n2\nTABLES\n").Append(Table("VPORT", [vport])).Append(Table("LTYPE", ltypes)).Append(Table("LAYER", layers))
            .Append(Table("STYLE", [style])).Append(Table("VIEW", [])).Append(Table("UCS", [])).Append(Table("APPID", ["2\nACAD\n70\n0"])).Append("0\nENDSEC\n")
            .Append("0\nSECTION\n2\nBLOCKS\n0\nENDSEC\n")
            .Append("0\nSECTION\n2\nENTITIES\n").Append(string.Join("\n", e)).Append("\n0\nENDSEC\n0\nEOF\n")
            .ToString();
    }
}

// Plane geometry (Geometry) and fitting text into shapes (Label): Util.
