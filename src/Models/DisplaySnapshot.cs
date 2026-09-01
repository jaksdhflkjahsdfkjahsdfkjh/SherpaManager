namespace SherpaManager.Models;

public sealed class DisplaySnapshot
{
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = string.Empty;
    public int PathStructureSize { get; set; }
    public int ModeStructureSize { get; set; }
    public List<string> Paths { get; set; } = [];
    public List<string> Modes { get; set; } = [];
}
