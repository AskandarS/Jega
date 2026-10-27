using System.Globalization;
using Jega.Core.Resources;

namespace Jega.Core;

/// <summary>
/// The language Jega speaks: which texts (Core's resx; Jega.Revit sets its own from Current) and how numbers are shown.
/// Set by the host from the project's Settings (ProjectOptions.Language); invariant until then, as in the tests.
/// </summary>
public static class Language
{
    public static CultureInfo Current { get; private set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Speaks $culture from now on: one of Config's Languages, else Config's default. True if that changed the
    /// language, so the host can say its own texts again.
    /// </summary>
    public static bool Use(string? culture)
    {
        var name = Of(culture);
        // CultureInfo spells its name its own way ("da-DK"), Config may not ("da-dk"): the same language either way.
        if (Same(Current.Name, name))
        {
            return false;
        }
        Current = CultureInfo.GetCultureInfo(name);
        Strings.Culture = Current;
        return true;
    }

    /// <summary>
    /// The language $culture means, as Config writes it: "da-dk" is "da-DK"; one Config doesn't offer (missing, or
    /// taken out) is Config's default, and a default it doesn't offer either is its first language. Compare stored
    /// languages through this, never as they are.
    /// </summary>
    public static string Of(string? culture)
    {
        var offered = Config.Current.Languages;
        return offered.FirstOrDefault(l => Same(l.Culture, culture))?.Culture
            ?? offered.FirstOrDefault(l => Same(l.Culture, Config.Current.DefaultLanguage))?.Culture
            ?? offered.FirstOrDefault()?.Culture
            ?? Config.Current.DefaultLanguage;
    }

    /// <summary>Whether $a and $b are the same language's culture, whatever their case.</summary>
    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
