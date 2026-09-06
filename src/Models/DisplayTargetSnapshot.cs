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

    /// <summary>
    /// Whether advanced colour was read when this profile was captured.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="AdvancedColorEnabled"/> being false, and the
    /// distinction matters: a profile saved before Sherpa understood HDR would
    /// otherwise look like "HDR was off", and restoring it would switch a
    /// monitor's HDR off for someone who never asked.
    /// </remarks>
    public bool AdvancedColorCaptured { get; set; }

    /// <summary>Whether the display and driver offered advanced colour at capture time.</summary>
    public bool AdvancedColorSupported { get; set; }

    /// <summary>Whether HDR was on for this display when the profile was captured.</summary>
    public bool AdvancedColorEnabled { get; set; }

    /// <summary>
    /// Set when Windows itself is holding advanced colour off, for instance
    /// because the mode in use cannot carry it. Recorded so a profile that cannot
    /// restore HDR can say why rather than silently doing nothing.
    /// </summary>
    public bool AdvancedColorForceDisabled { get; set; }

    /// <summary>DISPLAYCONFIG_COLOR_ENCODING, kept for the layout preview and diagnostics.</summary>
    public uint ColorEncoding { get; set; }

    /// <summary>Bits per colour channel, kept for the layout preview and diagnostics.</summary>
    public uint BitsPerColorChannel { get; set; }
}
