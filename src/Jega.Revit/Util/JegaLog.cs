using System.IO;

namespace Jega.Revit;

/// <summary>
/// %APPDATA%\Jega\jega.log: every failure Jega caught but didn't foresee (JegaCommand), and what it can't show (the
/// updater, a view switch), each with the Jega and Revit that ran, so a report can be traced. Kept under 1 MB: a
/// bigger one becomes jega.old.log and a new one starts. Never fails itself.
/// </summary>
internal static class JegaLog
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jega");

    /// <summary>Revit's version and build, set by App at startup.</summary>
    public static string Revit { get; set; } = "";

    private const long MaxBytes = 1024 * 1024;

    public static void Write(Exception e) => Append($"{e}\n");

    /// <summary>A line on why Jega did something it doesn't say (e.g. why a Toposolid was made again), for a report.</summary>
    public static void Note(string text) => Append(text);

    private static void Append(string text)
    {
        try
        {
            var log = Path.Combine(Folder, "jega.log");
            Directory.CreateDirectory(Folder);
            if (new FileInfo(log) is { Exists: true, Length: > MaxBytes })
            {
                File.Move(log, Path.Combine(Folder, "jega.old.log"), overwrite: true);
            }
            File.AppendAllText(log, $"{DateTime.Now:s} Jega {Core.Build.Version}, Revit {Revit}\n{text}\n\n");
        }
        catch (Exception)
        {
        }
    }
}
