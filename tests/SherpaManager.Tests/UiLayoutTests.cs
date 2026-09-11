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
    private static Task TestCaptureReplacementAsync()
    {
        var saved = new DisplaySnapshot { IsVerified = true, Summary = "Work monitor" };
        var captured = new DisplaySnapshot { Summary = "Triple screens", NvidiaSurround = new NvidiaSurroundSnapshot { StatusKnown = true, Enabled = true, HasConfiguredTopology = true } };
        var profile = new SwitchProfile { Display = saved, NvidiaSurroundMode = NvidiaSurroundMode.RequireDisabled };
        var confirmations = 0;
        Assert(!SherpaManager.Services.DisplayCaptureUpdate.Apply(profile, captured, () => { confirmations++; return false; }), "Cancellation must stop replacement.");
        Assert(ReferenceEquals(profile.Display, saved) && saved.IsVerified && profile.NvidiaSurroundMode == NvidiaSurroundMode.RequireDisabled,
            "Cancelling must preserve the saved snapshot, verification, and Surround preference.");
        Assert(SherpaManager.Services.DisplayCaptureUpdate.Apply(profile, captured, () => { confirmations++; return true; }), "Confirmed capture should be stored.");
        Assert(ReferenceEquals(profile.Display, captured) && profile.NvidiaSurroundMode == NvidiaSurroundMode.RequireEnabled, "Accepted capture should update layout and Surround together.");
        Assert(SherpaManager.Services.DisplayCaptureUpdate.Apply(new SwitchProfile(), captured, () => throw new Exception("First capture should not ask to replace anything.")), "First capture should succeed.");
        Assert(confirmations == 2, "Each replacement must ask once.");
        return Task.CompletedTask;
    }

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
        ((Button)window.FindName("DragLockButton")).DataContext = new AppSettings();
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

    /// <summary>
    /// The window opens tall enough to show five applications without paging.
    /// </summary>
    /// <remarks>
    /// Reads the size the window markup declares rather than restating it, so
    /// changing the default height is what this checks, not a copy of it.
    /// </remarks>
    private static Task TestDefaultHeightShowsFiveAppsAsync()
    {
        OnUiThread(() =>
        {
            var window = CreateEditorPreview();
            PreparePreview(window);
            try
            {
                var declared = (window.Width, window.Height);
                window.Show();
                var profile = (SwitchProfile)((ListBox)window.FindName("ProfilesList")).SelectedItem;
                // A captured layout, whose summary fills both lines of its box. The first
                // version of this test used a profile with none, got a one-line placeholder,
                // and passed while a real iRacing profile showed four rows.
                profile.Display = new DisplaySnapshot
                {
                    Summary = "Windows: one logical NVIDIA Surround display: DISPLAY1 5815×1080 (primary)  •  " +
                              "NVIDIA Surround: 1×3, 1920×1080 per panel at 144 Hz, enabled; complete panel order " +
                              "and bezel data captured."
                };
                for (var i = 1; i <= 12; i++)
                    profile.Applications.Add(new LaunchApplication { Name = $"App {i}", OrderLabel = i.ToString() });
                window.UpdateLayout();

                var table = (DataGrid)window.FindName("ApplicationsGrid");
                // The table scrolls by row, so the viewport is measured in rows.
                var rows = VisualChildren<ScrollViewer>(table).First().ViewportHeight;
                Assert(rows >= 5,
                    $"At its default {declared.Width} x {declared.Height} the window shows {rows} application rows, not five.");
                AssertInside(table, window);

                // Switching to a profile with no layout must not move the table: the
                // summary box keeps its height whatever it says.
                var before = table.ActualHeight;
                profile.Display = null;
                window.UpdateLayout();
                Assert(Math.Abs(table.ActualHeight - before) < 1,
                    $"The table changed height from {before:0} to {table.ActualHeight:0} when the layout summary did.");
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// The iRacing logo sits on the sidebar with no card behind it, and the other
    /// profile icons are drawn at the same visual size.
    /// </summary>
    /// <remarks>
    /// The whole logo is very nearly square (973 by 924), so the glyphs share its
    /// 36 px box and match it on width, which is what reads as size in a column of
    /// icons.
    /// </remarks>
    private static Task TestProfileIconsMatchAsync()
    {
        OnUiThread(() =>
        {
            var window = CreateEditorPreview();
            PreparePreview(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                var list = (ListBox)window.FindName("ProfilesList");

                T Part<T>(string profileName, string part) where T : FrameworkElement
                {
                    var item = list.Items.Cast<SwitchProfile>().First(p => p.Name == profileName);
                    var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
                    var presenter = VisualChildren<ContentPresenter>(container).First(p => p.ContentTemplate is not null);
                    return (T)presenter.ContentTemplate.FindName(part, presenter);
                }

                var badge = Part<Border>("iRacing", "RacingBadge");
                Assert(badge.Visibility == Visibility.Visible, "The iRacing profile should show its logo.");
                // A white card behind a transparent logo was the original complaint.
                Assert(badge.Background is null ||
                       badge.Background is SolidColorBrush { Color.A: 0 },
                    $"The iRacing logo sits on a {badge.Background} card instead of the sidebar.");

                var logo = VisualChildren<Image>(badge).Single();
                // Cropping the logo to its flag read as cut off, so it is shown whole.
                Assert(logo.Source is not CroppedBitmap, "The iRacing logo should be shown whole, not cropped.");

                var work = Part<System.Windows.Shapes.Path>("Work", "ProfileGlyph");
                Assert(work.IsVisible, "The Work profile should show its glyph.");
                // Width is what reads as size in a list of icons down one side.
                Assert(Math.Abs(work.ActualWidth - logo.ActualWidth) < 1,
                    $"The Work icon is {work.ActualWidth:0} wide against the logo's {logo.ActualWidth:0}.");
                // The iRacing logo is very nearly square, so the heights nearly agree too.
                Assert(Math.Abs(work.ActualHeight - logo.ActualHeight) <= 3,
                    $"The Work icon is {work.ActualHeight:0} tall against the logo's {logo.ActualHeight:0}.");

                // ACC shows its own logo, whole, at the same width as the others.
                var accBadge = Part<Border>("ACC", "AccBadge");
                Assert(accBadge.Visibility == Visibility.Visible, "The ACC profile should show its logo.");
                Assert(!Part<System.Windows.Shapes.Path>("ACC", "ProfileGlyph").IsVisible,
                    "The ACC profile should show its logo instead of the generic glyph.");
                Assert(accBadge.Background is null || accBadge.Background is SolidColorBrush { Color.A: 0 },
                    $"The ACC logo sits on a {accBadge.Background} card instead of the sidebar.");
                var acc = VisualChildren<Image>(accBadge).Single();
                // A logo left out of the project file still parses, and renders nothing.
                Assert(acc.Source is BitmapSource { PixelWidth: > 0 },
                    "The ACC logo did not load; it is probably not included as a resource.");
                Assert(acc.Source is not CroppedBitmap, "The ACC logo should be shown whole, not cropped.");
                Assert(Math.Abs(acc.ActualWidth - logo.ActualWidth) < 1,
                    $"The ACC logo is {acc.ActualWidth:0} wide against the iRacing logo's {logo.ActualWidth:0}.");

                // A profile called anything else keeps the generic glyph.
                Assert(Part<System.Windows.Shapes.Path>("Work", "ProfileGlyph").Data !=
                       (Geometry)System.Windows.Application.Current.FindResource("IconLayout"),
                    "Work should have its own suitcase rather than the generic glyph.");
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }

    /// <summary>Which profile names get a logo, however they are typed.</summary>
    private static Task TestProfileKindNamesAsync()
    {
        var converter = new SherpaManager.Controls.ProfileKindConverter();
        string Kind(string name) =>
            (string)converter.Convert(name, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);

        foreach (var name in new[] { "ACC", "acc", " Acc ", "Assetto Corsa Competizione" })
            Assert(Kind(name) == "ACC", $"'{name}' should be recognised as ACC; got {Kind(name)}.");
        foreach (var name in new[] { "iRacing", "IRACING" })
            Assert(Kind(name) == "iRacing", $"'{name}' should be recognised as iRacing; got {Kind(name)}.");
        Assert(Kind("Work") == "Work", "Work should be recognised.");
        // Near misses keep the generic glyph rather than someone else's logo.
        foreach (var name in new[] { "ACC League", "Assetto Corsa", "Accounting", "" })
            Assert(Kind(name) == "Profile", $"'{name}' should not get a logo; got {Kind(name)}.");
        return Task.CompletedTask;
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
                foreach (var size in new[] { (1240d, 900d), (1200d, 880d) })
                {
                    window.Width = size.Item1;
                    window.Height = size.Item2;
                    var tabs = (TabControl)window.FindName("MainTabs");
                    tabs.SelectedIndex = 0;
                    window.UpdateLayout();
                    var page = (FrameworkElement)window.FindName("ProfilePage");
                    AssertInside((FrameworkElement)window.FindName("ActivateButton"), window);
                    AssertInside((FrameworkElement)window.FindName("StatusText"), window);
                    AssertHorizontalFit(page);
                    AssertWrapPanelsDoNotOverlap(page);
                    var display = BoundsIn((FrameworkElement)window.FindName("DisplayCard"), window);
                    var audio = BoundsIn((FrameworkElement)window.FindName("AudioCard"), window);
                    var quick = BoundsIn((FrameworkElement)window.FindName("QuickCard"), window);
                    Assert(Math.Abs(display.Top - audio.Top) < 1 && Math.Abs(display.Bottom - quick.Bottom) < 1,
                        "The display card must align with the audio and quick-switching cards.");
                    SavePreview(window, $"editor-{size.Item1}");
                    AssertInside((FrameworkElement)window.FindName("ApplicationsGrid"), window);
                    var tableBounds = BoundsIn((FrameworkElement)window.FindName("ApplicationsGrid"), page);
                    Assert(tableBounds.Bottom <= page.ActualHeight + 1, "The table must remain above the status footer.");
                    SavePreview(window, $"applications-empty-{size.Item1}");
                    var profile = (SwitchProfile)((ListBox)window.FindName("ProfilesList")).SelectedItem;
                    profile.Applications.Add(new LaunchApplication { Name = "Crew Chief", Path = @"C:\Apps\CrewChief\CrewChiefV4.exe", OrderLabel = "1" });
                    profile.Applications.Add(new LaunchApplication { Name = "Trading Paints", Path = @"C:\Apps\TradingPaints\TradingPaints.exe", OrderLabel = "2" });
                    window.UpdateLayout();
                    SavePreview(window, $"applications-{size.Item1}");
                    var reorder = (AppSettings)((Button)window.FindName("DragLockButton")).DataContext;
                    reorder.AllowApplicationDragReorder = true;
                    window.UpdateLayout();
                    SavePreview(window, $"unlocked-{size.Item1}");
                    reorder.AllowApplicationDragReorder = false;
                    for (var i = 3; i <= 30; i++) profile.Applications.Add(new LaunchApplication { Name = $"App {i}", OrderLabel = i.ToString() });
                    window.UpdateLayout();
                    var table = (DataGrid)window.FindName("ApplicationsGrid");
                    Assert(VisualChildren<ScrollViewer>(table).Any(v => v.ScrollableHeight > 0), "Only the table should scroll when many apps are present.");
                    AssertInside(table, window);
                    profile.Description = new string('W', 120);
                    ((TextBlock)window.FindName("ApplicationIssuesText")).Text = "30 entries need attention";
                    window.UpdateLayout();
                    Assert(BoundsIn(table, page).Bottom <= page.ActualHeight + 1, "Long descriptions and warnings must not push the table behind the footer.");
                    profile.Description = "Triple screens, your headset, and everything you need on the grid.";
                    ((TextBlock)window.FindName("ApplicationIssuesText")).Text = string.Empty;
                    profile.Applications.Clear();
                    tabs.SelectedIndex = 1;
                    window.UpdateLayout();
                    var settings = (FrameworkElement)window.FindName("SettingsPage");
                    AssertHorizontalFit(settings);
                    foreach (var control in VisualChildren<Control>(settings).Where(c => c is Button or TextBox or ComboBox or CheckBox))
                        AssertInside(control, window);
                    AssertInside((FrameworkElement)window.FindName("StatusText"), window);
                    SavePreview(window, $"settings-{size.Item1}");
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

            var capture = new CaptureConfirmationWindow("iRacing", "Single work monitor, 2560 × 1440 at 144 Hz", "Three racing monitors, 7680 × 1440 at 144 Hz");
            PreparePreview(capture);
            try
            {
                capture.Show();
                capture.UpdateLayout();
                Assert(((Button)capture.FindName("CancelButton")).IsDefault, "Enter must default to cancelling an overwrite.");
                foreach (var button in VisualChildren<Button>(capture)) AssertInside(button, capture);
                SavePreview(capture, "capture-confirmation");
            }
            finally { capture.Close(); }
        });
        return Task.CompletedTask;
    }

    private static Task TestDialogPagingAsync()
    {
        OnUiThread(() =>
        {
            var list = new ListBox { ItemsSource = Enumerable.Range(1, 200).Select(i => $"Application {i}").ToList() };
            var pages = new SherpaManager.Controls.PagedContent { PageContent = list };
            var window = new Window { Content = pages, Width = 400, Height = 300 };
            PreparePreview(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                var viewport = VisualChildren<ScrollViewer>(list).First();
                var next = VisualChildren<Button>(pages).Single(b => Equals(b.Content, "Next"));
                var previous = VisualChildren<Button>(pages).Single(b => Equals(b.Content, "Previous"));
                Assert(viewport.ComputedVerticalScrollBarVisibility != Visibility.Visible, "Paged lists must not show a scrollbar.");
                Assert(next.IsEnabled && !previous.IsEnabled, "A long list should start with only Next enabled.");
                next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.UpdateLayout();
                Assert(viewport.VerticalOffset > 0, "Next must advance the list.");
                Assert(previous.IsEnabled, "Previous must become available after advancing.");
                previous.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.UpdateLayout();
                Assert(viewport.VerticalOffset == 0, "Previous must return to the beginning.");
                Assert(list.ItemContainerGenerator.ContainerFromIndex(199) is null, "Paging must preserve list virtualization.");
            }
            finally { window.Close(); }
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

    private static void AssertHorizontalFit(FrameworkElement scroll)
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
