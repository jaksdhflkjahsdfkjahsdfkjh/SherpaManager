using SherpaManager.Models;

namespace SherpaManager.Services;

/// <summary>
/// Turns a captured display snapshot into a scaled arrangement that can be
/// drawn. Pure geometry: it reads nothing from the machine and changes nothing.
/// </summary>
public static class DisplayLayoutBuilder
{
    public static DisplayLayoutView Build(DisplaySnapshot? snapshot, double maxWidth, double maxHeight)
    {
        if (snapshot is null)
            return DisplayLayoutView.Empty("This profile has no captured layout, so activation leaves your monitors as they are.");

        // A monitor with no size cannot be placed or drawn. Windows has been seen
        // to report one during a mode change, so skip rather than fail.
        var targets = snapshot.ActiveTargets
            .Where(target => target is { SourceWidth: > 0, SourceHeight: > 0 })
            .ToList();
        var skipped = snapshot.ActiveTargets.Count - targets.Count;

        if (targets.Count == 0)
            return DisplayLayoutView.Empty(snapshot.ActiveTargets.Count == 0
                ? "This layout contains no monitors."
                : "This layout contains no monitor with a usable size.");

        var minX = targets.Min(target => target.SourceX);
        var minY = targets.Min(target => target.SourceY);
        var maxX = targets.Max(target => target.SourceX + (int)target.SourceWidth);
        var maxY = targets.Max(target => target.SourceY + (int)target.SourceHeight);

        double contentWidth = Math.Max(1, maxX - minX);
        double contentHeight = Math.Max(1, maxY - minY);

        var usableWidth = Math.Max(1, maxWidth);
        var usableHeight = Math.Max(1, maxHeight);
        // Never enlarge: a single small display should not fill the dialog and
        // imply it is bigger than it is.
        var scale = Math.Min(1, Math.Min(usableWidth / contentWidth, usableHeight / contentHeight));

        var tiles = targets
            .OrderBy(target => target.SourceY)
            .ThenBy(target => target.SourceX)
            .Select(target => new DisplayLayoutTile(
                Describe(target),
                DescribeMode(target),
                (target.SourceX - minX) * scale,
                (target.SourceY - minY) * scale,
                target.SourceWidth * scale,
                target.SourceHeight * scale,
                target is { SourceX: 0, SourceY: 0 },
                target.Rotation))
            .ToList();

        return new DisplayLayoutView(tiles, contentWidth * scale, contentHeight * scale,
            BuildSummary(targets.Count, skipped, contentWidth, contentHeight));
    }

    private static string BuildSummary(int count, int skipped, double width, double height)
    {
        var summary = count == 1
            ? $"1 monitor, {width:0}×{height:0} desktop."
            : $"{count} monitors, {width:0}×{height:0} desktop.";
        return skipped > 0
            ? $"{summary} {skipped} entr{(skipped == 1 ? "y" : "ies")} had no usable size and {(skipped == 1 ? "was" : "were")} left out."
            : summary;
    }

    private static string Describe(DisplayTargetSnapshot target)
    {
        if (!string.IsNullOrWhiteSpace(target.FriendlyName)) return target.FriendlyName;
        if (!string.IsNullOrWhiteSpace(target.Identity)) return target.Identity;
        return $"Display {target.PathIndex + 1}";
    }

    private static string DescribeMode(DisplayTargetSnapshot target)
    {
        var resolution = $"{target.SourceWidth}×{target.SourceHeight}";
        var hertz = target.RefreshDenominator == 0
            ? 0
            : target.RefreshNumerator / (double)target.RefreshDenominator;
        return hertz > 0 ? $"{resolution} · {hertz:0.##} Hz" : resolution;
    }
}
