using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using ListBox = System.Windows.Controls.ListBox;

namespace SherpaManager.Controls;

/// <summary>Bounded dialog content with page actions when it cannot fit, without a scrollbar.</summary>
[System.Windows.Markup.ContentProperty(nameof(PageContent))]
public sealed class PagedContent : UserControl
{
    public static readonly DependencyProperty PageContentProperty = DependencyProperty.Register(
        nameof(PageContent), typeof(object), typeof(PagedContent), new PropertyMetadata(null, ContentChanged));

    private readonly ScrollViewer _viewport = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        CanContentScroll = false
    };
    private readonly StackPanel _pager = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button _previous = new() { Content = "Previous", Margin = new Thickness(0, 8, 8, 0) };
    private readonly Button _next = new() { Content = "Next", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _host = new();
    private ScrollViewer? _activeViewport;
    private ListBox? _list;

    public object? PageContent { get => GetValue(PageContentProperty); set => SetValue(PageContentProperty, value); }

    public PagedContent()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _host.Child = _viewport;
        grid.Children.Add(_host);
        Grid.SetRow(_pager, 1);
        _pager.Children.Add(_previous);
        _pager.Children.Add(_next);
        grid.Children.Add(_pager);
        Content = grid;
        _previous.Click += (_, _) => _activeViewport?.PageUp();
        _next.Click += (_, _) => _activeViewport?.PageDown();
        Attach(_viewport);
    }

    private void Attach(ScrollViewer viewport)
    {
        if (_activeViewport is not null) _activeViewport.ScrollChanged -= ViewportChanged;
        _activeViewport = viewport;
        _activeViewport.ScrollChanged += ViewportChanged;
        UpdateButtons();
    }

    private void ViewportChanged(object sender, ScrollChangedEventArgs args) => UpdateButtons();

    private void UpdateButtons()
    {
        if (_activeViewport is not { } viewport) return;
        // Reserve the footer so showing it cannot itself cause overflow.
        _pager.Visibility = viewport.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Hidden;
        _previous.IsEnabled = viewport.VerticalOffset > 0;
        _next.IsEnabled = viewport.VerticalOffset < viewport.ScrollableHeight;
    }

    private void ListLoaded(object sender, RoutedEventArgs args)
    {
        if (_list is not null && FindViewport(_list) is { } viewport) Attach(viewport);
    }

    private static ScrollViewer? FindViewport(DependencyObject root)
    {
        if (root is ScrollViewer viewport) return viewport;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindViewport(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }

    private static void ContentChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var control = (PagedContent)source;
        if (control._list is not null) control._list.Loaded -= control.ListLoaded;
        control._viewport.Content = null;
        control._host.Child = null;
        control._list = args.NewValue as ListBox;
        if (control._list is { } list)
        {
            // Page the list's own viewport, retaining recycling and keyboard selection.
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Hidden);
            control._host.Child = list;
            list.Loaded += control.ListLoaded;
        }
        else
        {
            control._host.Child = control._viewport;
            control._viewport.Content = args.NewValue;
            control.Attach(control._viewport);
        }
    }
}
