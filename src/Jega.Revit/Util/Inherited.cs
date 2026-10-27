using Autodesk.Revit.DB;

namespace Jega.Revit;

/// <summary>
/// What an element Jega makes again (the Toposolid, the neighbouring buildings, the site plan import) takes over from
/// the one it replaces: its workset in a workshared model and its phases, so the user's sorting survives Generate
/// Terrain, Refresh and Load from Address. Read before the old one is deleted; the first one made goes where Revit
/// puts it (the active workset, the view's phase).
/// </summary>
internal sealed record Inherited(int? Workset, ElementId? Created, ElementId? Demolished)
{
    /// <summary>What $old has; null without one.</summary>
    public static Inherited? From(Element? old) => old is null ? null : new(
        old.Document.IsWorkshared ? old.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM)?.AsInteger() : null,
        old.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId(),
        old.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED)?.AsElementId());

    /// <summary>Inside a transaction: given to $made where Revit lets it be set (an element owned by a view keeps the
    /// view's workset). Created before demolished, as Revit wants them in that order.</summary>
    public void To(Element made)
    {
        if (Workset is { } workset)
        {
            Set(made, BuiltInParameter.ELEM_PARTITION_PARAM, p => p.Set(workset));
        }
        if (Created is { } created && created != ElementId.InvalidElementId)
        {
            Set(made, BuiltInParameter.PHASE_CREATED, p => p.Set(created));
        }
        if (Demolished is { } demolished && demolished != ElementId.InvalidElementId)
        {
            Set(made, BuiltInParameter.PHASE_DEMOLISHED, p => p.Set(demolished));
        }
    }

    private static void Set(Element e, BuiltInParameter which, Func<Parameter, bool> set)
    {
        if (e.get_Parameter(which) is { IsReadOnly: false } p)
        {
            try
            {
                set(p);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Revit refuses it (a workset gone, a phase order it won't take): left where Revit put it.
            }
        }
    }
}
