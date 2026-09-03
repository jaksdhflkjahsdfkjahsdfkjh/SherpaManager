using System.Windows;
using System.Windows.Threading;

namespace SherpaManager;

public partial class DisplayConfirmationWindow : Window
{
    public const int RollbackSeconds = 10;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _rollbackDeadlineUtc;

    public bool KeepLayout { get; private set; }

    public DisplayConfirmationWindow(string summary)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) =>
        {
            _rollbackDeadlineUtc = DateTime.UtcNow + TimeSpan.FromSeconds(RollbackSeconds);
            UpdateCountdown();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow >= _rollbackDeadlineUtc)
        {
            Close();
            return;
        }
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var secondsRemaining = Math.Max(0,
            (int)Math.Ceiling((_rollbackDeadlineUtc - DateTime.UtcNow).TotalSeconds));
        CountdownText.Text = $"Reverting automatically in {secondsRemaining} second{(secondsRemaining == 1 ? string.Empty : "s")}.";
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        KeepLayout = true;
        Close();
    }

    private void Revert_Click(object sender, RoutedEventArgs e) => Close();
}
