using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Export": the site plan DXF and the terrain CSV written from what the model keeps, to a folder the user picks (the
/// project's own folder to start, else Documents): &lt;name&gt;-siteplan.dxf and &lt;name&gt;-terrain.csv, the name the
/// project's, else the address. The DXF is drawn again from the stored surroundings with the project's area and
/// distance to the boundary (Settings), so it is the site plan as it is now; without surroundings (a terrain of
/// one's own) only the CSV. Reads the model, changes nothing.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ExportCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_Export;

    // Only reads the model; writes its two files. No read-only-file warning.
    protected override bool Writes => false;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        var options = ProjectStore.ReadOptions(doc);
        var (result, receipt) = Run(doc, options.SiteplanMarginM, options.SetbackM, options.HiddenLayerList, options.RoofLabels);
        if (receipt is not null)
        {
            Receipt(receipt);
        }
        return result;
    }

    /// <summary>
    /// Asks for the folder and writes the files; $marginM and $setbackM: the site plan's area and distance to the
    /// boundary (Settings' saved ones); $hiddenLayers and $roofLabels: the layers left out and the roof levels, as in
    /// the import. Returns how it went and its receipt (none when cancelled: nothing to say).
    /// </summary>
    private static (Result Result, ReceiptText? Receipt) Run(Document doc, double marginM, double setbackM, IReadOnlyList<string> hiddenLayers,
        SiteplanDxf.RoofLabels roofLabels)
    {
        if (!StoredTerrain.TryGet(doc, out var stored, out var grid, out var problem))
        {
            return (Result.Cancelled, new ReceiptText(problem) { Warning = true });
        }
        var site = ProjectStore.ReadSite(doc);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Export_PickFolder,
            InitialDirectory = doc.PathName.Length > 0 ? Path.GetDirectoryName(doc.PathName) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true)
        {
            return (Result.Cancelled, null);
        }

        var name = doc.PathName.Length > 0 ? Path.GetFileNameWithoutExtension(doc.PathName) : Safe(site?.Address ?? doc.Title);
        var csvPath = Path.Combine(dialog.FolderName, $"{name}-terrain.csv");
        var dxfPath = Path.Combine(dialog.FolderName, $"{name}-siteplan.dxf");
        var json = ProjectStore.ReadSurroundings(doc);
        // Files of the same name there already (an Export before): asked first, never replaced without a word. Not
        // overwritten or closed: nothing written, nothing to say.
        string[] files = json is null ? [csvPath] : [dxfPath, csvPath];
        var there = files.Where(File.Exists).Select(Path.GetFileName).ToList();
        if (there.Count > 0 && Ask(Strings.Export_AskOverwrite, Fill(Strings.Export_AskOverwriteContent, string.Join(", ", there)),
                [(Strings.Export_Overwrite, Strings.Export_OverwriteNote), (Strings.Export_KeepFiles, Strings.Export_KeepFilesNote)]) != 0)
        {
            return (Result.Cancelled, null);
        }
        var written = new List<string>();
        var notes = new List<string>();
        try
        {
            File.WriteAllText(csvPath, stored.Csv);
            written.Add(Path.GetFileName(csvPath));
            if (json is not null)
            {
                var surroundings = Surroundings.Parse(json);
                var margin = Math.Min(marginM, surroundings.Margin);
                if (margin < marginM)
                {
                    notes.Add(Fill(Strings.Export_MarginCut, margin));
                }
                // Its 0,0 at the project's origin, as the import in the model; true north up whatever Settings says
                // of the import (the surveyor's and the authority's plan lie north up).
                File.WriteAllText(dxfPath, surroundings.SitePlan(grid, site?.Address ?? name, Core.Language.Current, margin, setbackM,
                    hiddenLayers, roofLabels, Placement.Origin(doc)));
                written.Insert(0, Path.GetFileName(dxfPath));
            }
            else
            {
                notes.Add(Strings.Export_OnlyTerrain);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return (Result.Failed, new ReceiptText(Strings.Export_Stopped)
            {
                Lines = [written.Count > 0 ? Fill(Strings.Export_Written, string.Join(", ", written)) : null, Strings.Export_StoppedNext],
                Details = [e.Message],
                Warning = true,
            });
        }
        return (Result.Succeeded, new ReceiptText(Fill(Strings.Export_Done, dialog.FolderName))
        {
            Lines = [.. written, .. notes],
            Footer = Receipts.Credit,
        });
    }

    // An address as a file name: what Windows refuses replaced, e.g. "Skolegade 1, 7100 Vejle".
    private static string Safe(string text) =>
        string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
}
