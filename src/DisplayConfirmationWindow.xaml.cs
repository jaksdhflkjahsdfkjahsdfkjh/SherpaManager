using System.Windows;
using System.Windows.Threading;

namespace SherpaManager;

public partial class DisplayConfirmationWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsRemaining = 15;

    public bool KeepLayout { get; private set; }

    public DisplayConfirmationWindow(string summary)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        UpdateCountdown();
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _secondsRemaining--;
        UpdateCountdown();
        if (_secondsRemaining <= 0) Close();
    }

    private void UpdateCountdown() =>
        CountdownText.Text = $"Reverting automatically in {_secondsRemaining} second{(_secondsRemaining == 1 ? string.Empty : "s")}.";

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        KeepLayout = true;
        Close();
    }

    private void Revert_Click(object sender, RoutedEventArgs e) => Close();
}
