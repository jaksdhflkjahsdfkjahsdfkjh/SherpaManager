using System.Windows;
using Media = System.Windows.Media;
using SherpaManager.Models;
using SherpaManager.Services;

namespace SherpaManager;

public partial class ActivationPreflightWindow : Window
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    public bool Proceed { get; private set; }
    public bool SkipFuturePreviews => SkipPreviewCheckBox.IsChecked == true;

    public ActivationPreflightWindow(ActivationPreflight preflight)
    {
        InitializeComponent();

        TitleText.Text = $"Switch to {preflight.ProfileName}?";
        HeadlineText.Text = preflight.Headline;
        ProceedButton.Content = preflight.HasProblems ? "Switch anyway" : "Switch now";

        SectionsList.ItemsSource = preflight.Sections
            .Select(section => new SectionView(section.Title,
                section.Items.Select(item => new RowView(item)).ToList()))
            .ToList();
    }

    private void Proceed_Click(object sender, RoutedEventArgs e)
    {
        Proceed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    public sealed record SectionView(string Title, IReadOnlyList<RowView> Rows);

    public sealed class RowView
    {
        public RowView(PreflightItem item)
        {
            Title = item.Title;
            Detail = item.Detail ?? string.Empty;
            Accent = item.Severity switch
            {
                PreflightSeverity.Problem => FindBrush("ProblemBrush", Media.Colors.Salmon),
                PreflightSeverity.Caution => FindBrush("CautionBrush", Media.Colors.Goldenrod),
                _ => FindBrush("TextBrush", Media.Colors.White)
            };
        }

        public string Title { get; }
        public string Detail { get; }
        public Media.Brush Accent { get; }
        public Visibility DetailVisibility =>
            string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;

        private static Media.Brush FindBrush(string key, Media.Color fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as Media.Brush ?? new Media.SolidColorBrush(fallback);
    }
}
