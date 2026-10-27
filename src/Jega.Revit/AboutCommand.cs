using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>"About": which build this is, the licence (GPL-3.0, no warranty) and the open data's credit.</summary>
[Transaction(TransactionMode.ReadOnly)]
public class AboutCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        if (data.Application.ActiveUIDocument?.Document is { } doc)
        {
            ProjectLanguage.Apply(doc);
        }
        new TaskDialog(Strings.Ribbon_About)
        {
            MainInstruction = Strings.About_Title,
            MainContent = Fill(Strings.About_Content, SiteFetcher.Credit, Strings.Author),
            FooterText = BuildInfo.Text,
        }.Show();
        return Result.Succeeded;
    }
}
