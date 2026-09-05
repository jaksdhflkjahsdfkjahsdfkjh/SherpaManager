using System.Text;
using System.Windows;
using Media = System.Windows.Media;
using SherpaManager.Models;
using SherpaManager.Services;

namespace SherpaManager;

public partial class ActivationHistoryWindow : Window
{
    private readonly IReadOnlyList<ActivationRecord> _records;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    public ActivationHistoryWindow(IReadOnlyList<ActivationRecord> records)
    {
        InitializeComponent();
        _records = records;

        SummaryText.Text = records.Count == 0
            ? "No profile has been switched since Sherpa Manager started. Switches are recorded here as they happen."
            : $"The last {records.Count} switch{(records.Count == 1 ? string.Empty : "es")} since Sherpa Manager started, newest first.";

        var now = DateTime.UtcNow;
        SwitchList.ItemsSource = records
            .Select((record, index) => new SwitchRow(record, index == 0, now))
            .ToList();
        if (SwitchList.Items.Count > 0) SwitchList.SelectedIndex = 0;
    }

    private void SwitchList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SwitchList.SelectedItem is not SwitchRow row)
        {
            StepList.ItemsSource = null;
            DetailHeader.Text = string.Empty;
            return;
        }

        // Repeat the number in the details, so a switch can be named in a report.
        DetailHeader.Text =
            $"{row.Number}  {row.Record.ProfileName}  ·  {row.Record.StartedLocal:HH:mm:ss}  ·  {row.Record.DescribeOutcome()}";

        var start = row.Record.StartedUtc;
        StepList.ItemsSource = row.Record.Steps
            .Select(step => new StepRow(step, start))
            .ToList();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (SwitchList.SelectedItem is not SwitchRow row) return;

        var record = row.Record;
        var text = new StringBuilder();
        text.AppendLine($"Sherpa Manager {AppVersion.Display} — profile switch #{record.Sequence}");
        text.AppendLine($"Profile: {record.ProfileName}");
        text.AppendLine($"Started: {record.StartedLocal:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Outcome: {record.DescribeOutcome()} in {record.DurationMs / 1000.0:0.#}s");
        text.AppendLine();
        foreach (var step in record.Steps)
        {
            var elapsed = (step.TimestampUtc - record.StartedUtc).TotalSeconds;
            text.AppendLine($"{elapsed,7:0.00}s  {(step.IsWarning ? "! " : "  ")}{step.Message}");
        }

        try
        {
            System.Windows.Clipboard.SetText(text.ToString());
            SummaryText.Text = "Copied the selected switch to the clipboard.";
        }
        catch (Exception exception)
        {
            SummaryText.Text = $"Could not copy: {exception.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static Media.Brush Brush(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Media.Brush
        ?? new Media.SolidColorBrush(Media.Colors.Gray);

    public sealed class SwitchRow
    {
        public SwitchRow(ActivationRecord record, bool isNewest, DateTime now)
        {
            Record = record;
            Number = $"#{record.Sequence}";
            Profile = record.ProfileName;
            When = $"{record.StartedLocal:HH:mm:ss}  ·  {DescribeAge(now - record.StartedUtc)}";
            Detail = $"{record.DescribeOutcome()} · {record.DurationMs / 1000.0:0.#}s";
            NewestVisibility = isNewest ? Visibility.Visible : Visibility.Collapsed;

            // Failed, finished with warnings, or clean: distinguishable before
            // reading any of the text.
            Accent = !record.Succeeded
                ? Brush("ProblemBrush")
                : record.Warnings.Count > 0
                    ? Brush("CautionBrush")
                    : Brush("AccentBrush");
        }

        public ActivationRecord Record { get; }
        public string Number { get; }
        public string Profile { get; }
        public string When { get; }
        public string Detail { get; }
        public Visibility NewestVisibility { get; }
        public Media.Brush Accent { get; }

        /// <summary>
        /// How long ago, in the coarsest useful unit. In a session running all day
        /// the clock time alone does not say whether something was a moment ago.
        /// </summary>
        internal static string DescribeAge(TimeSpan age)
        {
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            if (age.TotalSeconds < 45) return "just now";
            if (age.TotalMinutes < 60) return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours} h ago";
            return $"{(int)age.TotalDays} d ago";
        }
    }

    public sealed class StepRow
    {
        public StepRow(ActivationStep step, DateTime start)
        {
            Message = step.Message;
            Elapsed = $"{(step.TimestampUtc - start).TotalSeconds:0.00}s";
            Accent = step.IsWarning ? Brush("CautionBrush") : Brush("TextBrush");
        }

        public string Message { get; }
        public string Elapsed { get; }
        public Media.Brush Accent { get; }
    }
}
