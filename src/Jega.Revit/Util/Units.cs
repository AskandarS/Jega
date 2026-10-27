using Autodesk.Revit.DB;

namespace Jega.Revit;

/// <summary>Core works in mm, Revit internally in feet: the only place they meet.</summary>
internal static class Units
{
    /// <summary>A length in mm as Revit's internal feet: FeetFromMm(1) is 1 mm, not a foot.</summary>
    public static double FeetFromMm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

    /// <summary>A length in Revit's internal feet as mm.</summary>
    public static double MmFromFeet(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

    /// <summary>
    /// How numbers are shown to the user: in texts, fields and labels on the drawing. The project's language
    /// (Settings, ProjectLanguage): a comma in Danish, a point in English. What Jega stores or writes to files stays
    /// invariant.
    /// </summary>
    public static System.Globalization.CultureInfo Numbers => Core.Language.Current;

    /// <summary>A length in mm as metres with three decimals: 75.600.</summary>
    public static string Metres(double mm) => (mm / 1000).ToString("0.000", Numbers);

    /// <summary>
    /// A level (DVR90) in mm as metres with two decimals: 10.25. Danmarks Højdemodel is good to about ±0.1 m, so a
    /// third decimal would claim a precision the terrain doesn't have.
    /// </summary>
    public static string Level(double mm) => (mm / 1000).ToString("0.00", Numbers);

    /// <summary>A level (mm) as Level writes it, back in mm: 9983 → 9980. For what is worked out from written levels
    /// (a fall), so it adds up on the drawing, as the site plan's note does (SiteplanDxf).</summary>
    public static double LevelAsWritten(double mm) =>
        double.Parse((mm / 1000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture) * 1000;

    /// <summary>
    /// A fall (slope) in ‰, as Danish drawings give it, whole: 25 ‰. The terrain's ±0.1 m makes a decimal claim too
    /// much on any piece shorter than a few hundred metres.
    /// </summary>
    public static string Permille(double permille) => string.Create(Numbers, $"{permille:0} ‰");

    /// <summary>The distance between two model points in plan (feet), heights ignored.</summary>
    public static double Flat(XYZ a, XYZ b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
