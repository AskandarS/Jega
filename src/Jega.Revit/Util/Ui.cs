using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jega.Revit.Resources;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommandLinkId = Autodesk.Revit.UI.TaskDialogCommandLinkId;
using TaskDialogIcon = Autodesk.Revit.UI.TaskDialogIcon;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;
using TextBox = System.Windows.Controls.TextBox;
using ToolTip = System.Windows.Controls.ToolTip;

namespace Jega.Revit;

/// <summary>The pieces Jega's windows are built from, so they look the same.</summary>
internal static class Ui
{
    public const double LabelWidth = 190;

    /// <summary>The title of Jega's dialogs: the running command's button (JegaCommand), "Jega" outside one. Revit puts
    /// the add-in's name in front ("Jega - Nabobygninger"), so "Jega" alone would read "Jega - Jega".</summary>
    public static string Title { get; set; } = "Jega";

    /// <summary>A button's text on one line: "Nabo-\nbygninger" is Nabobygninger, "Hent fra\nadresse" Hent fra adresse.</summary>
    public static string OneLine(string text) => text.Replace("\r", "").Replace("-\n", "").Replace("\n", " ");

    /// <summary>A text from Strings (Resources/Strings.resx) with its {0}, {1} … filled in, numbers as Units.Numbers shows them.</summary>
    public static string Fill(string text, params object?[] args) => string.Format(Units.Numbers, text, args);

    /// <summary>How far and how much the site goes, with what shows as 0 left out ("flyttes 14,73 m", "drejes 3,0°",
    /// both, or null when it stays): a dialog never says "flyttes 0,00 m" or "-0,0°".</summary>
    public static string? Movement(double metres, double degrees)
    {
        var moves = Math.Abs(metres) >= 0.005;
        var turns = Math.Abs(degrees) >= 0.05;
        return (moves, turns) switch
        {
            (true, true) => Fill(Strings.Move_Both, metres, degrees),
            (true, false) => Fill(Strings.Move_Only, metres),
            (false, true) => Fill(Strings.Move_Turn, degrees),
            _ => null,
        };
    }

    /// <summary>$moveNote with the Movement filled in, or $stayNote when the site doesn't move.</summary>
    public static string Moved(string moveNote, string stayNote, double metres, double degrees) =>
        Movement(metres, degrees) is { } go ? Fill(moveNote, go) : stayNote;

    public static TextBox Field(double width = 90) => new() { Width = width, Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left };

    public static ComboBox Choice(params (string Value, string Label)[] items)
    {
        var box = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (value, label) in items)
        {
            box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
        box.SelectedIndex = 0;
        return box;
    }

    public static void Select(ComboBox box, string value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value) ?? box.Items[0];

    public static string Value(ComboBox box) => (string)((ComboBoxItem)box.SelectedItem).Tag;

