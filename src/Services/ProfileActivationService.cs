using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProfileActivationService(IDisplayConfigurationService displays, IProcessService processes)
{
    private readonly SemaphoreSlim _activationLock = new(1, 1);

    public async Task<bool> ActivateAsync(ProfileDocument document, SwitchProfile target,
        Action<string> report, Func<DisplaySnapshot, Task<bool>>? confirmDisplay = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _activationLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another profile switch is already in progress.");

        SwitchProfile? previous = null;
        IReadOnlyCollection<LaunchApplication> previousApplicationsInitiallyRunning = [];
        var startedTargetApplications = new List<LaunchApplication>();
        var displayApplied = false;
        var applicationTransitionStarted = false;
        try
        {
            var targetApps = target.Applications.Where(app => app.Enabled).ToList();
            foreach (var app in targetApps) processes.Validate(app);
            // Invalidate delayed close/minimize work before any awaited display or
            // close operation. A prior watcher must never act on an application the
            // profile being activated now wants to keep.
            foreach (var app in targetApps) processes.CancelPendingClose(app);
            var warnings = new List<string>();

            previous = document.Profiles.FirstOrDefault(profile => profile.Id == document.ActiveProfileId);
            previousApplicationsInitiallyRunning = previous is not null && previous.Id != target.Id
                ? FindRunningPreviousApplications(previous, targetApps)
                : [];
            if (target.Display is not null)
            {
                report("Applying display layout…");
                var needsConfirmation = document.Settings.ConfirmDisplayChanges &&
                                        !target.Display.IsVerified && confirmDisplay is not null;
                var displayResult = needsConfirmation
                    ? await displays.RestoreAsync(target.Display, target.NvidiaSurroundMode, confirmDisplay!, cancellationToken)
                    : await displays.RestoreAsync(target.Display, target.NvidiaSurroundMode, cancellationToken);
                report(displayResult.Message);
                if (!displayResult.Kept)
                {
                    report("The display test was reverted; profile applications were not started.");
                    return false;
                }
                if (needsConfirmation) target.Display.IsVerified = true;
                displayApplied = true;
            }
            else report("Keeping the current display layout.");

            if (previous is not null && previous.Id != target.Id)
            {
                applicationTransitionStarted = true;
                var closeResult = await ClosePreviousApplicationsAsync(previous, targetApps, report,
                    cancellationToken);

                warnings.AddRange(closeResult.Warnings);
                if (!closeResult.CanContinue)
                {
                    report("The profile switch was cancelled because an application from the current profile is still running. Restoring the previous profile…");
                    var applicationsToRestart = previousApplicationsInitiallyRunning
                        .Concat(closeResult.ApplicationsToRestart).ToList();
                    await CompensateFailedSwitchAsync(previous, applicationsToRestart, [], displayApplied, report);
                    return false;
                }
            }

            applicationTransitionStarted = true;
            var startedIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in targetApps)
            {
                try
                {
                    var identity = processes.GetIdentityKey(app);
                    if (!startedIdentities.Add(identity))
                    {
                        report($"Skipped duplicate entry {app.Name}.");
                        continue;
                    }

                    processes.CancelPendingClose(app);

                    if (processes.IsRunning(app))
                    {
                        if (app.StartMinimized)
                        {
                            report($"{app.Name} is already running; minimizing its window…");
                            var resolved = processes.Resolve(app);
                            var timeout = resolved.IsShortcutOrProtocol || resolved.ManagedExecutablePaths is { Count: > 1 }
                                ? TimeSpan.FromSeconds(45)
                                : TimeSpan.FromSeconds(15);
                            _ = ObserveMinimizationAsync(processes.MinimizeAsync(app, timeout, cancellationToken));
                        }
                        else report($"{app.Name} is already running.");
                        continue;
                    }

                    if (app.LaunchDelayMs > 0) await Task.Delay(app.LaunchDelayMs, cancellationToken);
                    report($"Starting {app.Name}…");
                    var launchResult = await processes.LaunchAsync(app, cancellationToken);
                    report(launchResult.Message);
                    if (launchResult.Started) startedTargetApplications.Add(app);
                    if (launchResult.HasWarning ||
                        (app.StartMinimized && !launchResult.Minimized && !launchResult.MinimizationPending))
                        warnings.Add(launchResult.Message);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    var warning = $"Could not start or manage {app.Name} ({app.Path}): {exception.Message}";
                    report(warning);
                    warnings.Add(warning);
                }
            }

            target.LastActivatedUtc = DateTime.UtcNow;
            document.ActiveProfileId = target.Id;
            report(warnings.Count == 0
                ? $"{target.Name} is ready."
                : $"{target.Name} is active with {warnings.Count} warning{(warnings.Count == 1 ? string.Empty : "s")}: {string.Join(" ", warnings)}");
            return true;
        }
        catch (OperationCanceledException)
        {
            if (applicationTransitionStarted)
                await CompensateFailedSwitchAsync(previous, previousApplicationsInitiallyRunning,
                    startedTargetApplications, displayApplied, report);
            report("Profile switch cancelled; the previous state was restored.");
            throw;
        }
        finally { _activationLock.Release(); }
    }

    private IReadOnlyCollection<LaunchApplication> FindRunningPreviousApplications(SwitchProfile previous,
        IReadOnlyCollection<LaunchApplication> targetApps)
    {
        var targetIdentities = targetApps.Select(processes.GetIdentityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runningIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var running = new List<LaunchApplication>();
        foreach (var app in previous.Applications.Where(app => app.Enabled && app.CloseOnDeactivate))
        {
            try
            {
                var identity = processes.GetIdentityKey(app);
                if (targetIdentities.Contains(identity) || !runningIdentities.Add(identity) ||
                    !processes.IsRunning(app))
                    continue;
                running.Add(app);
            }
            catch
            {
                // CloseAsync will report malformed or inaccessible entries later.
            }
        }
        return running;
    }

    private async Task<PreviousApplicationsCloseResult> ClosePreviousApplicationsAsync(SwitchProfile previous, IReadOnlyCollection<LaunchApplication> targetApps,
        Action<string> report, CancellationToken cancellationToken)
    {
        var targetIdentities = targetApps.Select(processes.GetIdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scheduledIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closeTasks = new List<(LaunchApplication App, bool WasRunning, Task<ProcessCloseResult> Task)>();
        var warnings = new List<string>();
        var applicationsToRestart = new List<LaunchApplication>();
        var canContinue = true;
        foreach (var app in previous.Applications.Where(app => app.Enabled && app.CloseOnDeactivate))
        {
            string previousIdentity;
            try { previousIdentity = processes.GetIdentityKey(app); }
            catch
            {
                var warning = $"Could not identify {app.Name}; its close step was skipped.";
                report(warning);
                warnings.Add(warning);
                continue;
            }

            if (targetIdentities.Contains(previousIdentity) || !scheduledIdentities.Add(previousIdentity)) continue;
            var wasRunning = false;
            try { wasRunning = processes.IsRunning(app); }
            catch
            {
                // CloseAsync still provides the authoritative result. Failure to
                // inspect the initial state must not prevent a normal close attempt.
            }
            report($"Closing {app.Name}…");
            closeTasks.Add((app, wasRunning, processes.CloseAsync(app, cancellationToken)));
        }

        foreach (var closeTask in closeTasks)
        {
            ProcessCloseResult closeResult;
            try { closeResult = await closeTask.Task; }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                closeResult = new ProcessCloseResult(ProcessCloseStatus.MonitoringFailed, 0,
                    $"Could not close {closeTask.App.Name}: {exception.Message}");
            }
            report(closeResult.Message);
            if ((closeTask.WasRunning || closeResult.MatchedCount > 0) &&
                closeResult.Status is ProcessCloseStatus.ClosedGracefully
                    or ProcessCloseStatus.ForcedClosed
                    or ProcessCloseStatus.CloseScheduled)
                applicationsToRestart.Add(closeTask.App);
            if (!closeResult.Succeeded)
            {
                warnings.Add(closeResult.Message);
                if (closeResult.Status is ProcessCloseStatus.StillRunning
                    or ProcessCloseStatus.AccessDenied
                    or ProcessCloseStatus.MonitoringFailed)
                    canContinue = false;
            }
        }
        return new PreviousApplicationsCloseResult(warnings, applicationsToRestart, canContinue);
    }

    private async Task CompensateFailedSwitchAsync(SwitchProfile? previous,
        IReadOnlyCollection<LaunchApplication> knownClosedApplications,
        IReadOnlyCollection<LaunchApplication> startedTargetApplications, bool displayApplied,
        Action<string> report)
    {
        var recoveryFailures = new List<string>();
        var previousApplications = (previous?.Applications ?? [])
            .Where(app => app.Enabled && app.CloseOnDeactivate)
            .ToList();

        foreach (var app in startedTargetApplications.Reverse())
        {
            try
            {
                processes.CancelPendingClose(app);
                if (!processes.IsRunning(app)) continue;
                report($"Closing {app.Name} from the cancelled profile…");
                var closeResult = await processes.CloseAsync(app, CancellationToken.None);
                report(closeResult.Message);
                if (!closeResult.Succeeded)
                    recoveryFailures.Add(closeResult.Message);
            }
            catch (Exception exception)
            {
                recoveryFailures.Add($"Could not close {app.Name} from the cancelled profile: {exception.Message}");
            }
        }

        // Stop delayed close observers before checking or relaunching anything.
        // Otherwise a watcher from the abandoned switch could close a recovered app.
        foreach (var app in previousApplications)
        {
            try { processes.CancelPendingClose(app); }
            catch { /* Recovery continues for the remaining applications. */ }
        }

        if (displayApplied)
        {
            try
            {
                report("Restoring the previous display layout…");
                var displayResult = await displays.RestoreLastRecoveryAsync(CancellationToken.None);
                report(displayResult.Message);
                if (!displayResult.Kept)
                    recoveryFailures.Add("The previous display layout was not kept.");
            }
            catch (Exception exception)
            {
                recoveryFailures.Add($"The previous display layout could not be restored: {exception.Message}");
            }
        }

        var restartIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in knownClosedApplications)
        {
            try
            {
                var identity = processes.GetIdentityKey(app);
                if (!restartIdentities.Add(identity) || processes.IsRunning(app)) continue;
                report($"Restarting {app.Name}…");
                var launchResult = await processes.LaunchAsync(app, CancellationToken.None);
                report(launchResult.Message);
                if (launchResult.HasWarning)
                    recoveryFailures.Add(launchResult.Message);
            }
            catch (Exception exception)
            {
                recoveryFailures.Add($"Could not restart {app.Name}: {exception.Message}");
            }
        }

        if (recoveryFailures.Count > 0)
            throw new InvalidOperationException(
                $"The profile switch stopped, but recovery was incomplete: {string.Join(" ", recoveryFailures)}");

        report("The previous state was restored.");
    }

    private static async Task ObserveMinimizationAsync(Task<bool> task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { /* Minimization is best effort and must not abort a profile switch. */ }
    }

    private sealed record PreviousApplicationsCloseResult(
        IReadOnlyCollection<string> Warnings,
        IReadOnlyCollection<LaunchApplication> ApplicationsToRestart,
        bool CanContinue);
}
