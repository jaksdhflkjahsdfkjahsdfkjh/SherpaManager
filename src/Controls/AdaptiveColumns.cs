using System.Windows;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace SherpaManager.Controls;

/// <summary>Two equal columns when both fit comfortably; a vertical stack on compact windows.</summary>
public sealed class AdaptiveColumns : Panel
{
    private const double MinimumColumnWidth = 360;
    private const double Gap = 16;

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ColumnCount(availableSize.Width);
        var width = columns == 2 ? (availableSize.Width - Gap) / 2 : availableSize.Width;
        double height = 0, rowHeight = 0, desiredWidth = 0;
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            child.Measure(new Size(width, double.PositiveInfinity));
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (index % columns == columns - 1 || index == InternalChildren.Count - 1)
            {
                height += rowHeight;
                rowHeight = 0;
            }
        }
        return new Size(double.IsInfinity(availableSize.Width) ? desiredWidth : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnCount(finalSize.Width);
        var width = columns == 2 ? (finalSize.Width - Gap) / 2 : finalSize.Width;
        double y = 0;
        for (var index = 0; index < InternalChildren.Count; index += columns)
        {
            var height = InternalChildren[index].DesiredSize.Height;
            if (columns == 2 && index + 1 < InternalChildren.Count)
                height = Math.Max(height, InternalChildren[index + 1].DesiredSize.Height);
            for (var column = 0; column < columns && index + column < InternalChildren.Count; column++)
                InternalChildren[index + column].Arrange(new Rect(column * (width + Gap), y, width, height));
            y += height;
        }
        return finalSize;
    }

    private static int ColumnCount(double width) => !double.IsInfinity(width) && width >= 2 * MinimumColumnWidth + Gap ? 2 : 1;
}
