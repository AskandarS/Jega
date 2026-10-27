using System.Globalization;
using System.IO;
using Autodesk.Revit.DB;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// The spot elevation's cross at each terrain line elevation, as the site plan's KOTER: a generic annotation family
/// "Jega Cross", an X Settings' "Elevation cross size" mm wide on paper (5 by default, twice the elevation's text;
/// annotations keep their paper size at any scale and stay square to the sheet). Not a Line, so the terrain line
/// updater never hears it. Made from Revit's own template into %APPDATA%\Jega\&lt;Revit version&gt;\Jega Cross.rfa (a
/// family saved by a newer Revit can't be loaded into an older one), then loaded by the buttons; the updater only uses
/// it once loaded, as a family can't be made or loaded inside an updater.
///
/// Made parametric if Revit allows it: a type parameter Size, the X's ends locked to reference planes dimensioned
/// with it, so Edit Type → Size (or Settings) changes every cross at once. If Revit refuses a step (in
/// Revit 2025: "not permitted in this type of family"), or the X doesn't follow Size, the cross is made as before: a
/// fixed X at Settings' size, made again for another size.
/// </summary>
internal static class CrossSymbol
{
    public static string Name => Core.Config.Current.Revit.CrossFamily;

    /// <summary>The type parameter that sets the cross's width on paper (parametric family only).</summary>
    public const string SizeParameter = "Size";

    // Written beside the .rfa: what it is, "parametric 1" or "fixed <size>", so the file is made again when it isn't.
    private const string ParametricKind = "parametric 1";

    private static string FilePath(Document doc) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jega", doc.Application.VersionNumber, Name + ".rfa");

    private static string KindPath(Document doc) => Path.ChangeExtension(FilePath(doc), ".kind");

    private static string Key(double sizeMm) => sizeMm.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FixedKind(double sizeMm) => "fixed " + Key(sizeMm);

    private static double StoredSize(Document doc, double? sizeMm) => sizeMm ?? ProjectStore.ReadOptions(doc).LevelCrossMm;

    /// <summary>The loaded symbol, or null; never loads, so the updater can call it.</summary>
    public static FamilySymbol? Find(Document doc) => Symbol(doc, Name);

