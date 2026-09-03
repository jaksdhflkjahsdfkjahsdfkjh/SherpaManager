namespace SherpaManager.Models;

public sealed class DisplayTargetSnapshot
{
    public string Identity { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string MonitorDevicePath { get; set; } = string.Empty;
    public uint AdapterLowPart { get; set; }
    public int AdapterHighPart { get; set; }
    public uint TargetId { get; set; }
    public uint ConnectorInstance { get; set; }
    public int PathIndex { get; set; }
    public uint SourceAdapterLowPart { get; set; }
    public int SourceAdapterHighPart { get; set; }
    public uint SourceId { get; set; }
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public uint SourceWidth { get; set; }
    public uint SourceHeight { get; set; }
    public uint PixelFormat { get; set; }
    public uint Rotation { get; set; }
    public uint Scaling { get; set; }
    public uint RefreshNumerator { get; set; }
    public uint RefreshDenominator { get; set; }
    public uint TargetActiveWidth { get; set; }
    public uint TargetActiveHeight { get; set; }
    public uint TargetVSyncNumerator { get; set; }
    public uint TargetVSyncDenominator { get; set; }
    public bool BoostRefreshRate { get; set; }
}
