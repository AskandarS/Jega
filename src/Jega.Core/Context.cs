using System.Text.Json;
using Jega.Core.Resources;

namespace Jega.Core;

/// <summary>A building around the lot: its outline (mm from the site's 0,0), ground and top (mm DVR90).</summary>
public sealed record ContextBuilding(bool Own, string? Bbr, double Base, double Top, IReadOnlyList<Point2> Ring)
{
    public double Height => Top - Base;
}

public sealed class ContextException(string message) : Exception(message);

/// <summary>
/// The context as stored in the model (SiteFetcher.ContextJson): the
/// buildings around the lot, outlines from GeoDanmark, heights from Danmarks Højdemodel. Same 0,0 as the terrain and
/// the site plan. Indicative, like the terrain.
/// </summary>
public static class Context
{
    /// <summary>The buildings in the file; throws ContextException with a plain reason if it isn't one.</summary>
    public static IReadOnlyList<ContextBuilding> Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new ContextException(Strings.Context_NotJson);
        }
        using (doc)
        try
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.GetInt32() != 1)
            {
                throw new ContextException(Strings.Context_NotContext);
            }
            if (!root.TryGetProperty("units", out var units) || units.GetString() != "mm")
            {
                throw new ContextException(Strings.Context_NotMm);
            }
            var buildings = new List<ContextBuilding>();
            foreach (var b in root.GetProperty("buildings").EnumerateArray())
            {
                var ring = b.GetProperty("ring").EnumerateArray().Select(p => new Point2(p[0].GetDouble(), p[1].GetDouble())).ToList();
                var (bottom, top) = (b.GetProperty("base").GetDouble(), b.GetProperty("top").GetDouble());
                // A ring needs three corners and some height; anything else can't become a mass and is skipped.
                if (ring.Count < 3 || top - bottom <= 0)
                {
                    continue;
                }
                var bbr = b.TryGetProperty("bbr", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                buildings.Add(new ContextBuilding(b.GetProperty("own").GetBoolean(), bbr, bottom, top, ring));
            }
            return buildings;
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            // A field missing, or of another type ("version": "1", a ring of one number): not a context Jega can read.
            throw new ContextException(Strings.Context_NotContext);
        }
    }
}
