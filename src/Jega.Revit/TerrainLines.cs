using System.Globalization;
using Autodesk.Revit.DB;
using Jega.Core;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>
/// The terrain lines, a calculation layer: detail lines of the style "Jega Line" or "… (elevations only)", in
/// any plan view, get labels from the terrain grid. Both: an elevation at every corner and end (terrain + the
/// project's offset). The full style also: the fall and slope of every piece with an arrow downhill, and HIGH /
/// LOW where the line's highest and lowest points are. "Elevations only" (what Terrain Line draws) shows nothing else.
/// Lines that meet end to end are one line; an arc is one piece. An update removes the labels of the views it is
/// given (the buttons: all) and makes them again, so they always match the lines; run by the updater for the views
/// where a terrain line changed, and by the buttons.
/// </summary>
internal static class TerrainLines
{
    public const string LabelRole = "LineLabel";

    // A short elevations-only line the Terrain Line button selects for Create Similar, so Revit's Detail Line tool
    // starts in that style (the API can't choose the tool's style itself). Never labelled; removed as soon as a
    // real line is drawn (the updater) or the button is clicked again.
    public const string SeedRole = "LineSeed";

    /// <summary>Inside a transaction (or an updater). Deletes the Create Similar seeds; not one someone else is holding.</summary>
    public static void RemoveSeeds(Document doc)
    {
        var seeds = ProjectStore.Marked(doc, SeedRole, typeof(CurveElement)).Where(s => Held(doc, s.Id) is null).ToList();
        if (seeds.Count > 0)
        {
            doc.Delete(seeds.Select(e => e.Id).ToList());
        }
    }


    /// <summary>
    /// Inside a transaction (or an updater). Removes and remakes every terrain line label, and its cross (the
    /// annotation symbol CrossSymbol, if loaded: not a Line, so the updater never hears it, as with the texts).
    /// Returns why nothing was updated when the stored terrain can't be read (the labels are then left as they are,
    /// never removed without a word); null otherwise. $views: only the labels in these views (the updater: where a
    /// line changed), so the rest of the model, and other people's views in a shared one, are left alone; null: all.
    /// </summary>
    public static string? Update(Document doc, IReadOnlySet<ElementId>? views = null)
    {
        var hasTerrain = StoredTerrain.TryGet(doc, out var stored, out var grid, out _);
        if (stored is not null && !hasTerrain)
        {
            return Strings.TerrainLine_Unreadable;
        }
        bool InScope(Element e) => views is null || views.Contains(e.OwnerViewId);
        var old = ProjectStore.Marked(doc, LabelRole, typeof(TextNote)).Concat(ProjectStore.Marked(doc, LabelRole, typeof(FamilyInstance)))
            .Where(InScope).ToList();
        // A shared model: a view where someone else is editing the labels (borrowed them), or where they changed in the
        // central model since the last Reload Latest, is left as it is and said: deleting them would refuse the whole
        // edit, also the user's own line elsewhere.
        var problems = new List<string>();
        var held = new HashSet<ElementId>();
        if (doc.IsWorkshared)  // Held says null otherwise; this spares asking for every label
        {
            foreach (var e in old)
            {
                if (held.Contains(e.OwnerViewId) || Held(doc, e.Id) is not { } owner)
                {
                    continue;
                }
                held.Add(e.OwnerViewId);
                problems.Add(Ui.Fill(owner.Length > 0 ? Strings.TerrainLine_Borrowed : Strings.TerrainLine_NotCurrent,
                    doc.GetElement(e.OwnerViewId)?.Name ?? "", owner));
            }
            old = old.Where(e => !held.Contains(e.OwnerViewId)).ToList();
        }
        if (old.Count > 0)
        {
            doc.Delete(old.Select(e => e.Id).ToList());
        }
        string? Said() => problems.Count == 0 ? null : string.Join("\n", problems);
        // The style is the option: the full one shows the falls, "elevations only" doesn't.
        var styles = LineStyles.TerrainLineStyles(doc);
        var curves = styles.Count == 0 || stored is null || grid is null ? []
            : new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>()
                .Where(c => c.ViewSpecific && InScope(c) && c.LineStyle is { } s && styles.ContainsKey(s.Id) && c.GeometryCurve is { IsBound: true }
                            && !ProjectStore.Is(c, SeedRole))
                .ToList();
        // Labelled now: all but the views left as they are.
        curves = curves.Where(c => !held.Contains(c.OwnerViewId)).ToList();
        if (curves.Count == 0 || stored is null || grid is null)
        {
            return Said();
        }
        var placement = Placement.Get(doc);
        var (textType, textMm) = TextType(doc);
        var cross = CrossSymbol.Find(doc);
        // Lines of the two styles are never joined into one: each keeps its own option. The full style first, so where
        // chains of the two meet, the corner's label is its own (HIGH / LOW with it), and the other chain's isn't written
        // again on top of it ("10,08" twice, "9,93" on "LOW 9,93").
        var labelled = new Dictionary<ElementId, List<(XYZ At, Element Text)>>();
        foreach (var group in curves.GroupBy(c => (c.OwnerViewId, c.LineStyle.Id)).OrderBy(g => styles[g.Key.Id] ? 0 : 1))
        {
            // Plan views only: the terrain's place is a plan's (in a section or a drafting view the elevations would be wrong).
            if (doc.GetElement(group.Key.OwnerViewId) is ViewPlan view)
            {
                if (!labelled.TryGetValue(view.Id, out var inView))
                {
                    labelled[view.Id] = inView = [];
                }
                foreach (var chain in Chains(group.ToList()))
                {
                    Label(doc, view, chain, grid, placement, stored.LastOffsetMm, textType, textMm, cross, styles[group.Key.Id], inView);
                }
            }
        }
        return Said();
    }

