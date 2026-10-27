using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Jega.Core;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>
/// The elevations along the lot boundary: the offset field, and per lot boundary the highest and lowest point (at a
/// corner or between two), the span and the length, as terrain and as terrain + offset, all in m DVR90. No corner
/// numbers or list: nothing in the model shows which corner is which. Recomputed on every change of the offset
/// (Core only, nothing in the model changes); the offset the window closes with is kept for the project.
/// </summary>
internal sealed class ResultWindow : Window
{
    private readonly TerrainGrid _grid;
    private readonly IReadOnlyList<DrawnEdge> _edges;
    private readonly TextBox _offset = new() { Width = 90, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _results = new();

    /// <summary>The offset the window was closed with, mm.</summary>
    public int Offset { get; private set; }

    public ResultWindow(TerrainGrid grid, IReadOnlyList<DrawnEdge> edges, int offset, string source)
    {
        (_grid, _edges, Offset) = (grid, edges, offset);
        Title = Strings.LotEdge_WindowTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var offsetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        offsetRow.Children.Add(new TextBlock { Text = Strings.LotEdge_Offset, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) });
        offsetRow.Children.Add(_offset);
        offsetRow.Children.Add(_error);
        _offset.Text = offset.ToString(CultureInfo.InvariantCulture);
        _offset.TextChanged += (_, _) => Recompute();

        var close = new Button { Content = Strings.LotEdge_Close, Padding = new Thickness(16, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(offsetRow);
        root.Children.Add(new ScrollViewer { Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 });
        root.Children.Add(Note(Ui.Fill(Strings.LotEdge_WindowNote, source)));
        root.Children.Add(close);
        Content = root;
        Recompute();
    }

    private void Recompute()
    {
        if (!int.TryParse(_offset.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var offset))
        {
            _error.Text = Strings.LotEdge_OffsetWhole;
            return;
        }
        _error.Text = "";
        Offset = offset;
        _results.Children.Clear();
        for (var n = 0; n < _edges.Count; n++)
        {
            var edge = _edges[n];
            var title = _edges.Count == 1 ? Strings.LotEdge_Boundary : Ui.Fill(Strings.LotEdge_BoundaryN, n + 1);
            title = string.IsNullOrEmpty(edge.ViewName) ? title : Ui.Fill(Strings.LotEdge_InView, title, edge.ViewName);
            _results.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, n == 0 ? 0 : 14, 0, 4) });
            try
            {
                Show(EdgeLevels.Compute(_grid, edge.Ring, offset));
            }
            catch (TerrainGridException e)
            {
                _results.Children.Add(new TextBlock { Text = e.Message, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
            }
        }
    }

    private void Show(EdgeLevelsResult r)
    {
        var mono = new FontFamily("Consolas");
        string Pair(LevelPoint p) => $"{Units.Level(p.Terrain)} / {Units.Level(p.Level)}";
        string Where(LevelPoint p) => p.Corner is not null ? Strings.LotEdge_AtCorner : Strings.LotEdge_BetweenCorners;

        var summary = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        (string, string)[] rows =
        [
            (Strings.LotEdge_Highest, $"{Pair(r.Highest)}   {Where(r.Highest)}"),
            (Strings.LotEdge_Lowest, $"{Pair(r.Lowest)}   {Where(r.Lowest)}"),
            (Strings.LotEdge_Span, Ui.Fill(Strings.LotEdge_SpanValue, r.Span)),
            (Strings.LotEdge_Length, Ui.Fill(Strings.LotEdge_LengthValue, Units.Metres(r.Length), r.Points.Count)),
        ];
        foreach (var (label, value) in rows)
        {
            summary.RowDefinitions.Add(new RowDefinition());
            var l = new TextBlock { Text = label, Foreground = Brushes.DimGray, Margin = new Thickness(0, 1, 14, 1) };
            var v = new TextBlock { Text = value, FontFamily = mono, Margin = new Thickness(0, 1, 0, 1) };
            Grid.SetRow(l, summary.RowDefinitions.Count - 1);
            Grid.SetRow(v, summary.RowDefinitions.Count - 1);
            Grid.SetColumn(v, 1);
            summary.Children.Add(l);
            summary.Children.Add(v);
        }
        _results.Children.Add(summary);
    }

    private static TextBlock Note(string text) =>
        new() { Text = text, Foreground = Brushes.DimGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
}
