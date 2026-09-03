using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProcessService(LaunchTargetResolver? resolver = null) : IProcessService
{
    private const uint WmClose = 0x0010;
    private const int SwMinimize = 6;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly TimeSpan StandardMinimizationObservation = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LauncherSettlingObservation = TimeSpan.FromSeconds(45);
    private readonly LaunchTargetResolver _resolver = resolver ?? new LaunchTargetResolver();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, DateTime>> _trackedProcesses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _processFamilyTrackingDeadlines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _pendingLaunches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingCloseRegistration> _pendingCloseWatchers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingMinimizationRegistration> _pendingMinimizations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _closeGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _identityIntentGates = new(StringComparer.OrdinalIgnoreCase);
    private long _nextCloseGeneration;

    public event Action<PendingProcessCloseOutcome>? PendingCloseCompleted;
    public event Action<PendingProcessMinimizationOutcome>? PendingMinimizationCompleted;

    public ResolvedLaunchTarget Resolve(LaunchApplication app) => _resolver.Resolve(app);

    public void Validate(LaunchApplication app)
    {
        _ = Resolve(app);
        var workingDirectory = Environment.ExpandEnvironmentVariables(app.WorkingDirectory.Trim());
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"The working directory for {app.Name} does not exist: {workingDirectory}");
    }

    public string GetIdentityKey(LaunchApplication app) => Resolve(app).IdentityKey;

    public void CancelPendingClose(LaunchApplication app)
    {
        var identity = Resolve(app).IdentityKey;
        PendingCloseRegistration? registration;
        PendingMinimizationRegistration? minimization;
        lock (GetIntentGate(identity))
        {
            // Invalidate even when the watcher has already published. Its outcome may
            // still be queued on the UI dispatcher when this app becomes desired again.
            _closeGenerations[identity] = NextCloseGeneration();
            _pendingCloseWatchers.TryRemove(identity, out registration);
            _pendingMinimizations.TryRemove(identity, out minimization);
        }
        registration?.Cancel();
        minimization?.Cancel();
    }

    public bool IsPendingCloseOutcomeCurrent(PendingProcessCloseOutcome outcome)
    {
        if (outcome.Generation <= 0 || string.IsNullOrWhiteSpace(outcome.IdentityKey)) return false;
        lock (GetIntentGate(outcome.IdentityKey))
            return _closeGenerations.TryGetValue(outcome.IdentityKey, out var generation) &&
                   generation == outcome.Generation;
    }

    public bool IsRunning(LaunchApplication app)
    {
        var target = Resolve(app);
        var processes = GetMatchingProcesses(app, target);
        try
        {
            if (processes.Any(IsAlive))
            {
                if (HasReachedFinalManagedProcess(processes, target))
                    _pendingLaunches.TryRemove(target.IdentityKey, out _);
                return true;
            }
            return IsPending(target.IdentityKey);
        }
        finally { DisposeAll(processes); }
    }

    public Task<ProcessLaunchResult> LaunchAsync(LaunchApplication app, CancellationToken cancellationToken)
    {
        Validate(app);
        cancellationToken.ThrowIfCancellationRequested();
        var target = Resolve(app);
        var workingDirectory = Environment.ExpandEnvironmentVariables(app.WorkingDirectory.Trim());
        if (string.IsNullOrWhiteSpace(workingDirectory) && File.Exists(target.LaunchPath))
            workingDirectory = Path.GetDirectoryName(target.LaunchPath) ?? string.Empty;

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = target.LaunchPath,
            Arguments = Environment.ExpandEnvironmentVariables(app.Arguments),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = app.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
        });

        if (target.IsShortcutOrProtocol || target.ManagedExecutablePaths is { Count: > 1 })
            _pendingLaunches[target.IdentityKey] = DateTime.UtcNow + TimeSpan.FromSeconds(45);

        if (process is not null)
        {
            if (ProcessMatchesTarget(process, target))
            {
                Track(target.IdentityKey, process);
                BeginProcessFamilyObservation(target.IdentityKey);
            }
            process.Dispose();
        }

        var lifecycleManageable = !string.IsNullOrWhiteSpace(target.ProcessName) ||
                                  _trackedProcesses.ContainsKey(target.IdentityKey);
        if (!lifecycleManageable)
        {
            var unavailableActions = app.StartMinimized ? "detect, minimize, or close" : "detect or close";
            return Task.FromResult(new ProcessLaunchResult(true, false,
                $"Started {app.Name}, but Sherpa cannot {unavailableActions} the application it launches until you set its Process name.",
                LifecycleManageable: false));
        }

        if (!app.StartMinimized)
            return Task.FromResult(new ProcessLaunchResult(true, false, $"Started {app.Name}."));

        // Shell launchers, splash screens, and Qt/Electron applications often create
        // their real window later. Watch the whole settling period without delaying
        // the rest of the profile launch sequence.
        var observation = RequiresLauncherSettling(target)
            ? LauncherSettlingObservation
            : StandardMinimizationObservation;
        _ = ObserveMinimizationAsync(MinimizeAsync(app, observation, cancellationToken));
        return Task.FromResult(new ProcessLaunchResult(true, false,
            $"Started {app.Name}; Sherpa is watching its windows and will report whether they finish minimized.",
            MinimizationPending: true));
    }

    public async Task<bool> MinimizeAsync(LaunchApplication app, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var target = Resolve(app);
        if (string.IsNullOrWhiteSpace(target.ProcessName) &&
            !_trackedProcesses.ContainsKey(target.IdentityKey)) return false;

        var registration = new PendingMinimizationRegistration(cancellationToken);
        PendingMinimizationRegistration? previousRegistration = null;
        lock (GetIntentGate(target.IdentityKey))
        {
            _pendingMinimizations.TryRemove(target.IdentityKey, out previousRegistration);
            _pendingMinimizations[target.IdentityKey] = registration;
        }
        previousRegistration?.Cancel();

        var completed = false;
        var minimized = false;
        string? failureMessage = null;
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            do
            {
                registration.Token.ThrowIfCancellationRequested();
                var processes = GetMatchingProcesses(app, target);
                try
                {
                    var processIds = processes.Where(IsAlive).Select(process => (uint)process.Id).ToHashSet();
                    if (processIds.Count > 0)
                    {
                        if (HasReachedFinalManagedProcess(processes, target))
                            _pendingLaunches.TryRemove(target.IdentityKey, out _);
                        MinimizeVisibleWindows(processIds);
                    }
                }
                finally { DisposeAll(processes); }

                await Task.Delay(250, registration.Token).ConfigureAwait(false);
            } while (DateTime.UtcNow < deadline);

            minimized = await VerifyMinimizedStateAsync(app, target, registration.Token).ConfigureAwait(false);
            completed = true;
            return minimized;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            failureMessage = $"Sherpa could not verify that {app.Name} finished minimized: {exception.Message}";
            throw;
        }
        finally
        {
            var ownsRegistration = RemoveExact(_pendingMinimizations, target.IdentityKey, registration);
            registration.Dispose();
            if (ownsRegistration && !cancellationToken.IsCancellationRequested && (completed || failureMessage is not null))
            {
                PublishPendingMinimizationOutcome(new PendingProcessMinimizationOutcome(
                    app.Id,
                    app.Name,
                    target.IdentityKey,
                    minimized,
                    failureMessage ?? (minimized
                        ? $"{app.Name} finished minimized."
                        : $"{app.Name} started, but Sherpa could not verify a minimized or tray-resident window.")));
            }
        }
    }

    private async Task<bool> VerifyMinimizedStateAsync(LaunchApplication app, ResolvedLaunchTarget target,
        CancellationToken cancellationToken)
    {
        var processes = GetMatchingProcesses(app, target);
        try
        {
            var running = processes.Where(IsAlive).ToList();
            if (running.Count == 0 || !HasReachedFinalManagedProcess(running, target)) return false;
            MinimizeVisibleWindows(running.Select(process => (uint)process.Id).ToHashSet());
        }
        finally { DisposeAll(processes); }

        // ShowWindowAsync posts the state change to the target UI thread. Give it a
        // short dispatch interval, then verify the state instead of reporting that a
        // minimization request was the same as a completed minimization.
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        processes = GetMatchingProcesses(app, target);
        try
        {
            var running = processes.Where(IsAlive).ToList();
            return running.Count > 0 && HasReachedFinalManagedProcess(running, target) &&
                   AreVisibleWindowsMinimized(running.Select(process => (uint)process.Id).ToHashSet());
        }
        finally { DisposeAll(processes); }
    }

    public async Task<ProcessCloseResult> CloseAsync(LaunchApplication app, CancellationToken cancellationToken)
    {
        var target = Resolve(app);
        CancelPendingMinimization(target.IdentityKey);
        var pendingLaunchExpiresAt = GetPendingLaunchExpiration(target.IdentityKey);
        var settlingLaunch = pendingLaunchExpiresAt is not null;
        if (settlingLaunch && !string.IsNullOrWhiteSpace(target.ProcessName))
            SchedulePendingClose(app, target, pendingLaunchExpiresAt!.Value);
        var processes = await GetMatchingProcessesAfterPendingLaunchAsync(app, target, cancellationToken);
        try
        {
            processes = KeepRunningProcesses(processes);
            if (processes.Count == 0)
            {
                if (settlingLaunch && !string.IsNullOrWhiteSpace(target.ProcessName))
                {
                    return new ProcessCloseResult(ProcessCloseStatus.CloseScheduled, 0,
                        $"{app.Name} is still launching. Sherpa is monitoring for its process, but closure is not confirmed yet.");
                }
                Forget(app, target);
                return new ProcessCloseResult(ProcessCloseStatus.NotRunning, 0, $"{app.Name} was not running.");
            }

            var processIds = processes.Select(process => (uint)process.Id).ToHashSet();
            var exited = await RequestCloseUntilExitedAsync(processes, processIds,
                TimeSpan.FromSeconds(6), cancellationToken);
            if (exited)
            {
                if (settlingLaunch)
                {
                    // The close intent ended the process which owned this launch. Keep
                    // the watcher alive to catch an already-queued child, but do not let
                    // a stale pending flag suppress an intentional reactivation.
                    RemoveExact(_pendingLaunches, target.IdentityKey, pendingLaunchExpiresAt!.Value);
                    return new ProcessCloseResult(ProcessCloseStatus.CloseScheduled, processIds.Count,
                        $"Closed the current {app.Name} launcher and started monitoring for a delayed app process; closure is not confirmed yet.");
                }
                Forget(app, target);
                return new ProcessCloseResult(ProcessCloseStatus.ClosedGracefully, processIds.Count, $"Closed {app.Name}.");
            }

            if (!app.ForceCloseAfterTimeout)
            {
                if (settlingLaunch && _pendingCloseWatchers.TryGetValue(target.IdentityKey, out var watcher))
                    watcher.MarkSynchronousFailureReported();
                return new ProcessCloseResult(ProcessCloseStatus.StillRunning, processIds.Count,
                    $"{app.Name} ignored the normal close request.");
            }

            var forced = await ForceProcessesAsync(app, target, processes, cancellationToken);
            if (forced.Succeeded)
            {
                if (settlingLaunch)
                    RemoveExact(_pendingLaunches, target.IdentityKey, pendingLaunchExpiresAt!.Value);
                else
                    Forget(app, target);
            }
            return forced;
        }
        finally { DisposeAll(processes); }
    }

    public async Task<ProcessCloseResult> ForceCloseAsync(LaunchApplication app, CancellationToken cancellationToken)
    {
        var target = Resolve(app);
        CancelPendingMinimization(target.IdentityKey);
        var pendingLaunchExpiresAt = GetPendingLaunchExpiration(target.IdentityKey);
        if (_pendingCloseWatchers.TryGetValue(target.IdentityKey, out var watcher))
            watcher.UpgradeToForceClose();
        var processes = await GetMatchingProcessesAfterPendingLaunchAsync(app, target, cancellationToken);
        try
        {
            processes = KeepRunningProcesses(processes);
            if (processes.Count == 0)
            {
                Forget(app, target);
                return new ProcessCloseResult(ProcessCloseStatus.NotRunning, 0, $"{app.Name} was not running.");
            }

            var result = await ForceProcessesAsync(app, target, processes, cancellationToken);
            if (result.Succeeded)
            {
                if (pendingLaunchExpiresAt is not null)
                    RemoveExact(_pendingLaunches, target.IdentityKey, pendingLaunchExpiresAt.Value);
                if (!_pendingCloseWatchers.ContainsKey(target.IdentityKey)) Forget(app, target);
            }
            return result;
        }
        finally { DisposeAll(processes); }
    }

    public async Task<ProcessCloseResult> ForceCloseAsync(LaunchApplication app,
        PendingProcessCloseOutcome expectedOutcome, CancellationToken cancellationToken)
    {
        var target = Resolve(app);
        if (app.Id != expectedOutcome.ApplicationId ||
            !target.IdentityKey.Equals(expectedOutcome.IdentityKey, StringComparison.OrdinalIgnoreCase))
            return SupersededForceCloseResult(app);

        if (!IsPendingCloseOutcomeCurrent(expectedOutcome)) return SupersededForceCloseResult(app);

        var processes = await GetMatchingProcessesAfterPendingLaunchAsync(app, target, cancellationToken);
        try
        {
            processes = KeepRunningProcesses(processes);
            ForceAttempt attempt;
            PendingMinimizationRegistration? minimization;
            lock (GetIntentGate(target.IdentityKey))
            {
                if (!_closeGenerations.TryGetValue(target.IdentityKey, out var generation) ||
                    generation != expectedOutcome.Generation)
                    return SupersededForceCloseResult(app);

                // Consume the event generation before touching the process. A profile
                // reactivation that wins this lock invalidates the event; if this close
                // wins, its process termination belongs to the still-current intent.
                _closeGenerations[target.IdentityKey] = NextCloseGeneration();
                _pendingMinimizations.TryRemove(target.IdentityKey, out minimization);
                if (processes.Count == 0)
                {
                    Forget(app, target);
                    return new ProcessCloseResult(ProcessCloseStatus.NotRunning, 0, $"{app.Name} was not running.");
                }
                attempt = BeginForceProcesses(app, target, processes);
            }

            minimization?.Cancel();
            return await CompleteForceAttemptAsync(app, processes, attempt, cancellationToken);
        }
        finally { DisposeAll(processes); }
    }

    private async Task<ProcessCloseResult> ForceProcessesAsync(LaunchApplication app, ResolvedLaunchTarget target,
        IReadOnlyCollection<Process> processes, CancellationToken cancellationToken)
    {
        var attempt = BeginForceProcesses(app, target, processes);
        return await CompleteForceAttemptAsync(app, processes, attempt, cancellationToken);
    }

    private ForceAttempt BeginForceProcesses(LaunchApplication app, ResolvedLaunchTarget target,
        IReadOnlyCollection<Process> processes)
    {
        var processIds = processes.Where(IsAlive).Select(process => (uint)process.Id).ToHashSet();
        var accessDenied = false;
        var attempted = false;
        foreach (var process in processes.Where(IsAlive))
        {
            // Implicit executable matching is canonical-path based. A user-entered
            // Process name is the only deliberately broad matching mode.
            if (!ProcessMatchesTarget(process, target) && !IsTracked(target.IdentityKey, process)) continue;
            try
            {
                process.Kill(entireProcessTree: true);
                attempted = true;
            }
            catch (Win32Exception) { accessDenied = true; }
            catch (InvalidOperationException) { /* It exited between checks. */ }
        }
        return new ForceAttempt(processIds, attempted, accessDenied);
    }

    private static async Task<ProcessCloseResult> CompleteForceAttemptAsync(LaunchApplication app,
        IReadOnlyCollection<Process> processes, ForceAttempt attempt, CancellationToken cancellationToken)
    {
        var exited = await WaitForProcessesToExitAsync(processes, TimeSpan.FromSeconds(4), cancellationToken);
        if (exited)
            return new ProcessCloseResult(ProcessCloseStatus.ForcedClosed, attempt.ProcessIds.Count,
                $"Force-closed {app.Name} after it ignored the normal close request.");
        return new ProcessCloseResult(attempt.AccessDenied ? ProcessCloseStatus.AccessDenied : ProcessCloseStatus.StillRunning,
            attempt.ProcessIds.Count, attempt.AccessDenied
                ? $"Windows denied permission to close {app.Name}."
                : $"{app.Name} is still running after the force-close attempt.");
    }

    private static ProcessCloseResult SupersededForceCloseResult(LaunchApplication app) =>
        new(ProcessCloseStatus.Superseded, 0,
            $"Skipped an obsolete force-close request for {app.Name} because the application is wanted again.");

    private async Task<List<Process>> GetMatchingProcessesAfterPendingLaunchAsync(LaunchApplication app,
        ResolvedLaunchTarget target, CancellationToken cancellationToken)
    {
        var processes = GetMatchingProcesses(app, target);
        if (processes.Any(IsAlive) || !IsPending(target.IdentityKey)) return processes;
        DisposeAll(processes);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            processes = GetMatchingProcesses(app, target);
            if (processes.Any(IsAlive)) return processes;
            DisposeAll(processes);
        } while (DateTime.UtcNow < deadline);

        return [];
    }

    private static async Task ObserveMinimizationAsync(Task<bool> task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { /* Best-effort window management must not crash the app. */ }
    }

    private List<Process> GetMatchingProcesses(LaunchApplication app, ResolvedLaunchTarget target)
    {
        TrackDescendantProcesses(target.IdentityKey);
        var byId = new Dictionary<int, Process>();
        var exactPathCouldNotBeChecked = false;
        if (!string.IsNullOrWhiteSpace(target.ProcessName))
        {
            foreach (var process in Process.GetProcessesByName(target.ProcessName))
            {
                var match = GetProcessTargetMatch(process, target);
                if (match == ProcessTargetMatch.Yes && byId.TryAdd(process.Id, process)) continue;
                if (match == ProcessTargetMatch.Unknown) exactPathCouldNotBeChecked = true;
                process.Dispose();
            }
        }

        if (_trackedProcesses.TryGetValue(target.IdentityKey, out var tracked))
        {
            foreach (var item in tracked)
            {
                try
                {
                    var process = Process.GetProcessById(item.Key);
                    if (process.StartTime.ToUniversalTime() == item.Value)
                    {
                        if (!byId.TryAdd(process.Id, process)) process.Dispose();
                    }
                    else process.Dispose();
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        if (exactPathCouldNotBeChecked && byId.Count == 0)
            throw new UnauthorizedAccessException(
                $"Windows would not allow Sherpa to verify the executable path for a running {target.ProcessName} process. Run Sherpa with the same administrator setting as that application.");
        return byId.Values.ToList();
    }

    private static bool ProcessMatchesTarget(Process process, ResolvedLaunchTarget target) =>
        GetProcessTargetMatch(process, target) == ProcessTargetMatch.Yes;

    private static ProcessTargetMatch GetProcessTargetMatch(Process process, ResolvedLaunchTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.ProcessName)) return ProcessTargetMatch.No;
        try
        {
            if (!process.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase))
                return ProcessTargetMatch.No;
        }
        catch (InvalidOperationException) { return ProcessTargetMatch.No; }
        catch (Win32Exception) { return ProcessTargetMatch.Unknown; }
        if (target.HasExplicitProcessName || string.IsNullOrWhiteSpace(target.ExecutablePath))
            return ProcessTargetMatch.Yes;
        var processPath = TryGetProcessImagePath(process, out var accessDenied);
        if (string.IsNullOrWhiteSpace(processPath))
        {
            if (!accessDenied) return ProcessTargetMatch.No;
            try
            {
                // A process that exits between enumeration and OpenProcess can report
                // access denied briefly. Do not turn that normal race into an elevated-
                // process warning or block a launcher handoff.
                if (process.HasExited || process.WaitForExit(25)) return ProcessTargetMatch.No;
            }
            catch (InvalidOperationException) { return ProcessTargetMatch.No; }
            catch (Win32Exception) { }
            return ProcessTargetMatch.Unknown;
        }
        var managedPaths = target.ManagedExecutablePaths ?? [target.ExecutablePath];
        return managedPaths.Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(processPath),
            StringComparison.OrdinalIgnoreCase)) ? ProcessTargetMatch.Yes : ProcessTargetMatch.No;
    }

    private static bool HasReachedFinalManagedProcess(IEnumerable<Process> processes, ResolvedLaunchTarget target)
    {
        if (target.ManagedExecutablePaths is not { Count: > 1 } managedPaths) return true;
        var finalPaths = managedPaths.Skip(1).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return processes.Any(process =>
        {
            var processPath = TryGetProcessImagePath(process);
            return processPath is not null && finalPaths.Contains(Path.GetFullPath(processPath));
        });
    }

    private static string? TryGetProcessImagePath(Process process) => TryGetProcessImagePath(process, out _);

    private static string? TryGetProcessImagePath(Process process, out bool accessDenied)
    {
        accessDenied = false;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Id);
        if (handle == IntPtr.Zero)
        {
            accessDenied = Marshal.GetLastWin32Error() is 5 or 1314;
            return null;
        }
        try
        {
            var capacity = 32768u;
            var path = new StringBuilder((int)capacity);
            if (QueryFullProcessImageName(handle, 0, path, ref capacity)) return path.ToString();
            accessDenied = Marshal.GetLastWin32Error() is 5 or 1314;
            return null;
        }
        finally { CloseHandle(handle); }
    }

    private void Track(string identity, Process process)
    {
        try
        {
            // LaunchAsync is only reached after IsRunning returned false. Start a
            // fresh family so stale PIDs from an application that exited on its own
            // cannot be mistaken for parents after Windows recycles those IDs.
            var tracked = new ConcurrentDictionary<int, DateTime>();
            tracked[process.Id] = process.StartTime.ToUniversalTime();
            _trackedProcesses[identity] = tracked;
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private bool IsTracked(string identity, Process process)
    {
        if (!_trackedProcesses.TryGetValue(identity, out var tracked) ||
            !tracked.TryGetValue(process.Id, out var startTime))
            return false;
        try { return process.StartTime.ToUniversalTime() == startTime; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    private void BeginProcessFamilyObservation(string identity)
    {
        var deadline = DateTime.UtcNow + LauncherSettlingObservation;
        _processFamilyTrackingDeadlines.AddOrUpdate(identity, deadline,
            (_, current) => LaterOf(current, deadline));
        _ = ObserveProcessFamilyAsync(identity);
    }

    private async Task ObserveProcessFamilyAsync(string identity)
    {
        try
        {
            while (_processFamilyTrackingDeadlines.TryGetValue(identity, out var deadline) &&
                   deadline > DateTime.UtcNow && _trackedProcesses.ContainsKey(identity))
            {
                TrackDescendantProcesses(identity);
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
        catch
        {
            // Process-family discovery is an additional lifecycle signal. Exact-path
            // and explicit process-name matching remain available if Windows denies it.
        }
        finally
        {
            if (_processFamilyTrackingDeadlines.TryGetValue(identity, out var deadline) &&
                deadline <= DateTime.UtcNow)
                RemoveExact(_processFamilyTrackingDeadlines, identity, deadline);
        }
    }

    private void TrackDescendantProcesses(string identity)
    {
        if (!_processFamilyTrackingDeadlines.TryGetValue(identity, out var deadline) ||
            deadline <= DateTime.UtcNow ||
            !_trackedProcesses.TryGetValue(identity, out var tracked) || tracked.IsEmpty)
            return;

        var relatedStartTimes = tracked.ToDictionary(item => item.Key, item => item.Value);
        var processTree = GetProcessTreeSnapshot();
        var foundAnotherGeneration = true;
        while (foundAnotherGeneration)
        {
            foundAnotherGeneration = false;
            foreach (var entry in processTree)
            {
                var processId = unchecked((int)entry.ProcessId);
                var parentProcessId = unchecked((int)entry.ParentProcessId);
                if (processId <= 0 || relatedStartTimes.ContainsKey(processId) ||
                    !relatedStartTimes.TryGetValue(parentProcessId, out var parentStartTime))
                    continue;

                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (!IsAlive(process)) continue;
                    var startTime = process.StartTime.ToUniversalTime();
                    // A recycled parent PID must not attach an unrelated, older process
                    // to the application family.
                    if (startTime + TimeSpan.FromSeconds(1) < parentStartTime) continue;
                    tracked.TryAdd(processId, startTime);
                    relatedStartTimes[processId] = startTime;
                    foundAnotherGeneration = true;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
    }

    private static IReadOnlyList<ProcessTreeEntry> GetProcessTreeSnapshot()
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == new IntPtr(-1)) return [];
        try
        {
            var nativeEntry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref nativeEntry)) return [];
            var entries = new List<ProcessTreeEntry>();
            do
            {
                entries.Add(new ProcessTreeEntry(nativeEntry.ProcessId, nativeEntry.ParentProcessId));
                nativeEntry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            } while (Process32Next(snapshot, ref nativeEntry));
            return entries;
        }
        finally { CloseHandle(snapshot); }
    }

    private bool IsPending(string identity)
    {
        return GetPendingLaunchExpiration(identity) is not null;
    }

    private DateTime? GetPendingLaunchExpiration(string identity)
    {
        if (!_pendingLaunches.TryGetValue(identity, out var expiresAt)) return null;
        if (expiresAt > DateTime.UtcNow) return expiresAt;
        RemoveExact(_pendingLaunches, identity, expiresAt);
        return null;
    }

    private void SchedulePendingClose(LaunchApplication app, ResolvedLaunchTarget target, DateTime pendingExpiresAt)
    {
        PendingCloseRegistration registration;
        lock (GetIntentGate(target.IdentityKey))
        {
            if (_pendingCloseWatchers.TryGetValue(target.IdentityKey, out var existing))
            {
                if (app.ForceCloseAfterTimeout) existing.UpgradeToForceClose();
                return;
            }

            var generation = NextCloseGeneration();
            registration = new PendingCloseRegistration(app.ForceCloseAfterTimeout, generation);
            _pendingCloseWatchers[target.IdentityKey] = registration;
            _closeGenerations[target.IdentityKey] = generation;
        }
        var frozenApp = app.Clone();
        frozenApp.Id = app.Id;
        _ = ObservePendingCloseAsync(frozenApp, target, pendingExpiresAt, registration);
    }

    private async Task ObservePendingCloseAsync(LaunchApplication app, ResolvedLaunchTarget target,
        DateTime pendingExpiresAt, PendingCloseRegistration registration)
    {
        var cancellationToken = registration.Token;
        var firstSeen = new Dictionary<int, DateTime>();
        var observedProcessIds = new HashSet<int>();
        var forceAttemptedProcessIds = new HashSet<int>();
        var closeDeadline = DateTime.MinValue;
        var finalManagedProcessSeen = false;
        var accessDenied = false;
        ProcessCloseResult? outcome = null;
        try
        {
            while (DateTime.UtcNow < (finalManagedProcessSeen
                       ? closeDeadline
                       : LaterOf(pendingExpiresAt, closeDeadline)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processes = GetMatchingProcesses(app, target);
                try
                {
                    var running = processes.Where(IsAlive).ToList();
                    if (running.Count > 0)
                    {
                        var now = DateTime.UtcNow;
                        if (HasReachedFinalManagedProcess(running, target))
                        {
                            finalManagedProcessSeen = true;
                            // Once the real child process appears it is no longer a
                            // pending launch. This prevents a quick reactivation from
                            // mistaking stale launch state for a running application.
                            RemoveExact(_pendingLaunches, target.IdentityKey, pendingExpiresAt);
                        }

                        lock (GetIntentGate(target.IdentityKey))
                        {
                            lock (registration)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (!OwnsPendingClose(target.IdentityKey, registration))
                                    throw new OperationCanceledException();
                                RequestCloseForWindows(running.Select(process => (uint)process.Id).ToHashSet());
                                foreach (var process in running)
                                {
                                    observedProcessIds.Add(process.Id);
                                    if (firstSeen.TryAdd(process.Id, now))
                                    {
                                        // Include one extra polling interval so a process
                                        // first observed near expiry always receives the
                                        // complete six-second graceful-close window.
                                        closeDeadline = LaterOf(closeDeadline,
                                            now + TimeSpan.FromMilliseconds(6250));
                                    }
                                    else if (registration.ForceCloseRequested &&
                                             now - firstSeen[process.Id] >= TimeSpan.FromSeconds(6))
                                    {
                                        try
                                        {
                                            process.Kill(entireProcessTree: true);
                                            if (forceAttemptedProcessIds.Add(process.Id))
                                                closeDeadline = LaterOf(closeDeadline,
                                                    now + TimeSpan.FromSeconds(4));
                                        }
                                        catch (Win32Exception) { accessDenied = true; }
                                        catch (InvalidOperationException) { }
                                    }
                                }
                            }
                        }
                    }
                }
                finally { DisposeAll(processes); }
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            if (registration.ForceCloseRequested)
            {
                var remainingProcesses = GetMatchingProcesses(app, target);
                try
                {
                    remainingProcesses = KeepRunningProcesses(remainingProcesses);
                    if (remainingProcesses.Count > 0)
                    {
                        ForceAttempt finalAttempt;
                        lock (GetIntentGate(target.IdentityKey))
                        {
                            lock (registration)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (!OwnsPendingClose(target.IdentityKey, registration))
                                    throw new OperationCanceledException();
                                finalAttempt = BeginForceProcesses(app, target, remainingProcesses);
                            }
                        }
                        if (finalAttempt.Attempted)
                        {
                            foreach (var processId in finalAttempt.ProcessIds)
                                forceAttemptedProcessIds.Add((int)processId);
                        }
                        accessDenied |= finalAttempt.AccessDenied;
                        _ = await CompleteForceAttemptAsync(app, remainingProcesses, finalAttempt, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                finally { DisposeAll(remainingProcesses); }
            }

            RemoveExact(_pendingLaunches, target.IdentityKey, pendingExpiresAt);
            outcome = GetPendingCloseOutcome(app, target, observedProcessIds.Count,
                forceAttemptedProcessIds.Count > 0, accessDenied);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            RemoveExact(_pendingLaunches, target.IdentityKey, pendingExpiresAt);
            outcome = new ProcessCloseResult(ProcessCloseStatus.MonitoringFailed, observedProcessIds.Count,
                $"Sherpa could not finish monitoring {app.Name} for closure: {exception.Message}");
        }
        finally
        {
            var publishOutcome = false;
            lock (GetIntentGate(target.IdentityKey))
            {
                lock (registration)
                {
                    var ownsRegistration = _pendingCloseWatchers.TryGetValue(target.IdentityKey, out var current) &&
                                           ReferenceEquals(current, registration);
                    if (ownsRegistration && !cancellationToken.IsCancellationRequested)
                    {
                        // Remove old tracking before making the watcher unavailable.
                        // A reactivation can then safely register a new process without
                        // this cleanup deleting the new entry.
                        _trackedProcesses.TryRemove(target.IdentityKey, out _);
                        _processFamilyTrackingDeadlines.TryRemove(target.IdentityKey, out _);
                    }
                    var removedOwnRegistration = RemoveExact(
                        _pendingCloseWatchers, target.IdentityKey, registration);
                    publishOutcome = ownsRegistration && removedOwnRegistration &&
                                     !cancellationToken.IsCancellationRequested && outcome is not null;
                    registration.DisposeUnsafe();
                }
            }

            if (publishOutcome) PublishPendingCloseOutcome(new PendingProcessCloseOutcome(
                app.Id, app.Name, target.IdentityKey, outcome!,
                outcome!.Status == ProcessCloseStatus.StillRunning &&
                !registration.ForceCloseRequested && !registration.SynchronousFailureReported,
                registration.Generation));
        }
    }

    private ProcessCloseResult GetPendingCloseOutcome(LaunchApplication app, ResolvedLaunchTarget target,
        int observedCount, bool forceCloseAttempted, bool accessDenied)
    {
        var processes = GetMatchingProcesses(app, target);
        try
        {
            var remainingCount = processes.Count(IsAlive);
            if (remainingCount > 0)
                return new ProcessCloseResult(accessDenied
                        ? ProcessCloseStatus.AccessDenied
                        : ProcessCloseStatus.StillRunning,
                    remainingCount, accessDenied
                        ? $"Windows denied permission to close a delayed {app.Name} process."
                        : $"A delayed {app.Name} process ignored the normal close request and is still running.");
            if (forceCloseAttempted)
                return new ProcessCloseResult(ProcessCloseStatus.ForcedClosed, observedCount,
                    $"Force-closed the delayed {app.Name} process after it ignored the normal close request.");
            if (observedCount > 0)
                return new ProcessCloseResult(ProcessCloseStatus.ClosedGracefully, observedCount,
                    $"Closed the delayed {app.Name} process.");
            return new ProcessCloseResult(ProcessCloseStatus.NotRunning, 0,
                $"No delayed {app.Name} process appeared before close monitoring ended.");
        }
        finally { DisposeAll(processes); }
    }

    private void PublishPendingCloseOutcome(PendingProcessCloseOutcome outcome)
    {
        var handlers = PendingCloseCompleted;
        if (handlers is null) return;
        foreach (Action<PendingProcessCloseOutcome> handler in handlers.GetInvocationList())
        {
            try { handler(outcome); }
            catch { /* One observer must not prevent delivery to the others. */ }
        }
    }

    private void PublishPendingMinimizationOutcome(PendingProcessMinimizationOutcome outcome)
    {
        var handlers = PendingMinimizationCompleted;
        if (handlers is null) return;
        foreach (Action<PendingProcessMinimizationOutcome> handler in handlers.GetInvocationList())
        {
            try { handler(outcome); }
            catch { /* One observer must not prevent delivery to the others. */ }
        }
    }

    private void CancelPendingMinimization(string identity)
    {
        PendingMinimizationRegistration? registration;
        lock (GetIntentGate(identity))
            _pendingMinimizations.TryRemove(identity, out registration);
        registration?.Cancel();
    }

    private object GetIntentGate(string identity) => _identityIntentGates.GetOrAdd(identity, static _ => new object());

    private bool OwnsPendingClose(string identity, PendingCloseRegistration registration) =>
        _pendingCloseWatchers.TryGetValue(identity, out var current) && ReferenceEquals(current, registration) &&
        _closeGenerations.TryGetValue(identity, out var generation) && generation == registration.Generation;

    private long NextCloseGeneration() => Interlocked.Increment(ref _nextCloseGeneration);

    private static bool RequiresLauncherSettling(ResolvedLaunchTarget target) =>
        target.IsShortcutOrProtocol || target.ManagedExecutablePaths is { Count: > 1 };

    private static DateTime LaterOf(DateTime left, DateTime right) => left >= right ? left : right;

    private static bool RemoveExact<T>(ConcurrentDictionary<string, T> dictionary, string key, T value)
        where T : notnull =>
        ((ICollection<KeyValuePair<string, T>>)dictionary).Remove(new KeyValuePair<string, T>(key, value));

    private void Forget(LaunchApplication app, ResolvedLaunchTarget target)
    {
        _trackedProcesses.TryRemove(target.IdentityKey, out _);
        _processFamilyTrackingDeadlines.TryRemove(target.IdentityKey, out _);
        _pendingLaunches.TryRemove(target.IdentityKey, out _);
    }

    private static List<Process> KeepRunningProcesses(IEnumerable<Process> processes)
    {
        var runningProcesses = new List<Process>();
        foreach (var process in processes)
        {
            if (IsAlive(process)) runningProcesses.Add(process);
            else process.Dispose();
        }
        return runningProcesses;
    }

    private static void MinimizeVisibleWindows(HashSet<uint> processIds)
    {
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains(processId) && IsWindowVisible(window) && !IsIconic(window))
                ShowWindowAsync(window, SwMinimize);
            return true;
        }, IntPtr.Zero);
    }

    private static bool AreVisibleWindowsMinimized(HashSet<uint> processIds)
    {
        var minimized = true;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains(processId) && IsWindowVisible(window) && !IsIconic(window))
                minimized = false;
            return true;
        }, IntPtr.Zero);
        return minimized;
    }

    private static void RequestCloseForWindows(HashSet<uint> processIds)
    {
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains(processId)) PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    private static async Task<bool> WaitForProcessesToExitAsync(IEnumerable<Process> processes, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var processList = processes.ToList();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (processList.All(process => !IsAlive(process))) return true;
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        return processList.All(process => !IsAlive(process));
    }

    private static async Task<bool> RequestCloseUntilExitedAsync(IEnumerable<Process> processes,
        HashSet<uint> processIds, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var processList = processes.ToList();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (processList.All(process => !IsAlive(process))) return true;
            RequestCloseForWindows(processIds);
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        return processList.All(process => !IsAlive(process));
    }

    private static bool IsAlive(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; }
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes) process.Dispose();
    }

    private sealed record ForceAttempt(HashSet<uint> ProcessIds, bool Attempted, bool AccessDenied);
    private sealed record ProcessTreeEntry(uint ProcessId, uint ParentProcessId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    private enum ProcessTargetMatch
    {
        No,
        Yes,
        Unknown
    }

    private sealed class PendingCloseRegistration(bool forceCloseRequested, long generation) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _forceCloseRequested = forceCloseRequested ? 1 : 0;
        private int _synchronousFailureReported;

        public CancellationToken Token => _cancellation.Token;
        public bool ForceCloseRequested => Volatile.Read(ref _forceCloseRequested) != 0;
        public bool SynchronousFailureReported => Volatile.Read(ref _synchronousFailureReported) != 0;
        public long Generation { get; } = generation;

        public void UpgradeToForceClose() => Interlocked.Exchange(ref _forceCloseRequested, 1);
        public void MarkSynchronousFailureReported() => Interlocked.Exchange(ref _synchronousFailureReported, 1);

        public void Cancel()
        {
            lock (this)
            {
                try { _cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        public void Dispose()
        {
            lock (this) DisposeUnsafe();
        }

        public void DisposeUnsafe() => _cancellation.Dispose();
    }

    private sealed class PendingMinimizationRegistration(CancellationToken cancellationToken) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        public CancellationToken Token => _cancellation.Token;

        public void Cancel()
        {
            lock (this)
            {
                try { _cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        public void Dispose()
        {
            lock (this) _cancellation.Dispose();
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder executableName,
        ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