    /// <summary>
    /// Why Jega mustn't change or delete $id in a shared model: someone else is editing it (their name), or it changed
    /// in the central model since the last Reload Latest (""); null when it may, and always in a model that isn't
    /// shared. Revit answers from what this copy knows (since its last Reload Latest), so it is a good guess, not a
    /// guarantee. A model it can't answer for (e.g. detached keeping its worksets): null, as there is no one else.
    /// </summary>
    public static string? Held(Document doc, ElementId id)
    {
        if (!doc.IsWorkshared)
        {
            return null;
        }
        try
        {
            if (WorksharingUtils.GetCheckoutStatus(doc, id, out var owner) == CheckoutStatus.OwnedByOtherUser)
            {
                return owner;
            }
            return WorksharingUtils.GetModelUpdatesStatus(doc, id) is ModelUpdatesStatus.UpdatedInCentral or ModelUpdatesStatus.DeletedInCentral
                ? "" : null;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    // Jega's label text type, marked: found again after it is renamed for another size.
    private const string TextTypeRole = "LabelText";

    /// <summary>
    /// The labels' text type, or null before the first label: the marked one (one with a name of Jega's first: Edit
    /// Type → Duplicate copies the mark), else Config's LabelTextType by name (made before it was marked). Never an
    /// office's own type, so Settings never resizes one; never makes one, so Settings can call it.
    /// </summary>
    public static TextNoteType? FindTextType(Document doc) =>
        ProjectStore.Marked(doc, TextTypeRole, typeof(TextNoteType)).OfType<TextNoteType>().OrderBy(t => IsJegaName(t.Name) ? 0 : 1).FirstOrDefault()
        ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(t => t.Name == Config.Current.Revit.LabelTextType);

    /// <summary>The labels' text height on paper, mm: the type's Text Size (Edit Type may have changed it), else Settings'.</summary>
    public static double TextSizeMm(Document doc) =>
        FindTextType(doc)?.get_Parameter(BuiltInParameter.TEXT_SIZE) is { } p ? Units.MmFromFeet(p.AsDouble()) : ProjectStore.ReadOptions(doc).LabelTextMm;

    /// <summary>
    /// Inside a transaction (Settings): the labels' text type to $mm, renamed to say so ("Jega 2.5mm") when it still has
    /// a name of Jega's and the new one is free. No type yet: it is made at the size with the first label.
    /// </summary>
    public static void SetTextSizeMm(Document doc, double mm)
    {
        if (FindTextType(doc) is not { } type)
        {
            return;
        }
        // Marked now, so it is found again under its new name (one from before the mark has only Config's name).
        if (!ProjectStore.Is(type, TextTypeRole))
        {
            ProjectStore.Mark(type, TextTypeRole);
        }
        type.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(Units.FeetFromMm(mm));
        var name = TextTypeName(mm);
        if (type.Name != name && IsJegaName(type.Name)
            && !new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Any(t => t.Name == name))
        {
            type.Name = name;
        }
    }

    // "Jega 3mm" for 3 mm: Config's name at its size, else the same pattern.
    private static string TextTypeName(double mm) =>
        Math.Abs(mm - Config.Current.Revit.LabelTextMm) < 1e-6 ? Config.Current.Revit.LabelTextType : $"Jega {mm.ToString("0.##", CultureInfo.InvariantCulture)}mm";

    // A name Jega gave (Config's, or "Jega <size>mm"): one the user gave in Edit Type is kept.
    private static bool IsJegaName(string name) =>
        name == Config.Current.Revit.LabelTextType || (name.StartsWith("Jega ", StringComparison.Ordinal) && name.EndsWith("mm", StringComparison.Ordinal)
            && double.TryParse(name[5..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out _));

    /// <summary>
    /// The labels' text type (FindTextType); none yet: made as a copy of the default text type at Settings' "Label
    /// text size" and marked. Its size changes only from Settings, so what is set in Edit Type stays until then.
    /// </summary>
    private static (ElementId Id, double SizeMm) TextType(Document doc)
    {
        if (FindTextType(doc) is { } existing)
        {
            // Marked when it isn't yet, unless someone else is holding it: then used as it is.
            if (!ProjectStore.Is(existing, TextTypeRole) && Held(doc, existing.Id) is null)
            {
                ProjectStore.Mark(existing, TextTypeRole);
            }
            return (existing.Id, Units.MmFromFeet(existing.get_Parameter(BuiltInParameter.TEXT_SIZE).AsDouble()));
        }
        var size = ProjectStore.ReadOptions(doc).LabelTextMm;
        var source = doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType
            ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
        // The name taken by an office's own type: "Jega 3mm (2)", (3) … instead.
        var taken = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Select(t => t.Name).ToHashSet();
        var name = TextTypeName(size);
        for (var n = 2; taken.Contains(name); n++)
        {
            name = $"{TextTypeName(size)} ({n})";
        }
        var type = (TextNoteType)source.Duplicate(name);
        type.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(Units.FeetFromMm(size));
        ProjectStore.Mark(type, TextTypeRole);
        return (type.Id, size);
    }


    // full: "Jega Line", with falls and HIGH / LOW; otherwise "elevations only": just the elevation at every vertex.
    // Both: a cross where each elevation is for.
    // Every label keeps the lines of the pieces it is at (ProjectStore.MarkLines): a vertex both its pieces, a fall its
    // piece. So every line of the chain is on a label in the view, which is all the updater needs (ARCHITECTURE §5.1).
    // $labelled: the vertices labelled in this view so far, by other chains; one of them is this chain's corner too, its
    // label takes this chain's lines and no second one is written.
    private static void Label(Document doc, View view, List<Piece> pieces, TerrainGrid grid, Placement placement, int offset,
        ElementId textType, double textMm, FamilySymbol? cross, bool full, List<(XYZ At, Element Text)> labelled)
    {
        var chain = pieces.Select(p => p.Curve).ToList();
        Element Text(XYZ at, string text, double rotation, VerticalTextAlignment vertical, params string[] lines)
        {
            var note = TerrainLines.Text(doc, view, at, text, rotation, textType, vertical);
            ProjectStore.MarkLines(note, lines);
            return note;
        }
        void Cross(XYZ at, params string[] lines)
        {
            if (TerrainLines.Cross(doc, view, at, cross) is { } made)
            {
                ProjectStore.MarkLines(made, lines);
            }
        }
        var scale = view.Scale;
        double Paper(double mm) => Units.FeetFromMm(mm * scale);
        // The texts' distances from the line were set for Config's text height: they grow and shrink with the text.
        double Away(double mm) => Paper(mm * textMm / Config.Current.Revit.LabelTextMm);
        var points = chain.Select(c => (IReadOnlyList<Point2>)c.Tessellate().Select(placement.ToCsv).ToList()).ToList();
        LineLevelsResult r;
        try
        {
            r = LineLevels.Compute(grid, points, offset);
        }
        catch (TerrainGridException e)
        {
            // The one label of a chain off the terrain: it has all its lines.
            Text(chain[0].GetEndPoint(0) + view.UpDirection * Away(3), e.Message, 0, VerticalTextAlignment.Bottom, pieces.Select(p => p.Line).ToArray());
            return;
        }

        var marks = full && r.Highest.Terrain - r.Lowest.Terrain >= 1;
        string Value(LevelPoint p) => offset == 0 ? Units.Level(p.Terrain) : $"{Units.Level(p.Level)} ({Units.Level(p.Terrain)})";
        string Mark(LevelPoint p) => !marks ? "" : p == r.Highest ? Strings.TerrainLine_High + " " : p == r.Lowest ? Strings.TerrainLine_Low + " " : "";

        // An elevation at every vertex, set off away from the line: along the outside of the corner, or past an end.
        for (var i = 0; i < r.Vertices.Count; i++)
        {
            var v = r.Vertices[i];
            var at = placement.Point(v.X, v.Y, chain[0].GetEndPoint(0).Z);
            XYZ? outgoing = i < chain.Count ? Tangent(chain[i], 0) : null;
            XYZ? back = i > 0 ? -Tangent(chain[i - 1], 1) : r.Closed ? -Tangent(chain[^1], 1) : null;
            var away = (outgoing, back) switch
            {
                ({ } o, { } b) => -(o + b),
                ({ } o, null) => -o,
                (null, { } b) => -b,
                _ => XYZ.BasisY,
            };
            if (away.GetLength() < 1e-6)
            {
                away = view.ViewDirection.CrossProduct(outgoing!);  // a straight joint: to the side
            }
            // The vertex's pieces: the one starting here, and the one ending here (round a closed chain, the last).
            string[] lines = (i < pieces.Count, i > 0 ? i - 1 : r.Closed ? pieces.Count - 1 : -1) switch
            {
                (true, >= 0 and var b) => [pieces[i].Line, pieces[b].Line],
                (true, _) => [pieces[i].Line],
                (false, >= 0 and var b) => [pieces[b].Line],
                _ => [],
            };
            // Labelled already by another chain meeting here (within 10 mm): that label holds this chain's lines too.
            if (labelled.FirstOrDefault(l => Units.Flat(l.At, at) < Units.FeetFromMm(10)) is { Text: { } shared })
            {
                ProjectStore.MarkLines(shared, ProjectStore.LinesOf(shared).Concat(lines).Distinct());
                continue;
            }
            labelled.Add((at, Text(at + away.Normalize() * Away(5), Mark(v) + Value(v), 0, VerticalTextAlignment.Middle, lines)));
            Cross(at, lines);
        }

        // The piece a point between vertices is on, by its distance along the chain (both in mm, LineLevels').
        string PieceAt(double distance)
        {
            var along = 0.0;
            foreach (var f in r.Falls)
            {
                along += f.Length;
                if (distance <= along + 1e-6)
                {
                    return pieces[f.Piece].Line;
                }
            }
            return pieces[^1].Line;
        }

        // HIGH and LOW between vertices get a label of their own.
        foreach (var p in new[] { r.Highest, r.Lowest }.Where(p => marks && p.Corner is null))
        {
            var at = placement.Point(p.X, p.Y, chain[0].GetEndPoint(0).Z);
            Text(at + view.UpDirection * Away(2), Mark(p) + Value(p), 0, VerticalTextAlignment.Bottom, PieceAt(p.Distance));
            Cross(at, PieceAt(p.Distance));
        }

        // The fall of every piece along it, above its middle, readable, with the arrow pointing downhill. Worked out from
        // its ends' levels as written (to the cm), so it adds up on the drawing: 9,98 and 9,93 give 50 mm, not the 55 mm
        // the unrounded terrain has (as the site plan's note does).
        double Written(LevelPoint p) => Units.LevelAsWritten(offset == 0 ? p.Terrain : p.Level);
        foreach (var f in full ? r.Falls : [])
        {
            var curve = chain[f.Piece];
            var tangent = Tangent(curve, 0.5);
            var reading = tangent.DotProduct(view.RightDirection) >= -1e-9 ? tangent : -tangent;
            var angle = Math.Atan2(reading.DotProduct(view.UpDirection), reading.DotProduct(view.RightDirection));
            var normal = view.ViewDirection.CrossProduct(reading).Normalize();
            var fall = Written(r.Vertices[(f.Piece + 1) % r.Vertices.Count]) - Written(r.Vertices[f.Piece]);
            var permille = f.Length > 0 ? fall / f.Length * 1000 : 0;
            var text = Math.Abs(fall) < 0.5
                ? $"0 mm · {Units.Permille(0)}"
                : string.Create(Units.Numbers, $"{((fall < 0) == reading.IsAlmostEqualTo(tangent) ? "→" : "←")} {Math.Abs(fall):0} mm · {Units.Permille(Math.Abs(permille))}");
            Text(curve.Evaluate(0.5, true) + normal * Away(1), text, angle, VerticalTextAlignment.Bottom, pieces[f.Piece].Line);
        }
    }

    private static TextNote Text(Document doc, View view, XYZ at, string text, double rotation, ElementId type, VerticalTextAlignment vertical)
    {
        var options = new TextNoteOptions(type)
        {
            HorizontalAlignment = HorizontalTextAlignment.Center,
            VerticalAlignment = vertical,
            Rotation = rotation,
        };
        var note = TextNote.Create(doc, view.Id, at, text, options);
        ProjectStore.Mark(note, LabelRole);
        return note;
    }

    // The spot elevation's cross (CrossSymbol) at the point the elevation is for; none if the symbol isn't loaded.
    private static FamilyInstance? Cross(Document doc, View view, XYZ at, FamilySymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }
        var cross = doc.Create.NewFamilyInstance(at, symbol, view);
        ProjectStore.Mark(cross, LabelRole);
        return cross;
    }

    // The direction along the curve at a normalized parameter (0 start, 1 end), flat on the plan.
    private static XYZ Tangent(Curve c, double t)
    {
        var d = c.ComputeDerivatives(t, true).BasisX;
        return new XYZ(d.X, d.Y, 0).Normalize();
    }

    /// <summary>A piece of a chain: its curve, turned to run the chain's way, and the line it is of (its UniqueId).</summary>
    private readonly record struct Piece(Curve Curve, string Line);

    /// <summary>
    /// The lines joined end to end into chains, each piece turned to run one way and keeping its line. An open chain
    /// starts at a free end; a closed one anywhere. Branches (three lines at one point) split into separate chains.
    /// </summary>
    private static List<List<Piece>> Chains(List<CurveElement> lines)
    {
        var tolerance = Units.FeetFromMm(1);
        bool Near(XYZ a, XYZ b) => Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
        var left = lines.Select(l => new Piece(l.GeometryCurve, l.UniqueId)).ToList();
        bool Free(Piece p, int end) => !left.Any(o => o.Line != p.Line
            && (Near(o.Curve.GetEndPoint(0), p.Curve.GetEndPoint(end)) || Near(o.Curve.GetEndPoint(1), p.Curve.GetEndPoint(end))));
        Piece Reversed(Piece p) => p with { Curve = p.Curve.CreateReversed() };

        var chains = new List<List<Piece>>();
        while (left.Count > 0)
        {
            // A piece with a free start, or one with a free end turned round, or (all closed) any.
            var freeStart = left.FindIndex(p => Free(p, 0));
            var freeEnd = freeStart < 0 ? left.FindIndex(p => Free(p, 1)) : -1;
            var first = freeStart >= 0 ? freeStart : freeEnd >= 0 ? freeEnd : 0;
            var original = left[first];
            left.RemoveAt(first);
            var chain = new List<Piece> { first == freeEnd ? Reversed(original) : original };
            while (!Near(chain[^1].Curve.GetEndPoint(1), chain[0].Curve.GetEndPoint(0)))
            {
                var end = chain[^1].Curve.GetEndPoint(1);
                var next = left.FindIndex(p => Near(p.Curve.GetEndPoint(0), end) || Near(p.Curve.GetEndPoint(1), end));
                if (next < 0)
                {
                    break;
                }
                var piece = left[next];
                left.RemoveAt(next);
                chain.Add(Near(piece.Curve.GetEndPoint(0), end) ? piece : Reversed(piece));
            }
            chains.Add(chain);
        }
        return chains;
    }
}
