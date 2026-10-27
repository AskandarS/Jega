using Autodesk.Revit.DB;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// The DVR90 height of Revit's 0 (±0.00): the Elevation of the internal origin in the project's shared coordinates
/// (the Survey Point's), unless the project keeps it in Jega (ProjectOptions.FloorDvr90Mm: links, or coordinates Jega
/// didn't give). Terrain at DVR90 z sits at Revit Z = z - this. 0 = absolute elevations (the terrain at its real
/// heights); else relative elevations, the floor drawn at 0. Only the user sets it (Settings, "Relative elevations"):
/// no command does.
/// </summary>
internal static class Dvr90
{
    public static (double Mm, bool FromSurveyPoint) AtRevitZero(Document doc) =>
        ProjectStore.ReadOptions(doc).FloorDvr90Mm is { } mm ? (mm, false) : (SurveyPointMm(doc), true);

    /// <summary>The Survey Point's elevation, mm: the shared elevation of Revit's 0. With the floor kept in Jega it may
    /// still hold one set before (Settings says so).</summary>
    public static double SurveyPointMm(Document doc) => Units.MmFromFeet(doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation);

    /// <summary>
    /// "Relative elevations: the floor (±0.00) is at Revit's 0, 9.99 m DVR90 (…)" for the summaries, or that the
    /// elevations are absolute, or which terrain point was put on which level (Settings, "Terrain point on a level").
    /// </summary>
    public static string Describe(Document doc, double mm, bool fromSurvey)
    {
        var options = ProjectStore.ReadOptions(doc);
        if (OnLevel(doc, options) is { } level)
        {
            return Fill(Strings.Dvr90_OnLevel, Units.Level(mm), TerrainPoint(options.ZeroTerrainAt), level.Name);
        }
        return Math.Abs(mm) < 1
            ? Strings.Dvr90_Absolute
            : Fill(Strings.Dvr90_Relative, Units.Level(mm), fromSurvey ? Strings.Dvr90_FromSurvey : Strings.Dvr90_Entered);
    }

    /// <summary>The level Settings' "Terrain point on a level" put the terrain on, or null (not used, or the level is gone).</summary>
    public static Level? OnLevel(Document doc, Core.ProjectOptions options) =>
        options.ZeroLevel.Length > 0 ? doc.GetElement(options.ZeroLevel) as Level : null;

    /// <summary>"the lowest terrain" …, for the messages.</summary>
    public static string TerrainPoint(string at) => at switch
    {
        Core.ProjectOptions.LevelHighest => Strings.Dvr90_PointHighest,
        Core.ProjectOptions.LevelAverage => Strings.Dvr90_PointAverage,
        Core.ProjectOptions.LevelAddress => Strings.Dvr90_PointAddress,
        Core.ProjectOptions.LevelHouse => Strings.Dvr90_PointHouse,
        _ => Strings.Dvr90_PointLowest,
    };
}
