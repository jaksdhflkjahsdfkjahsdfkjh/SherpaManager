using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProfileActivationService(DisplayConfigurationService displays, IProcessService processes)
{
    private readonly SemaphoreSlim _activationLock = new(1, 1);

    public async Task<bool> ActivateAsync(ProfileDocument document, SwitchProfile target,
        Action<string> report, Func<DisplaySnapshot, Task<bool>>? confirmDisplay = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _activationLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another profile switch is already in progress.");

        try
        {
            var targetApps = target.Applications.Where(app => app.Enabled).ToList();
            foreach (var app in targetApps) processes.Validate(app);
            // Invalidate delayed close/minimize work before any awaited display or
            // close operation. A prior watcher must never act on an application the
            // profile being activated now wants to keep.
            foreach (var app in targetApps) processes.CancelPendingClose(app);
            var warnings = new List<string>();

            var previous = document.Profiles.FirstOrDefault(profile => profile.Id == document.ActiveProfileId);
            if (previous is not null && previous.Id != target.Id)
            {
                var closeResult = await ClosePreviousApplicationsAsync(previous, targetApps, report,
                    cancellationToken);
                warnings.AddRange(closeResult.Warnings);
                if (!closeResult.CanContinue)
                {
                    report("The profile switch was cancelled because an application from the current profile is still running.");
                    return false;
                }
            }

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
            }
            else report("Keeping the current display layout.");

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
        finally { _activationLock.Release(); }
    }

    private async Task<PreviousApplicationsCloseResult> ClosePreviousApplicationsAsync(SwitchProfile previous, IReadOnlyCollection<LaunchApplication> targetApps,
        Action<string> report, CancellationToken cancellationToken)
    {
        var targetIdentities = targetApps.Select(processes.GetIdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scheduledIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closeTasks = new List<(LaunchApplication App, Task<ProcessCloseResult> Task)>();
        var warnings = new List<string>();
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
            report($"Closing {app.Name}…");
            closeTasks.Add((app, processes.CloseAsync(app, cancellationToken)));
        }

        foreach (var closeTask in closeTasks)
        {
            var closeResult = await closeTask.Task;
            report(closeResult.Message);
            if (!closeResult.Succeeded)
            {
                warnings.Add(closeResult.Message);
                if (closeResult.Status is ProcessCloseStatus.StillRunning
                    or ProcessCloseStatus.AccessDenied
                    or ProcessCloseStatus.MonitoringFailed)
                    canContinue = false;
            }
        }
        return new PreviousApplicationsCloseResult(warnings, canContinue);
    }

    private static async Task ObserveMinimizationAsync(Task<bool> task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { /* Minimization is best effort and must not abort a profile switch. */ }
    }

    private sealed record PreviousApplicationsCloseResult(
        IReadOnlyCollection<string> Warnings,
        bool CanContinue);
}
