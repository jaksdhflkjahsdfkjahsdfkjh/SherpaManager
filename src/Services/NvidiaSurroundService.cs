using System.Runtime.InteropServices;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class NvidiaSurroundService : INvidiaSurroundService, IDisposable
{
    private const int NvApiOk = 0;
    private const int NvApiEndEnumeration = -7;
    private const int NvApiIncompatibleStructVersion = -9;
    private const int NvApiDataNotFound = -121;
    private const int NvApiDriverReloadRequired = -157;
    private const uint InitializeId = 0x0150E828;
    private const uint UnloadId = 0xD22BDD7E;
    private const uint GetCurrentTopologyId = 0xEC32944E;
    private const uint GetTopologyGroupId = 0xCB89381D;
    private const uint GetGpuBusId = 0x1BE0B8E5;
    private const uint EnableCurrentTopologyId = 0x5F1AA66C;
    private const uint SetDisplayGridsId = 0x4D959A89;
    private const uint ValidateDisplayGridsId = 0xCF43903D;
    private const uint EnumDisplayGridsId = 0xDF2887AF;
    private const uint CurrentGpuTopologyFlag = 1;
    private const uint NoDriverReloadFlag = 2;
    private const uint SafeSetFlags = CurrentGpuTopologyFlag | NoDriverReloadFlag;
    private const int MaxMosaicRows = 8;
    private const int MaxMosaicColumns = 8;
    private const int MaxTopologiesPerGroup = 2;
    private const int MaxMosaicDisplays = 64;
    private const int MaxNvApiDisplays = 128;
    private readonly IDiagnosticLog _diagnostics;

    private readonly object _sync = new();
    private bool _initialized;
    private bool _apiInitialized;
    private string? _initializationError;
    private GetCurrentTopologyDelegate? _getCurrentTopology;
    private GetTopologyGroupDelegate? _getTopologyGroup;
    private GetGpuBusIdDelegate? _getGpuBusId;
    private EnableCurrentTopologyDelegate? _enableCurrentTopology;
    private EnumDisplayGridsDelegate? _enumDisplayGrids;
    private ValidateDisplayGridsDelegate? _validateDisplayGrids;
    private SetDisplayGridsDelegate? _setDisplayGrids;
    private UnloadDelegate? _unload;
    private string? _lastDiagnosticStatus;

    public NvidiaSurroundService(IDiagnosticLog? diagnostics = null) =>
        _diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    public NvidiaSurroundSnapshot GetStatus()
    {
        lock (_sync)
        {
            EnsureInitialized();
            if (_getCurrentTopology is null)
                return RecordStatus(new NvidiaSurroundSnapshot
                {
                    Description = _initializationError ?? "NVIDIA NVAPI is unavailable."
                });

            var topology = new MosaicTopologyBrief { Version = MakeVersion<MosaicTopologyBrief>(1) };
            var setting = new MosaicDisplaySetting { Version = MakeVersion<MosaicDisplaySetting>(2) };
            var result = _getCurrentTopology(ref topology, ref setting, out var overlapX, out var overlapY);
            if (result == NvApiIncompatibleStructVersion)
            {
                topology = new MosaicTopologyBrief { Version = MakeVersion<MosaicTopologyBrief>(1) };
                setting = new MosaicDisplaySetting { Version = MakeVersion(5 * sizeof(uint), 1) };
                result = _getCurrentTopology(ref topology, ref setting, out overlapX, out overlapY);
            }
            if (result == NvApiDataNotFound)
            {
                topology = new MosaicTopologyBrief { Version = MakeVersion<MosaicTopologyBrief>(1) };
                setting = new MosaicDisplaySetting { Version = MakeVersion<MosaicDisplaySetting>(2) };
                overlapX = 0;
                overlapY = 0;
            }
            else if (result != NvApiOk)
            {
                return RecordStatus(new NvidiaSurroundSnapshot
                {
                    ApiAvailable = true,
                    StatusKnown = false,
                    Description = $"NVIDIA Surround status is unavailable (NVAPI {result})."
                }, result);
            }

            var refreshRateTimes1000 = setting.RefreshRateTimes1000 > 0
                ? setting.RefreshRateTimes1000
                : setting.RefreshRate * 1000;
            var snapshot = new NvidiaSurroundSnapshot
            {
                ApiAvailable = true,
                StatusKnown = true,
                HasConfiguredTopology = topology.Topology != 0,
                Enabled = topology.Enabled != 0,
                IsPossible = topology.IsPossible != 0,
                Topology = topology.Topology,
                PerDisplayWidth = setting.Width,
                PerDisplayHeight = setting.Height,
                RefreshRateTimes1000 = refreshRateTimes1000,
                OverlapX = overlapX,
                OverlapY = overlapY
            };

            var gridApiReturned = TryEnumerateDisplayGrids(out var displayGrids, out var gridFailure);
            var capturedModernGrid = false;
            if (gridApiReturned && displayGrids.Any(IsMultiDisplayGrid))
            {
                try
                {
                    ValidateManagedGrids(displayGrids);
                    capturedModernGrid = true;
                }
                catch (InvalidOperationException exception)
                {
                    gridFailure = exception.Message;
                }
            }
            if (!capturedModernGrid && string.IsNullOrWhiteSpace(gridFailure))
                gridFailure = "no active multi-panel grid was returned";
            if (capturedModernGrid)
            {
                var primaryGrid = displayGrids.First(IsMultiDisplayGrid);
                snapshot.HasConfiguredTopology = true;
                snapshot.Enabled = true;
                snapshot.IsPossible = true;
                snapshot.Topology = snapshot.Topology != 0
                    ? snapshot.Topology
                    : GetLegacyTopology(primaryGrid.Rows, primaryGrid.Columns);
                snapshot.PerDisplayWidth = primaryGrid.PerDisplayWidth;
                snapshot.PerDisplayHeight = primaryGrid.PerDisplayHeight;
                snapshot.RefreshRateTimes1000 = primaryGrid.RefreshRate * 1000;
                snapshot.FullGridCaptured = true;
                snapshot.DisplayGrids = displayGrids;
                snapshot.GridMembershipVerified = true;
                snapshot.GridTopologyCount = (uint)displayGrids.Count;
            }

            if (!snapshot.HasConfiguredTopology)
            {
                snapshot.GridMembershipVerified = true;
                snapshot.Description = "NVIDIA Surround: not configured.";
                return RecordStatus(snapshot);
            }

            if (topology.Topology != 0 &&
                TryGetGridFingerprint(ref topology, out var topologyCount, out var gridCells, out _))
            {
                snapshot.GridMembershipVerified = true;
                snapshot.GridTopologyCount = topologyCount;
                snapshot.GridCells = gridCells;
            }

            if (snapshot.Enabled)
            {
                if (!capturedModernGrid)
                {
                    snapshot.StatusKnown = false;
                    snapshot.Description =
                        $"NVIDIA Surround is enabled, but its complete grid could not be captured ({gridFailure}). " +
                        "Sherpa will not switch this profile until Capture current can read panel order, bezels, and resolution.";
                    return RecordStatus(snapshot);
                }
            }

            var topologyName = GetTopologyName(snapshot.Topology);
            var refresh = $"{snapshot.RefreshRateTimes1000 / 1000d:0.###} Hz";
            var bezel = snapshot.Enabled && snapshot.DisplayGrids.Any(grid => grid.ApplyWithBezelCorrection)
                ? ", bezel-corrected resolution"
                : string.Empty;
            snapshot.Description = snapshot.Enabled
                ? $"NVIDIA Surround: {topologyName}, {snapshot.PerDisplayWidth}×{snapshot.PerDisplayHeight} per panel at {refresh}, enabled{bezel}; complete panel order and bezel data captured."
                : $"NVIDIA Surround: {topologyName}, {snapshot.PerDisplayWidth}×{snapshot.PerDisplayHeight} per panel at {refresh}, disabled. Capture the sim profile while Surround is enabled to save its complete grid.";
            if (!snapshot.IsPossible)
                snapshot.Description += " The saved legacy topology is not currently possible.";
            return RecordStatus(snapshot);
        }
    }

    public void ApplyConfiguration(NvidiaSurroundSnapshot snapshot, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_sync)
        {
            _diagnostics.Write("info", "nvidia.apply.started", data: new Dictionary<string, object?>
            {
                ["enabled"] = enabled,
                ["gridCount"] = snapshot.DisplayGrids?.Count ?? 0,
                ["fullGridCaptured"] = snapshot.FullGridCaptured
            });
            var current = GetStatus();
            if (!current.ApiAvailable)
                throw new InvalidOperationException(current.Description);
            if (!current.StatusKnown)
                throw new InvalidOperationException(
                    $"Sherpa cannot safely change NVIDIA Surround because its current status is unknown. {current.Description}");

            if (!enabled)
            {
                if (!current.Enabled) return;
                if (_enableCurrentTopology is null)
                    throw new InvalidOperationException("This NVIDIA driver does not expose Surround disabling through NVAPI.");
                var disableResult = _enableCurrentTopology(0);
                _diagnostics.Write(disableResult == NvApiOk ? "info" : "error", "nvidia.disable.completed",
                    data: new Dictionary<string, object?> { ["nvapiCode"] = disableResult });
                if (disableResult != NvApiOk)
                    throw new InvalidOperationException($"NVIDIA could not disable Surround (NVAPI {disableResult}). No Windows display changes were applied.");
                return;
            }

            if (!snapshot.FullGridCaptured || snapshot.DisplayGrids is null ||
                !snapshot.DisplayGrids.Any(IsMultiDisplayGrid))
                throw new InvalidOperationException(
                    "This profile does not contain a complete NVIDIA Surround grid. Enable and fully configure Surround in NVIDIA Control Panel, then use Capture current again.");
            if (_validateDisplayGrids is null || _setDisplayGrids is null)
                throw new InvalidOperationException(
                    "The installed NVIDIA driver does not expose the full grid API required to restore panel order, bezel correction, and resolution.");

            ValidateManagedGrids(snapshot.DisplayGrids);
            ApplyDisplayGrids(snapshot.DisplayGrids);
            _diagnostics.Write("info", "nvidia.apply.completed", data: new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["gridCount"] = snapshot.DisplayGrids.Count
            });
        }
    }

    private NvidiaSurroundSnapshot RecordStatus(NvidiaSurroundSnapshot status, int? nvApiCode = null)
    {
        var fingerprint = $"{status.ApiAvailable}|{status.StatusKnown}|{status.Enabled}|" +
                          $"{status.HasConfiguredTopology}|{status.Topology}|{status.FullGridCaptured}|" +
                          $"{status.DisplayGrids?.Count ?? 0}|{nvApiCode}";
        if (fingerprint == _lastDiagnosticStatus) return status;
        _lastDiagnosticStatus = fingerprint;
        var data = new Dictionary<string, object?>
        {
            ["apiAvailable"] = status.ApiAvailable,
            ["statusKnown"] = status.StatusKnown,
            ["surroundEnabled"] = status.Enabled,
            ["configuredTopology"] = status.HasConfiguredTopology,
            ["topology"] = status.Topology,
            ["fullGridCaptured"] = status.FullGridCaptured,
            ["gridCount"] = status.DisplayGrids?.Count ?? 0
        };
        if (nvApiCode.HasValue) data["nvapiCode"] = nvApiCode.Value;
        _diagnostics.Write(status.StatusKnown ? "info" : "warning", "nvidia.status", status.Description, data);
        return status;
    }

    private void ApplyDisplayGrids(IReadOnlyList<NvidiaSurroundDisplayGridSnapshot> grids)
    {
        var v2Grids = grids.Select(ToNativeV2).ToArray();
        var result = ValidateAndSet(v2Grids, out var validationFailure);
        _diagnostics.Write(result == NvApiOk ? "info" : "warning", "nvidia.grid.apply_v2",
            validationFailure, new Dictionary<string, object?> { ["nvapiCode"] = result });
        if (result == NvApiIncompatibleStructVersion)
        {
            if (grids.Any(grid => grid.PixelShift || grid.Displays.Any(display => display.PixelShiftType != 0)))
                throw new InvalidOperationException("The installed NVIDIA driver cannot restore this pixel-shift grid format.");
            var v1Grids = grids.Select(ToNativeV1).ToArray();
            result = ValidateAndSet(v1Grids, out validationFailure);
            _diagnostics.Write(result == NvApiOk ? "info" : "error", "nvidia.grid.apply_v1",
                validationFailure, new Dictionary<string, object?> { ["nvapiCode"] = result });
        }

        if (result == NvApiDriverReloadRequired)
            throw new InvalidOperationException(
                "NVIDIA requires a driver reload for this Surround topology. Sherpa deliberately blocked that disruptive change; apply it once in NVIDIA Control Panel and capture again.");
        if (result != NvApiOk)
            throw new InvalidOperationException(
                $"NVIDIA rejected the saved Surround grid (NVAPI {result}{validationFailure}). No Windows display changes were applied.");
    }

    private int ValidateAndSet<T>(T[] grids, out string validationFailure) where T : struct
    {
        validationFailure = string.Empty;
        var gridBuffer = IntPtr.Zero;
        var statusBuffer = IntPtr.Zero;
        try
        {
            gridBuffer = AllocateStructureArray(grids);
            var statuses = Enumerable.Range(0, grids.Length).Select(_ => CreateTopologyStatus()).ToArray();
            statusBuffer = AllocateStructureArray(statuses);
            var result = _validateDisplayGrids!(SafeSetFlags, gridBuffer, statusBuffer, (uint)grids.Length);
            if (result != NvApiOk) return result;

            var problems = new List<string>();
            var statusSize = Marshal.SizeOf<MosaicDisplayTopologyStatus>();
            for (var index = 0; index < statuses.Length; index++)
            {
                var status = Marshal.PtrToStructure<MosaicDisplayTopologyStatus>(
                    IntPtr.Add(statusBuffer, index * statusSize));
                if (status.ErrorFlags != 0)
                    problems.Add($"grid {index + 1} error flags 0x{status.ErrorFlags:X}");
                foreach (var display in (status.Displays ?? []).Take((int)Math.Min(status.DisplayCount, MaxNvApiDisplays)))
                    if (display.ErrorFlags != 0)
                        problems.Add($"display 0x{display.DisplayId:X8} error flags 0x{display.ErrorFlags:X}");
            }
            if (problems.Count > 0)
            {
                validationFailure = ": " + string.Join(", ", problems);
                return -172;
            }

            return _setDisplayGrids!(gridBuffer, (uint)grids.Length, SafeSetFlags);
        }
        finally
        {
            FreeStructureArray<T>(gridBuffer, grids.Length);
            FreeStructureArray<MosaicDisplayTopologyStatus>(statusBuffer, grids.Length);
        }
    }

    private bool TryEnumerateDisplayGrids(out List<NvidiaSurroundDisplayGridSnapshot> grids,
        out string failure)
    {
        grids = [];
        if (_enumDisplayGrids is null)
        {
            failure = "the NVIDIA driver does not expose the modern display-grid API";
            return false;
        }

        uint count = 0;
        var countResult = _enumDisplayGrids(IntPtr.Zero, ref count);
        if (countResult == NvApiEndEnumeration || count == 0)
        {
            failure = "no active NVIDIA display grid was returned";
            return false;
        }
        if (countResult != NvApiOk)
        {
            failure = $"NVAPI {countResult}";
            return false;
        }
        if (count > MaxNvApiDisplays)
        {
            failure = $"the driver returned an unreasonable grid count ({count})";
            return false;
        }

        var result = EnumerateV2(count, out grids);
        if (result == NvApiIncompatibleStructVersion)
            result = EnumerateV1(count, out grids);
        if (result != NvApiOk)
        {
            failure = $"NVAPI {result}";
            grids = [];
            return false;
        }
        if (grids.Count == 0)
        {
            failure = "the driver returned an empty grid list";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private int EnumerateV2(uint capacity, out List<NvidiaSurroundDisplayGridSnapshot> grids)
    {
        var native = Enumerable.Range(0, (int)capacity).Select(_ => CreateGridV2()).ToArray();
        return EnumerateNative(native, capacity, FromNative, out grids);
    }

    private int EnumerateV1(uint capacity, out List<NvidiaSurroundDisplayGridSnapshot> grids)
    {
        var native = Enumerable.Range(0, (int)capacity).Select(_ => CreateGridV1()).ToArray();
        return EnumerateNative(native, capacity, FromNative, out grids);
    }

    private int EnumerateNative<T>(T[] native, uint capacity,
        Func<T, NvidiaSurroundDisplayGridSnapshot> convert,
        out List<NvidiaSurroundDisplayGridSnapshot> grids) where T : struct
    {
        grids = [];
        var buffer = IntPtr.Zero;
        try
        {
            buffer = AllocateStructureArray(native);
            var returned = capacity;
            var result = _enumDisplayGrids!(buffer, ref returned);
            if (result != NvApiOk) return result;
            if (returned > capacity) return -174;
            var size = Marshal.SizeOf<T>();
            for (var index = 0; index < returned; index++)
            {
                var value = Marshal.PtrToStructure<T>(IntPtr.Add(buffer, index * size));
                grids.Add(convert(value));
            }
            return NvApiOk;
        }
        finally
        {
            FreeStructureArray<T>(buffer, native.Length);
        }
    }

    private bool TryGetGridFingerprint(ref MosaicTopologyBrief topology, out uint topologyCount,
        out List<NvidiaSurroundGridCellSnapshot> gridCells, out string failure)
    {
        topologyCount = 0;
        gridCells = [];
        if (_getTopologyGroup is null || _getGpuBusId is null)
        {
            failure = "the NVIDIA driver does not expose legacy topology identity details";
            return false;
        }

        var details = new MosaicTopologyDetails[MaxTopologiesPerGroup];
        for (var index = 0; index < details.Length; index++)
            details[index].GpuLayout = new MosaicTopologyCell[MaxMosaicRows * MaxMosaicColumns];
        var group = new MosaicTopologyGroup
        {
            Version = MakeVersion<MosaicTopologyGroup>(1),
            Topologies = details
        };
        var result = _getTopologyGroup(ref topology, ref group);
        if (result != NvApiOk)
        {
            failure = $"NVAPI {result}";
            return false;
        }
        if (group.Brief.Topology != topology.Topology ||
            group.Count is 0 or > MaxTopologiesPerGroup || group.Topologies is null)
        {
            failure = "the driver returned inconsistent topology details";
            return false;
        }

        var gpuBusIds = new Dictionary<IntPtr, uint>();
        for (var topologyIndex = 0u; topologyIndex < group.Count; topologyIndex++)
        {
            var current = group.Topologies[topologyIndex];
            if (current.ValidityMask != 0 || current.RowCount is 0 or > MaxMosaicRows ||
                current.ColumnCount is 0 or > MaxMosaicColumns || current.GpuLayout is null)
            {
                failure = "the configured legacy grid is incomplete or not currently valid";
                return false;
            }

            for (var row = 0u; row < current.RowCount; row++)
                for (var column = 0u; column < current.ColumnCount; column++)
                {
                    var cell = current.GpuLayout[(row * MaxMosaicColumns) + column];
                    if (cell.PhysicalGpu == IntPtr.Zero || cell.DisplayOutputId == 0)
                    {
                        failure = "the driver did not identify every GPU and panel in the legacy grid";
                        return false;
                    }
                    if (!gpuBusIds.TryGetValue(cell.PhysicalGpu, out var gpuBusId))
                    {
                        var busResult = _getGpuBusId(cell.PhysicalGpu, out gpuBusId);
                        if (busResult != NvApiOk)
                        {
                            failure = $"the driver could not identify a grid GPU (NVAPI {busResult})";
                            return false;
                        }
                        gpuBusIds[cell.PhysicalGpu] = gpuBusId;
                    }
                    gridCells.Add(new NvidiaSurroundGridCellSnapshot
                    {
                        TopologyIndex = topologyIndex,
                        Row = row,
                        Column = column,
                        GpuBusId = gpuBusId,
                        DisplayOutputId = cell.DisplayOutputId,
                        OverlapX = cell.OverlapX,
                        OverlapY = cell.OverlapY
                    });
                }
        }

        topologyCount = group.Count;
        failure = string.Empty;
        return gridCells.Count > 0;
    }

    private static void ValidateManagedGrids(IReadOnlyList<NvidiaSurroundDisplayGridSnapshot> grids)
    {
        if (grids.Count is 0 or > MaxNvApiDisplays)
            throw new InvalidOperationException("The saved NVIDIA display-grid list is empty or invalid.");
        var displayIds = new HashSet<uint>();
        foreach (var grid in grids)
        {
            if (grid.Rows is 0 or > MaxMosaicRows || grid.Columns is 0 or > MaxMosaicColumns ||
                grid.Rows * grid.Columns > MaxMosaicDisplays)
                throw new InvalidOperationException("A saved NVIDIA grid has invalid row or column dimensions.");
            if (grid.PerDisplayWidth == 0 || grid.PerDisplayHeight == 0 || grid.RefreshRate == 0)
                throw new InvalidOperationException("A saved NVIDIA grid has an invalid resolution or refresh rate.");
            if (grid.Displays is null || grid.Displays.Count != grid.Rows * grid.Columns)
                throw new InvalidOperationException("A saved NVIDIA grid does not contain exactly one display for every grid position.");

            var positions = new HashSet<(uint Row, uint Column)>();
            foreach (var display in grid.Displays)
            {
                if (display.DisplayId == 0 || !displayIds.Add(display.DisplayId) ||
                    display.Row >= grid.Rows || display.Column >= grid.Columns ||
                    display.Rotation > 4 || !positions.Add((display.Row, display.Column)))
                    throw new InvalidOperationException("A saved NVIDIA grid contains an invalid or duplicate panel position.");
            }
        }
    }

    private static bool IsMultiDisplayGrid(NvidiaSurroundDisplayGridSnapshot grid) =>
        grid.Rows * grid.Columns > 1;

    private static NvidiaSurroundDisplayGridSnapshot FromNative(MosaicGridTopologyV2 grid)
    {
        ValidateNativeGrid(grid.Rows, grid.Columns, grid.DisplayCount, grid.Displays?.Length ?? 0);
        return new NvidiaSurroundDisplayGridSnapshot
        {
            Rows = grid.Rows,
            Columns = grid.Columns,
            ApplyWithBezelCorrection = (grid.Flags & 1) != 0,
            ImmersiveGaming = (grid.Flags & 2) != 0,
            BaseMosaic = (grid.Flags & 4) != 0,
            PixelShift = (grid.Flags & 32) != 0,
            PerDisplayWidth = grid.DisplaySettings.Width,
            PerDisplayHeight = grid.DisplaySettings.Height,
            BitsPerPixel = grid.DisplaySettings.BitsPerPixel,
            RefreshRate = grid.DisplaySettings.RefreshRate,
            Displays = Enumerable.Range(0, (int)grid.DisplayCount).Select(index =>
            {
                var display = grid.Displays![index];
                return new NvidiaSurroundDisplayGridCellSnapshot
                {
                    Row = (uint)index / grid.Columns,
                    Column = (uint)index % grid.Columns,
                    DisplayId = display.DisplayId,
                    OverlapX = display.OverlapX,
                    OverlapY = display.OverlapY,
                    Rotation = display.Rotation,
                    CloneGroup = display.CloneGroup,
                    PixelShiftType = display.PixelShiftType
                };
            }).ToList()
        };
    }

    private static NvidiaSurroundDisplayGridSnapshot FromNative(MosaicGridTopologyV1 grid)
    {
        ValidateNativeGrid(grid.Rows, grid.Columns, grid.DisplayCount, grid.Displays?.Length ?? 0);
        return new NvidiaSurroundDisplayGridSnapshot
        {
            Rows = grid.Rows,
            Columns = grid.Columns,
            ApplyWithBezelCorrection = (grid.Flags & 1) != 0,
            ImmersiveGaming = (grid.Flags & 2) != 0,
            BaseMosaic = (grid.Flags & 4) != 0,
            PerDisplayWidth = grid.DisplaySettings.Width,
            PerDisplayHeight = grid.DisplaySettings.Height,
            BitsPerPixel = grid.DisplaySettings.BitsPerPixel,
            RefreshRate = grid.DisplaySettings.RefreshRate,
            Displays = Enumerable.Range(0, (int)grid.DisplayCount).Select(index =>
            {
                var display = grid.Displays![index];
                return new NvidiaSurroundDisplayGridCellSnapshot
                {
                    Row = (uint)index / grid.Columns,
                    Column = (uint)index % grid.Columns,
                    DisplayId = display.DisplayId,
                    OverlapX = display.OverlapX,
                    OverlapY = display.OverlapY,
                    Rotation = display.Rotation,
                    CloneGroup = display.CloneGroup
                };
            }).ToList()
        };
    }

    private static void ValidateNativeGrid(uint rows, uint columns, uint displayCount, int arrayLength)
    {
        if (rows is 0 or > MaxMosaicRows || columns is 0 or > MaxMosaicColumns ||
            displayCount == 0 || displayCount != rows * columns || displayCount > arrayLength)
            throw new InvalidOperationException("NVIDIA returned an invalid display-grid structure.");
    }

    private static MosaicGridTopologyV2 ToNativeV2(NvidiaSurroundDisplayGridSnapshot grid)
    {
        var native = CreateGridV2();
        native.Rows = grid.Rows;
        native.Columns = grid.Columns;
        native.DisplayCount = (uint)grid.Displays.Count;
        native.Flags = CreateGridFlags(grid, includePixelShift: true);
        native.DisplaySettings = ToNativeSetting(grid);
        foreach (var display in grid.Displays)
        {
            var index = checked((int)((display.Row * grid.Columns) + display.Column));
            native.Displays[index] = new MosaicGridTopologyDisplayV2
            {
                Version = MakeVersion<MosaicGridTopologyDisplayV2>(2),
                DisplayId = display.DisplayId,
                OverlapX = display.OverlapX,
                OverlapY = display.OverlapY,
                Rotation = display.Rotation,
                CloneGroup = display.CloneGroup,
                PixelShiftType = display.PixelShiftType
            };
        }
        return native;
    }

    private static MosaicGridTopologyV1 ToNativeV1(NvidiaSurroundDisplayGridSnapshot grid)
    {
        var native = CreateGridV1();
        native.Rows = grid.Rows;
        native.Columns = grid.Columns;
        native.DisplayCount = (uint)grid.Displays.Count;
        native.Flags = CreateGridFlags(grid, includePixelShift: false);
        native.DisplaySettings = ToNativeSetting(grid);
        foreach (var display in grid.Displays)
        {
            var index = checked((int)((display.Row * grid.Columns) + display.Column));
            native.Displays[index] = new MosaicGridTopologyDisplayV1
            {
                DisplayId = display.DisplayId,
                OverlapX = display.OverlapX,
                OverlapY = display.OverlapY,
                Rotation = display.Rotation,
                CloneGroup = display.CloneGroup
            };
        }
        return native;
    }

    private static MosaicDisplaySettingV1 ToNativeSetting(NvidiaSurroundDisplayGridSnapshot grid) => new()
    {
        Version = MakeVersion<MosaicDisplaySettingV1>(1),
        Width = grid.PerDisplayWidth,
        Height = grid.PerDisplayHeight,
        BitsPerPixel = grid.BitsPerPixel,
        RefreshRate = grid.RefreshRate
    };

    private static uint CreateGridFlags(NvidiaSurroundDisplayGridSnapshot grid, bool includePixelShift) =>
        (grid.ApplyWithBezelCorrection ? 1u : 0u) |
        (grid.ImmersiveGaming ? 2u : 0u) |
        (grid.BaseMosaic ? 4u : 0u) |
        (includePixelShift && grid.PixelShift ? 32u : 0u);

    private static MosaicGridTopologyV2 CreateGridV2() => new()
    {
        Version = MakeVersion<MosaicGridTopologyV2>(2),
        Displays = Enumerable.Range(0, MaxMosaicDisplays).Select(_ =>
            new MosaicGridTopologyDisplayV2 { Version = MakeVersion<MosaicGridTopologyDisplayV2>(2) }).ToArray(),
        DisplaySettings = new MosaicDisplaySettingV1 { Version = MakeVersion<MosaicDisplaySettingV1>(1) }
    };

    private static MosaicGridTopologyV1 CreateGridV1() => new()
    {
        Version = MakeVersion<MosaicGridTopologyV1>(1),
        Displays = new MosaicGridTopologyDisplayV1[MaxMosaicDisplays],
        DisplaySettings = new MosaicDisplaySettingV1 { Version = MakeVersion<MosaicDisplaySettingV1>(1) }
    };

    private static MosaicDisplayTopologyStatus CreateTopologyStatus() => new()
    {
        Version = MakeVersion<MosaicDisplayTopologyStatus>(1),
        Displays = new MosaicDisplayTopologyStatusDisplay[MaxNvApiDisplays]
    };

    private static IntPtr AllocateStructureArray<T>(T[] values) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(checked(size * values.Length));
        for (var index = 0; index < values.Length; index++)
            Marshal.StructureToPtr(values[index], IntPtr.Add(buffer, index * size), false);
        return buffer;
    }

    private static void FreeStructureArray<T>(IntPtr buffer, int count) where T : struct
    {
        if (buffer == IntPtr.Zero) return;
        var size = Marshal.SizeOf<T>();
        for (var index = 0; index < count; index++)
            Marshal.DestroyStructure<T>(IntPtr.Add(buffer, index * size));
        Marshal.FreeHGlobal(buffer);
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var initializePointer = NvApiQueryInterface(InitializeId);
            if (initializePointer == IntPtr.Zero)
            {
                _diagnostics.Write("warning", "nvidia.initialize.unavailable",
                    "The NVIDIA driver does not expose NVAPI initialization.");
                _initializationError = "The NVIDIA driver does not expose NVAPI initialization.";
                return;
            }
            var initialize = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(initializePointer);
            var result = initialize();
            if (result != NvApiOk)
            {
                _diagnostics.Write("error", "nvidia.initialize.failed",
                    data: new Dictionary<string, object?> { ["nvapiCode"] = result });
                _initializationError = $"NVIDIA NVAPI initialization failed ({result}).";
                return;
            }
            _apiInitialized = true;

            _getCurrentTopology = GetDelegate<GetCurrentTopologyDelegate>(GetCurrentTopologyId);
            _getTopologyGroup = GetDelegate<GetTopologyGroupDelegate>(GetTopologyGroupId);
            _getGpuBusId = GetDelegate<GetGpuBusIdDelegate>(GetGpuBusId);
            _enableCurrentTopology = GetDelegate<EnableCurrentTopologyDelegate>(EnableCurrentTopologyId);
            _enumDisplayGrids = GetDelegate<EnumDisplayGridsDelegate>(EnumDisplayGridsId);
            _validateDisplayGrids = GetDelegate<ValidateDisplayGridsDelegate>(ValidateDisplayGridsId);
            _setDisplayGrids = GetDelegate<SetDisplayGridsDelegate>(SetDisplayGridsId);
            _unload = GetDelegate<UnloadDelegate>(UnloadId);
            _diagnostics.Write("info", "nvidia.initialize.completed", data: new Dictionary<string, object?>
            {
                ["topologyApiAvailable"] = _getCurrentTopology is not null,
                ["gridApiAvailable"] = _enumDisplayGrids is not null && _validateDisplayGrids is not null &&
                                       _setDisplayGrids is not null
            });
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _diagnostics.Error("nvidia.initialize.exception", exception);
            _initializationError = exception switch
            {
                DllNotFoundException => "No NVIDIA driver was found.",
                EntryPointNotFoundException => "The installed NVIDIA driver does not expose NVAPI.",
                _ => "The NVIDIA driver architecture does not match Sherpa Manager."
            };
        }
    }

    private static T? GetDelegate<T>(uint functionId) where T : Delegate
    {
        var pointer = NvApiQueryInterface(functionId);
        return pointer == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private static uint MakeVersion<T>(int version) where T : struct =>
        (uint)(Marshal.SizeOf<T>() | (version << 16));

    private static uint MakeVersion(int size, int version) => (uint)(size | (version << 16));

    public void Dispose()
    {
        lock (_sync)
        {
            if (_apiInitialized)
            {
                try { _unload?.Invoke(); }
                catch { /* Driver cleanup is best effort during application shutdown. */ }
            }
            _apiInitialized = false;
            _getCurrentTopology = null;
            _getTopologyGroup = null;
            _getGpuBusId = null;
            _enableCurrentTopology = null;
            _enumDisplayGrids = null;
            _validateDisplayGrids = null;
            _setDisplayGrids = null;
            _unload = null;
        }
    }

    private static string GetTopologyName(uint topology) => topology switch
    {
        1 => "1×2",
        2 => "2×1",
        3 => "1×3",
        4 => "3×1",
        5 => "1×4",
        6 => "4×1",
        7 => "2×2",
        8 => "2×3",
        9 => "2×4",
        10 => "3×2",
        11 => "4×2",
        12 => "1×5",
        13 => "1×6",
        14 => "7×1",
        _ => topology == 0 ? "not configured" : $"topology {topology}"
    };

    private static uint GetLegacyTopology(uint rows, uint columns) => (rows, columns) switch
    {
        (1, 2) => 1,
        (2, 1) => 2,
        (1, 3) => 3,
        (3, 1) => 4,
        (1, 4) => 5,
        (4, 1) => 6,
        (2, 2) => 7,
        (2, 3) => 8,
        (2, 4) => 9,
        (3, 2) => 10,
        (4, 2) => 11,
        (1, 5) => 12,
        (1, 6) => 13,
        (7, 1) => 14,
        _ => 0
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MosaicTopologyBrief
    {
        public uint Version;
        public uint Topology;
        public uint Enabled;
        public uint IsPossible;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MosaicDisplaySetting
    {
        public uint Version;
        public uint Width;
        public uint Height;
        public uint BitsPerPixel;
        public uint RefreshRate;
        public uint RefreshRateTimes1000;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MosaicDisplaySettingV1
    {
        public uint Version;
        public uint Width;
        public uint Height;
        public uint BitsPerPixel;
        public uint RefreshRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MosaicGridTopologyDisplayV1
    {
        public uint DisplayId;
        public int OverlapX;
        public int OverlapY;
        public uint Rotation;
        public uint CloneGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MosaicGridTopologyDisplayV2
    {
        public uint Version;
        public uint DisplayId;
        public int OverlapX;
        public int OverlapY;
        public uint Rotation;
        public uint CloneGroup;
        public uint PixelShiftType;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct MosaicGridTopologyV1
    {
        public uint Version;
        public uint Rows;
        public uint Columns;
        public uint DisplayCount;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxMosaicDisplays)]
        public MosaicGridTopologyDisplayV1[] Displays;
        public MosaicDisplaySettingV1 DisplaySettings;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct MosaicGridTopologyV2
    {
        public uint Version;
        public uint Rows;
        public uint Columns;
        public uint DisplayCount;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxMosaicDisplays)]
        public MosaicGridTopologyDisplayV2[] Displays;
        public MosaicDisplaySettingV1 DisplaySettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MosaicDisplayTopologyStatusDisplay
    {
        public uint DisplayId;
        public uint ErrorFlags;
        public uint WarningFlags;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct MosaicDisplayTopologyStatus
    {
        public uint Version;
        public uint ErrorFlags;
        public uint WarningFlags;
        public uint DisplayCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxNvApiDisplays)]
        public MosaicDisplayTopologyStatusDisplay[] Displays;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct MosaicTopologyCell
    {
        public IntPtr PhysicalGpu;
        public uint DisplayOutputId;
        public int OverlapX;
        public int OverlapY;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct MosaicTopologyDetails
    {
        public uint Version;
        public IntPtr LogicalGpu;
        public uint ValidityMask;
        public uint RowCount;
        public uint ColumnCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxMosaicRows * MaxMosaicColumns)]
        public MosaicTopologyCell[] GpuLayout;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct MosaicTopologyGroup
    {
        public uint Version;
        public MosaicTopologyBrief Brief;
        public uint Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxTopologiesPerGroup)]
        public MosaicTopologyDetails[] Topologies;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCurrentTopologyDelegate(ref MosaicTopologyBrief topology, ref MosaicDisplaySetting setting,
        out int overlapX, out int overlapY);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetTopologyGroupDelegate(ref MosaicTopologyBrief topology,
        ref MosaicTopologyGroup topologyGroup);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetGpuBusIdDelegate(IntPtr physicalGpu, out uint busId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnableCurrentTopologyDelegate(uint enabled);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumDisplayGridsDelegate(IntPtr gridTopologies, ref uint gridCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ValidateDisplayGridsDelegate(uint setTopologyFlags, IntPtr gridTopologies,
        IntPtr topologyStatuses, uint gridCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetDisplayGridsDelegate(IntPtr gridTopologies, uint gridCount, uint setTopologyFlags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnloadDelegate();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvApiQueryInterface(uint functionId);
}
