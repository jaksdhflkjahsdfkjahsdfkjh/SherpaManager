using System.ComponentModel;
using System.Runtime.InteropServices;
using SherpaManager.Models;
using Forms = System.Windows.Forms;

namespace SherpaManager.Services;

public sealed class DisplayConfigurationService : IDisplayConfigurationService, IDisposable
{
    private const uint QdcAllPaths = 0x00000001;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint QdcVirtualModeAware = 0x00000010;
    private const uint QdcVirtualRefreshRateAware = 0x00000040;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private const uint SdcSaveToDatabase = 0x00000200;
    private const uint SdcAllowChanges = 0x00000400;
    private const uint SdcVirtualModeAware = 0x00008000;
    private const uint SdcVirtualRefreshRateAware = 0x00020000;
    private const uint DisplayConfigPathSupportVirtualMode = 0x00000008;
    private const uint DisplayConfigPathBoostRefreshRate = 0x00000010;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint DisplayConfigModeInfoTypeSource = 1;
    private const uint DisplayConfigModeInfoTypeTarget = 2;
    private const uint DisplayConfigModeInfoTypeDesktopImage = 3;
    private const uint InvalidModeIndex = 0xffffffff;
    private const uint GetTargetName = 2;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorBadConfiguration = 1610;

    private readonly INvidiaSurroundService _nvidiaSurround;
    private readonly DisplayRecoveryStore _recoveryStore;

    public DisplayConfigurationService(INvidiaSurroundService? nvidiaSurround = null,
        DisplayRecoveryStore? recoveryStore = null)
    {
        _nvidiaSurround = nvidiaSurround ?? new NvidiaSurroundService();
        _recoveryStore = recoveryStore ?? new DisplayRecoveryStore();
    }

    public string RecoveryFilePath => _recoveryStore.FilePath;
    public bool HasRecoverySnapshot => _recoveryStore.Load() is not null;

    public NvidiaSurroundSnapshot GetNvidiaSurroundStatus() => _nvidiaSurround.GetStatus();

    public DisplaySnapshot Capture()
    {
        var queryFlags = GetActiveQueryFlags();
        var (paths, modes) = QueryConfiguration(queryFlags);
        var screens = Forms.Screen.AllScreens;
        var screenSummary = string.Join("  •  ", screens.Select(screen =>
            $"{screen.DeviceName.Replace(@"\\.\", string.Empty)} {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " (primary)" : string.Empty)}"));
        var surround = _nvidiaSurround.GetStatus();
        var likelyCombined = screens.Any(screen => screen.Bounds.Height > 0 &&
                                                   screen.Bounds.Width / (double)screen.Bounds.Height >= 3.0);
        var surroundSummary = likelyCombined && !surround.HasConfiguredTopology
            ? $"{surround.Description} A very wide logical display is active; it may be a combined GPU layout."
            : surround.Description;
        var windowsMode = screens.Length switch
        {
            0 => "Windows: no active logical display",
            1 when surround.Enabled => "Windows: one logical NVIDIA Surround display",
            1 when likelyCombined => "Windows: one logical wide display",
            1 => $"Windows: show only on {screens[0].DeviceName.Replace(@"\\.\", string.Empty)}",
            _ => $"Windows: extend across {screens.Length} logical displays"
        };

        return new DisplaySnapshot
        {
            SnapshotVersion = 3,
            CapturedAtUtc = DateTime.UtcNow,
            Summary = $"{windowsMode}: {screenSummary}  •  {surroundSummary}",
            QueryFlags = queryFlags,
            PathStructureSize = Marshal.SizeOf<DisplayConfigPathInfo>(),
            ModeStructureSize = Marshal.SizeOf<DisplayConfigModeInfo>(),
            LogicalDisplayCount = screens.Length,
            ActiveTargets = paths.Select((path, index) => CreateTargetSnapshot(path, modes, index)).ToList(),
            NvidiaSurround = surround,
            Paths = paths.Select(ToBase64).ToList(),
            Modes = modes.Select(ToBase64).ToList()
        };
    }

