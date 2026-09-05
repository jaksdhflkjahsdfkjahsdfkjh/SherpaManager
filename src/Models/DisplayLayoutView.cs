namespace SherpaManager.Models;

/// <summary>One monitor, placed and scaled for drawing.</summary>
public sealed record DisplayLayoutTile(
    string Name,
    string Detail,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsPrimary,
    uint Rotation)
{
    public string RotationText => Rotation switch
    {
        2 => "90°",
        3 => "180°",
        4 => "270°",
        _ => string.Empty
    };
}

/// <summary>
/// A drawable arrangement of monitors, scaled to fit a given area. Coordinates
/// are relative to the top-left of the arrangement, not the Windows virtual
/// desktop, so a layout whose monitors sit at negative coordinates still draws
/// from zero.
/// </summary>
public sealed record DisplayLayoutView(
    IReadOnlyList<DisplayLayoutTile> Tiles,
    double Width,
    double Height,
    string Summary)
{
    public bool HasTiles => Tiles.Count > 0;

    public static DisplayLayoutView Empty(string summary) => new([], 0, 0, summary);
}
