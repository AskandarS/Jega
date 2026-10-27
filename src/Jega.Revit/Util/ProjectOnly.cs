using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Jega.Revit;

/// <summary>
/// Jega's buttons work on a project: greyed out with no document open and in the family editor, where a site,
/// a Toposolid or a level isn't to be had. Export, which only reads, has this; the others WritableProject (the same, for now). About
/// is always there (Always).
/// </summary>
public class ProjectOnly : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication app, CategorySet selected) =>
        app.ActiveUIDocument?.Document is { IsFamilyDocument: false };
}

/// <summary>The buttons that write the model. Not greyed out by Document.IsReadOnly: in Revit 2025 that greyed them out
/// in an ordinary project; a model that really can't be written is caught by JegaCommand.</summary>
public class WritableProject : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication app, CategorySet selected) =>
        app.ActiveUIDocument?.Document is { IsFamilyDocument: false };
}

/// <summary>A project's plan view active: the only views with an Orientation (Project North / True North).</summary>
public class PlanViewOnly : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication app, CategorySet selected) =>
        app.ActiveUIDocument?.Document is { IsFamilyDocument: false } && app.ActiveUIDocument.ActiveView is ViewPlan;
}

/// <summary>About: with no document open too (without a class Revit greys every button out then).</summary>
public class Always : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication app, CategorySet selected) => true;
}
