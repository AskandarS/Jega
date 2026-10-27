using System.Globalization;

namespace Jega.Core;

/// <summary>Jega.Core's own Ui.Fill: a text from Strings (Resources/Strings.resx) with its {0}, {1} … filled in.</summary>
internal static class Texts
{
    /// <summary>Numbers as the language shows them (Language.Current), as Jega.Revit's Units.Numbers does.</summary>
    public static CultureInfo Numbers => Language.Current;

    public static string Fill(string text, params object?[] args) => string.Format(Numbers, text, args);
}
