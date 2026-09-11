using System.Windows;
using System.Windows.Media;

namespace SherpaManager.Controls;

/// <summary>Optional vector icon for the shared button template. Content stays text for accessibility.</summary>
public static class Icon
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "Data", typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static Geometry? GetData(DependencyObject element) => (Geometry?)element.GetValue(DataProperty);
    public static void SetData(DependencyObject element, Geometry? value) => element.SetValue(DataProperty, value);
}
