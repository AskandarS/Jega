namespace Jega.Core;

/// <summary>Degrees and radians, as the plan's angles need them.</summary>
public static class Angles
{
    public static double ToRadians(double degrees) => degrees * Math.PI / 180;

    public static double ToDegrees(double radians) => radians * 180 / Math.PI;

    /// <summary>An angle in degrees as 0 to 360.</summary>
    public static double Normalize360(double degrees) => (degrees % 360 + 360) % 360;

    /// <summary>The difference a − b in degrees, from −180 to 180: how far b is turned to reach a.</summary>
    public static double TurnDegrees(double aRadians, double bRadians) => ToDegrees(Math.IEEERemainder(aRadians - bRadians, 2 * Math.PI));
}
