using System.Windows;
using System.Windows.Interop;
using SherpaManager.Services;

namespace SherpaManager;

public partial class CaptureConfirmationWindow : Window
{
    public CaptureConfirmationWindow(string profileName, string previous, string captured)
    {
        InitializeComponent();
        ProfileText.Text = $"You are updating the display layout for {profileName}.";
        PreviousText.Text = previous;
        CapturedText.Text = captured;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void Replace_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
