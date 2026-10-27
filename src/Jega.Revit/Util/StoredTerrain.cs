using System.Diagnostics.CodeAnalysis;
using Autodesk.Revit.DB;
using Jega.Core;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>The terrain stored in the model (ProjectStore), read as a grid, with one message for every command when it's missing.</summary>
internal static class StoredTerrain
{
    public static string Missing => Strings.StoredTerrain_Missing;

    // The last terrain read: whose (the document, weakly), which (its DataStorage and ImportedUtc, new with every
    // terrain loaded and back with an Undo), and its text and grid. So the terrain line updater, on every edit, takes
    // out neither the CSV (up to about 30 MB) nor parses it again.
    private static (WeakReference<Document> Doc, ElementId Storage, string ImportedUtc, string Csv, TerrainGrid Grid)? _cache;

    /// <summary>The stored terrain and its grid; false with $problem (what to do) when there is none or it can't be read.</summary>
    public static bool TryGet(Document doc, [NotNullWhen(true)] out ProjectSettings? stored, [NotNullWhen(true)] out TerrainGrid? grid, out string problem)
    {
        (stored, grid, problem) = (null, null, "");
        if (ProjectStore.ReadWithoutCsv(doc) is not var (settings, storage))
        {
            problem = Missing;
            return false;
        }
        // The same document by Revit's Equals, not only the same .NET object, which Revit needn't hand out every time.
        if (_cache is { } c && c.Doc.TryGetTarget(out var cached) && cached.IsValidObject && cached.Equals(doc) && c.Storage == storage
            && c.ImportedUtc == settings.ImportedUtc)
        {
            (stored, grid) = (settings with { Csv = c.Csv }, c.Grid);
            return true;
        }
        stored = ProjectStore.Read(doc)!;
        try
        {
            grid = TerrainGrid.Parse(stored.Csv);
        }
        catch (TerrainGridException e)
        {
            // $stored stays: a terrain that can't be read is not none (TerrainLines.Update says so instead of removing).
            problem = $"{Strings.StoredTerrain_Unreadable}\n\n{e.Message}";
            return false;
        }
        _cache = (new WeakReference<Document>(doc), storage, stored.ImportedUtc, stored.Csv, grid);
        return true;
    }
}
