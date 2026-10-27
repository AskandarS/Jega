namespace Jega.Core;

/// <summary>
/// The project's origin (nulpunkt): the point the site's coordinates are measured from, ETRS89/UTM32 (m). Set once,
/// from the address point rounded to whole metres, so the shared coordinates read as round numbers; the address point
/// itself stays a point of information.
/// </summary>
public static class ProjectOrigin
{
    /// <summary>The origin for the address point ($e, $n; UTM32, m): each rounded to whole metres, a half up.</summary>
    public static (double E, double N) Of(double e, double n) =>
        (Math.Round(e, MidpointRounding.AwayFromZero), Math.Round(n, MidpointRounding.AwayFromZero));
}
