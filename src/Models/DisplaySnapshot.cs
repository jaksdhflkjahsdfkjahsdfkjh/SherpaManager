namespace SherpaManager.Models;

public sealed class DisplaySnapshot
{
    public int SnapshotVersion { get; set; } = 3;
    public bool IsVerified { get; set; }
    public string VerificationEnvironmentFingerprint { get; set; } = string.Empty;
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = string.Empty;
    public uint QueryFlags { get; set; }
    public int PathStructureSize { get; set; }
    public int ModeStructureSize { get; set; }
    public int LogicalDisplayCount { get; set; }
    public List<DisplayTargetSnapshot> ActiveTargets { get; set; } = [];
    public NvidiaSurroundSnapshot? NvidiaSurround { get; set; }
    public List<string> Paths { get; set; } = [];
    public List<string> Modes { get; set; } = [];
}
