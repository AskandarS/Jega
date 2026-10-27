using Pt = (double X, double Y);

namespace Jega.Core;

/// <summary>Danmarks Højdemodel's 0.4 m cells (SiteFetcher.Dtm): which values are real, where a cell's centre is, and boxes on whole cells.</summary>
public static class HeightCells
{
    /// <summary>The model's cells; they start at whole multiples of it (UTM32, m).</summary>
    public const double Size = 0.4;

    /// <summary>A real height, not the model's no-data: Denmark lies between about -7 and 171 m DVR90.</summary>
    public static bool Valid(double z) => double.IsFinite(z) && z > -100 && z < 500;

    /// <summary>The centre of cell (i, j), counted from the top left corner (UTM32, m).</summary>
    public static Pt Centre(SiteFetcher.Dtm dtm, int i, int j) => (dtm.X0 + (i + 0.5) * dtm.Dx, dtm.Y0 - (j + 0.5) * dtm.Dy);

    /// <summary>
    /// The box around (minX, minY)–(maxX, maxY) plus $margin, snapped outwards to whole $cell (default the model's own,
    /// so its own values come back, not resampled ones; a coarser cell is a whole multiple of it).
    /// </summary>
    public static (double X1, double Y1, double X2, double Y2) Snap(double minX, double minY, double maxX, double maxY, double margin,
        double cell = Size) =>
        (Math.Floor((minX - margin) / cell) * cell, Math.Floor((minY - margin) / cell) * cell,
         Math.Ceiling((maxX + margin) / cell) * cell, Math.Ceiling((maxY + margin) / cell) * cell);

    /// <summary>How many cells of $cell span $length (m); the small margin keeps whole cells with float noise from getting one too many.</summary>
    public static int Count(double length, double cell) => (int)Math.Ceiling(length / cell - 1e-6);
}
