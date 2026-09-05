using System.Diagnostics;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProfileActivationService(IDisplayConfigurationService displays, IProcessService processes,
    IDiagnosticLog? diagnostics = null, IAudioDeviceService? audio = null)
{
    /// <summary>
    /// How long to wait for a monitor's audio endpoint to appear after that
    /// monitor is enabled. Windows registers it well after the display itself is
    /// usable.
    /// </summary>
    private static readonly TimeSpan AudioEndpointWait = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _activationLock = new(1, 1);
    private readonly IDiagnosticLog _diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    public async Task<bool> ActivateAsync(ProfileDocument document, SwitchProfile target,
        Action<string> report, Func<DisplaySnapshot, Task<bool>>? confirmDisplay = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _activationLock.WaitAsync(0, cancellationToken))
        {
            _diagnostics.Write("warning", "activation.rejected", "Another profile switch is already in progress.",
                new Dictionary<string, object?> { ["targetProfileId"] = target.Id });
            throw new InvalidOperationException("Another profile switch is already in progress.");
        }

        var totalDuration = Stopwatch.StartNew();
        var previousStageMilliseconds = 0L;
        var outcome = "failed";
        void LogStage(string stage, IReadOnlyDictionary<string, object?>? data = null)
        {
            var elapsed = totalDuration.ElapsedMilliseconds;
            _diagnostics.Write("info", "activation.stage", stage, data,
                Math.Max(0, elapsed - previousStageMilliseconds));
            previousStageMilliseconds = elapsed;
        }
        _diagnostics.Write("info", "activation.started", data: new Dictionary<string, object?>
        {
            ["targetProfileId"] = target.Id,
            ["applicationCount"] = target.Applications.Count,
            ["hasDisplayLayout"] = target.Display is not null,
            ["surroundMode"] = target.NvidiaSurroundMode,
            ["audioOutputRequested"] = !string.IsNullOrWhiteSpace(target.AudioOutputDeviceId),
            ["audioInputRequested"] = !string.IsNullOrWhiteSpace(target.AudioInputDeviceId),
            ["audioServiceAvailable"] = audio is not null
        });
        SwitchProfile? previous = null;
        IReadOnlyCollection<LaunchApplication> previousApplicationsInitiallyRunning = [];
        var startedTargetApplications = new List<LaunchApplication>();
        var displayApplied = false;
        var applicationTransitionStarted = false;
        try
        {
            var targetApps = target.Applications.Where(app => app.Enabled).ToList();
            foreach (var app in targetApps) processes.Validate(app);
            LogStage("validation.completed", new Dictionary<string, object?>
            {
                ["enabledApplicationCount"] = targetApps.Count
            });
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
                var canConfirmDisplay = document.Settings.ConfirmDisplayChanges && confirmDisplay is not null;
                var displayResult = canConfirmDisplay
                    ? await displays.RestoreAsync(target.Display, target.NvidiaSurroundMode, confirmDisplay!,
                        cancellationToken, confirmOnlyWhenVerificationChanged: true)
                    : await displays.RestoreAsync(target.Display, target.NvidiaSurroundMode, cancellationToken);
                LogStage("display.completed", new Dictionary<string, object?>
                {
                    ["kept"] = displayResult.Kept,
                    ["usedAdjustedModes"] = displayResult.UsedAdjustedModes
                });
                report(displayResult.Message);
                if (!displayResult.Kept)
                {
                    report("The display test was reverted; profile applications were not started.");
                    outcome = "display_reverted";
                    return false;
                }
                displayApplied = true;

                // Windows returns from the topology call before the monitors have
                // finished re-syncing. Starting or closing applications during that
                // window is how they end up on the wrong monitor or the wrong size.
                var settleDelay = document.Settings.DisplaySettleDelayMs;
                if (settleDelay > 0)
                {
                    report($"Waiting {settleDelay / 1000.0:0.#}s for the displays to settle…");
                    await Task.Delay(settleDelay, cancellationToken);
                    LogStage("display.settled", new Dictionary<string, object?>
                    {
                        ["settleDelayMs"] = settleDelay
                    });
                }
            }
            else
            {
                report("Keeping the current display layout.");
                LogStage("display.skipped");
            }

            // Before applications start: many sim and voice applications read the
            // default output once at launch and never look again.
            await ApplyAudioAsync(target, displayApplied, report, warnings, cancellationToken);
            LogStage("audio.completed");

            if (previous is not null && previous.Id != target.Id)
            {
                applicationTransitionStarted = true;
                var closeResult = await ClosePreviousApplicationsAsync(previous, targetApps, report,
                    cancellationToken);
                LogStage("previous_applications.completed", new Dictionary<string, object?>
                {
                    ["canContinue"] = closeResult.CanContinue,
                    ["warningCount"] = closeResult.Warnings.Count
                });

                warnings.AddRange(closeResult.Warnings);
                if (!closeResult.CanContinue)
                {
                    report("The profile switch was cancelled because an application from the current profile is still running. Restoring the previous profile…");
                    var applicationsToRestart = previousApplicationsInitiallyRunning
                        .Concat(closeResult.ApplicationsToRestart).ToList();
                    await CompensateFailedSwitchAsync(previous, applicationsToRestart, [], displayApplied, report);
                    outcome = "previous_application_close_failed";
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
                    _diagnostics.Error("activation.application.failed", exception,
                        new Dictionary<string, object?>
                        {
                            ["applicationId"] = app.Id,
                            ["applicationPath"] = app.Path
                        });
                    var warning = $"Could not start or manage {app.Name} ({app.Path}): {exception.Message}";
                    report(warning);
                    warnings.Add(warning);
                }
            }
            LogStage("target_applications.completed", new Dictionary<string, object?>
            {
                ["startedCount"] = startedTargetApplications.Count,
                ["warningCount"] = warnings.Count
            });

            target.LastActivatedUtc = DateTime.UtcNow;
            document.ActiveProfileId = target.Id;
            report(warnings.Count == 0
                ? $"{target.Name} is ready."
                : $"{target.Name} is active with {warnings.Count} warning{(warnings.Count == 1 ? string.Empty : "s")}: {string.Join(" ", warnings)}");
            outcome = warnings.Count == 0 ? "succeeded" : "succeeded_with_warnings";
            return true;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            if (applicationTransitionStarted)
                await CompensateFailedSwitchAsync(previous, previousApplicationsInitiallyRunning,
                    startedTargetApplications, displayApplied, report);
            report("Profile switch cancelled; the previous state was restored.");
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.Error("activation.failed", exception, new Dictionary<string, object?>
            {
                ["targetProfileId"] = target.Id,
                ["outcome"] = outcome
            }, totalDuration.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            totalDuration.Stop();
            _diagnostics.Write(outcome.StartsWith("succeeded", StringComparison.Ordinal) ? "info" : "warning",
                "activation.completed", outcome, new Dictionary<string, object?>
                {
                    ["targetProfileId"] = target.Id,
                    ["outcome"] = outcome
                }, totalDuration.ElapsedMilliseconds);
            _activationLock.Release();
        }
    }

    /// <summary>
    /// Switches the default audio output when the profile asks for one. A failure
    /// is a warning, never a rollback: audio is recoverable in two clicks and is
    /// not worth abandoning an otherwise good display and application switch.
    /// </summary>
    private async Task ApplyAudioAsync(SwitchProfile target, bool displayApplied, Action<string> report,
        List<string> warnings, CancellationToken cancellationToken)
    {
        if (audio is null)
        {
            // Logged rather than returned quietly: a silent skip here is
            // indistinguishable from a switch that did not work.
            _diagnostics.Write("info", "activation.audio.skipped", "No audio service was supplied.");
            return;
        }

        await ApplyAudioEndpointAsync("output", target.AudioOutputDeviceId, target.AudioOutputDeviceName,
            audio.GetOutputDevices, audio.GetDefaultOutputDevice, target, displayApplied, report, warnings,
            cancellationToken);
        await ApplyAudioEndpointAsync("input", target.AudioInputDeviceId, target.AudioInputDeviceName,
            audio.GetInputDevices, audio.GetDefaultInputDevice, target, displayApplied, report, warnings,
            cancellationToken);
    }

    private async Task ApplyAudioEndpointAsync(string kind, string deviceId, string deviceName,
        Func<IReadOnlyList<AudioDevice>> list, Func<AudioDevice?> current, SwitchProfile target,
        bool displayApplied, Action<string> report, List<string> warnings, CancellationToken cancellationToken)
    {
        if (audio is null || string.IsNullOrWhiteSpace(deviceId))
        {
            _diagnostics.Write("info", "activation.audio.skipped", $"The profile selects no audio {kind} device.");
            return;
        }

        var wanted = deviceName is { Length: > 0 } name ? name : "the saved device";
        try
        {
            if (current() is { } active && active.Id == deviceId)
            {
                report($"Audio {kind} is already {active.Name}.");
                return;
            }

            if (!await WaitForAudioDeviceAsync(deviceId, displayApplied, kind, wanted, list, report,
                    cancellationToken))
            {
                var missing = $"The audio {kind} device for {target.Name} ({wanted}) is not connected, so it was left unchanged.";
                report(missing);
                warnings.Add(missing);
                return;
            }

            report($"Switching audio {kind} to {wanted}…");
            audio.SetDefaultDevice(deviceId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _diagnostics.Error("activation.audio.failed", exception, new Dictionary<string, object?>
            {
                ["targetProfileId"] = target.Id,
                ["kind"] = kind
            });
            var warning = $"Could not switch the audio {kind} to {wanted}: {exception.Message}";
            report(warning);
            warnings.Add(warning);
        }
    }

    /// <summary>
    /// Waits for an audio endpoint to appear, when a display change could be what
    /// brings it.
    /// </summary>
    /// <remarks>
    /// A monitor's audio endpoint does not exist until Windows has finished
    /// enabling that monitor, and it arrives some time after the topology call
    /// returns. Enumerating immediately finds nothing and the profile silently
    /// keeps the old output. Only waited for when this switch actually applied a
    /// display layout: with no display change, an absent device is genuinely
    /// absent and making the user wait would be pointless.
    /// </remarks>
    private async Task<bool> WaitForAudioDeviceAsync(string deviceId, bool displayApplied, string kind,
        string wanted, Func<IReadOnlyList<AudioDevice>> list, Action<string> report,
        CancellationToken cancellationToken)
    {
        if (audio is null) return false;
        if (list().Any(device => device.Id == deviceId)) return true;
        if (!displayApplied) return false;

        report($"Waiting for the {kind} device {wanted} to become available…");
        var deadline = DateTime.UtcNow + AudioEndpointWait;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            if (!list().Any(device => device.Id == deviceId)) continue;

            _diagnostics.Write("info", "activation.audio.endpoint_appeared");
            return true;
        }

        return false;
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
            catch (Exception exception)
            {
                _diagnostics.Error("activation.application_identity.failed", exception,
                    new Dictionary<string, object?>
                    {
                        ["applicationId"] = app.Id,
                        ["applicationPath"] = app.Path
                    });
                var warning = $"Could not identify {app.Name}; its close step was skipped.";
                report(warning);
                warnings.Add(warning);
                continue;
            }

            if (targetIdentities.Contains(previousIdentity) || !scheduledIdentities.Add(previousIdentity)) continue;
            var wasRunning = false;
            try { wasRunning = processes.IsRunning(app); }
            catch (Exception exception)
            {
                _diagnostics.Error("activation.application_running_check.failed", exception,
                    new Dictionary<string, object?>
                    {
                        ["applicationId"] = app.Id,
                        ["applicationPath"] = app.Path
                    });
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
                _diagnostics.Error("activation.application_close.failed", exception,
                    new Dictionary<string, object?>
                    {
                        ["applicationId"] = closeTask.App.Id,
                        ["applicationPath"] = closeTask.App.Path
                    });
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
                _diagnostics.Error("activation.recovery.target_close.failed", exception,
                    new Dictionary<string, object?>
                    {
                        ["applicationId"] = app.Id,
                        ["applicationPath"] = app.Path
                    });
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
                _diagnostics.Error("activation.recovery.display.failed", exception);
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
                _diagnostics.Error("activation.recovery.application_restart.failed", exception,
                    new Dictionary<string, object?>
                    {
                        ["applicationId"] = app.Id,
                        ["applicationPath"] = app.Path
                    });
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
