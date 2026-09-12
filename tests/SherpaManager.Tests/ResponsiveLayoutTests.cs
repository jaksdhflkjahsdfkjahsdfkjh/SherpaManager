using System.Windows;
using System.Windows.Controls;
using SherpaManager.Controls;
using SherpaManager.Models;
using SherpaManager.Services;

namespace SherpaManager.Tests;

internal static partial class Program
{
    private static Task TestResponsiveDesktopAsync()
    {
        OnUiThread(() =>
        {
            var window = CreateEditorPreview();
            PreparePreview(window);
            try
            {
                ShowPreview(window);
                var profile = (SwitchProfile)((ListBox)window.FindName("ProfilesList")).SelectedItem;
                for (var i = 1; i <= 12; i++) profile.Applications.Add(new LaunchApplication { Name = $"App {i}", OrderLabel = i.ToString() });
                var editor = (ResponsiveEditor)window.FindName("ProfilePage");
                foreach (var size in new[] { (1536d, 820d), (1280d, 680d), (960d, 510d), (900d, 480d) })
                {
                    window.Width = size.Item1;
                    window.Height = size.Item2;
                    ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0;
                    window.UpdateLayout();
                    Assert(editor.IsCompact, $"Expected compact sections at {size}.");
                    var sections = VisualChildren<RadioButton>(editor).ToArray();
                    Assert(sections.Length == 3, "All compact profile sections must be available.");
                    foreach (var section in sections)
                    {
                        section.IsChecked = true;
                        window.UpdateLayout();
                        foreach (var tab in sections) AssertInside(tab, window);
                        AssertInside((FrameworkElement)window.FindName("ActivateButton"), window);
                        AssertInside((FrameworkElement)window.FindName("StatusText"), window);
                        if (section.Content.ToString() == "Applications")
                        {
                            var table = (DataGrid)window.FindName("ApplicationsGrid");
                            Assert(table.ActualHeight >= 60, $"Application table is unusable at {size}: {table.ActualHeight}.");
                            AssertInside(table, window);
                            foreach (var button in VisualChildren<Button>((FrameworkElement)window.FindName("ApplicationsCard"))) AssertInside(button, window);
                        }
                        SavePreview(window, $"compact-{size.Item1}-{section.Content.ToString()!.Replace(' ', '-')}");
                        if (section.Content.ToString() != "Applications")
                        {
                            var profilePager = VisualChildren<PagedContent>(editor).Single();
                            var pending = VisualChildren<Button>(profilePager).Where(button => button.IsVisible).Cast<FrameworkElement>()
                                .Concat(VisualChildren<ComboBox>(profilePager).Where(combo => combo.IsVisible)).ToHashSet();
                            var nextPage = VisualChildren<Button>(profilePager).Single(button => Equals(button.Content, "Next"));
                            for (var page = 0; page < 12; page++)
                            {
                                pending.RemoveWhere(control => FullyInside(control, window));
                                if (!nextPage.IsEnabled) break;
                                nextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                window.UpdateLayout();
                            }
                            Assert(pending.Count == 0, $"Profile controls cannot be reached in {section.Content} at {size}.");
                        }
                    }
                    ((TabControl)window.FindName("MainTabs")).SelectedIndex = 1;
                    window.UpdateLayout();
                    var settings = (FrameworkElement)window.FindName("SettingsPage");
                    var pager = VisualChildren<PagedContent>(settings).Single();
                    VisualChildren<ScrollViewer>(pager).First().ScrollToTop();
                    window.UpdateLayout();
                    var next = VisualChildren<Button>(pager).Single(button => Equals(button.Content, "Next"));
                    var unseen = VisualChildren<CheckBox>(settings).Cast<FrameworkElement>()
                        .Concat(VisualChildren<ComboBox>(settings)).Concat(VisualChildren<TextBox>(settings)).ToHashSet();
                    for (var page = 0; page < 12; page++)
                    {
                        unseen.RemoveWhere(control => FullyInside(control, window));
                        if (!next.IsEnabled) break;
                        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        window.UpdateLayout();
                    }
                    Assert(unseen.Count == 0, $"Settings controls cannot be reached at {size}: {string.Join(", ", unseen.Select(control => control.Name))}");
                    SavePreview(window, $"compact-settings-{size.Item1}");
                }
                window.Width = 1240;
                window.Height = 956;
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0;
                window.UpdateLayout();
                Assert(!editor.IsCompact, "Restoring the large window should restore the full overview.");
                Assert(((FrameworkElement)window.FindName("DisplayCard")).IsVisible && ((FrameworkElement)window.FindName("ApplicationsGrid")).IsVisible,
                    "Sections disappeared when restoring the full overview.");
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }

    private static bool FullyInside(FrameworkElement element, Window window)
    {
        if (!element.IsVisible) return false;
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        // Include clipping by the paging viewport, not just the outer window.
        for (DependencyObject? parent = element; parent is not null && parent != window; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollContentPresenter viewport)
            {
                var clip = viewport.TransformToAncestor(window).TransformBounds(new Rect(viewport.RenderSize));
                if (!clip.Contains(bounds)) return false;
            }
        }
        return new Rect(0, 0, window.ActualWidth, window.ActualHeight).Contains(bounds);
    }

    private static Task TestWindowWorkAreaAsync()
    {
        foreach (var work in new[] { new Rect(0, 0, 1920, 1040), new Rect(-1920, 40, 1920, 1040), new Rect(1920, 0, 2560, 1400) })
        foreach (var scale in new[] { 1, 1.25, 1.5, 2 })
        {
            var result = WindowPlacement.FitBounds(new Rect(-30000, -30000, 1240 * scale, 956 * scale), work);
            Assert(work.Contains(result), "A scaled or disconnected-monitor window extends beyond the work area.");
        }
        OnUiThread(() =>
        {
            var window = new Window { Width = 1240, Height = 956, MinHeight = 880, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            window.SourceInitialized += (_, _) => WindowTheme.ApplyDarkTitleBar(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            try
            {
                window.Show();
                Assert(!double.IsInfinity(window.MaxHeight), "The native window did not attach monitor work-area constraints.");
                Assert(window.MinHeight <= window.MaxHeight, "Monitor fitting left conflicting height constraints.");
            }
            finally { window.Close(); }
        });
        return Task.CompletedTask;
    }

    private static Task TestCompactSafetyDialogsAsync()
    {
        OnUiThread(() =>
        {
            var summary = string.Join(" ", Enumerable.Repeat("Monitor layout with a long hardware description.", 100));
            Window[] windows =
            [
                new DisplayConfirmationWindow(summary),
                new CaptureConfirmationWindow(new string('W', 40), summary, summary),
                new DisplayLayoutWindow("Racing", new DisplaySnapshot { Summary = summary }),
                new ActivationPreflightWindow(new ActivationPreflight { ProfileName = new string('W', 40) }),
                new ActivationHistoryWindow([]), new ApplicationPickerWindow([])
            ];
            foreach (var window in windows)
            {
                PreparePreview(window);
                window.MaxHeight = 480;
                window.Height = 480;
                window.Width = Math.Min(window.Width, 760);
                try
                {
                    ShowPreview(window);
                    window.UpdateLayout();
                    foreach (var button in VisualChildren<Button>(window).Where(button => button.IsVisible)) AssertInside(button, window);
                    if (window is DisplayConfirmationWindow)
                        AssertInside((FrameworkElement)window.FindName("CountdownText"), window);
                    SavePreview(window, "compact-" + window.GetType().Name);
                }
                finally { window.Close(); }
            }
        });
        return Task.CompletedTask;
    }
}
