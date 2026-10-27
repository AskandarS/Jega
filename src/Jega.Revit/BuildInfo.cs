using System.Reflection;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// Which Jega is running: its version (Core.Build.Version), when it was built, and in a Debug build from which copy of
/// the project (written into the assembly by the .csproj; a release never carries the build machine's folders). Shown
/// in About's footer, so an old build in Revit, or a tester's version, is seen at once.
/// </summary>
internal static class BuildInfo
{
    public static string Text => Meta("SourceDir") is { } dir
        ? Fill(Strings.About_BuiltFrom, Core.Build.Version, Meta("BuildTime") ?? "?", dir)
        : Fill(Strings.About_Built, Core.Build.Version, Meta("BuildTime") ?? "?");

    private static string? Meta(string key) =>
        typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
}
