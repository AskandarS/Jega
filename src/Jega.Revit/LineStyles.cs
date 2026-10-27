using Autodesk.Revit.DB;
using Jega.Core;

namespace Jega.Revit;

/// <summary>
/// Jega's line styles (subcategories of Lines), as Config.Revit defines them. Made on first use and never changed
/// after, so what the user sets in Manage → Additional Settings → Line Styles stays.
/// </summary>
internal static class LineStyles
{
    private static RevitLook Look => Config.Current.Revit;

    /// <summary>The grid frame.</summary>
    public static GraphicsStyle GridFrame(Document doc) => Get(doc, Look.GridFrame);

    /// <summary>The lot boundary's outline (Draw Lot Boundary).</summary>
    public static GraphicsStyle Edge(Document doc) => Get(doc, Look.Edge);

    public static string TerrainLineName => Look.TerrainLine.Name;

    /// <summary>The terrain lines (the calculation layer).</summary>
    public static GraphicsStyle TerrainLine(Document doc) => Get(doc, Look.TerrainLine);

    /// <summary>The same layer without the fall labels: switching a line's style is the option.</summary>
    public static string TerrainLineLevelsOnlyName => Look.TerrainLineLevelsOnly.Name;

    /// <summary>The terrain line styles in the model, each with whether it shows the falls (the full style) or only the elevations.</summary>
    public static Dictionary<ElementId, bool> TerrainLineStyles(Document doc)
    {
        var styles = new Dictionary<ElementId, bool>();
        foreach (var (name, full) in new[] { (TerrainLineName, true), (TerrainLineLevelsOnlyName, false) })
        {
            if (Find(doc, name) is { } style)
            {
                styles[style.Id] = full;
            }
        }
        return styles;
    }

    public static GraphicsStyle TerrainLineLevelsOnly(Document doc) => Get(doc, Look.TerrainLineLevelsOnly);

    /// <summary>The style if it exists; never makes it, so it can be called outside a transaction.</summary>
    public static GraphicsStyle? Find(Document doc, string name)
    {
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        return lines.SubCategories.Contains(name) ? lines.SubCategories.get_Item(name).GetGraphicsStyle(GraphicsStyleType.Projection) : null;
    }

    /// <summary>
    /// The pattern for the site plan's setback line: the first of Config's SetbackPatterns ("Demolished", "Dash") the
    /// model has, by that name or else the shortest starting with it; null if it has none.
    /// </summary>
    public static ElementId? DashPattern(Document doc)
    {
        var patterns = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>().ToList();
        return Look.SetbackPatterns
            .Select(name => patterns.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            ?? patterns.Where(p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name.Length).FirstOrDefault())
            .FirstOrDefault(p => p is not null)?.Id;
    }

    /// <summary>Inside a transaction when the style doesn't exist yet.</summary>
    private static GraphicsStyle Get(Document doc, LineStyle style)
    {
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        var sub = lines.SubCategories.Contains(style.Name) ? lines.SubCategories.get_Item(style.Name) : null;
        if (sub is null)
        {
            sub = doc.Settings.Categories.NewSubcategory(lines, style.Name);
            sub.LineColor = new Color(style.Colour.R, style.Colour.G, style.Colour.B);
            sub.SetLineWeight(style.Weight, GraphicsStyleType.Projection);
            // Dashed: the first line pattern called "Dash…", if the model has one; else it stays solid.
            var dash = !style.Dashed ? null : new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                .Where(p => p.Name.StartsWith("Dash", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name.Length).FirstOrDefault();
            if (dash is not null)
            {
                sub.SetLinePatternId(dash.Id, GraphicsStyleType.Projection);
            }
        }
        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
    }
}
