using System.ComponentModel;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>The keys on this PC (Settings, Data access) as a SiteFetcher, for the commands that fetch.</summary>
internal static class Keys
{
    /// <summary>Where a Datafordeler key is made (free: Administration → your user → IT system → API key).</summary>
    public const string KeyUrl = "https://datafordeler.dk";

    /// <summary>Opens KeyUrl in the user's browser; nothing if Windows has none to open it with.</summary>
    public static void OpenKeyPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(KeyUrl) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No browser registered: the text beside the link says where to go.
        }
    }

    /// <summary>The fetcher, or null with $problem saying what to do: no key, a key this Windows user can't read, or a settings file that can't be read.</summary>
    public static SiteFetcher? Fetcher(out string problem)
    {
        var machine = MachineOptions.Load(out var unreadable);
        problem = "";
        if (unreadable is not null)
        {
            problem = Fill(Strings.Keys_FileUnreadable, unreadable);
            return null;
        }
        if (!Secret.TryUnprotect(machine.DatafordelerKeyProtected, out var key))
        {
            problem = Strings.Keys_KeyUnreadable;
            return null;
        }
        if (key.Length == 0)
        {
            problem = Strings.Keys_NoKey;
            return null;
        }
        return new SiteFetcher(key, machine.AddressToken);
    }

    /// <summary>
    /// The fetcher; without one, what's wrong with an "Open Settings" button (Settings then opens on Data access, as
    /// the key is missing) and one that also opens datafordeler.dk to get a key; once a key is saved there, the
    /// fetcher, so the command carries on. Null: cancelled, or still no key (said once more).
    /// </summary>
    public static SiteFetcher? FetcherOrAsk(ExternalCommandData data)
    {
        if (Fetcher(out var problem) is { } fetcher)
        {
            return fetcher;
        }
        var dialog = new TaskDialog(Ui.Title) { MainInstruction = Strings.Keys_Needed, MainContent = problem, CommonButtons = TaskDialogCommonButtons.Cancel };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Keys_OpenSettings, Strings.Keys_OpenSettingsNote);
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Strings.Keys_GetKey, Strings.Keys_GetKeyNote);
        var answer = dialog.Show();
        if (answer == TaskDialogResult.CommandLink2)
        {
            OpenKeyPage();
        }
        else if (answer != TaskDialogResult.CommandLink1)
        {
            return null;
        }
        var message = "";
        if (new SettingsCommand().Execute(data, ref message, new ElementSet()) != Result.Succeeded)
        {
            return null;
        }
        if (Fetcher(out problem) is { } saved)
        {
            return saved;
        }
        TaskDialog.Show(Ui.Title, problem);
        return null;
    }
}
