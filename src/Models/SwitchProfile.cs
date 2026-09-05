using System.Collections.ObjectModel;

namespace SherpaManager.Models;

public sealed class SwitchProfile : ObservableObject
{
    private string _name = "New profile";
    private string _description = string.Empty;
    private string _accentColor = "#6C57FF";
    private string _hotkey = string.Empty;
    private string _audioOutputDeviceId = string.Empty;
    private string _audioOutputDeviceName = string.Empty;
    private DisplaySnapshot? _display;
    private DateTime? _lastActivatedUtc;
    private NvidiaSurroundMode _nvidiaSurroundMode;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string AccentColor { get => _accentColor; set => SetProperty(ref _accentColor, value); }
    public string Hotkey { get => _hotkey; set => SetProperty(ref _hotkey, value); }

    /// <summary>Windows endpoint id to make the default output, or empty to leave audio alone.</summary>
    public string AudioOutputDeviceId
    {
        get => _audioOutputDeviceId;
        set => SetProperty(ref _audioOutputDeviceId, value);
    }

    /// <summary>
    /// The endpoint name as it was when chosen. Kept so a profile can still name
    /// the device it wants when that device is currently unplugged.
    /// </summary>
    public string AudioOutputDeviceName
    {
        get => _audioOutputDeviceName;
        set => SetProperty(ref _audioOutputDeviceName, value);
    }
    public DisplaySnapshot? Display { get => _display; set { if (SetProperty(ref _display, value)) OnPropertyChanged(nameof(DisplaySummary)); } }
    public DateTime? LastActivatedUtc { get => _lastActivatedUtc; set => SetProperty(ref _lastActivatedUtc, value); }
    public NvidiaSurroundMode NvidiaSurroundMode { get => _nvidiaSurroundMode; set => SetProperty(ref _nvidiaSurroundMode, value); }
    public ObservableCollection<LaunchApplication> Applications { get; set; } = [];

    /// <summary>A duplicate never inherits the shortcut: two profiles cannot own one combination.</summary>
    public string DisplaySummary => Display?.Summary ?? "No display layout captured — activation will keep the current layout.";

    public SwitchProfile Clone(string? name = null) => new()
    {
        Name = name ?? $"{Name} copy",
        Description = Description,
        AccentColor = AccentColor,
        NvidiaSurroundMode = NvidiaSurroundMode,
        AudioOutputDeviceId = AudioOutputDeviceId,
        AudioOutputDeviceName = AudioOutputDeviceName,
        Display = Display is null ? null : new DisplaySnapshot
        {
            CapturedAtUtc = Display.CapturedAtUtc,
            SnapshotVersion = Display.SnapshotVersion,
            IsVerified = Display.IsVerified,
            VerificationEnvironmentFingerprint = Display.VerificationEnvironmentFingerprint,
            VerifiedAtUtc = Display.VerifiedAtUtc,
            Summary = Display.Summary,
            QueryFlags = Display.QueryFlags,
            PathStructureSize = Display.PathStructureSize,
            ModeStructureSize = Display.ModeStructureSize,
            LogicalDisplayCount = Display.LogicalDisplayCount,
            ActiveTargets = Display.ActiveTargets.Select(target => new DisplayTargetSnapshot
            {
                Identity = target.Identity,
                FriendlyName = target.FriendlyName,
                MonitorDevicePath = target.MonitorDevicePath,
                AdapterLowPart = target.AdapterLowPart,
                AdapterHighPart = target.AdapterHighPart,
                TargetId = target.TargetId,
                ConnectorInstance = target.ConnectorInstance,
                PathIndex = target.PathIndex,
                SourceAdapterLowPart = target.SourceAdapterLowPart,
                SourceAdapterHighPart = target.SourceAdapterHighPart,
                SourceId = target.SourceId,
                SourceX = target.SourceX,
                SourceY = target.SourceY,
                SourceWidth = target.SourceWidth,
                SourceHeight = target.SourceHeight,
                PixelFormat = target.PixelFormat,
                Rotation = target.Rotation,
                Scaling = target.Scaling,
                RefreshNumerator = target.RefreshNumerator,
                RefreshDenominator = target.RefreshDenominator,
                TargetActiveWidth = target.TargetActiveWidth,
                TargetActiveHeight = target.TargetActiveHeight,
                TargetVSyncNumerator = target.TargetVSyncNumerator,
                TargetVSyncDenominator = target.TargetVSyncDenominator,
                BoostRefreshRate = target.BoostRefreshRate
            }).ToList(),
            NvidiaSurround = Display.NvidiaSurround is null ? null : new NvidiaSurroundSnapshot
            {
                ApiAvailable = Display.NvidiaSurround.ApiAvailable,
                StatusKnown = Display.NvidiaSurround.StatusKnown,
                HasConfiguredTopology = Display.NvidiaSurround.HasConfiguredTopology,
                Enabled = Display.NvidiaSurround.Enabled,
                IsPossible = Display.NvidiaSurround.IsPossible,
                Topology = Display.NvidiaSurround.Topology,
                PerDisplayWidth = Display.NvidiaSurround.PerDisplayWidth,
                PerDisplayHeight = Display.NvidiaSurround.PerDisplayHeight,
                RefreshRateTimes1000 = Display.NvidiaSurround.RefreshRateTimes1000,
                OverlapX = Display.NvidiaSurround.OverlapX,
                OverlapY = Display.NvidiaSurround.OverlapY,
                GridMembershipVerified = Display.NvidiaSurround.GridMembershipVerified,
                GridTopologyCount = Display.NvidiaSurround.GridTopologyCount,
                GridCells = Display.NvidiaSurround.GridCells?.Select(cell =>
                    new NvidiaSurroundGridCellSnapshot
                    {
                        TopologyIndex = cell.TopologyIndex,
                        Row = cell.Row,
                        Column = cell.Column,
                        GpuBusId = cell.GpuBusId,
                        DisplayOutputId = cell.DisplayOutputId,
                        OverlapX = cell.OverlapX,
                        OverlapY = cell.OverlapY
                    }).ToList() ?? [],
                FullGridCaptured = Display.NvidiaSurround.FullGridCaptured,
                DisplayGrids = Display.NvidiaSurround.DisplayGrids?.Select(grid =>
                    new NvidiaSurroundDisplayGridSnapshot
                    {
                        Rows = grid.Rows,
                        Columns = grid.Columns,
                        ApplyWithBezelCorrection = grid.ApplyWithBezelCorrection,
                        ImmersiveGaming = grid.ImmersiveGaming,
                        BaseMosaic = grid.BaseMosaic,
                        PixelShift = grid.PixelShift,
                        PerDisplayWidth = grid.PerDisplayWidth,
                        PerDisplayHeight = grid.PerDisplayHeight,
                        BitsPerPixel = grid.BitsPerPixel,
                        RefreshRate = grid.RefreshRate,
                        Displays = grid.Displays?.Select(display =>
                            new NvidiaSurroundDisplayGridCellSnapshot
                            {
                                Row = display.Row,
                                Column = display.Column,
                                DisplayId = display.DisplayId,
                                OverlapX = display.OverlapX,
                                OverlapY = display.OverlapY,
                                Rotation = display.Rotation,
                                CloneGroup = display.CloneGroup,
                                PixelShiftType = display.PixelShiftType
                            }).ToList() ?? []
                    }).ToList() ?? [],
                Description = Display.NvidiaSurround.Description
            },
            Paths = [.. Display.Paths],
            Modes = [.. Display.Modes]
        },
        Applications = new ObservableCollection<LaunchApplication>(Applications.Select(x => x.Clone()))
    };
}
