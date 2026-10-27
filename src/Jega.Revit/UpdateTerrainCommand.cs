using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Load Terrain CSV File": pick the terrain CSV, check it with Core (nothing is stored if a check fails), store the
/// points in the model, and draw the grid frame. The Toposolid is made by Generate Terrain.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class UpdateTerrainCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_LoadTerrain;

    // The grid frame and the labels are placed by the frame.
    protected override bool UsesFrame => true;

    // The folder last picked from, per user, so the picker starts there next time.
    private static readonly string LastFolderFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jega", "last-folder.txt");

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        if (doc.ActiveView is not ViewPlan view || view.GenLevel is null)
        {
            Receipt(new ReceiptText(Strings.LoadTerrain_OpenPlan) { Warning = true });
            return Result.Cancelled;
        }
        if (PickFile() is not { } path)
        {
            return Result.Cancelled;
        }

        TerrainGrid grid;
        string csv;
        try
        {
            csv = File.ReadAllText(path);
            grid = TerrainGrid.Parse(csv);
        }
        catch (Exception e) when (e is TerrainGridException or IOException or UnauthorizedAccessException)
        {
            // The file's own problem (TerrainGrid's message is for the user): it says what's wrong with it.
            Receipt(new ReceiptText(Strings.LoadTerrain_Unusable) { Lines = [e.Message], Warning = true });
            return Result.Cancelled;
        }

        // The elevation cross made before the transaction (a family document can't be made inside one), loaded inside.
        var problems = new List<string>();
        if (CrossSymbol.Prepare(doc) is { } crossProblem)
        {
            problems.Add(crossProblem);
        }
        int views;
        using (var t = new Transaction(doc, Strings.Undo_LoadTerrainFile))
        {
            t.Start();
            ProjectStore.WriteTerrain(doc, csv, Path.GetFileName(path));
            views = GridFrameWriter.Draw(doc, view, grid, Placement.Get(doc));
            if (CrossSymbol.Load(doc) is { } crossLoad)
            {
                problems.Add(crossLoad);
            }
            if (TerrainLines.Update(doc) is { } labelProblem)
            {
                problems.Add(labelProblem);
            }
            if (t.Commit() != TransactionStatus.Committed)
            {
                Receipt(new ReceiptText(Strings.LoadTerrain_RevitStopped) { Warning = true });
                return Result.Failed;
            }
        }
        RememberFolder(path);

        // A Toposolid already made still shows the terrain before: Refresh makes it from this one (a step, as it must be
        // done). What can be done next as lines, as Load from Address says them.
        var hasToposolid = LotTerrainCommand.Exists(doc);
        var steps = hasToposolid ? new List<string> { Strings.Receipt_RefreshToposolidStep } : [];
        var loaded = Fill(Strings.LoadTerrain_Loaded, Path.GetFileName(path));
        Receipt(new ReceiptText(problems.Count > 0 ? problems[0] : loaded)
        {
            Lines = [.. problems.Skip(1), problems.Count > 0 ? loaded : null,
                hasToposolid ? null : Strings.Address_GenerateLine, Receipts.AbsoluteLevels(doc) ? Receipts.FloorLine(doc) : null],
            Steps = steps,
            Details = [.. Receipts.Terrain(doc, grid, views), Receipts.Levels(doc)],
            Warning = problems.Count > 0,
        });
        return Result.Succeeded;
    }

    private static string? PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.LoadTerrain_PickFile,
            Filter = Strings.LoadTerrain_Filter,
            CheckFileExists = true,
        };
        try
        {
            if (File.Exists(LastFolderFile) && File.ReadAllText(LastFolderFile).Trim() is { } folder && Directory.Exists(folder))
            {
                dialog.InitialDirectory = folder;
            }
        }
        catch (IOException)
        {
            // No remembered folder: the picker starts where Windows decides.
        }
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void RememberFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastFolderFile)!);
            File.WriteAllText(LastFolderFile, Path.GetDirectoryName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only a convenience: the next pick starts elsewhere.
        }
    }
}
