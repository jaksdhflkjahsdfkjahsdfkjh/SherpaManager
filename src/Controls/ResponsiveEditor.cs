using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using RadioButton = System.Windows.Controls.RadioButton;
using Orientation = System.Windows.Controls.Orientation;
using Size = System.Windows.Size;

namespace SherpaManager.Controls;

/// <summary>Retains the full overview on large desktops and readable sections on smaller ones.</summary>
public sealed class ResponsiveEditor : Grid
{
    private readonly StackPanel _navigation = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private readonly PagedContent _pages = new();
    private Grid? _overview;
    private FrameworkElement? _applications;
    private bool? _compact;
    private int _section;

    public bool IsCompact => _compact == true;

    public ResponsiveEditor()
    {
        foreach (var label in new[] { "Display", "Audio & shortcuts", "Applications" })
        {
            var index = _navigation.Children.Count;
            var tab = new RadioButton { Content = label, GroupName = "ProfileSections", Margin = new Thickness(0, 0, 8, 0) };
            tab.SetResourceReference(StyleProperty, "SectionTab");
            AutomationProperties.SetName(tab, label);
            tab.Checked += (_, _) =>
            {
                _section = index;
                if (IsCompact) ArrangeSections();
                _pages.ScrollToStart();
            };
            _navigation.Children.Add(tab);
        }
        ((RadioButton)_navigation.Children[0]).IsChecked = true;
        Children.Add(_navigation);
        SetRow(_navigation, 1);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (_overview is null)
        {
            _overview = Children.OfType<Grid>().FirstOrDefault(child => child.Name == "OverviewGrid");
            _applications = Children.OfType<FrameworkElement>().FirstOrDefault(child => child.Name == "ApplicationsCard");
        }
        var compact = constraint.Width < 900 || constraint.Height < 720;
        if (_overview is not null && _applications is not null && _compact != compact)
        {
            _compact = compact;
            if (compact)
            {
                Children.Remove(_overview);
                _pages.PageContent = _overview;
                Children.Add(_pages);
                SetRow(_pages, 2);
            }
            else if (_pages.PageContent is not null)
            {
                Children.Remove(_pages);
                _pages.PageContent = null;
                Children.Add(_overview);
                SetRow(_overview, 2);
            }
            _navigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            ArrangeSections();
        }
        return base.MeasureOverride(constraint);
    }

    private void ArrangeSections()
    {
        if (_overview is null || _applications is null) return;
        var compact = IsCompact;
        RowDefinitions[2].Height = compact ? new GridLength(_section == 2 ? 0 : 1, GridUnitType.Star) : GridLength.Auto;
        RowDefinitions[3].Height = new GridLength(!compact || _section == 2 ? 1 : 0, GridUnitType.Star);
        _applications.Visibility = !compact || _section == 2 ? Visibility.Visible : Visibility.Collapsed;
        _pages.Visibility = compact && _section != 2 ? Visibility.Visible : Visibility.Collapsed;
        _overview.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 16);
        _overview.ColumnDefinitions[2].Width = new GridLength(compact ? 0 : 1, GridUnitType.Star);
        var cards = _overview.Children.OfType<FrameworkElement>().ToArray();
        for (var index = 0; index < cards.Length; index++)
        {
            cards[index].Visibility = !compact || (_section == 0 ? index == 0 : index != 0) ? Visibility.Visible : Visibility.Collapsed;
            SetColumn(cards[index], compact || index == 0 ? 0 : 2);
        }
        InvalidateMeasure();
    }
}