    private static FamilySymbol? Symbol(Document doc, string familyName) =>
        new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_GenericAnnotation)
            .Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == familyName);

    /// <summary>The loaded cross has a Size parameter (Edit Type changes it).</summary>
    public static bool Parametric(Document doc) => Find(doc)?.LookupParameter(SizeParameter) is not null;

    /// <summary>The cross's width on paper, mm: the parametric family's Size, else Settings' size (the fixed one's).</summary>
    public static double SizeMm(Document doc) =>
        Find(doc)?.LookupParameter(SizeParameter) is { StorageType: StorageType.Double } p ? Units.MmFromFeet(p.AsDouble()) : ProjectStore.ReadOptions(doc).LevelCrossMm;

    /// <summary>Inside a transaction, parametric family only: its Size, so every cross follows.</summary>
    public static void SetSizeMm(Document doc, double mm) => Find(doc)?.LookupParameter(SizeParameter)?.Set(Units.FeetFromMm(mm));

    // The project's cross is right: parametric (any size), or the fixed one of this size (marked when loaded).
    private static bool Current(Document doc, double size) =>
        Find(doc) is { } s && (s.LookupParameter(SizeParameter) is not null || ProjectStore.Is(s.Family, "Cross " + Key(size)));

    private static string? FileKind(Document doc) =>
        File.Exists(FilePath(doc)) && File.Exists(KindPath(doc)) ? File.ReadAllText(KindPath(doc)).Trim() : null;

    private static bool FileCurrent(Document doc, double size) => FileKind(doc) is ParametricKind || FileKind(doc) == FixedKind(size);

    /// <summary>
    /// Outside a transaction, before Load: makes the .rfa if the project's cross isn't right (Settings' size, or $sizeMm
    /// when Settings is just changing it, for a fixed one) and the file isn't either. Null when ready without a word,
    /// else what to say: why there are no crosses, or that the cross has a fixed size and why.
    /// </summary>
    public static string? Prepare(Document doc, double? sizeMm = null)
    {
        var size = StoredSize(doc, sizeMm);
        if (Current(doc, size) || FileCurrent(doc, size))
        {
            return null;
        }
        var app = doc.Application;
        if (Template(app) is not { } template)
        {
            return Strings.Cross_NoTemplate;
        }
        string? note = null;
        try
        {
            var family = app.NewFamilyDocument(template);
            try
            {
                var kind = ParametricKind;
                var step = "";
                using (var t = new Transaction(family, "Cross"))
                {
                    t.Start();
                    try
                    {
                        DrawParametric(family, Units.FeetFromMm(size), s => step = s);
                        t.Commit();
                    }
                    catch (Exception e) when (e is Autodesk.Revit.Exceptions.ApplicationException or CrossException)
                    {
                        t.RollBack();
                        kind = FixedKind(size);
                        note = Fill(Strings.Cross_Fixed, size, step, e.Message);
                    }
                }
                if (kind != ParametricKind)
                {
                    using var t = new Transaction(family, "Cross");
                    t.Start();
                    DrawFixed(family, Units.FeetFromMm(size));
                    t.Commit();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath(doc))!);
                family.SaveAs(FilePath(doc), new SaveAsOptions { OverwriteExistingFile = true, Compact = true });
                File.WriteAllText(KindPath(doc), kind);
            }
            finally
            {
                family.Close(false);
            }
            return note;
        }
        catch (Exception e)
        {
            return Fill(Strings.Cross_NotMade, e.Message);
        }
    }

    // The X doesn't follow Size: made fixed instead.
    private sealed class CrossException(string message) : Exception(message);

    private static View FamilyView(Document family)
    {
        // The template's note to the family's maker.
        var notes = new FilteredElementCollector(family).OfClass(typeof(TextNote)).ToElementIds();
        if (notes.Count > 0)
        {
            family.Delete(notes);
        }
        return new FilteredElementCollector(family).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v.ViewType is not (ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Undefined))
            .OrderBy(v => v is ViewPlan ? 0 : 1).First();
    }

    // The X as two plain lines, $size wide (feet): what the cross was before Size.
    private static void DrawFixed(Document family, double size)
    {
        var view = FamilyView(family);
        var d = size / 2;
        family.FamilyCreate.NewDetailCurve(view, Line.CreateBound(new XYZ(-d, -d, 0), new XYZ(d, d, 0)));
        family.FamilyCreate.NewDetailCurve(view, Line.CreateBound(new XYZ(-d, d, 0), new XYZ(d, -d, 0)));
    }

    /// <summary>
    /// Inside the family's transaction. The X with its four ends locked to four reference planes, two upright and two
    /// across, each pair dimensioned equally about the family's centre and labelled Size, so Size is the X's width.
    /// Then tried at twice the size and back; throws CrossException if the X doesn't follow. $step hears each step,
    /// so a refusal can say which.
    /// </summary>
    private static void DrawParametric(Document family, double size, Action<string> step)
    {
        var view = FamilyView(family);
        var manager = family.FamilyManager;
        step(Strings.Cross_StepType);
        if (manager.CurrentType is null)
        {
            manager.NewType(Name);
        }
        step(Strings.Cross_StepParameter);
        var parameter = manager.AddParameter(SizeParameter, GroupTypeId.Graphics, SpecTypeId.Length, false);
        manager.Set(parameter, size);

        var create = family.FamilyCreate;
        var d = size / 2;
        step(Strings.Cross_StepPlanes);
        // The centre planes: the template's (they define the origin), else made here and pinned.
        var upright = CentrePlane(family, view, XYZ.BasisX);
        var across = CentrePlane(family, view, XYZ.BasisY);
        ReferencePlane Plane(XYZ through, XYZ along) =>
            create.NewReferencePlane(through - along * size * 2, through + along * size * 2, XYZ.BasisZ, view);
        var left = Plane(new XYZ(-d, 0, 0), XYZ.BasisY);
        var right = Plane(new XYZ(d, 0, 0), XYZ.BasisY);
        var bottom = Plane(new XYZ(0, -d, 0), XYZ.BasisX);
        var top = Plane(new XYZ(0, d, 0), XYZ.BasisX);

        // Left and right equally about the centre (EQ), and Size apart; the same for bottom and top. Each dimension
        // on its own line, beside the X.
        void Pair(ReferencePlane a, ReferencePlane centre, ReferencePlane b, Line equal, Line overall)
        {
            var three = new ReferenceArray();
            three.Append(a.GetReference());
            three.Append(centre.GetReference());
            three.Append(b.GetReference());
            step(Strings.Cross_StepEqual);
            create.NewDimension(view, equal, three).AreSegmentsEqual = true;
            var two = new ReferenceArray();
            two.Append(a.GetReference());
            two.Append(b.GetReference());
            step(Strings.Cross_StepSize);
            create.NewDimension(view, overall, two).FamilyLabel = parameter;
        }
        Line Across(double y) => Line.CreateBound(new XYZ(-size, y, 0), new XYZ(size, y, 0));
        Line Upright(double x) => Line.CreateBound(new XYZ(x, -size, 0), new XYZ(x, size, 0));
        Pair(left, upright, right, Across(-size), Across(-size * 1.5));
        Pair(bottom, across, top, Upright(-size), Upright(-size * 1.5));

        // The two strokes, each end held by an upright and an across plane.
        DetailCurve Stroke(XYZ from, XYZ to, ReferencePlane fromX, ReferencePlane fromY, ReferencePlane toX, ReferencePlane toY)
        {
            step(Strings.Cross_StepLines);
            var line = create.NewDetailCurve(view, Line.CreateBound(from, to));
            var curve = line.GeometryCurve;
            step(Strings.Cross_StepLock);
            create.NewAlignment(view, fromX.GetReference(), curve.GetEndPointReference(0));
            create.NewAlignment(view, fromY.GetReference(), curve.GetEndPointReference(0));
            create.NewAlignment(view, toX.GetReference(), curve.GetEndPointReference(1));
            create.NewAlignment(view, toY.GetReference(), curve.GetEndPointReference(1));
            return line;
        }
        var strokes = new[]
        {
            Stroke(new XYZ(-d, -d, 0), new XYZ(d, d, 0), left, bottom, right, top),
            Stroke(new XYZ(-d, d, 0), new XYZ(d, -d, 0), left, top, right, bottom),
        };

        // Tried at twice the size and back: the strokes must follow, or the X would never change with Size.
        step(Strings.Cross_StepTry);
        manager.Set(parameter, size * 2);
        family.Regenerate();
        var follows = strokes.All(s => Math.Abs(s.GeometryCurve.Length - size * 2 * Math.Sqrt(2)) < Units.FeetFromMm(0.01));
        manager.Set(parameter, size);
        family.Regenerate();
        if (!follows)
        {
            throw new CrossException(Strings.Cross_DoesNotFollow);
        }
    }

    // The plane through the family's origin facing $normal: the template's own if it has one, else a new one, pinned.
    private static ReferencePlane CentrePlane(Document family, View view, XYZ normal)
    {
        var existing = new FilteredElementCollector(family).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>()
            .FirstOrDefault(p => p.Normal.CrossProduct(normal).IsZeroLength() && Math.Abs(p.BubbleEnd.DotProduct(normal)) < 1e-9);
        if (existing is not null)
        {
            return existing;
        }
        var along = normal.CrossProduct(XYZ.BasisZ);
        var plane = family.FamilyCreate.NewReferencePlane(-along, along, XYZ.BasisZ, view);
        plane.Pinned = true;
        return plane;
    }

    /// <summary>
    /// Inside a transaction, after Prepare (same $sizeMm): loads the .rfa if the project's cross isn't right. A parametric
    /// one gets Settings' size as its Size; a fixed one is marked with its size. Returns why it couldn't be loaded, or null.
    /// </summary>
    public static string? Load(Document doc, double? sizeMm = null)
    {
        var size = StoredSize(doc, sizeMm);
        if (!Current(doc, size) && FileCurrent(doc, size))
        {
            if (!doc.LoadFamily(FilePath(doc), new Overwrite(), out var family) && family is null || Find(doc) is not { } loaded)
            {
                return Fill(Strings.Cross_NotLoaded, FilePath(doc));
            }
            if (loaded.LookupParameter(SizeParameter) is { } p)
            {
                p.Set(Units.FeetFromMm(size));
            }
            else
            {
                ProjectStore.Mark(loaded.Family, "Cross " + Key(size));
            }
        }
        if (Find(doc) is { IsActive: false } symbol)
        {
            symbol.Activate();
        }
        return null;
    }

    // A family already in the project is replaced by the file's.
    private sealed class Overwrite : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    // Revit's metric generic annotation template: in this language's folder (FamilyTemplatePath) first, else in any of
    // them (they're beside it), else under Revit's own. A folder that can't be read (an office's network drive, a
    // folder without access) is passed over, so Load from Address, Refresh and Terrain Line never stop on it; none
    // found: null (Cross_NoTemplate).
    private static string? Template(Autodesk.Revit.ApplicationServices.Application app)
    {
        var own = app.FamilyTemplatePath.TrimEnd('\\', '/');
        var roots = new[] { own, Path.GetDirectoryName(own), $@"C:\ProgramData\Autodesk\RVT {app.VersionNumber}\Family Templates" }
            .Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        IEnumerable<string> Files(string root)
        {
            try
            {
                return Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.rft", options).ToList() : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return [];
            }
        }
        var all = roots.SelectMany(r => Files(r!)).ToList();
        return all.FirstOrDefault(f => Path.GetFileName(f).Equals("Metric Generic Annotation.rft", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(f => Path.GetFileName(f).Contains("Generic Annotation", StringComparison.OrdinalIgnoreCase));
    }
}
