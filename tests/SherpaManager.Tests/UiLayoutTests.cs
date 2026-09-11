using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SherpaManager.Models;

namespace SherpaManager.Tests;

internal static partial class Program
{
    // Render the actual editor markup without constructing the hardware services,
    // registering hotkeys, loading personal profiles, or activating a startup profile.
    private static Window CreateEditorPreview()
    {
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiMarkup", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var events = new HashSet<string>
        {
            "Closing", "StateChanged", "PreviewKeyDown", "Click", "SelectionChanged", "LostFocus",
            "Checked", "Unchecked", "PreviewMouseLeftButtonDown", "PreviewMouseMove", "DragOver", "Drop", "RowEditEnding"
        };
        foreach (var element in source.Descendants().ToList())
        {
            if (element.Name.LocalName == "EventSetter") { element.Remove(); continue; }
            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.Name == xaml + "Class" || events.Contains(attribute.Name.LocalName)) attribute.Remove();
                else if (attribute.Value.StartsWith("Assets/", StringComparison.Ordinal))
                    attribute.Value = "pack://application:,,,/SherpaManager;component/" + attribute.Value;
            }
        }
        var window = (Window)XamlReader.Parse(source.ToString().Replace(
            "clr-namespace:SherpaManager.Controls", "clr-namespace:SherpaManager.Controls;assembly=SherpaManager", StringComparison.Ordinal));
        var profile = new SwitchProfile
        {
            Name = "iRacing",
            Description = "Triple screens, your headset, and everything you need on the grid."
        };
        var list = (ListBox)window.FindName("ProfilesList");
        list.ItemsSource = new[] { new SwitchProfile { Name = "Work" }, profile, new SwitchProfile { Name = "ACC" } };
        list.SelectedItem = profile;
        ((TextBlock)window.FindName("VersionText")).Text = "v0.7.0";
        ((DataGridColumn)window.FindName("DelayColumn")).Visibility = Visibility.Collapsed;
        ((TextBlock)window.FindName("ActiveProfileText")).Text = "Work";
        ((Button)window.FindName("HotkeyButton")).Content = "Set shortcut";
        foreach (var name in new[] { "AudioOutputCombo", "AudioInputCombo" })
        {
            var combo = (ComboBox)window.FindName(name);
            combo.ItemsSource = new[] { new { Id = "", Label = "Do not change" }, new { Id = "test", Label = "Headphones — USB audio interface with a very long device name" } };
            combo.SelectedIndex = 0;
        }
        ((ComboBox)window.FindName("SurroundModeCombo")).ItemsSource = new[] { new { Value = NvidiaSurroundMode.Ignore, Label = "Do not manage" } };
        ((ComboBox)window.FindName("SurroundModeCombo")).SelectedIndex = 0;
        ((ComboBox)window.FindName("StartupProfileCombo")).ItemsSource = new[] { new { Value = "", Label = "Do not activate a profile" } };
        ((ComboBox)window.FindName("StartupProfileCombo")).SelectedIndex = 0;
        ((ComboBox)window.FindName("LaunchReadinessCombo")).ItemsSource = new[] { new { Value = 0, Label = "It to finish starting" } };
        ((ComboBox)window.FindName("LaunchReadinessCombo")).SelectedIndex = 0;
        ((TextBox)window.FindName("DisplaySettleDelayBox")).Text = "5000";
        ((TextBox)window.FindName("LaunchReadinessTimeoutBox")).Text = "10000";
        ((CheckBox)window.FindName("CloseToTrayCheckBox")).IsChecked = true;
        ((CheckBox)window.FindName("ConfirmDisplayChangesCheckBox")).IsChecked = true;
        ((CheckBox)window.FindName("ShowActivationPreviewCheckBox")).IsChecked = true;
        return window;
    }

    private static Task TestMainLayoutAsync()
    {
        OnUiThread(() =>
        {
            var window = CreateEditorPreview();
            PreparePreview(window);
            try
            {
                window.Show();
                foreach (var size in new[] { (1200d, 880d), (960d, 640d) })
                {
                    window.Width = size.Item1;
                    window.Height = size.Item2;
                    var tabs = (TabControl)window.FindName("MainTabs");
                    tabs.SelectedIndex = 0;
                    window.UpdateLayout();
                    var scroll = (ScrollViewer)window.FindName("ProfileScroll");
                    scroll.ScrollToTop();
                    window.UpdateLayout();
                    Assert(scroll.ScrollableHeight > 0, "The profile page must scroll at this height.");
                    AssertInside((FrameworkElement)window.FindName("ActivateButton"), window);
                    AssertInside((FrameworkElement)window.FindName("StatusText"), window);
                    AssertHorizontalFit(scroll);
                    AssertWrapPanelsDoNotOverlap(scroll);
                    SavePreview(window, $"editor-{size.Item1}");
                    scroll.ScrollToBottom();
                    window.UpdateLayout();
                    AssertInside((FrameworkElement)window.FindName("ApplicationsGrid"), window);
                    SavePreview(window, $"applications-empty-{size.Item1}");
                    var profile = (SwitchProfile)((ListBox)window.FindName("ProfilesList")).SelectedItem;
                    profile.Applications.Add(new LaunchApplication { Name = "Crew Chief", Path = @"C:\Apps\CrewChief\CrewChiefV4.exe", OrderLabel = "1" });
                    profile.Applications.Add(new LaunchApplication { Name = "Trading Paints", Path = @"C:\Apps\TradingPaints\TradingPaints.exe", OrderLabel = "2" });
                    window.UpdateLayout();
                    SavePreview(window, $"applications-{size.Item1}");
                    profile.Applications.Clear();
                    tabs.SelectedIndex = 1;
                    window.UpdateLayout();
                    var settings = (ScrollViewer)window.FindName("SettingsScroll");
                    settings.ScrollToTop();
                    window.UpdateLayout();
                    AssertHorizontalFit(settings);
                    AssertInside((FrameworkElement)window.FindName("StatusText"), window);
                    SavePreview(window, $"settings-{size.Item1}");
                    settings.ScrollToBottom();
                    window.UpdateLayout();
                    AssertInside((FrameworkElement)window.FindName("LaunchReadinessTimeoutBox"), window);
                    SavePreview(window, $"settings-bottom-{size.Item1}");
                }
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }

    private static Task TestDialogLayoutAsync()
    {
        OnUiThread(() =>
        {
            var confirmation = new DisplayConfirmationWindow(
                "Triple-screen racing setup: 7680 × 1440 at 144 Hz, HDR enabled. " +
                "The primary display and audio devices have changed. Check that all three monitors are visible.");
            PreparePreview(confirmation);
            try
            {
                confirmation.Show();
                confirmation.UpdateLayout();
                AssertInside((FrameworkElement)confirmation.FindName("CountdownText"), confirmation);
                foreach (var button in VisualChildren<Button>(confirmation)) AssertInside(button, confirmation);
                SavePreview(confirmation, "display-confirmation");
            }
            finally { confirmation.Close(); }

            var picker = new ApplicationPickerWindow([]) { Width = 520, Height = 420 };
            PreparePreview(picker);
            try
            {
                picker.Show();
                picker.UpdateLayout();
                var buttons = VisualChildren<Button>(picker).ToList();
                foreach (var button in buttons) AssertInside(button, picker);
                for (var i = 0; i < buttons.Count; i++)
                    for (var j = i + 1; j < buttons.Count; j++)
                        Assert(!BoundsIn(buttons[i], picker).IntersectsWith(BoundsIn(buttons[j], picker)), "Application picker buttons overlap.");
                SavePreview(picker, "application-picker-compact");
            }
            finally { picker.Close(); }
        });
        return Task.CompletedTask;
    }

    private static void PreparePreview(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
    }

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static void AssertInside(FrameworkElement element, Window window)
    {
        var bounds = BoundsIn(element, window);
        Assert(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0 &&
               bounds.Right <= window.ActualWidth + 1 && bounds.Bottom <= window.ActualHeight + 1,
            $"{element.Name} is clipped: {bounds} in {window.ActualWidth} × {window.ActualHeight}.");
    }

    private static void AssertHorizontalFit(ScrollViewer scroll)
    {
        foreach (var element in VisualChildren<Control>(scroll).Where(e => e is TextBox or ComboBox or CheckBox or Button))
        {
            var bounds = BoundsIn(element, scroll);
            Assert(bounds.Left >= -1 && bounds.Right <= scroll.ActualWidth + 1,
                $"{element.Name} extends outside the page: {bounds} / {scroll.ActualWidth}.");
        }
    }

    private static void AssertWrapPanelsDoNotOverlap(Visual root)
    {
        foreach (var panel in VisualChildren<WrapPanel>(root))
        {
            var children = panel.Children.OfType<FrameworkElement>().Where(c => c.IsVisible).ToList();
            for (var i = 0; i < children.Count; i++)
                for (var j = i + 1; j < children.Count; j++)
                    Assert(!BoundsIn(children[i], panel).IntersectsWith(BoundsIn(children[j], panel)), "Toolbar actions overlap.");
        }
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void SavePreview(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SHERPA_UI_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var visual = (FrameworkElement)window.Content;
        var margin = visual.Margin;
        var width = (int)Math.Ceiling(visual.ActualWidth + margin.Left + margin.Right);
        var height = (int)Math.Ceiling(visual.ActualHeight + margin.Top + margin.Bottom);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(visual), null, new Rect(margin.Left, margin.Top, visual.ActualWidth, visual.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
