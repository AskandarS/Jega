using System.Reflection;

namespace Jega.Core;

/// <summary>This build of Jega.</summary>
public static class Build
{
    /// <summary>
    /// Its version (Directory.Build.props), e.g. "0.9.0-beta.1", without the commit the SDK adds after a "+". Written
    /// into the model's stored JSON as "writtenBy", and never read back by Jega for now: it is there so a later Jega can
    /// tell which models an earlier one wrote, which can't be added afterwards to models already made.
    /// </summary>
    public static string Version { get; } =
        typeof(Build).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
}