    public static TextBlock Section(string text, bool first = false) => new()
    {
        Text = text, Foreground = Brushes.DimGray, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 0 : 14, 0, 6),
    };

    public static TextBlock Note(string text = "", double top = 2) => new()
    {
        Text = text, Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(LabelWidth, top, 0, 2),
    };

    public static TextBlock Error() => new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public static FrameworkElement Indented(FrameworkElement e)
    {
        e.Margin = new Thickness(LabelWidth, 4, 0, 2);
        return e;
    }

    /// <summary>A label, the field, and a grey note after it.</summary>
    public static Grid Row(string label, FrameworkElement field, string? note, double top = 2)
    {
        var grid = new Grid { Margin = new Thickness(0, top, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        if (note is not null)
        {
            var n = new TextBlock { Text = note, Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(n, 2);
            grid.Children.Add(n);
        }
        return grid;
    }

    /// <summary>A small grey "?" in a ring that shows $text on hover, wrapped (a plain tooltip runs on in one line) and
    /// long enough to read. It shows at once: the default half second felt slow on a help mark. A click shows it too
    /// (people click a "?"), and it stays until the mouse leaves.</summary>
    public static Border Help(string text)
    {
        var tip = new ToolTip { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 } };
        var help = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip,
            Child = new TextBlock
            {
                Text = "?", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        tip.PlacementTarget = help;
        help.MouseLeftButtonUp += (_, e) => { tip.IsOpen = true; e.Handled = true; };
        help.MouseLeave += (_, _) => tip.IsOpen = false;
        ToolTipService.SetInitialShowDelay(help, 100);
        ToolTipService.SetShowDuration(help, 30_000);
        return help;
    }

    public static Button Button(string text, double padding = 8) => new() { Content = text, Padding = new Thickness(padding, 1, padding, 1), Margin = new Thickness(6, 0, 0, 0) };

    /// <summary>Shows a command's receipt (ReceiptText): the result, its lines and steps, the details folded away.</summary>
    public static void Receipt(ReceiptText r)
    {
        var shown = r.Lines.Where(l => !string.IsNullOrWhiteSpace(l))
            .Concat(r.Steps.Select((s, i) => $"{i + 1}. {s}")).ToList();
        var details = r.Details.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        var dialog = new TaskDialog(Title)
        {
            MainInstruction = r.Result,
            MainContent = string.Join("\n", shown),
            ExpandedContent = string.Join("\n", details),
            FooterText = r.Footer ?? "",
            MainIcon = r.Warning ? TaskDialogIcon.TaskDialogIconWarning : TaskDialogIcon.TaskDialogIconNone,
        };
        dialog.Show();
    }

    /// <summary>
    /// Asks before something is done that can't be had back: $question on top, $content under it, a command link per
    /// choice (its text and a grey line under it), Cancel, and $footer. Returns the choice's index from 0, or null when
    /// cancelled or closed. Up to four choices (Revit's command links).
    /// </summary>
    public static int? Ask(string question, string content, IReadOnlyList<(string Text, string? Note)> choices, string? footer = null)
    {
        TaskDialogCommandLinkId[] links = [TaskDialogCommandLinkId.CommandLink1, TaskDialogCommandLinkId.CommandLink2,
            TaskDialogCommandLinkId.CommandLink3, TaskDialogCommandLinkId.CommandLink4];
        var dialog = new TaskDialog(Title)
        {
            MainInstruction = question,
            MainContent = content,
            CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel,
            FooterText = footer ?? "",
        };
        for (var i = 0; i < choices.Count && i < links.Length; i++)
        {
            if (choices[i].Note is { } note)
            {
                dialog.AddCommandLink(links[i], choices[i].Text, note);
            }
            else
            {
                dialog.AddCommandLink(links[i], choices[i].Text);
            }
        }
        var result = dialog.Show();
        TaskDialogResult[] results = [TaskDialogResult.CommandLink1, TaskDialogResult.CommandLink2, TaskDialogResult.CommandLink3, TaskDialogResult.CommandLink4];
        var index = Array.IndexOf(results, result);
        return index >= 0 && index < choices.Count ? index : null;
    }

    /// <summary>A command stopped by a failure it didn't foresee (JegaCommand): what it was, where the log is, and a link
    /// that opens the log's folder. The error's type under "Show details"; the rest is in the log.</summary>
    public static void Failed(Exception e)
    {
        var dialog = new TaskDialog(Title)
        {
            MainInstruction = Fill(Strings.Failed_Title, e.Message),
            MainContent = Strings.Failed_Content,
            ExpandedContent = e.GetType().FullName,
            MainIcon = TaskDialogIcon.TaskDialogIconError,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Strings.Failed_OpenFolder);
        if (dialog.Show() == TaskDialogResult.CommandLink1)
        {
            try
            {
                Process.Start(new ProcessStartInfo(JegaLog.Folder) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // The path is in the message: it can be opened by hand.
            }
        }
    }
}

/// <summary>
/// A command's receipt, the same for every command: $Result in one line (what happened, past tense; or, with $Warning,
/// the problem that needs action), at most a few $Lines worth seeing at once, the $Steps to take now (numbered), and
/// everything else ($Details: numbers, coordinates, notes, error texts) under Revit's "Show details". $Footer: a fixed
/// line under it all (the data's credit). Empty lines are left out.
/// </summary>
internal sealed record ReceiptText(string Result)
{
    public IReadOnlyList<string?> Lines { get; init; } = [];
    public IReadOnlyList<string> Steps { get; init; } = [];
    public IReadOnlyList<string?> Details { get; init; } = [];
    public bool Warning { get; init; }
    public string? Footer { get; init; }
}
