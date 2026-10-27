using Autodesk.Revit.DB;

namespace Jega.Revit;

/// <summary>What an import's transform says about the plan: its north, and whether it's mirrored.</summary>
internal static class Transforms
{
    /// <summary>The transform's north (its +Y), radians counter-clockwise from the model's +Y (project north).</summary>
    public static double NorthAngle(Transform t) => Math.Atan2(-t.BasisY.X, t.BasisY.Y);

    /// <summary>Mirrored in plan: X and Y no longer turn counter-clockwise, so nothing can follow it by moving and turning.</summary>
    public static bool IsMirrored(Transform t) => t.BasisX.CrossProduct(t.BasisY).Z <= 0;
}
