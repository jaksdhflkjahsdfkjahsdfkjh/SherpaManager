using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Media = System.Windows.Media;
using SherpaManager.Models;
using SherpaManager.Services;

namespace SherpaManager;

public partial class DisplayLayoutWindow : Window
{
    private const double CanvasWidth = 560;
    private const double CanvasHeight = 300;

    private readonly DisplaySnapshot? _snapshot;

    public DisplayLayoutWindow(string profileName, DisplaySnapshot? snapshot)
    {
        InitializeComponent();
        _snapshot = snapshot;

        TitleText.Text = $"{profileName} display layout";

        var view = DisplayLayoutBuilder.Build(snapshot, CanvasWidth, CanvasHeight);
        SummaryText.Text = view.Summary;

        if (view.HasTiles) DrawLayout(view);
        else
        {
            NoLayoutText.Text = view.Summary;
            NoLayoutText.Visibility = Visibility.Visible;
        }

        MonitorList.ItemsSource = view.Tiles;
        MonitorCard.Visibility = view.HasTiles ? Visibility.Visible : Visibility.Collapsed;

        var surround = BuildSurroundRows(snapshot?.NvidiaSurround);
        SurroundList.ItemsSource = surround;
        SurroundCard.Visibility = surround.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DrawLayout(DisplayLayoutView view)
    {
        LayoutCanvas.Width = view.Width;
        LayoutCanvas.Height = view.Height;

        foreach (var tile in view.Tiles)
        {
            var caption = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            caption.Children.Add(new TextBlock
            {
                Text = tile.Name,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("TextBrush")
            });
            caption.Children.Add(new TextBlock
            {
                Text = tile.Detail,
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = Brush("MutedTextBrush")
            });
            if (tile.IsPrimary || tile.RotationText.Length > 0)
                caption.Children.Add(new TextBlock
                {
                    Text = string.Join("  ", new[]
                    {
                        tile.IsPrimary ? "primary" : null,
                        tile.RotationText.Length > 0 ? $"rotated {tile.RotationText}" : null
                    }.Where(part => part is not null)),
                    TextAlignment = TextAlignment.Center,
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = Brush("AccentBrush")
                });

            var border = new Border
            {
                // Inset by the border width so adjacent monitors read as separate
                // panels instead of one block with a shared edge.
                Width = Math.Max(2, tile.Width - 4),
                Height = Math.Max(2, tile.Height - 4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(tile.IsPrimary ? 2 : 1),
                BorderBrush = Brush(tile.IsPrimary ? "AccentBrush" : "BorderBrush"),
                Background = Brush("CardBrush"),
                Padding = new Thickness(6),
                Child = caption
            };

            Canvas.SetLeft(border, tile.X + 2);
            Canvas.SetTop(border, tile.Y + 2);
            LayoutCanvas.Children.Add(border);
        }
    }

    private static List<SurroundRow> BuildSurroundRows(NvidiaSurroundSnapshot? surround)
    {
        var rows = new List<SurroundRow>();
        if (surround is null || !surround.ApiAvailable) return rows;

        rows.Add(new SurroundRow("State", surround.StatusKnown
            ? surround.Enabled ? "Enabled" : "Disabled"
            : "Unknown"));

        var grid = surround.DisplayGrids?.FirstOrDefault(candidate => candidate.Rows * candidate.Columns > 1);
        if (grid is not null)
        {
            rows.Add(new SurroundRow("Topology", $"{grid.Rows} × {grid.Columns}"));
            rows.Add(new SurroundRow("Per display", grid.RefreshRate > 0
                ? $"{grid.PerDisplayWidth}×{grid.PerDisplayHeight} · {grid.RefreshRate} Hz"
                : $"{grid.PerDisplayWidth}×{grid.PerDisplayHeight}"));
            rows.Add(new SurroundRow("Bezel correction",
                grid.ApplyWithBezelCorrection ? "Applied" : "Not applied"));
            if (grid.BitsPerPixel > 0) rows.Add(new SurroundRow("Colour depth", $"{grid.BitsPerPixel}-bit"));

            var panels = grid.Displays?
                .OrderBy(display => display.Row)
                .ThenBy(display => display.Column)
                .Select(display => $"R{display.Row + 1}C{display.Column + 1} 0x{display.DisplayId:X8}")
                .ToList() ?? [];
            if (panels.Count > 0) rows.Add(new SurroundRow("Panel order", string.Join("   ", panels)));
        }
        else if (!string.IsNullOrWhiteSpace(surround.Description))
        {
            rows.Add(new SurroundRow("Detail", surround.Description));
        }

        if (!surround.FullGridCaptured && surround.StatusKnown && surround.Enabled)
            rows.Add(new SurroundRow("Capture",
                "This snapshot predates full grid capture. Recapture it while Surround is on."));

        return rows;
    }

    private void OpenNvidiaSettings_Click(object sender, RoutedEventArgs e)
    {
        var target = NvidiaAppLocator.Locate();
        if (target is null)
        {
            SummaryText.Text = "The NVIDIA app was not found. Install it from nvidia.com.";
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(target.FileName) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(target.Arguments)) startInfo.Arguments = target.Arguments;
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            SummaryText.Text = $"Could not open {target.DisplayName}: {exception.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static Media.Brush Brush(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Media.Brush
        ?? new Media.SolidColorBrush(Media.Colors.Gray);

    public sealed record SurroundRow(string Label, string Value);
}
