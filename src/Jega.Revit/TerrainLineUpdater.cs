using System.IO;
using Autodesk.Revit.DB;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// Keeps the terrain line labels up to date: Revit calls it inside the user's own edit whenever a line is drawn,
/// changed or deleted, so the labels follow the lines and Undo takes both back together. Optional, so a model
/// opened without Jega gives no warning.
/// </summary>
internal sealed class TerrainLineUpdater(AddInId addin) : IUpdater
{
    private readonly UpdaterId _id = new(addin, new Guid("92a64d07-eeba-4e3a-be9d-88cd2635cca9"));

    public static void Register(AddInId addin)
    {
        var updater = new TerrainLineUpdater(addin);
        UpdaterRegistry.RegisterUpdater(updater, true);
        var lines = new ElementCategoryFilter(BuiltInCategory.OST_Lines);
        UpdaterRegistry.AddTrigger(updater._id, lines, Element.GetChangeTypeElementAddition());
        UpdaterRegistry.AddTrigger(updater._id, lines, Element.GetChangeTypeAny());
        UpdaterRegistry.AddTrigger(updater._id, lines, Element.GetChangeTypeElementDeletion());
    }

    /// <summary>At Revit's shutdown: the updater taken back, if Register got as far as making it.</summary>
    public static void Unregister(AddInId addin)
    {
        var id = new TerrainLineUpdater(addin)._id;
        if (UpdaterRegistry.IsUpdaterRegistered(id))
        {
            UpdaterRegistry.UnregisterUpdater(id);
        }
    }

    /// <summary>
    /// What went wrong in an update the user couldn't be told about (an updater can't show a dialog), per document:
    /// shown and cleared by the next Terrain Line or Refresh in that model, not another one open beside it.
    /// </summary>
    private static readonly Dictionary<Document, string> Problems = [];

    /// <summary>The problem kept from the last update in $doc, cleared: for the commands that show it. Null when none.</summary>
    public static string? TakeProblem(Document doc) => Problems.Remove(doc, out var problem) ? problem : null;

    public void Execute(UpdaterData data)
    {
        var doc = data.GetDocument();
        try
        {
            // A family has no terrain lines (and no language).
            if (doc.IsFamilyDocument)
            {
                return;
            }
            // Neither of Jega's line styles in the model: no terrain line and no seed, so nothing to do. Asked first, as
            // this runs on every line drawn in every project, Jega's or not.
            var styles = LineStyles.TerrainLineStyles(doc);
            if (styles.Count == 0)
            {
                return;
            }
            // The labels in this document's language (usually the active project's), then back to the one spoken.
            var spoken = Core.Language.Current.Name;
            ProjectLanguage.Use(ProjectStore.ReadOptions(doc).Language, ribbon: false);
            try
            {
                // The first real line drawn after Terrain Line: the seed has done its job (its style is in the tool).
                if (data.GetAddedElementIds().Any(id => doc.GetElement(id) is CurveElement c && !ProjectStore.Is(c, TerrainLines.SeedRole)))
                {
                    TerrainLines.RemoveSeeds(doc);
                }
                // Only where a terrain line changed.
                var views = Views(doc, data, styles);
                if (views.Count == 0)
                {
                    return;  // no terrain line among the lines changed: nothing of Jega's to do
                }
                if (TerrainLines.Update(doc, views) is { } problem)
                {
                    Problems[doc] = problem;
                }
            }
            finally
            {
                ProjectLanguage.Use(spoken, ribbon: false);
            }
        }
        catch (Exception e)
        {
            // Never break the user's edit over a label: the next change, or the Terrain Line button, tries again.
            // Kept for the next Terrain Line or Refresh to show, and written down, so it can be traced.
            Problems[doc] = Fill(Strings.TerrainLine_UpdateFailed, e.GetType().Name, e.Message);
            JegaLog.Write(e);
        }
    }

    /// <summary>
    /// The views where a terrain line was drawn, changed or deleted: a line of a Jega style now; and a view with a label
    /// whose lines (ProjectStore.LinesOf) are gone or no longer of Jega's styles, as a deleted line can't be asked its
    /// view, nor a restyled one its style before: every view's labels asked when a line was deleted, else only those in
    /// the views of the other lines changed.
    /// Whoever drew the line, and whenever. Empty when only other lines changed, as an ordinary line in a section.
    /// </summary>
    private static HashSet<ElementId> Views(Document doc, UpdaterData data, Dictionary<ElementId, bool> styles)
    {
        bool Terrain(CurveElement c) => c.LineStyle is { } s && styles.ContainsKey(s.Id);
        var views = new HashSet<ElementId>();
        // Where a label may have lost its line: a line deleted can be anywhere (null: every view); one of another style
        // changed (perhaps it was a terrain line until this edit) only in its own view.
        HashSet<ElementId>? maybe = data.GetDeletedElementIds().Count > 0 ? null : [];
        foreach (var id in data.GetAddedElementIds())
        {
            if (doc.GetElement(id) is CurveElement { ViewSpecific: true } c && Terrain(c))
            {
                views.Add(c.OwnerViewId);
            }
        }
        foreach (var id in data.GetModifiedElementIds())
        {
            if (doc.GetElement(id) is CurveElement { ViewSpecific: true } c)
            {
                if (Terrain(c))
                {
                    views.Add(c.OwnerViewId);
                }
                else
                {
                    maybe?.Add(c.OwnerViewId);
                }
            }
        }
        if (maybe is null || maybe.Count > 0)
        {
            // A line no longer there, or no longer a terrain line; asked once for all the labels of its chain.
            var terrain = new Dictionary<string, bool>();
            bool Gone(string line)
            {
                if (!terrain.TryGetValue(line, out var still))
                {
                    terrain[line] = still = doc.GetElement(line) is CurveElement l && Terrain(l);
                }
                return !still;
            }
            var labels = maybe is null ? ProjectStore.LabelsWithLines(doc)
                : maybe.Where(v => !views.Contains(v)).SelectMany(v => ProjectStore.LabelsWithLines(doc, v));
            foreach (var label in labels)
            {
                if (!views.Contains(label.OwnerViewId) && ProjectStore.LinesOf(label).Any(Gone))
                {
                    views.Add(label.OwnerViewId);
                }
            }
        }
        return views;
    }

    public UpdaterId GetUpdaterId() => _id;

    public ChangePriority GetChangePriority() => ChangePriority.Annotations;

    public string GetUpdaterName() => Strings.TerrainLine_UpdaterName;

    public string GetAdditionalInformation() => Strings.TerrainLine_UpdaterInfo;
}
