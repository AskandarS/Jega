using System.Globalization;
using System.Runtime.CompilerServices;

namespace Jega.Core.Tests;

/// <summary>
/// The tests read Core's own texts (the resx without a language), whatever language Windows speaks: until
/// Language.Use sets one, the resx follows the UI language, and on a Danish Windows the error texts came out Danish.
/// Only the texts: numbers keep the machine's culture, so one shown or stored by it is still caught.
/// </summary>
internal static class Culture
{
    [ModuleInitializer]
    internal static void InvariantTexts()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
