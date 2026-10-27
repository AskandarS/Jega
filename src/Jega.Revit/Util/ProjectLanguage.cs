using Autodesk.Revit.DB;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>
/// The project's language (Settings, ProjectOptions.Language) as the one Jega speaks: both resx, the numbers and the
/// ribbon. Applied first by every command, when a view of a project is activated (App), by the updater for the
/// document it runs in, and by Settings' OK; so after an Undo or a Synchronize the next command speaks it again.
/// </summary>
internal static class ProjectLanguage
{
    /// <summary>$doc's language; a family has none, so the project's spoken before stays (no switch in the family editor).</summary>
    public static void Apply(Document doc)
    {
        if (!doc.IsFamilyDocument)
        {
            Use(ProjectStore.ReadOptions(doc).Language);
        }
    }

    /// <summary>
    /// Speaks $culture for a change that may not be saved (Settings' OK): Keep once it is. Disposed without that
    /// (Revit refused, or something threw), the language spoken before again: the project's, as the command applied
    /// it first, or in a family the one before it.
    /// </summary>
    public static Trial Try(string culture)
    {
        var before = Core.Language.Current.Name;
        Use(culture);
        return new Trial(before);
    }

    public sealed class Trial(string before) : IDisposable
    {
        private bool _kept;

        public void Keep() => _kept = true;

        public void Dispose()
        {
            if (!_kept)
            {
                Use(before);
            }
        }
    }

    /// <summary>Speaks $culture (Core.Language.Use), says the ribbon again if that changed it; $ribbon false: not the ribbon.</summary>
    public static void Use(string? culture, bool ribbon = true)
    {
        if (!Core.Language.Use(culture))
        {
            return;
        }
        Strings.Culture = Core.Language.Current;
        if (ribbon)
        {
            App.Relabel();
        }
    }
}