    public Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot, NvidiaSurroundMode surroundMode,
        CancellationToken cancellationToken = default) =>
        RestoreCoreAsync(snapshot, surroundMode, null, saveEmergencySnapshot: true, cancellationToken);

    public Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot, NvidiaSurroundMode surroundMode,
        Func<DisplaySnapshot, Task<bool>> confirm, CancellationToken cancellationToken = default) =>
        RestoreCoreAsync(snapshot, surroundMode, confirm, saveEmergencySnapshot: true, cancellationToken);

    private async Task<DisplayRestoreResult> RestoreCoreAsync(DisplaySnapshot requested,
        NvidiaSurroundMode surroundMode, Func<DisplaySnapshot, Task<bool>>? confirm,
        bool saveEmergencySnapshot, CancellationToken cancellationToken)
    {
        ValidateSnapshotStructures(requested);
        var emergencySnapshot = Capture();
        if (saveEmergencySnapshot) _recoveryStore.Save(emergencySnapshot);
        var surroundWasManaged = surroundMode != NvidiaSurroundMode.Ignore;
        if (surroundWasManaged && emergencySnapshot.NvidiaSurround?.StatusKnown != true)
            throw new InvalidOperationException("Sherpa cannot change NVIDIA Surround because it could not capture a known rollback state. Try again after NVIDIA Control Panel reports the topology normally.");

        try
        {
            await ApplySurroundModeAsync(requested.NvidiaSurround, surroundMode, cancellationToken);
            await WaitForTargetInventoryAsync(requested, cancellationToken);
            var prepared = PrepareSnapshotForCurrentHardware(requested);

            var usedAdjustedModes = ApplyRaw(prepared, saveToDatabase: false);
            var settled = await WaitForAppliedSnapshotAsync(prepared, surroundMode,
                requireExactSemantics: !usedAdjustedModes, cancellationToken);

            if (confirm is not null && !await confirm(settled))
            {
                await RollbackAsync(emergencySnapshot, surroundWasManaged, CancellationToken.None);
                return new DisplayRestoreResult(usedAdjustedModes,
                    "The display test was reverted; the requested layout was not saved to Windows.", Kept: false);
            }

            var commitSnapshot = usedAdjustedModes ? settled : prepared;
            ApplyRaw(commitSnapshot, saveToDatabase: true);
            var committed = await WaitForAppliedSnapshotAsync(commitSnapshot, surroundMode,
                requireExactSemantics: true, cancellationToken);

            CopyLayout(committed, requested);
            return new DisplayRestoreResult(usedAdjustedModes,
                usedAdjustedModes
                    ? "Display layout applied and verified. Windows selected compatible mode values, which Sherpa saved for this profile."
                    : "Display layout applied, verified, and saved.");
        }
        catch (Exception failure)
        {
            var rollbackMessage = "Automatic rollback failed; use Win+P or Windows Display Settings to recover.";
            try
            {
                await RollbackAsync(emergencySnapshot, surroundWasManaged, CancellationToken.None);
                rollbackMessage = "The previous display layout was restored and verified automatically.";
            }
            catch (Exception rollbackFailure)
            {
                rollbackMessage = $"Automatic rollback also failed: {rollbackFailure.Message} Use Win+P or Windows Display Settings to recover.";
            }

            throw new InvalidOperationException($"{failure.Message} {rollbackMessage}", failure);
        }
    }

    public async Task<DisplayRestoreResult> RestoreLastRecoveryAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _recoveryStore.Load()
            ?? throw new InvalidOperationException("No valid display recovery snapshot is available yet.");
        var surroundMode = snapshot.NvidiaSurround is { StatusKnown: true } surround
            ? surround.Enabled ? NvidiaSurroundMode.RequireEnabled : NvidiaSurroundMode.RequireDisabled
            : NvidiaSurroundMode.Ignore;
        return await RestoreCoreAsync(snapshot, surroundMode, null, saveEmergencySnapshot: false, cancellationToken);
    }

    private async Task ApplySurroundModeAsync(NvidiaSurroundSnapshot? expected, NvidiaSurroundMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == NvidiaSurroundMode.Ignore) return;
        if (expected?.StatusKnown != true)
            throw new InvalidOperationException("This profile does not contain a verified NVIDIA Surround status. Recapture it or choose 'Do not manage'.");

        var current = _nvidiaSurround.GetStatus();
        EnsureKnownSurroundStatus(current);

        var enabled = mode == NvidiaSurroundMode.RequireEnabled;
        if (enabled && !expected.FullGridCaptured)
            throw new InvalidOperationException("This profile predates complete NVIDIA Surround capture. Enable the required Surround layout in NVIDIA Control Panel and use Capture current again.");
        _nvidiaSurround.ApplyConfiguration(expected, enabled);
        await WaitForSurroundStateAsync(expected, enabled, cancellationToken);
    }

    private async Task WaitForSurroundStateAsync(NvidiaSurroundSnapshot expected, bool enabled,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _nvidiaSurround.GetStatus();
            if (status.StatusKnown && status.Enabled == enabled &&
                (!enabled || SurroundFingerprintMatches(expected, status))) return;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"NVIDIA Surround did not finish {(enabled ? "enabling" : "disabling")} within 25 seconds.");
    }

    private static void EnsureKnownSurroundStatus(NvidiaSurroundSnapshot status)
    {
        if (!status.ApiAvailable || !status.StatusKnown)
            throw new InvalidOperationException($"Sherpa cannot safely manage NVIDIA Surround because its status is unknown. {status.Description}");
    }

    private static bool SurroundFingerprintMatches(NvidiaSurroundSnapshot expected, NvidiaSurroundSnapshot actual)
    {
        if (!expected.StatusKnown || !actual.StatusKnown) return false;
        if (expected.HasConfiguredTopology != actual.HasConfiguredTopology) return false;
        if (!expected.HasConfiguredTopology) return true;
        if (expected.FullGridCaptured || actual.FullGridCaptured)
        {
            if (!expected.FullGridCaptured || !actual.FullGridCaptured) return false;
            var expectedGrids = (expected.DisplayGrids ?? []).Select(DisplayGridFingerprint)
                .OrderBy(value => value, StringComparer.Ordinal).ToList();
            var actualGrids = (actual.DisplayGrids ?? []).Select(DisplayGridFingerprint)
                .OrderBy(value => value, StringComparer.Ordinal).ToList();
            return expectedGrids.Count > 0 && expectedGrids.SequenceEqual(actualGrids, StringComparer.Ordinal);
        }
        if (!expected.GridMembershipVerified || !actual.GridMembershipVerified ||
            expected.GridTopologyCount != actual.GridTopologyCount)
            return false;
        var expectedCells = (expected.GridCells ?? []).OrderBy(CellKey).ToList();
        var actualCells = (actual.GridCells ?? []).OrderBy(CellKey).ToList();
        return expected.Topology == actual.Topology &&
               expected.PerDisplayWidth == actual.PerDisplayWidth &&
               expected.PerDisplayHeight == actual.PerDisplayHeight &&
               expected.RefreshRateTimes1000 == actual.RefreshRateTimes1000 &&
               expected.OverlapX == actual.OverlapX && expected.OverlapY == actual.OverlapY &&
               expectedCells.Count > 0 && expectedCells.Count == actualCells.Count &&
               expectedCells.Zip(actualCells).All(pair =>
                   pair.First.TopologyIndex == pair.Second.TopologyIndex &&
                   pair.First.Row == pair.Second.Row && pair.First.Column == pair.Second.Column &&
                   pair.First.GpuBusId == pair.Second.GpuBusId &&
                   pair.First.DisplayOutputId == pair.Second.DisplayOutputId &&
                   pair.First.OverlapX == pair.Second.OverlapX && pair.First.OverlapY == pair.Second.OverlapY);
    }

    private static (uint Topology, uint Row, uint Column) CellKey(NvidiaSurroundGridCellSnapshot cell) =>
        (cell.TopologyIndex, cell.Row, cell.Column);

    private static string DisplayGridFingerprint(NvidiaSurroundDisplayGridSnapshot grid)
    {
        var displays = string.Join(";", (grid.Displays ?? [])
            .OrderBy(display => display.Row).ThenBy(display => display.Column)
            .Select(display => $"{display.Row},{display.Column},{display.DisplayId},{display.OverlapX},{display.OverlapY},{display.Rotation},{display.CloneGroup},{display.PixelShiftType}"));
        return $"{grid.Rows}x{grid.Columns}|{grid.ApplyWithBezelCorrection}|{grid.ImmersiveGaming}|{grid.BaseMosaic}|{grid.PixelShift}|" +
               $"{grid.PerDisplayWidth}x{grid.PerDisplayHeight}x{grid.BitsPerPixel}@{grid.RefreshRate}|{displays}";
    }

    private async Task RollbackAsync(DisplaySnapshot emergencySnapshot, bool restoreSurround,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        if (restoreSurround && emergencySnapshot.NvidiaSurround is { StatusKnown: true } original)
        {
            try
            {
                _nvidiaSurround.ApplyConfiguration(original, original.Enabled);
                await WaitForSurroundStateAsync(original, original.Enabled, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"NVIDIA Surround could not be restored to its previous state: {exception.Message}", exception));
            }
        }

        try
        {
            await WaitForTargetInventoryAsync(emergencySnapshot, cancellationToken);
            // A driver topology transition can re-enumerate adapter LUIDs and
            // source/target IDs. Remap the emergency snapshot just like a normal
            // restore instead of replaying identifiers captured before the change.
            var preparedEmergency = PrepareSnapshotForCurrentHardware(emergencySnapshot);
            ApplyRaw(preparedEmergency, saveToDatabase: true);
            await WaitForAppliedSnapshotAsync(preparedEmergency, NvidiaSurroundMode.Ignore,
                requireExactSemantics: preparedEmergency.SnapshotVersion >= 3, cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(new InvalidOperationException(
                $"The Windows display layout could not be restored: {exception.Message}", exception));
        }

        if (failures.Count > 0)
        {
            var details = string.Join(" ", failures.Select(failure => failure.Message));
            throw new InvalidOperationException(details, new AggregateException(failures));
        }
    }

    private bool ApplyRaw(DisplaySnapshot snapshot, bool saveToDatabase)
    {
        var paths = snapshot.Paths.Select(FromBase64<DisplayConfigPathInfo>).ToArray();
        var modes = snapshot.Modes.Select(FromBase64<DisplayConfigModeInfo>).ToArray();
        var awarenessFlags = GetSetAwarenessFlags(snapshot);
        var baseFlags = SdcUseSuppliedDisplayConfig | awarenessFlags;

        var exactValidation = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            baseFlags | SdcValidate);
        var useAdjustedModes = exactValidation == ErrorBadConfiguration;
        if (!useAdjustedModes) ThrowIfFailed(exactValidation);
        if (useAdjustedModes)
        {
            ThrowIfFailed(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
                baseFlags | SdcValidate | SdcAllowChanges));
        }

        var applyFlags = baseFlags | SdcApply;
        if (useAdjustedModes) applyFlags |= SdcAllowChanges;
        if (saveToDatabase) applyFlags |= SdcSaveToDatabase;
        ThrowIfFailed(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, applyFlags));
        return useAdjustedModes;
    }

    private async Task<DisplaySnapshot> WaitForAppliedSnapshotAsync(DisplaySnapshot expected,
        NvidiaSurroundMode surroundMode, bool requireExactSemantics, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        var consecutiveMatches = 0;
        DisplaySnapshot? lastMatch = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var actual = Capture();
                var matches = TopologiesMatch(expected, actual, surroundMode, requireExactSemantics);
                consecutiveMatches = matches ? consecutiveMatches + 1 : 0;
                lastMatch = matches ? actual : null;
                if (consecutiveMatches >= 2 && lastMatch is not null) return lastMatch;
            }
            catch (Win32Exception) { consecutiveMatches = 0; }
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException(requireExactSemantics
            ? "Windows did not settle on the requested resolution, position, refresh rate, rotation, and monitor set."
            : "Windows did not settle on the requested monitor set.");
    }

    private static bool TopologiesMatch(DisplaySnapshot expected, DisplaySnapshot actual,
        NvidiaSurroundMode surroundMode, bool requireExactSemantics)
    {
        if (expected.ActiveTargets.Count == 0)
            return expected.Paths.Count == actual.Paths.Count;

        var expectedTargets = expected.ActiveTargets.Select(target => target.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualTargets = actual.ActiveTargets.Select(target => target.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedTargets.SetEquals(actualTargets) || expected.LogicalDisplayCount != actual.LogicalDisplayCount)
            return false;
        if (!SurroundStateMatches(expected.NvidiaSurround, actual.NvidiaSurround, surroundMode)) return false;
        if (!requireExactSemantics || expected.SnapshotVersion < 3) return true;

        foreach (var expectedTarget in expected.ActiveTargets)
        {
            var actualTarget = actual.ActiveTargets.First(target =>
                target.Identity.Equals(expectedTarget.Identity, StringComparison.OrdinalIgnoreCase));
            if (!TargetSemanticsMatch(expectedTarget, actualTarget)) return false;
        }
        return true;
    }

    private static bool SurroundStateMatches(NvidiaSurroundSnapshot? expected, NvidiaSurroundSnapshot? actual,
        NvidiaSurroundMode mode)
    {
        if (mode == NvidiaSurroundMode.Ignore) return true;
        if (expected is null || actual is null || !actual.StatusKnown)
            return false;
        var enabled = mode == NvidiaSurroundMode.RequireEnabled;
        return actual.Enabled == enabled && (!enabled || SurroundFingerprintMatches(expected, actual));
    }

    private static bool TargetSemanticsMatch(DisplayTargetSnapshot expected, DisplayTargetSnapshot actual) =>
        expected.PathIndex == actual.PathIndex &&
        expected.SourceAdapterLowPart == actual.SourceAdapterLowPart &&
        expected.SourceAdapterHighPart == actual.SourceAdapterHighPart &&
        expected.SourceId == actual.SourceId &&
        expected.SourceX == actual.SourceX && expected.SourceY == actual.SourceY &&
        expected.SourceWidth == actual.SourceWidth && expected.SourceHeight == actual.SourceHeight &&
        expected.PixelFormat == actual.PixelFormat && expected.Rotation == actual.Rotation &&
        expected.Scaling == actual.Scaling &&
        RationalMatches(expected.RefreshNumerator, expected.RefreshDenominator,
            actual.RefreshNumerator, actual.RefreshDenominator) &&
        expected.TargetActiveWidth == actual.TargetActiveWidth &&
        expected.TargetActiveHeight == actual.TargetActiveHeight &&
        RationalMatches(expected.TargetVSyncNumerator, expected.TargetVSyncDenominator,
            actual.TargetVSyncNumerator, actual.TargetVSyncDenominator) &&
        expected.BoostRefreshRate == actual.BoostRefreshRate;

    private static bool RationalMatches(uint firstNumerator, uint firstDenominator,
        uint secondNumerator, uint secondDenominator)
    {
        if (firstDenominator == 0 || secondDenominator == 0)
            return firstNumerator == secondNumerator && firstDenominator == secondDenominator;
        var first = firstNumerator / (double)firstDenominator;
        var second = secondNumerator / (double)secondDenominator;
        return Math.Abs(first - second) <= 0.05;
    }

    private async Task WaitForTargetInventoryAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        Exception? lastFailure = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ValidateTargetInventory(snapshot);
                return;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                lastFailure = exception;
            }
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        throw lastFailure ?? new InvalidOperationException("The requested monitors did not become available.");
    }

    private void ValidateTargetInventory(DisplaySnapshot snapshot)
    {
        if (snapshot.ActiveTargets.Count == 0) return;
        var queryFlags = QdcAllPaths | GetAwarenessQueryFlags();
        var (paths, modes) = QueryConfiguration(queryFlags);
        var inventory = paths.Select((path, index) => CreateTargetSnapshot(path, modes, index)).ToList();

        foreach (var expected in snapshot.ActiveTargets)
        {
            var sameIdentity = inventory.Where(item =>
                item.Identity.Equals(expected.Identity, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sameIdentity.Count == 0)
                throw new InvalidOperationException($"The monitor '{expected.FriendlyName}' is missing. Reconnect it or recapture this profile.");
        }
    }

    private DisplaySnapshot PrepareSnapshotForCurrentHardware(DisplaySnapshot snapshot)
    {
        // Version 1 snapshots did not retain a stable monitor device path. They can
        // still be replayed in the same Windows session, but cannot be remapped.
        if (snapshot.ActiveTargets.Count == 0) return snapshot;

        var savedPaths = snapshot.Paths.Select(FromBase64<DisplayConfigPathInfo>).ToArray();
        var savedModes = snapshot.Modes.Select(FromBase64<DisplayConfigModeInfo>).ToArray();
        var (currentPaths, currentModes) = QueryConfiguration(QdcAllPaths | GetAwarenessQueryFlags());
        var currentCandidates = currentPaths.Select((path, index) => new CurrentPathCandidate(path,
            CreateTargetSnapshot(path, currentModes, index))).ToList();
        var expectedTargets = new Dictionary<EndpointKey, DisplayTargetSnapshot>();
        foreach (var target in snapshot.ActiveTargets)
        {
            var endpoint = new EndpointKey(target.AdapterLowPart, target.AdapterHighPart, target.TargetId);
            if (!expectedTargets.TryAdd(endpoint, target))
                throw new InvalidOperationException("A display snapshot contains duplicate target metadata. Recapture this profile.");
        }

        var targetMappings = new Dictionary<EndpointKey, EndpointKey>();
        var sourceMappings = new Dictionary<EndpointKey, EndpointKey>();
        var targetSourceMappings = new Dictionary<EndpointKey, EndpointKey>();
        var adapterMappings = new Dictionary<AdapterKey, Luid>();
        var pathPlans = new List<SavedPathPlan>(savedPaths.Length);

        for (var index = 0; index < savedPaths.Length; index++)
        {
            var savedPath = savedPaths[index];
            var oldTarget = EndpointKey.From(savedPath.TargetInfo.AdapterId, savedPath.TargetInfo.Id);
            var oldSource = EndpointKey.From(savedPath.SourceInfo.AdapterId, savedPath.SourceInfo.Id);
            if (!expectedTargets.TryGetValue(oldTarget, out var expected))
                throw new InvalidOperationException("A saved display path has no stable monitor identity. Recapture this profile.");
            if (expected.PathIndex != index)
                throw new InvalidOperationException("A display snapshot contains inconsistent path metadata. Recapture this profile.");

            var stableIdentity = GetStableMonitorIdentity(expected);
            var candidates = currentCandidates.Where(candidate => candidate.Path.TargetInfo.TargetAvailable &&
                (stableIdentity is null
                    ? EndpointKey.From(candidate.Path.TargetInfo.AdapterId, candidate.Path.TargetInfo.Id) == oldTarget
                    : candidate.Target.MonitorDevicePath.Equals(stableIdentity,
                        StringComparison.OrdinalIgnoreCase))).ToList();

            if (candidates.Count == 0)
            {
                var reason = stableIdentity is null
                    ? "does not have a stable Windows monitor identity"
                    : "is no longer available on the saved display adapter";
                throw new InvalidOperationException($"Windows could not safely remap '{expected.FriendlyName}' because it {reason}. Reconnect it or recapture this profile.");
            }

            var targetEndpoints = candidates.Select(candidate => EndpointKey.From(
                candidate.Path.TargetInfo.AdapterId, candidate.Path.TargetInfo.Id)).Distinct().ToList();
            if (targetEndpoints.Count != 1)
                throw new InvalidOperationException($"Windows returned more than one target for '{expected.FriendlyName}'. Recapture this profile after checking for duplicate monitor entries.");

            var newTarget = targetEndpoints[0];
            AddConsistentMapping(targetMappings, oldTarget, newTarget, expected.FriendlyName);
            AddConsistentAdapterMapping(adapterMappings, savedPath.TargetInfo.AdapterId,
                new Luid { LowPart = newTarget.LowPart, HighPart = newTarget.HighPart });
            pathPlans.Add(new SavedPathPlan(index, savedPath, expected, oldTarget, oldSource,
                newTarget, candidates));
        }

        if (pathPlans.Count != expectedTargets.Count)
            throw new InvalidOperationException("A display snapshot contains target metadata that is not used by a saved path. Recapture this profile.");

        var sourceGroups = new List<SourceGroupPlan>();
        foreach (var group in pathPlans.GroupBy(plan => plan.OldSource))
        {
            HashSet<EndpointKey>? commonSources = null;
            foreach (var plan in group)
            {
                var sourcesForTarget = plan.Candidates
                    .Where(candidate => VirtualModeSupportMatches(plan.SavedPath, candidate.Path))
                    .Select(candidate => EndpointKey.From(candidate.Path.SourceInfo.AdapterId,
                        candidate.Path.SourceInfo.Id)).ToHashSet();
                commonSources = commonSources is null ? sourcesForTarget : Intersect(commonSources, sourcesForTarget);
            }

            if (commonSources is null || commonSources.Count == 0)
                throw new InvalidOperationException($"Windows could not find one desktop source that can drive the saved clone group containing '{group.First().Expected.FriendlyName}'. Recapture this profile after the hardware change.");

            var oldAdapter = new AdapterKey(group.Key.LowPart, group.Key.HighPart);
            if (adapterMappings.TryGetValue(oldAdapter, out var mappedAdapter))
            {
                commonSources.RemoveWhere(source => source.LowPart != mappedAdapter.LowPart ||
                                                    source.HighPart != mappedAdapter.HighPart);
            }
            if (commonSources.Count == 0)
                throw new InvalidOperationException($"The saved desktop source for '{group.First().Expected.FriendlyName}' moved to an incompatible display adapter. Recapture this profile.");

            sourceGroups.Add(new SourceGroupPlan(group.Key, group.ToList(), commonSources.ToList()));
        }

        var sourceAssignments = new Dictionary<EndpointKey, EndpointKey>();
        if (!TryAssignSources(sourceGroups.OrderBy(group => group.Options.Count).ToList(), 0,
                new HashSet<EndpointKey>(), sourceAssignments, adapterMappings))
            throw new InvalidOperationException("Windows could not assign distinct desktop sources for the saved extended and cloned displays. Recapture this profile after the hardware change.");

        foreach (var plan in pathPlans)
        {
            var newSource = sourceAssignments[plan.OldSource];
            var current = plan.Candidates.First(candidate =>
                VirtualModeSupportMatches(plan.SavedPath, candidate.Path) &&
                EndpointKey.From(candidate.Path.SourceInfo.AdapterId, candidate.Path.SourceInfo.Id) == newSource).Path;
            AddConsistentMapping(sourceMappings, plan.OldSource, newSource, plan.Expected.FriendlyName);
            AddConsistentMapping(targetSourceMappings, plan.OldTarget, newSource, plan.Expected.FriendlyName);

            ValidatePathModeReferences(plan.SavedPath, savedModes);
            var preparedPath = plan.SavedPath;
            preparedPath.TargetInfo.AdapterId = current.TargetInfo.AdapterId;
            preparedPath.TargetInfo.Id = current.TargetInfo.Id;
            preparedPath.TargetInfo.OutputTechnology = current.TargetInfo.OutputTechnology;
            preparedPath.TargetInfo.TargetAvailable = current.TargetInfo.TargetAvailable;
            preparedPath.TargetInfo.StatusFlags = current.TargetInfo.StatusFlags;
            preparedPath.SourceInfo.AdapterId = current.SourceInfo.AdapterId;
            preparedPath.SourceInfo.Id = current.SourceInfo.Id;
            preparedPath.SourceInfo.StatusFlags = current.SourceInfo.StatusFlags;
            savedPaths[plan.Index] = preparedPath;
        }

        for (var index = 0; index < savedModes.Length; index++)
        {
            var mode = savedModes[index];
            var oldEndpoint = EndpointKey.From(mode.AdapterId, mode.Id);
            var newEndpoint = default(EndpointKey);
            var hasEndpointMapping = mode.InfoType switch
            {
                DisplayConfigModeInfoTypeSource => sourceMappings.TryGetValue(oldEndpoint, out newEndpoint),
                DisplayConfigModeInfoTypeTarget or DisplayConfigModeInfoTypeDesktopImage =>
                    targetMappings.TryGetValue(oldEndpoint, out newEndpoint),
                _ => throw new InvalidOperationException("A display snapshot contains an unsupported mode-information type. Capture it again with this Sherpa version.")
            };
            if (!hasEndpointMapping)
                throw new InvalidOperationException("A display snapshot contains mode information that is not referenced by an active path. Recapture this profile.");

            mode.AdapterId = new Luid { LowPart = newEndpoint.LowPart, HighPart = newEndpoint.HighPart };
            mode.Id = newEndpoint.Id;
            savedModes[index] = mode;
        }

        var preparedTargets = snapshot.ActiveTargets.Select(CloneTarget).ToList();
        foreach (var target in preparedTargets)
        {
            var oldTarget = new EndpointKey(target.AdapterLowPart, target.AdapterHighPart, target.TargetId);
            if (!targetMappings.TryGetValue(oldTarget, out var newTarget) ||
                !targetSourceMappings.TryGetValue(oldTarget, out var newSource))
            {
                throw new InvalidOperationException($"Windows could not remap the saved metadata for '{target.FriendlyName}'. Recapture this profile.");
            }
            target.AdapterLowPart = newTarget.LowPart;
            target.AdapterHighPart = newTarget.HighPart;
            target.TargetId = newTarget.Id;
            target.SourceAdapterLowPart = newSource.LowPart;
            target.SourceAdapterHighPart = newSource.HighPart;
            target.SourceId = newSource.Id;
        }

        return new DisplaySnapshot
        {
            SnapshotVersion = snapshot.SnapshotVersion,
            IsVerified = snapshot.IsVerified,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            Summary = snapshot.Summary,
            QueryFlags = snapshot.QueryFlags,
            PathStructureSize = snapshot.PathStructureSize,
            ModeStructureSize = snapshot.ModeStructureSize,
            LogicalDisplayCount = snapshot.LogicalDisplayCount,
            ActiveTargets = preparedTargets,
            NvidiaSurround = snapshot.NvidiaSurround,
            Paths = savedPaths.Select(ToBase64).ToList(),
            Modes = savedModes.Select(ToBase64).ToList()
        };
    }

    private static string? GetStableMonitorIdentity(DisplayTargetSnapshot target)
    {
        if (!string.IsNullOrWhiteSpace(target.MonitorDevicePath)) return target.MonitorDevicePath.Trim();
        return target.Identity.StartsWith(@"\\?\DISPLAY#", StringComparison.OrdinalIgnoreCase)
            ? target.Identity.Trim()
            : null;
    }

    private static bool VirtualModeSupportMatches(DisplayConfigPathInfo saved, DisplayConfigPathInfo current) =>
        (saved.Flags & DisplayConfigPathSupportVirtualMode) ==
        (current.Flags & DisplayConfigPathSupportVirtualMode);

    private static HashSet<EndpointKey> Intersect(HashSet<EndpointKey> first,
        HashSet<EndpointKey> second)
    {
        first.IntersectWith(second);
        return first;
    }

    private static bool TryAssignSources(IReadOnlyList<SourceGroupPlan> groups, int index,
        HashSet<EndpointKey> usedSources, Dictionary<EndpointKey, EndpointKey> assignments,
        Dictionary<AdapterKey, Luid> adapterMappings)
    {
        if (index >= groups.Count) return true;
        var group = groups[index];
        var oldAdapter = new AdapterKey(group.OldSource.LowPart, group.OldSource.HighPart);
        var orderedOptions = group.Options
            .OrderByDescending(option => option.Id == group.OldSource.Id)
            .ThenBy(option => option.HighPart)
            .ThenBy(option => option.LowPart)
            .ThenBy(option => option.Id);

        foreach (var option in orderedOptions)
        {
            if (!usedSources.Add(option)) continue;
            var addedAdapter = false;
            if (adapterMappings.TryGetValue(oldAdapter, out var mappedAdapter))
            {
                if (mappedAdapter.LowPart != option.LowPart || mappedAdapter.HighPart != option.HighPart)
                {
                    usedSources.Remove(option);
                    continue;
                }
            }
            else
            {
                adapterMappings[oldAdapter] = new Luid
                { LowPart = option.LowPart, HighPart = option.HighPart };
                addedAdapter = true;
            }

            assignments[group.OldSource] = option;
            if (TryAssignSources(groups, index + 1, usedSources, assignments, adapterMappings)) return true;
            assignments.Remove(group.OldSource);
            if (addedAdapter) adapterMappings.Remove(oldAdapter);
            usedSources.Remove(option);
        }
        return false;
    }

    private static void ValidatePathModeReferences(DisplayConfigPathInfo path,
        DisplayConfigModeInfo[] modes, ISet<uint>? referencedModeIndexes = null,
        bool requireSourceAndTargetModes = false)
    {
        var sourceEndpoint = EndpointKey.From(path.SourceInfo.AdapterId, path.SourceInfo.Id);
        var targetEndpoint = EndpointKey.From(path.TargetInfo.AdapterId, path.TargetInfo.Id);
        if (UsesPackedModeIndexes(path, modes))
        {
            ValidateModeReference(path.SourceInfo.ModeInfoIdx >> 16, modes,
                DisplayConfigModeInfoTypeSource, sourceEndpoint, referencedModeIndexes,
                requireSourceAndTargetModes);
            ValidateModeReference(path.TargetInfo.ModeInfoIdx >> 16, modes,
                DisplayConfigModeInfoTypeTarget, targetEndpoint, referencedModeIndexes,
                requireSourceAndTargetModes);
            ValidateModeReference(path.TargetInfo.ModeInfoIdx & 0xffff, modes,
                DisplayConfigModeInfoTypeDesktopImage, targetEndpoint, referencedModeIndexes);
            return;
        }

        ValidateModeReference(path.SourceInfo.ModeInfoIdx, modes,
            DisplayConfigModeInfoTypeSource, sourceEndpoint, referencedModeIndexes,
            requireSourceAndTargetModes);
        ValidateModeReference(path.TargetInfo.ModeInfoIdx, modes,
            DisplayConfigModeInfoTypeTarget, targetEndpoint, referencedModeIndexes,
            requireSourceAndTargetModes);
    }

    private static bool UsesPackedModeIndexes(DisplayConfigPathInfo path, DisplayConfigModeInfo[] modes)
    {
        if ((path.Flags & DisplayConfigPathSupportVirtualMode) != 0) return true;

        var sourceEndpoint = EndpointKey.From(path.SourceInfo.AdapterId, path.SourceInfo.Id);
        var targetEndpoint = EndpointKey.From(path.TargetInfo.AdapterId, path.TargetInfo.Id);
        var directReferencesMatch =
            ModeReferenceMatches(path.SourceInfo.ModeInfoIdx, modes,
                DisplayConfigModeInfoTypeSource, sourceEndpoint) &&
            ModeReferenceMatches(path.TargetInfo.ModeInfoIdx, modes,
                DisplayConfigModeInfoTypeTarget, targetEndpoint);
        if (directReferencesMatch) return false;

        // Some NVIDIA Surround drivers return the virtual-aware packed union
        // while omitting DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE. Only accept
        // that representation when both packed indexes resolve to the exact
        // expected mode types and endpoints; malformed snapshots still fail.
        return ModeReferenceMatches(path.SourceInfo.ModeInfoIdx >> 16, modes,
                   DisplayConfigModeInfoTypeSource, sourceEndpoint) &&
               ModeReferenceMatches(path.TargetInfo.ModeInfoIdx >> 16, modes,
                   DisplayConfigModeInfoTypeTarget, targetEndpoint);
    }

    private static bool ModeReferenceMatches(uint modeIndex, DisplayConfigModeInfo[] modes,
        uint expectedType, EndpointKey expectedEndpoint)
    {
        if (modeIndex == InvalidModeIndex || modeIndex == 0xffff || modeIndex >= modes.Length)
            return false;
        var mode = modes[modeIndex];
        return mode.InfoType == expectedType &&
               EndpointKey.From(mode.AdapterId, mode.Id) == expectedEndpoint;
    }

    private static void ValidateModeReference(uint modeIndex, DisplayConfigModeInfo[] modes,
        uint expectedType, EndpointKey expectedEndpoint, ISet<uint>? referencedModeIndexes = null,
        bool required = false)
    {
        var invalidIndex = modeIndex == InvalidModeIndex || modeIndex == 0xffff;
        if (invalidIndex)
        {
            if (required)
                throw new InvalidOperationException(
                    "A display snapshot is missing source or target mode information. Recapture this profile.");
            return;
        }
        if (modeIndex >= modes.Length)
            throw new InvalidOperationException("A display snapshot contains an invalid mode-array index. Recapture this profile.");
        var mode = modes[modeIndex];
        if (mode.InfoType != expectedType || EndpointKey.From(mode.AdapterId, mode.Id) != expectedEndpoint)
            throw new InvalidOperationException("A display snapshot contains inconsistent path and mode information. Recapture this profile.");
        referencedModeIndexes?.Add(modeIndex);
    }

    private static DisplayTargetSnapshot CloneTarget(DisplayTargetSnapshot target) => new()
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
    };

    private static void AddConsistentMapping(Dictionary<EndpointKey, EndpointKey> mappings,
        EndpointKey oldEndpoint, EndpointKey newEndpoint, string displayName)
    {
        if (mappings.TryGetValue(oldEndpoint, out var existing) && existing != newEndpoint)
            throw new InvalidOperationException($"Windows returned conflicting paths for '{displayName}'. Recapture this profile.");
        mappings[oldEndpoint] = newEndpoint;
    }

    private static void AddConsistentAdapterMapping(Dictionary<AdapterKey, Luid> mappings,
        Luid oldAdapter, Luid newAdapter)
    {
        var oldKey = AdapterKey.From(oldAdapter);
        if (mappings.TryGetValue(oldKey, out var existing) &&
            (existing.LowPart != newAdapter.LowPart || existing.HighPart != newAdapter.HighPart))
            throw new InvalidOperationException("A saved adapter maps to more than one current display adapter. Recapture this profile.");
        mappings[oldKey] = newAdapter;
    }

    private static DisplayTargetSnapshot CreateTargetSnapshot(DisplayConfigPathInfo path,
        DisplayConfigModeInfo[] modes, int pathIndex)
    {
        var targetName = GetTargetDeviceName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
        var devicePath = targetName?.MonitorDevicePath?.Trim() ?? string.Empty;
        var identity = !string.IsNullOrWhiteSpace(devicePath)
            ? devicePath.ToUpperInvariant()
            : $"{path.TargetInfo.AdapterId.HighPart:X8}:{path.TargetInfo.AdapterId.LowPart:X8}:{path.TargetInfo.Id}";
        var packedModeIndexes = UsesPackedModeIndexes(path, modes);
        var sourceMode = GetMode(path.SourceInfo.ModeInfoIdx, packedModeIndexes, modes,
            DisplayConfigModeInfoTypeSource);
        var targetMode = GetMode(path.TargetInfo.ModeInfoIdx, packedModeIndexes, modes,
            DisplayConfigModeInfoTypeTarget);
        return new DisplayTargetSnapshot
        {
            Identity = identity,
            FriendlyName = string.IsNullOrWhiteSpace(targetName?.MonitorFriendlyDeviceName)
                ? $"Display target {path.TargetInfo.Id}"
                : targetName.Value.MonitorFriendlyDeviceName.Trim(),
            MonitorDevicePath = devicePath,
            AdapterLowPart = path.TargetInfo.AdapterId.LowPart,
            AdapterHighPart = path.TargetInfo.AdapterId.HighPart,
            TargetId = path.TargetInfo.Id,
            ConnectorInstance = targetName?.ConnectorInstance ?? 0,
            PathIndex = pathIndex,
            SourceAdapterLowPart = path.SourceInfo.AdapterId.LowPart,
            SourceAdapterHighPart = path.SourceInfo.AdapterId.HighPart,
            SourceId = path.SourceInfo.Id,
            SourceX = sourceMode?.ModeInfo.SourceMode.Position.X ?? 0,
            SourceY = sourceMode?.ModeInfo.SourceMode.Position.Y ?? 0,
            SourceWidth = sourceMode?.ModeInfo.SourceMode.Width ?? 0,
            SourceHeight = sourceMode?.ModeInfo.SourceMode.Height ?? 0,
            PixelFormat = sourceMode?.ModeInfo.SourceMode.PixelFormat ?? 0,
            Rotation = path.TargetInfo.Rotation,
            Scaling = path.TargetInfo.Scaling,
            RefreshNumerator = path.TargetInfo.RefreshRate.Numerator,
            RefreshDenominator = path.TargetInfo.RefreshRate.Denominator,
            TargetActiveWidth = targetMode?.ModeInfo.TargetMode.TargetVideoSignalInfo.ActiveSize.Cx ?? 0,
            TargetActiveHeight = targetMode?.ModeInfo.TargetMode.TargetVideoSignalInfo.ActiveSize.Cy ?? 0,
            TargetVSyncNumerator = targetMode?.ModeInfo.TargetMode.TargetVideoSignalInfo.VSyncFreq.Numerator ?? 0,
            TargetVSyncDenominator = targetMode?.ModeInfo.TargetMode.TargetVideoSignalInfo.VSyncFreq.Denominator ?? 0,
            BoostRefreshRate = (path.Flags & DisplayConfigPathBoostRefreshRate) != 0
        };
    }

    private static DisplayConfigModeInfo? GetMode(uint modeIndex, bool packedModeIndexes,
        DisplayConfigModeInfo[] modes, uint expectedType)
    {
        if (packedModeIndexes)
        {
            // With virtual-mode awareness, CCD packs clone/desktop indexes into
            // the low word and the source/target mode-array index into the high word.
            modeIndex >>= 16;
            if (modeIndex == 0xffff) return null;
        }
        else if (modeIndex == InvalidModeIndex) return null;
        if (modeIndex >= modes.Length) return null;
        var mode = modes[modeIndex];
        return mode.InfoType == expectedType ? mode : null;
    }

    private static void CopyLayout(DisplaySnapshot source, DisplaySnapshot destination)
    {
        destination.SnapshotVersion = source.SnapshotVersion;
        destination.CapturedAtUtc = source.CapturedAtUtc;
        destination.Summary = source.Summary;
        destination.QueryFlags = source.QueryFlags;
        destination.PathStructureSize = source.PathStructureSize;
        destination.ModeStructureSize = source.ModeStructureSize;
        destination.LogicalDisplayCount = source.LogicalDisplayCount;
        destination.ActiveTargets = source.ActiveTargets;
        destination.NvidiaSurround = source.NvidiaSurround;
        destination.Paths = source.Paths;
        destination.Modes = source.Modes;
    }

    private static DisplayConfigTargetDeviceName? GetTargetDeviceName(Luid adapterId, uint targetId)
    {
        var name = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };
        return DisplayConfigGetDeviceInfo(ref name) == 0 ? name : null;
    }

    private static (DisplayConfigPathInfo[] Paths, DisplayConfigModeInfo[] Modes) QueryConfiguration(uint flags)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            ThrowIfFailed(GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount));
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var result = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ErrorInsufficientBuffer) continue;
            ThrowIfFailed(result);
            return (paths.Take((int)pathCount).ToArray(), modes.Take((int)modeCount).ToArray());
        }
        throw new InvalidOperationException("The connected displays changed while reading the layout. Try again.");
    }

    private static uint GetActiveQueryFlags() => QdcOnlyActivePaths | GetAwarenessQueryFlags();

    private static uint GetAwarenessQueryFlags()
    {
        var flags = QdcVirtualModeAware;
        if (Environment.OSVersion.Version.Build >= 22000) flags |= QdcVirtualRefreshRateAware;
        return flags;
    }

    private static uint GetSetAwarenessFlags(DisplaySnapshot snapshot)
    {
        if (snapshot.SnapshotVersion < 2 || (snapshot.QueryFlags & QdcVirtualModeAware) == 0) return 0;
        var flags = SdcVirtualModeAware;
        if ((snapshot.QueryFlags & QdcVirtualRefreshRateAware) != 0) flags |= SdcVirtualRefreshRateAware;
        return flags;
    }

    internal static void ValidateSnapshotStructures(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SnapshotVersion is < 1 or > 3)
            throw new InvalidOperationException("This display snapshot was created by an unsupported Sherpa Manager version.");
        if (snapshot.Paths is null || snapshot.Modes is null || snapshot.ActiveTargets is null)
            throw new InvalidOperationException("This display snapshot is incomplete.");
        if (snapshot.Paths.Count == 0 || snapshot.Modes.Count == 0)
            throw new InvalidOperationException("This display snapshot is empty.");
        if (snapshot.PathStructureSize != Marshal.SizeOf<DisplayConfigPathInfo>() ||
            snapshot.ModeStructureSize != Marshal.SizeOf<DisplayConfigModeInfo>())
            throw new InvalidOperationException("This display snapshot was created by an incompatible Sherpa Manager version. Capture it again.");

        DisplayConfigPathInfo[] paths;
        DisplayConfigModeInfo[] modes;
        try
        {
            paths = snapshot.Paths.Select(FromBase64<DisplayConfigPathInfo>).ToArray();
            modes = snapshot.Modes.Select(FromBase64<DisplayConfigModeInfo>).ToArray();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidOperationException("This display snapshot contains invalid encoded display data.", exception);
        }

        var referencedModeIndexes = new HashSet<uint>();
        var targetEndpoints = new HashSet<EndpointKey>();
        foreach (var path in paths)
        {
            if ((path.Flags & DisplayConfigPathActive) == 0)
                throw new InvalidOperationException(
                    "A display snapshot contains an inactive path. Capture the active layout again.");
            var targetEndpoint = EndpointKey.From(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            if (!targetEndpoints.Add(targetEndpoint))
                throw new InvalidOperationException(
                    "A display snapshot contains the same active target more than once. Recapture this profile.");
            ValidatePathModeReferences(path, modes, referencedModeIndexes,
                requireSourceAndTargetModes: true);
        }

        var modeEndpoints = new HashSet<ModeEndpointKey>();
        for (var index = 0; index < modes.Length; index++)
        {
            var mode = modes[index];
            if (mode.InfoType is not (DisplayConfigModeInfoTypeSource or
                DisplayConfigModeInfoTypeTarget or DisplayConfigModeInfoTypeDesktopImage))
                throw new InvalidOperationException(
                    "A display snapshot contains an unsupported mode-information type. Recapture this profile.");
            var key = new ModeEndpointKey(mode.InfoType, EndpointKey.From(mode.AdapterId, mode.Id));
            if (!modeEndpoints.Add(key))
                throw new InvalidOperationException(
                    "A display snapshot contains duplicate mode information. Recapture this profile.");
            if (!referencedModeIndexes.Contains((uint)index))
                throw new InvalidOperationException(
                    "A display snapshot contains mode information that is not referenced by an active path. Recapture this profile.");
        }

        if (snapshot.ActiveTargets.Count == 0)
        {
            if (snapshot.SnapshotVersion >= 3)
                throw new InvalidOperationException(
                    "This display snapshot has no stable monitor metadata. Recapture this profile.");
            return;
        }
        if (snapshot.ActiveTargets.Count != paths.Length)
            throw new InvalidOperationException(
                "A display snapshot has inconsistent path and monitor metadata. Recapture this profile.");

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < paths.Length; index++)
        {
            var target = snapshot.ActiveTargets[index];
            if (target is null)
                throw new InvalidOperationException(
                    "A display snapshot contains empty monitor metadata. Recapture this profile.");
            var path = paths[index];
            if (target.PathIndex != index ||
                target.AdapterLowPart != path.TargetInfo.AdapterId.LowPart ||
                target.AdapterHighPart != path.TargetInfo.AdapterId.HighPart ||
                target.TargetId != path.TargetInfo.Id)
                throw new InvalidOperationException(
                    "A display snapshot has inconsistent target metadata. Recapture this profile.");
            if (snapshot.SnapshotVersion >= 3 &&
                (target.SourceAdapterLowPart != path.SourceInfo.AdapterId.LowPart ||
                 target.SourceAdapterHighPart != path.SourceInfo.AdapterId.HighPart ||
                 target.SourceId != path.SourceInfo.Id))
                throw new InvalidOperationException(
                    "A display snapshot has inconsistent source metadata. Recapture this profile.");

            var identity = GetStableMonitorIdentity(target) ?? target.Identity?.Trim();
            if (string.IsNullOrWhiteSpace(identity) || !identities.Add(identity))
                throw new InvalidOperationException(
                    "A display snapshot does not uniquely identify every monitor. Recapture this profile.");
        }
    }

    private static string ToBase64<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return Convert.ToBase64String(bytes);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static T FromBase64<T>(string value) where T : struct
    {
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length != Marshal.SizeOf<T>()) throw new InvalidOperationException("A display snapshot contains invalid data.");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return Marshal.PtrToStructure<T>(pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != 0) throw new Win32Exception(result);
    }

    public void Dispose()
    {
        if (_nvidiaSurround is IDisposable disposable) disposable.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray, ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray, uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[] modeInfoArray, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    private readonly record struct EndpointKey(uint LowPart, int HighPart, uint Id)
    {
        public static EndpointKey From(Luid adapter, uint id) => new(adapter.LowPart, adapter.HighPart, id);
    }

    private readonly record struct ModeEndpointKey(uint Type, EndpointKey Endpoint);

    private readonly record struct AdapterKey(uint LowPart, int HighPart)
    {
        public static AdapterKey From(Luid adapter) => new(adapter.LowPart, adapter.HighPart);
    }

    private readonly record struct CurrentPathCandidate(DisplayConfigPathInfo Path, DisplayTargetSnapshot Target);

    private sealed record SavedPathPlan(
        int Index,
        DisplayConfigPathInfo SavedPath,
        DisplayTargetSnapshot Expected,
        EndpointKey OldTarget,
        EndpointKey OldSource,
        EndpointKey NewTarget,
        IReadOnlyList<CurrentPathCandidate> Candidates);

    private sealed record SourceGroupPlan(
        EndpointKey OldSource,
        IReadOnlyList<SavedPathPlan> Paths,
        IReadOnlyList<EndpointKey> Options);

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion { public uint Cx; public uint Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode { public DisplayConfigVideoSignalInfo TargetVideoSignalInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }
}
