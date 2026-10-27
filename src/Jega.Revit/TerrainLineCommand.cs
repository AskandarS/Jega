using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Terrain Line", in a plan view only (else it says so): makes the line styles "Jega Line (elevations only)" and "Jega Line" if needed, brings
/// every label up to date, and starts Revit's Detail Line tool in "elevations only" (Create Similar). Then the updater: draw,
/// move or delete such lines and their elevations follow.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class TerrainLineCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_TerrainLine;

    // The labels and the seed are placed by the frame: after a new work direction, Refreshed first.
    protected override bool UsesFrame => true;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        // Terrain lines are detail lines on a plan: in a 3D view, section or sheet there's nothing to draw on.
        if (doc.ActiveView is not ViewPlan)
        {
            TaskDialog.Show(Ui.Title, Fill(Strings.TerrainLine_NotPlan, doc.ActiveView.Name));
            return Result.Cancelled;
        }
        if (!StoredTerrain.TryGet(doc, out _, out _, out var problem))
        {
            TaskDialog.Show(Ui.Title, problem);
            return Result.Cancelled;
        }
        var first = LineStyles.Find(doc, LineStyles.TerrainLineLevelsOnlyName) is null;
        var crossProblem = CrossSymbol.Prepare(doc);
        ElementId? similar = null;
        string? labelProblem;
        using (var t = new Transaction(doc, Strings.Undo_TerrainLine))
        {
            t.Start();
            crossProblem ??= CrossSymbol.Load(doc);
            LineStyles.TerrainLine(doc);
            var style = LineStyles.TerrainLineLevelsOnly(doc);
            TerrainLines.RemoveSeeds(doc);
            // This plan's labels only: in a shared model, Terrain Line is for everyone, each in their own views. A
            // problem the updater kept may be another view's, so it is said too.
            labelProblem = TerrainLines.Update(doc, new HashSet<ElementId> { doc.ActiveView.Id }) ?? TerrainLineUpdater.TakeProblem(doc);
            // Revit's Detail Line tool starts in the last style used (often Thin Lines) and the API can't set it,
            // but Create Similar on a line starts it in that line's style: an elevations-only line in this view, or a
            // short seed at the address point that goes again as soon as a real line is drawn.
            if (doc.ActiveView is ViewPlan view)
            {
                similar = new FilteredElementCollector(doc, view.Id).OfClass(typeof(CurveElement)).Cast<CurveElement>()
                    .FirstOrDefault(c => c.LineStyle?.Id == style.Id)?.Id;
                if (similar is null)
                {
                    var z = view.GenLevel?.ProjectElevation ?? 0;
                    var at = Placement.Get(doc).Point(0, 0, z);
                    var seed = doc.Create.NewDetailCurve(view, Line.CreateBound(at, at + XYZ.BasisX * Units.FeetFromMm(100)));
                    seed.LineStyle = style;
                    ProjectStore.Mark(seed, TerrainLines.SeedRole);
                    similar = seed.Id;
                }
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                TaskDialog.Show(Ui.Title, Strings.TerrainLine_NotStarted);
                return Result.Failed;
            }
        }
        // Problems are said every time, not only at the first run.
        if (!first && (crossProblem ?? labelProblem) is not null)
        {
            TaskDialog.Show(Ui.Title, string.Join("\n\n", new[] { crossProblem, labelProblem }.OfType<string>()));
        }
        if (first)
        {
            TaskDialog.Show(Ui.Title,
                Fill(Strings.TerrainLine_FirstRun, LineStyles.TerrainLineLevelsOnlyName, LineStyles.TerrainLineName)
                + "\n\n" + (crossProblem ?? Fill(Strings.TerrainLine_FirstRunCross, CrossSymbol.Name))
                + (labelProblem is null ? "" : $"\n\n{labelProblem}"));
        }
        if (similar is not null)
        {
            data.Application.ActiveUIDocument.Selection.SetElementIds([similar]);
            data.Application.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.CreateSimilar));
        }
        return Result.Succeeded;
    }
}
