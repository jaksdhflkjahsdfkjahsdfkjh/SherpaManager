using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using SherpaManager.Models;
using SherpaManager.Services;

namespace SherpaManager.Tests;

internal static class Program
{
    private static int _passed;

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("New applications default to minimized and close on switch", TestApplicationDefaultsAsync),
            ("Extensionless iRacing Start Menu URL resolves with lifecycle association", TestHiddenUrlExtensionAsync),
            ("A shell handler is not mistaken for the app it launches", TestShellHandlerIsolationAsync),
            ("Untrackable scripts report lifecycle limitations", TestUntrackableScriptWarningAsync),
            ("Settings persist", TestSettingsPersistenceAsync),
            ("Diagnostics redact paths and preserve native error codes", TestDiagnosticsRedactionAsync),
            ("Diagnostics rotate within their file limit", TestDiagnosticsRotationAsync),
            ("Copied diagnostics contain a redacted live summary", TestDiagnosticsReportAsync),
            ("Single-instance activation signal works", TestSingleInstanceSignalAsync),
            ("Single-instance acknowledgement waits for completed activation", TestSingleInstanceDelayedAcknowledgementAsync),
            ("A rejected activation can hand ownership to a replacement instance", TestSingleInstanceShutdownHandoffAsync),
            ("Disabled destination app does not suppress closing", TestActivationClosingAsync),
            ("Display layout is applied before old applications close", TestTransactionalActivationOrderingAsync),
            ("Profile activation rechecks display verification environment", TestVerificationEnvironmentRecheckAsync),
            ("Display verification requires the same saved environment", TestVerificationEnvironmentPolicyAsync),
            ("Rejected display layout leaves old applications running", TestRejectedDisplayLeavesApplicationsAsync),
            ("Failed application close restores the previous profile", TestFailedCloseRestoresPreviousProfileAsync),
            ("Cancelled application launch restores the previous profile", TestCancelledLaunchRestoresPreviousProfileAsync),
            ("Duplicate launch identities start only once", TestDuplicateSuppressionAsync),
            ("Partial app launch remains a manageable active profile", TestPartialLaunchAsync),
            ("Invalid display snapshots fail before any NVIDIA action", TestDisplayPreflightAsync),
            ("NVIDIA packed mode indexes validate without their advertised flag", TestNvidiaPackedModeIndexesAsync),
            ("Display recovery falls back to its backup", TestDisplayRecoveryBackupAsync),
            ("Interrupted display transactions persist until completed", TestDisplayTransactionStoreAsync),
            ("Profile duplication preserves display verification data", TestProfileCloneAsync),
            ("NVIDIA Surround fingerprint includes GPU and panel order", TestSurroundFingerprintAsync),
            ("NVIDIA Surround interop layout matches the x64 API", TestNvidiaInteropLayoutAsync),
            ("Display rollback countdown is ten seconds", TestDisplayRollbackCountdownAsync),
            ("Visible fixture starts minimized and closes gracefully", TestVisibleFixtureAsync),
            ("Hidden fixture receives WM_CLOSE", TestHiddenFixtureAsync),
            ("Close on switch force-closes an app that ignores WM_CLOSE", TestAutomaticForceCloseFixtureAsync),
            ("Delayed launcher child window is minimized", TestDelayedLauncherMinimizationAsync),
            ("Closing a launcher also closes its delayed bin child", TestDelayedLauncherCloseAsync),
            ("Closing a launcher also closes a differently named child", TestDifferentlyNamedLauncherChildCloseAsync),
            ("Closing a launcher before handoff does not suppress relaunch", TestPreHandoffLauncherCloseAsync),
            ("A delayed unresponsive child is force-closed automatically", TestDelayedCloseOutcomeAsync),
            ("Same-named executables are matched by exact path", TestSameNamedExecutableIsolationAsync),
            ("Activation preview reports enabled, disabled, and changed monitors", TestPreviewDisplayChangesAsync),
            ("Activation preview separates apps that start, stay, and close", TestPreviewApplicationPlanAsync),
            ("Activation preview reports unusable applications as problems", TestPreviewApplicationProblemsAsync),
            ("Activation preview describes the Surround transition", TestPreviewSurroundAsync),
            ("Activation preview changes nothing", TestPreviewIsReadOnlyAsync),
            ("Activation preview window renders every item", TestPreviewWindowRendersAsync)
        };

        var selectedTests = tests.ToList();
        if (Environment.GetEnvironmentVariable("SHERPA_HARDWARE_TESTS") == "1")
            selectedTests.Add(("Display capture and same-topology recovery round trip", TestDisplayRoundTripAsync));
        if (Environment.GetEnvironmentVariable("SHERPA_VALIDATE_SAVED_PROFILES") == "1")
            selectedTests.Add(("Saved profile display snapshots pass preflight", TestSavedProfileSnapshotsAsync));

        foreach (var test in selectedTests)
        {
            try
            {
                await test.Run();
                _passed++;
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
                return 1;
            }
        }

        Console.WriteLine($"{_passed}/{selectedTests.Count} tests passed.");
        return 0;
    }

    private static Task TestApplicationDefaultsAsync()
    {
        var app = new LaunchApplication();
        Assert(app.StartMinimized, "StartMinimized should default to true.");
        Assert(app.CloseOnDeactivate, "CloseOnDeactivate should default to true.");
        Assert(app.Id != Guid.Empty, "Applications need a stable identifier.");
        app.LaunchDelayMs = int.MaxValue;
        Assert(app.LaunchDelayMs == LaunchApplication.MaximumLaunchDelayMs,
            "Launch delays should be capped so a bad value cannot block Sherpa indefinitely.");
        return Task.CompletedTask;
    }

    private static Task TestHiddenUrlExtensionAsync()
    {
        using var directory = new TemporaryDirectory();
        var shortcut = Path.Combine(directory.Path, "iRacing.url");
        File.WriteAllText(shortcut, "[InternetShortcut]\nURL=steam://rungameid/266410\n");
        var app = new LaunchApplication { Name = "iRacing", Path = Path.Combine(directory.Path, "iRacing") };
        var resolved = new LaunchTargetResolver().Resolve(app);
        Assert(resolved.LaunchPath.Equals(shortcut, StringComparison.OrdinalIgnoreCase), "The hidden .url extension was not resolved.");
        Assert(resolved.ProcessName.Equals("iRacingUI", StringComparison.OrdinalIgnoreCase),
            "The official iRacing Steam shortcut should infer the iRacingUI lifecycle process.");
        return Task.CompletedTask;
    }

    private static Task TestShellHandlerIsolationAsync()
    {
        var matcher = typeof(ProcessService).GetMethod("ProcessMatchesTarget",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The process-target matcher was not found.");
        using var shellHandler = Process.GetCurrentProcess();
        var target = new ResolvedLaunchTarget("steam://rungameid/266410", null, "iRacingUI",
            "process:iRacingUI", IsShortcutOrProtocol: true);
        var matched = (bool)(matcher.Invoke(null, [shellHandler, target])
            ?? throw new InvalidOperationException("The process-target matcher returned no result."));
        Assert(!matched, "A process returned by shell execution was tracked even though its process name did not match iRacingUI.");
        return Task.CompletedTask;
    }

    private static async Task TestUntrackableScriptWarningAsync()
    {
        using var directory = new TemporaryDirectory();
        var script = Path.Combine(directory.Path, "quick-exit.cmd");
        await File.WriteAllTextAsync(script, "@echo off\r\nexit /b 0\r\n");
        var app = new LaunchApplication
        {
            Name = "Quick script",
            Path = script,
            StartMinimized = false
        };

        var result = await new ProcessService().LaunchAsync(app, CancellationToken.None);
        Assert(result.Started, "The script was not started.");
        Assert(!result.LifecycleManageable && result.HasWarning,
            "An untrackable script did not report that Sherpa cannot detect or close its child application.");
    }

    private static async Task TestSettingsPersistenceAsync()
    {
        using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", directory.Path);
        try
        {
            var store = new ProfileStore();
            var document = await store.LoadAsync();
            document.Settings.MinimizeToTrayOnClose = false;
            document.Profiles[0].Display = new DisplaySnapshot
            {
                NvidiaSurround = new NvidiaSurroundSnapshot
                {
                    FullGridCaptured = true,
                    DisplayGrids = [CreateCapturedSurroundGrid()]
                }
            };
            await store.SaveAsync(document);
            var reloaded = await new ProfileStore().LoadAsync();
            Assert(!reloaded.Settings.MinimizeToTrayOnClose, "Close behavior was not persisted.");
            Assert(reloaded.Profiles[0].Display?.NvidiaSurround?.DisplayGrids.Single().Displays[1].DisplayId == 102,
                "The complete NVIDIA display grid was not persisted.");
            reloaded.Settings.MinimizeToTrayOnClose = true;
            await store.SaveAsync(reloaded);
            Assert(File.Exists(store.FilePath + ".bak"), "Profile save did not retain a backup.");
            File.WriteAllText(store.FilePath, "not json");
            var recovered = await new ProfileStore().LoadAsync();
            Assert(!recovered.Settings.MinimizeToTrayOnClose, "A corrupt profile file did not recover from profiles.json.bak.");
            await store.SaveAsync(recovered);
            File.WriteAllText(store.FilePath, "corrupt again");
            var recoveredAgain = await new ProfileStore().LoadAsync();
            Assert(!recoveredAgain.Settings.MinimizeToTrayOnClose,
                "Saving a recovered profile overwrote the last known-good backup with corrupt data.");
            File.WriteAllText(store.FilePath, "{\"profiles\":[null],\"settings\":{}}");
            var recoveredNullEntry = await new ProfileStore().LoadAsync();
            Assert(recoveredNullEntry.Profiles.Count > 0 && !recoveredNullEntry.Settings.MinimizeToTrayOnClose,
                "Malformed-but-valid profile JSON bypassed recovery from profiles.json.bak.");
            await store.SaveAsync(recoveredNullEntry);
            await store.SaveAsync(recoveredNullEntry);
            Assert(File.Exists(store.FilePath + ".bak"),
                "Atomic profile backup rotation failed when a backup already existed.");
        }
        finally { Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", previous); }
    }

    private static Task TestSingleInstanceSignalAsync()
    {
        var key = "Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(key);
        SingleInstanceService? secondary = null;
        var secondaryThread = new Thread(() => secondary = new SingleInstanceService(key));
        secondaryThread.Start();
        secondaryThread.Join();
        using var secondaryInstance = secondary ?? throw new InvalidOperationException("Secondary coordinator was not created.");
        Assert(primary.IsPrimaryInstance, "First coordinator should be primary.");
        Assert(!secondaryInstance.IsPrimaryInstance, "Second coordinator should not be primary.");
        using var received = new ManualResetEventSlim();
        primary.StartListening(received.Set);
        Assert(secondaryInstance.SignalPrimaryInstance(), "Secondary did not receive an acknowledgement.");
        Assert(received.Wait(TimeSpan.FromSeconds(2)), "Primary did not receive the activation signal.");
        return Task.CompletedTask;
    }

    private static async Task TestSingleInstanceDelayedAcknowledgementAsync()
    {
        var key = "Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(key);
        SingleInstanceService? secondary = null;
        var secondaryThread = new Thread(() => secondary = new SingleInstanceService(key));
        secondaryThread.Start();
        secondaryThread.Join();
        using var secondaryInstance = secondary ?? throw new InvalidOperationException("Secondary coordinator was not created.");
        var activationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(async () =>
        {
            activationStarted.TrySetResult();
            await allowCompletion.Task;
            return true;
        });

        var signal = Task.Run(() => secondaryInstance.SignalPrimaryInstance(TimeSpan.FromSeconds(3)));
        await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(150);
        Assert(!signal.IsCompleted, "The second instance was acknowledged before window activation completed.");
        allowCompletion.TrySetResult();
        Assert(await signal, "The completed window activation was not acknowledged.");
    }

    private static async Task TestSingleInstanceShutdownHandoffAsync()
    {
        var key = "Test-" + Guid.NewGuid().ToString("N");
        using var primaryReady = new ManualResetEventSlim();
        using var activationRejected = new ManualResetEventSlim();
        using var releasePrimary = new ManualResetEventSlim();
        Exception? primaryFailure = null;
        var primaryThread = new Thread(() =>
        {
            try
            {
                using var primary = new SingleInstanceService(key);
                primary.StartListening(() =>
                {
                    activationRejected.Set();
                    return Task.FromResult(false);
                });
                primaryReady.Set();
                releasePrimary.Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception exception) { primaryFailure = exception; }
        });
        primaryThread.Start();
        Assert(primaryReady.Wait(TimeSpan.FromSeconds(2)), "The primary coordinator did not start.");

        var promoted = Task.Run(() =>
        {
            using var replacement = new SingleInstanceService(key);
            var signalled = replacement.SignalPrimaryInstance(TimeSpan.FromMilliseconds(800));
            return signalled && replacement.IsPrimaryInstance;
        });
        Assert(activationRejected.Wait(TimeSpan.FromSeconds(2)), "The primary did not reject activation during shutdown.");
        releasePrimary.Set();
        Assert(await promoted, "The replacement instance did not acquire ownership after the primary exited.");
        Assert(primaryThread.Join(TimeSpan.FromSeconds(2)), "The primary coordinator thread did not exit.");
        if (primaryFailure is not null) throw new InvalidOperationException("The primary coordinator failed.", primaryFailure);
    }

    private static async Task TestActivationClosingAsync()
    {
        var processes = new FakeProcessService();
        var previousApp = new LaunchApplication { Path = "previous.exe", Enabled = true, CloseOnDeactivate = true };
        var previous = new SwitchProfile { Name = "Previous", Applications = [previousApp] };
        var target = new SwitchProfile
        {
            Name = "Target",
            Applications = [new LaunchApplication { Path = "previous.exe", Enabled = false }]
        };
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target] };
        var activated = await new ProfileActivationService(new DisplayConfigurationService(), processes)
            .ActivateAsync(document, target, _ => { });
        Assert(activated, "Activation should complete.");
        Assert(processes.Closed.Contains(previousApp.Id), "Disabled previous app was not closed.");
    }

    private static async Task TestTransactionalActivationOrderingAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var previousApp = new LaunchApplication { Name = "Previous app", Path = "previous.exe" };
        processes.RunningPaths.Add(previousApp.Path);
        var previous = new SwitchProfile { Name = "Previous", Applications = [previousApp] };
        var target = new SwitchProfile
        {
            Name = "Target",
            Display = new DisplaySnapshot { IsVerified = true }
        };
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target] };

        var activated = await new ProfileActivationService(displays, processes)
            .ActivateAsync(document, target, _ => { });

        Assert(activated, "Activation should complete.");
        var displayIndex = processes.Events.IndexOf("display:apply");
        var closeIndex = processes.Events.IndexOf("close:previous.exe");
        Assert(displayIndex >= 0 && closeIndex > displayIndex,
            "The previous application was closed before the target display layout was ready.");
    }

    private static async Task TestVerificationEnvironmentRecheckAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var target = new SwitchProfile
        {
            Name = "Target",
            Display = new DisplaySnapshot
            {
                IsVerified = true,
                VerificationEnvironmentFingerprint = "saved-environment"
            }
        };
        var document = new ProfileDocument { Profiles = [target] };
        var confirmations = 0;

        displays.VerificationEnvironmentChanged = false;
        var activated = await new ProfileActivationService(displays, processes)
            .ActivateAsync(document, target, _ => { }, _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            });

        Assert(activated, "Activation should complete when the verified environment still matches.");
        Assert(displays.ConfirmationRestoreCalls == 1 && displays.ConfirmOnlyWhenVerificationChanged,
            "Activation did not ask the display service to recheck the saved environment fingerprint.");
        Assert(confirmations == 0,
            "A stable, already verified display environment unexpectedly asked for confirmation again.");

        displays.VerificationEnvironmentChanged = true;
        activated = await new ProfileActivationService(displays, processes)
            .ActivateAsync(document, target, _ => { }, _ =>
            {
                confirmations++;
                return Task.FromResult(true);
            });

        Assert(activated, "Activation should complete after the changed environment is confirmed.");
        Assert(confirmations == 1,
            "A changed display environment did not require a fresh confirmation.");
    }

    private static Task TestVerificationEnvironmentPolicyAsync()
    {
        var matches = typeof(DisplayConfigurationService).GetMethod("VerificationEnvironmentMatches",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The display verification policy was not found.");
        var snapshot = new DisplaySnapshot
        {
            IsVerified = true,
            VerificationEnvironmentFingerprint = "ABC123"
        };

        Assert((bool)(matches.Invoke(null, [snapshot, "ABC123"]) ?? false),
            "An unchanged verified environment was not accepted.");
        Assert(!(bool)(matches.Invoke(null, [snapshot, "DIFFERENT"]) ?? true),
            "A changed environment incorrectly remained verified.");
        snapshot.VerificationEnvironmentFingerprint = string.Empty;
        Assert(!(bool)(matches.Invoke(null, [snapshot, "ABC123"]) ?? true),
            "A legacy profile without an environment fingerprint incorrectly skipped confirmation.");
        snapshot.VerificationEnvironmentFingerprint = "ABC123";
        snapshot.IsVerified = false;
        Assert(!(bool)(matches.Invoke(null, [snapshot, "ABC123"]) ?? true),
            "An unverified profile incorrectly skipped confirmation.");
        return Task.CompletedTask;
    }

    private static Task TestDiagnosticsRedactionAsync()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new DiagnosticsService(directory.Path, maximumFileBytes: 4096,
            maximumFileCount: 3, () => new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc));
        const string privatePath = @"C:\Users\PrivateUser\Games\secret.exe";
        var exception = new InvalidOperationException("NVAPI -157 rejected the operation.",
            new Win32Exception(5, $"Access denied for {privatePath}"));
        diagnostics.Error("test.native_error", exception, new Dictionary<string, object?>
        {
            ["applicationPath"] = privatePath,
            ["arguments"] = new[] { "--password", "secret" }
        });

        var log = string.Join('\n', diagnostics.ReadRecentLines(20));
        Assert(!log.Contains("PrivateUser", StringComparison.Ordinal) &&
               !log.Contains("secret.exe", StringComparison.OrdinalIgnoreCase),
            "Diagnostics exposed an application path.");
        Assert(log.Contains("<redacted-path>", StringComparison.Ordinal),
            $"Diagnostics did not mark the redacted path. Log: {log}");
        Assert(log.Contains("\"win32Codes\":[5]", StringComparison.Ordinal),
            "The exact Windows error code was not preserved.");
        Assert(log.Contains("\"nvapiCodes\":[-157]", StringComparison.Ordinal),
            "The exact NVAPI error code was not preserved.");
        Assert(!log.Contains("--password", StringComparison.Ordinal),
            "Diagnostics exposed command-line arguments.");
        return Task.CompletedTask;
    }

    private static Task TestDiagnosticsRotationAsync()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new DiagnosticsService(directory.Path, maximumFileBytes: 256,
            maximumFileCount: 3);
        for (var index = 0; index < 20; index++)
            diagnostics.Write("info", "test.rotation", $"event-{index:D2}-" + new string('x', 120));

        Assert(File.Exists(Path.Combine(directory.Path, "sherpa.1.log")),
            "The diagnostic log was not rotated.");
        Assert(Directory.GetFiles(directory.Path, "sherpa*.log").Length <= 3,
            "Diagnostics retained more files than configured.");
        Assert(diagnostics.ReadRecentLines(20).Any(line => line.Contains("event-19", StringComparison.Ordinal)),
            "The newest event was lost during rotation.");
        return Task.CompletedTask;
    }

    private static Task TestDiagnosticsReportAsync()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new DiagnosticsService(directory.Path, maximumFileBytes: 4096,
            maximumFileCount: 3);
        diagnostics.Write("info", "activation.stage", "display.completed", durationMs: 42);
        var topology = new DisplaySnapshot
        {
            LogicalDisplayCount = 1,
            ActiveTargets = [new DisplayTargetSnapshot
            {
                FriendlyName = "Test monitor",
                MonitorDevicePath = @"C:\private\monitor-path",
                SourceWidth = 1920,
                SourceHeight = 1080,
                RefreshNumerator = 60000,
                RefreshDenominator = 1000
            }],
            NvidiaSurround = new NvidiaSurroundSnapshot
            {
                ApiAvailable = true,
                StatusKnown = true,
                Description = "NVIDIA Surround: not configured."
            }
        };

        var report = diagnostics.CreateClipboardReport(topology);
        Assert(report.Contains("Sherpa version:", StringComparison.Ordinal) &&
               report.Contains("Windows:", StringComparison.Ordinal) &&
               report.Contains("NVIDIA API status:", StringComparison.Ordinal) &&
               report.Contains("Test monitor: 1920x1080", StringComparison.Ordinal) &&
               report.Contains("activation.stage", StringComparison.Ordinal),
            "The copied diagnostic report is missing required sections.");
        Assert(!report.Contains("monitor-path", StringComparison.Ordinal),
            "The copied diagnostic report exposed a monitor path.");
        return Task.CompletedTask;
    }

    private static async Task TestRejectedDisplayLeavesApplicationsAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events)
        {
            TargetResult = new DisplayRestoreResult(false, "reverted", Kept: false)
        };
        var previousApp = new LaunchApplication { Name = "Previous app", Path = "previous.exe" };
        processes.RunningPaths.Add(previousApp.Path);
        var previous = new SwitchProfile { Name = "Previous", Applications = [previousApp] };
        var target = new SwitchProfile
        {
            Name = "Target",
            Display = new DisplaySnapshot { IsVerified = true }
        };
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target] };

        var activated = await new ProfileActivationService(displays, processes)
            .ActivateAsync(document, target, _ => { });

        Assert(!activated, "A reverted display layout must stop activation.");
        Assert(processes.Closed.Count == 0, "An old-profile application was closed after display rejection.");
        Assert(processes.RunningPaths.Contains(previousApp.Path), "The old-profile application did not remain running.");
        Assert(document.ActiveProfileId == previous.Id, "The rejected target was recorded as active.");
    }

    private static async Task TestFailedCloseRestoresPreviousProfileAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var closedApp = new LaunchApplication { Name = "Closed app", Path = "closed.exe" };
        var stubbornApp = new LaunchApplication { Name = "Stubborn app", Path = "stubborn.exe" };
        processes.RunningPaths.Add(closedApp.Path);
        processes.RunningPaths.Add(stubbornApp.Path);
        processes.CloseResultsByPath[closedApp.Path] =
            new ProcessCloseResult(ProcessCloseStatus.ClosedGracefully, 1, "closed");
        processes.CloseResultsByPath[stubbornApp.Path] =
            new ProcessCloseResult(ProcessCloseStatus.StillRunning, 1, "still running");
        var previous = new SwitchProfile
        {
            Name = "Previous",
            Applications = [closedApp, stubbornApp]
        };
        var targetApp = new LaunchApplication { Name = "Target app", Path = "target.exe" };
        var target = new SwitchProfile
        {
            Name = "Target",
            Display = new DisplaySnapshot { IsVerified = true },
            Applications = [targetApp]
        };
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target] };

        var activated = await new ProfileActivationService(displays, processes)
            .ActivateAsync(document, target, _ => { });

        Assert(!activated, "Activation should stop when an old-profile application cannot be closed.");
        Assert(displays.RecoveryRestoreCalls == 1, "The previous display layout was not restored.");
        Assert(processes.Launched.Contains(closedApp.Id), "The successfully closed old-profile app was not restarted.");
        Assert(!processes.Launched.Contains(targetApp.Id), "A target app started after the transaction failed.");
        Assert(processes.RunningPaths.Contains(closedApp.Path) && processes.RunningPaths.Contains(stubbornApp.Path),
            "The previous application state was not recovered.");
        Assert(document.ActiveProfileId == previous.Id, "The failed target was recorded as active.");
        var rollbackIndex = processes.Events.IndexOf("display:rollback");
        var restartIndex = processes.Events.IndexOf("launch:closed.exe");
        Assert(rollbackIndex >= 0 && restartIndex > rollbackIndex,
            "The old application was restarted before its display layout was restored.");
    }

    private static async Task TestCancelledLaunchRestoresPreviousProfileAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var previousApp = new LaunchApplication { Name = "Previous app", Path = "previous.exe" };
        processes.RunningPaths.Add(previousApp.Path);
        var previous = new SwitchProfile { Name = "Previous", Applications = [previousApp] };
        var startedTargetApp = new LaunchApplication { Name = "Started target", Path = "target-started.exe" };
        var delayedTargetApp = new LaunchApplication
        {
            Name = "Delayed target",
            Path = "target-delayed.exe",
            LaunchDelayMs = LaunchApplication.MaximumLaunchDelayMs
        };
        var target = new SwitchProfile
        {
            Name = "Target",
            Display = new DisplaySnapshot { IsVerified = true },
            Applications = [startedTargetApp, delayedTargetApp]
        };
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target] };
        using var cancellation = new CancellationTokenSource();

        var activation = new ProfileActivationService(displays, processes)
            .ActivateAsync(document, target, _ => { }, cancellationToken: cancellation.Token);
        var launchDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!processes.Launched.Contains(startedTargetApp.Id) && DateTime.UtcNow < launchDeadline)
            await Task.Delay(10);
        Assert(processes.Launched.Contains(startedTargetApp.Id), "The first target app did not start before cancellation.");
        cancellation.Cancel();

        var cancelled = false;
        try { await activation; }
        catch (OperationCanceledException) { cancelled = true; }

        Assert(cancelled, "The cancelled activation did not report cancellation.");
        Assert(displays.RecoveryRestoreCalls == 1, "Cancellation did not restore the previous display layout.");
        Assert(processes.Closed.Contains(startedTargetApp.Id), "The target app started by the cancelled switch was not closed.");
        Assert(!processes.RunningPaths.Contains(startedTargetApp.Path), "The cancelled target app is still running.");
        Assert(processes.RunningPaths.Contains(previousApp.Path), "The previous app was not restarted after cancellation.");
        Assert(document.ActiveProfileId == previous.Id, "The cancelled target was recorded as active.");
    }

    private static async Task TestDuplicateSuppressionAsync()
    {
        var processes = new FakeProcessService();
        var target = new SwitchProfile
        {
            Name = "Target",
            Applications =
            [
                new LaunchApplication { Path = "same.exe" },
                new LaunchApplication { Path = "same.exe" }
            ]
        };
        var document = new ProfileDocument { Profiles = [target] };
        await new ProfileActivationService(new DisplayConfigurationService(), processes).ActivateAsync(document, target, _ => { });
        Assert(processes.Launched.Count == 1, $"Expected one launch, got {processes.Launched.Count}.");
    }

    private static async Task TestPartialLaunchAsync()
    {
        var processes = new FakeProcessService { ThrowOnLaunchPath = "broken.exe" };
        var target = new SwitchProfile
        {
            Name = "Target",
            Applications =
            [
                new LaunchApplication { Path = "started.exe" },
                new LaunchApplication { Path = "broken.exe" }
            ]
        };
        var document = new ProfileDocument { Profiles = [target] };
        var messages = new List<string>();

        var activated = await new ProfileActivationService(new DisplayConfigurationService(), processes)
            .ActivateAsync(document, target, messages.Add);

        Assert(activated, "A single failed app should not abandon the completed parts of a profile switch.");
        Assert(document.ActiveProfileId == target.Id, "The partially launched target was not recorded as active.");
        Assert(processes.Launched.Count == 1, "The launch sequence did not preserve the successfully started app.");
        Assert(messages.Any(message => message.Contains("broken", StringComparison.OrdinalIgnoreCase)),
            "The failed app was not reported to the user.");
    }

    private static Task TestDisplayRecoveryBackupAsync()
    {
        using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", directory.Path);
        try
        {
            var store = new DisplayRecoveryStore();
            store.Save(CreateStoredDisplaySnapshot("safe"));
            store.Save(CreateStoredDisplaySnapshot("newer"));
            File.WriteAllText(store.FilePath, "{}");
            var recovered = store.Load();
            Assert(recovered?.Summary == "safe", "An unusable recovery file did not fall back to its backup.");
            store.Save(recovered!);
            store.Save(CreateStoredDisplaySnapshot("latest"));
            File.WriteAllText(store.FilePath, "corrupt again");
            Assert(store.Load()?.Summary == "safe",
                "Saving a recovered display layout overwrote the last known-good backup with corrupt data.");
            return Task.CompletedTask;
        }
        finally { Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", previous); }
    }

    private static Task TestDisplayTransactionStoreAsync()
    {
        using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", directory.Path);
        try
        {
            var store = new DisplayTransactionStore();
            store.Begin(CreateStoredDisplaySnapshot("before switch"), "requested sim layout");

            var pending = store.GetPendingTransaction();
            Assert(pending is not null, "The interrupted-transaction marker was not persisted.");
            Assert(pending!.RecoveryAvailable, "The pre-change recovery snapshot was not persisted.");
            Assert(pending.RequestedSummary == "requested sim layout", "The requested layout was not described.");
            Assert(store.LoadRecovery().Summary == "before switch", "The wrong pre-change layout was loaded.");

            try
            {
                store.Begin(CreateStoredDisplaySnapshot("replacement"), "overlapping operation");
                throw new InvalidOperationException("An unresolved transaction was overwritten.");
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("waiting for recovery",
                       StringComparison.OrdinalIgnoreCase))
            {
                // Expected: a new display operation must not destroy crash recovery.
            }

            File.WriteAllText(Path.Combine(directory.Path, "display-transaction.json"), "damaged marker");
            pending = store.GetPendingTransaction();
            Assert(pending is { RecoveryAvailable: true },
                "A malformed marker hid the valid recovery snapshot.");

            store.Complete();
            Assert(!store.HasPendingTransaction && store.GetPendingTransaction() is null,
                "Completed transaction files were not cleared.");
            Assert(!File.Exists(Path.Combine(directory.Path, "display-transaction-recovery.json")),
                "The completed transaction recovery snapshot was left behind.");
            return Task.CompletedTask;
        }
        finally { Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", previous); }
    }

    private static async Task TestDisplayPreflightAsync()
    {
        using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", directory.Path);
        try
        {
            var nvidia = new FakeNvidiaSurroundService();
            using var service = new DisplayConfigurationService(nvidia, new DisplayRecoveryStore());
            try
            {
                await service.RestoreAsync(new DisplaySnapshot(), NvidiaSurroundMode.RequireEnabled);
                throw new InvalidOperationException("An empty display snapshot was accepted.");
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                // Expected: validation must happen before capture, recovery writes, or NVAPI.
            }
            Assert(nvidia.GetStatusCalls == 0 && nvidia.ApplyConfigurationCalls == 0,
                "NVIDIA state was queried or changed before snapshot validation completed.");
            Assert(!File.Exists(service.RecoveryFilePath),
                "An emergency recovery file was written for a request that failed preflight validation.");
        }
        finally { Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", previous); }
    }

    private static Task TestNvidiaPackedModeIndexesAsync()
    {
        var validator = typeof(DisplayConfigurationService).GetMethod("ValidateSnapshotStructures",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The display snapshot validator was not found.");

        var path = new byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(0, 4), 0x1234); // source adapter
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(8, 4), 0); // source id
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(12, 4), 0x0001ffff); // source index 1, invalid clone group
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(20, 4), 0x1234); // target adapter
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(28, 4), 7); // target id
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(32, 4), 0x0000ffff); // target index 0, no desktop mode
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(68, 4), 1); // active, but no virtual-mode-support flag

        var targetMode = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(targetMode.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(targetMode.AsSpan(4, 4), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(targetMode.AsSpan(8, 4), 0x1234);
        var sourceMode = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMode.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMode.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMode.AsSpan(8, 4), 0x1234);

        var snapshot = new DisplaySnapshot
        {
            SnapshotVersion = 1,
            PathStructureSize = path.Length,
            ModeStructureSize = sourceMode.Length,
            LogicalDisplayCount = 1,
            Paths = [Convert.ToBase64String(path)],
            Modes = [Convert.ToBase64String(targetMode), Convert.ToBase64String(sourceMode)]
        };
        validator.Invoke(null, [snapshot]);

        BinaryPrimitives.WriteUInt32LittleEndian(sourceMode.AsSpan(4, 4), 99);
        snapshot.Modes[1] = Convert.ToBase64String(sourceMode);
        try
        {
            validator.Invoke(null, [snapshot]);
            throw new InvalidOperationException("A malformed packed-mode snapshot was accepted.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            // Expected: the compatibility path must still fail closed.
        }
        return Task.CompletedTask;
    }

    private static DisplaySnapshot CreateStoredDisplaySnapshot(string summary)
    {
        // DISPLAYCONFIG_PATH_INFO contains one active path referencing one source
        // and one target DISPLAYCONFIG_MODE_INFO. Version 1 intentionally has no
        // stable-monitor metadata; this fixture is only exercised by the recovery
        // store and is never applied to the machine.
        var path = new byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(8, 4), 1);  // source id
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(12, 4), 0); // source mode index
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(28, 4), 2); // target id
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(32, 4), 1); // target mode index
        BinaryPrimitives.WriteUInt32LittleEndian(path.AsSpan(68, 4), 1); // active flag

        var sourceMode = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMode.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(sourceMode.AsSpan(4, 4), 1);
        var targetMode = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(targetMode.AsSpan(0, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(targetMode.AsSpan(4, 4), 2);

        return new DisplaySnapshot
        {
            SnapshotVersion = 1,
            Summary = summary,
            PathStructureSize = path.Length,
            ModeStructureSize = sourceMode.Length,
            LogicalDisplayCount = 1,
            Paths = [Convert.ToBase64String(path)],
            Modes = [Convert.ToBase64String(sourceMode), Convert.ToBase64String(targetMode)]
        };
    }

    private static Task TestProfileCloneAsync()
    {
        var profile = new SwitchProfile
        {
            Display = new DisplaySnapshot
            {
                SnapshotVersion = 3,
                IsVerified = true,
                VerificationEnvironmentFingerprint = "ABC123",
                VerifiedAtUtc = new DateTime(2026, 9, 3, 20, 0, 0, DateTimeKind.Utc),
                ActiveTargets = [new DisplayTargetSnapshot
                {
                    Identity = "monitor",
                    PathIndex = 2,
                    SourceWidth = 5760,
                    SourceHeight = 1080,
                    SourceX = -1920,
                    RefreshNumerator = 144000,
                    RefreshDenominator = 1000,
                    TargetActiveWidth = 1920,
                    TargetActiveHeight = 1080
                }],
                NvidiaSurround = new NvidiaSurroundSnapshot
                {
                    ApiAvailable = true,
                    StatusKnown = true,
                    HasConfiguredTopology = true,
                    Topology = 3,
                    GridMembershipVerified = true,
                    GridTopologyCount = 1,
                    GridCells = [new NvidiaSurroundGridCellSnapshot
                    {
                        TopologyIndex = 0,
                        Row = 0,
                        Column = 0,
                        GpuBusId = 4,
                        DisplayOutputId = 8
                    }]
                }
            }
        };

        var clone = profile.Clone();
        var target = clone.Display?.ActiveTargets.Single()
            ?? throw new InvalidOperationException("The cloned display target is missing.");
        Assert(target.PathIndex == 2 && target.SourceWidth == 5760 && target.SourceX == -1920,
            "The cloned profile lost v3 display semantics.");
        Assert(clone.Display?.NvidiaSurround?.StatusKnown == true,
            "The cloned profile lost its verified NVIDIA status.");
        Assert(clone.Display?.NvidiaSurround?.GridCells.Single().GpuBusId == 4,
            "The cloned profile lost its NVIDIA GPU/grid fingerprint.");
        Assert(clone.Display?.IsVerified == true &&
               clone.Display.VerificationEnvironmentFingerprint == "ABC123" &&
               clone.Display.VerifiedAtUtc == profile.Display.VerifiedAtUtc,
            "The cloned profile lost its display verification environment.");
        profile.Display.NvidiaSurround.FullGridCaptured = true;
        profile.Display.NvidiaSurround.DisplayGrids = [CreateCapturedSurroundGrid()];
        clone = profile.Clone();
        Assert(clone.Display?.NvidiaSurround?.DisplayGrids.Single().ApplyWithBezelCorrection == true,
            "The cloned profile lost its NVIDIA bezel-correction setting.");
        Assert(clone.Display?.NvidiaSurround?.DisplayGrids.Single().Displays[1].DisplayId == 102,
            "The cloned profile lost its complete NVIDIA panel order.");
        return Task.CompletedTask;
    }

    private static Task TestSurroundFingerprintAsync()
    {
        static NvidiaSurroundSnapshot Create(uint firstOutput, uint secondOutput, uint busId = 4) => new()
        {
            StatusKnown = true,
            HasConfiguredTopology = true,
            Topology = 4,
            PerDisplayWidth = 1920,
            PerDisplayHeight = 1080,
            RefreshRateTimes1000 = 144000,
            GridMembershipVerified = true,
            GridTopologyCount = 1,
            GridCells =
            [
                new NvidiaSurroundGridCellSnapshot { Row = 0, Column = 0, GpuBusId = busId, DisplayOutputId = firstOutput },
                new NvidiaSurroundGridCellSnapshot { Row = 0, Column = 1, GpuBusId = busId, DisplayOutputId = secondOutput }
            ]
        };

        var method = typeof(DisplayConfigurationService).GetMethod("SurroundFingerprintMatches",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The Surround fingerprint validator is missing.");
        static bool Matches(MethodInfo method, NvidiaSurroundSnapshot left, NvidiaSurroundSnapshot right) =>
            (bool)(method.Invoke(null, [left, right]) ?? false);

        var expected = Create(1, 2);
        Assert(Matches(method, expected, Create(1, 2)), "An identical Surround grid was rejected.");
        Assert(!Matches(method, expected, Create(2, 1)), "Changed Surround panel order was accepted.");
        Assert(!Matches(method, expected, Create(1, 2, busId: 5)), "A changed Surround GPU was accepted.");
        var unverified = Create(1, 2);
        unverified.GridMembershipVerified = false;
        Assert(!Matches(method, expected, unverified), "An unverified Surround grid was accepted.");

        expected.FullGridCaptured = true;
        expected.DisplayGrids = [CreateCapturedSurroundGrid()];
        var fullMatch = Create(9, 10, busId: 99);
        fullMatch.FullGridCaptured = true;
        fullMatch.DisplayGrids = [CreateCapturedSurroundGrid()];
        Assert(Matches(method, expected, fullMatch), "An identical complete Surround grid was rejected.");
        fullMatch.DisplayGrids[0].Displays.Reverse();
        Assert(Matches(method, expected, fullMatch), "Enumeration order changed the complete-grid fingerprint.");
        fullMatch.DisplayGrids[0].Displays.Single(display => display.Column == 1).DisplayId = 999;
        Assert(!Matches(method, expected, fullMatch), "A changed complete Surround panel order was accepted.");
        fullMatch.DisplayGrids = [CreateCapturedSurroundGrid()];
        fullMatch.DisplayGrids[0].ApplyWithBezelCorrection = false;
        Assert(!Matches(method, expected, fullMatch), "A changed bezel-correction mode was accepted.");
        return Task.CompletedTask;
    }

    private static NvidiaSurroundDisplayGridSnapshot CreateCapturedSurroundGrid() => new()
    {
        Rows = 1,
        Columns = 3,
        ApplyWithBezelCorrection = true,
        PerDisplayWidth = 1920,
        PerDisplayHeight = 1080,
        BitsPerPixel = 32,
        RefreshRate = 144,
        Displays =
        [
            new NvidiaSurroundDisplayGridCellSnapshot { Row = 0, Column = 0, DisplayId = 101, OverlapX = 0 },
            new NvidiaSurroundDisplayGridCellSnapshot { Row = 0, Column = 1, DisplayId = 102, OverlapX = 120 },
            new NvidiaSurroundDisplayGridCellSnapshot { Row = 0, Column = 2, DisplayId = 103, OverlapX = 120 }
        ]
    };

    private static Task TestNvidiaInteropLayoutAsync()
    {
        var serviceType = typeof(NvidiaSurroundService);
        static Type Nested(Type owner, string name) => owner.GetNestedType(name, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing NVAPI interop type {name}.");
        var cellSize = Marshal.SizeOf(Nested(serviceType, "MosaicTopologyCell"));
        var detailsSize = Marshal.SizeOf(Nested(serviceType, "MosaicTopologyDetails"));
        var groupSize = Marshal.SizeOf(Nested(serviceType, "MosaicTopologyGroup"));
        var gridDisplayV1Size = Marshal.SizeOf(Nested(serviceType, "MosaicGridTopologyDisplayV1"));
        var gridDisplayV2Size = Marshal.SizeOf(Nested(serviceType, "MosaicGridTopologyDisplayV2"));
        var gridV1Size = Marshal.SizeOf(Nested(serviceType, "MosaicGridTopologyV1"));
        var gridV2Size = Marshal.SizeOf(Nested(serviceType, "MosaicGridTopologyV2"));
        var statusSize = Marshal.SizeOf(Nested(serviceType, "MosaicDisplayTopologyStatus"));

        Assert(IntPtr.Size == 8, "Sherpa's NVAPI build must run as x64.");
        Assert(cellSize == 24, $"Unexpected NVAPI grid-cell size: {cellSize}.");
        Assert(detailsSize == 1568, $"Unexpected NVAPI topology-details size: {detailsSize}.");
        Assert(groupSize == 3160, $"Unexpected NVAPI topology-group size: {groupSize}.");
        Assert(gridDisplayV1Size == 20, $"Unexpected NVAPI v1 grid-display size: {gridDisplayV1Size}.");
        Assert(gridDisplayV2Size == 28, $"Unexpected NVAPI v2 grid-display size: {gridDisplayV2Size}.");
        Assert(gridV1Size == 1320, $"Unexpected NVAPI v1 display-grid size: {gridV1Size}.");
        Assert(gridV2Size == 1832, $"Unexpected NVAPI v2 display-grid size: {gridV2Size}.");
        Assert(statusSize == 2064, $"Unexpected NVAPI validation-status size: {statusSize}.");
        return Task.CompletedTask;
    }

    private static Task TestDisplayRollbackCountdownAsync()
    {
        Assert(DisplayConfirmationWindow.RollbackSeconds == 10,
            "Display rollback confirmation must remain a ten-second safety window.");
        return Task.CompletedTask;
    }

    private static async Task TestVisibleFixtureAsync()
    {
        using var directory = new TemporaryDirectory();
        var minimizedSignal = Path.Combine(directory.Path, "minimized.signal");
        var app = FixtureApplication($"--state-file=\"{minimizedSignal}\"");
        var service = new ProcessService();
        var launch = await service.LaunchAsync(app, CancellationToken.None);
        Assert(launch.Started, "Fixture did not start.");
        Assert(await WaitForFileAsync(minimizedSignal, TimeSpan.FromSeconds(4)),
            "The visible fixture did not reach the minimized state.");
        var close = await service.CloseAsync(app, CancellationToken.None);
        Assert(close.Status == ProcessCloseStatus.ClosedGracefully, close.Message);
    }

    private static async Task TestHiddenFixtureAsync()
    {
        var app = FixtureApplication("--hidden");
        var service = new ProcessService();
        await service.LaunchAsync(app, CancellationToken.None);
        await Task.Delay(500);
        var close = await service.CloseAsync(app, CancellationToken.None);
        Assert(close.Status == ProcessCloseStatus.ClosedGracefully, close.Message);
    }

    private static async Task TestAutomaticForceCloseFixtureAsync()
    {
        var app = FixtureApplication("--ignore-close");
        var service = new ProcessService();
        await service.LaunchAsync(app, CancellationToken.None);
        var close = await service.CloseAsync(app, CancellationToken.None);
        Assert(close.Status == ProcessCloseStatus.ForcedClosed, close.Message);
    }

    private static async Task TestDelayedLauncherMinimizationAsync()
    {
        using var directory = new TemporaryDirectory();
        var launcherDirectory = Path.Combine(directory.Path, "launcher");
        var childDirectory = Path.Combine(directory.Path, "launcher", "bin");
        CopyFixtureOutput(launcherDirectory);
        CopyFixtureOutput(childDirectory);

        var minimizedSignal = Path.Combine(directory.Path, "minimized.signal");
        var app = new LaunchApplication
        {
            Name = "Delayed launcher fixture",
            Path = Path.Combine(launcherDirectory, "WindowFixture.exe"),
            Arguments = $"--launcher-delay-ms=4000 --child-path=\"{Path.Combine(childDirectory, "WindowFixture.exe")}\" --state-file=\"{minimizedSignal}\"",
            StartMinimized = true,
            CloseOnDeactivate = true
        };
        var service = new ProcessService();
        try
        {
            var launch = await service.LaunchAsync(app, CancellationToken.None);
            Assert(launch.Started, "The delayed launcher fixture did not start.");
            Assert(launch.MinimizationPending, "The delayed minimization watcher was not scheduled.");
            Assert(await WaitForFileAsync(minimizedSignal, TimeSpan.FromSeconds(7)),
                "The delayed child window was not minimized after its launcher exited.");
        }
        finally
        {
            await service.ForceCloseAsync(app, CancellationToken.None);
        }
    }

    private static async Task TestSameNamedExecutableIsolationAsync()
    {
        using var directory = new TemporaryDirectory();
        var firstDirectory = Path.Combine(directory.Path, "first");
        var secondDirectory = Path.Combine(directory.Path, "second");
        CopyFixtureOutput(firstDirectory);
        CopyFixtureOutput(secondDirectory);

        var first = new LaunchApplication
        {
            Name = "First fixture",
            Path = Path.Combine(firstDirectory, "WindowFixture.exe"),
            StartMinimized = false
        };
        var second = new LaunchApplication
        {
            Name = "Second fixture",
            Path = Path.Combine(secondDirectory, "WindowFixture.exe"),
            StartMinimized = false
        };
        var service = new ProcessService();
        try
        {
            await service.LaunchAsync(first, CancellationToken.None);
            await service.LaunchAsync(second, CancellationToken.None);
            Assert(service.IsRunning(first) && service.IsRunning(second), "Both same-named fixtures should be running.");

            var close = await service.CloseAsync(first, CancellationToken.None);
            Assert(close.Status == ProcessCloseStatus.ClosedGracefully, close.Message);
            Assert(!service.IsRunning(first), "The first executable is still running after its close request.");
            Assert(service.IsRunning(second), "Closing one executable also closed a same-named executable from another directory.");
        }
        finally
        {
            await service.ForceCloseAsync(first, CancellationToken.None);
            await service.ForceCloseAsync(second, CancellationToken.None);
        }
    }

    private static async Task TestDelayedLauncherCloseAsync()
    {
        using var directory = new TemporaryDirectory();
        var launcherDirectory = Path.Combine(directory.Path, "launcher");
        var childDirectory = Path.Combine(launcherDirectory, "bin");
        CopyFixtureOutput(launcherDirectory);
        CopyFixtureOutput(childDirectory);
        var startedSignal = Path.Combine(directory.Path, "started.signal");
        var app = new LaunchApplication
        {
            Name = "Delayed close fixture",
            Path = Path.Combine(launcherDirectory, "WindowFixture.exe"),
            Arguments = $"--launcher-delay-ms=4000 --child-path=\"{Path.Combine(childDirectory, "WindowFixture.exe")}\" --started-file=\"{startedSignal}\"",
            StartMinimized = true,
            CloseOnDeactivate = true
        };
        var service = new ProcessService();
        try
        {
            await service.LaunchAsync(app, CancellationToken.None);
            var close = await service.CloseAsync(app, CancellationToken.None);
            Assert(close.Status == ProcessCloseStatus.CloseScheduled, close.Message);
            Assert(await WaitForFileAsync(startedSignal, TimeSpan.FromSeconds(7)),
                "The delayed child was not created, so the close-intent path was not exercised.");
            var childId = int.Parse(await File.ReadAllTextAsync(startedSignal));
            Assert(await WaitForProcessExitAsync(childId, TimeSpan.FromSeconds(3)),
                "The delayed bin child remained running after its profile was deactivated.");
            service.CancelPendingClose(app);
            Assert(!service.IsRunning(app),
                "The completed delayed close left a stale pending-launch flag that would suppress reactivation.");
        }
        finally
        {
            await service.ForceCloseAsync(app, CancellationToken.None);
        }
    }

    private static async Task TestDifferentlyNamedLauncherChildCloseAsync()
    {
        using var directory = new TemporaryDirectory();
        var launcherDirectory = Path.Combine(directory.Path, "launcher");
        var childDirectory = Path.Combine(directory.Path, "helper");
        CopyFixtureOutput(launcherDirectory);
        CopyFixtureOutput(childDirectory);
        var originalChildPath = Path.Combine(childDirectory, "WindowFixture.exe");
        var childPath = Path.Combine(childDirectory, "CompanionProcess.exe");
        File.Move(originalChildPath, childPath);
        var startedSignal = Path.Combine(directory.Path, "different-child-started.signal");
        var app = new LaunchApplication
        {
            Name = "Different-child launcher fixture",
            Path = Path.Combine(launcherDirectory, "WindowFixture.exe"),
            Arguments = $"--launcher-delay-ms=750 --child-path=\"{childPath}\" --started-file=\"{startedSignal}\"",
            StartMinimized = false,
            CloseOnDeactivate = true
        };
        var service = new ProcessService();
        var childId = 0;
        try
        {
            var launch = await service.LaunchAsync(app, CancellationToken.None);
            Assert(launch.Started, "The differently named child fixture did not launch.");
            Assert(await WaitForFileAsync(startedSignal, TimeSpan.FromSeconds(4)),
                "The differently named child process did not start.");
            childId = int.Parse(await File.ReadAllTextAsync(startedSignal));
            var equivalentProfileEntry = app.Clone();
            Assert(service.IsRunning(equivalentProfileEntry),
                "The launched process family was not retained across equivalent profile entries.");

            var close = await service.CloseAsync(equivalentProfileEntry, CancellationToken.None);
            Assert(close.Status == ProcessCloseStatus.ClosedGracefully, close.Message);
            Assert(await WaitForProcessExitAsync(childId, TimeSpan.FromSeconds(3)),
                "The differently named child remained running after its profile was deactivated.");
        }
        finally
        {
            if (childId > 0)
            {
                try
                {
                    using var child = Process.GetProcessById(childId);
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
    }

    private static async Task TestPreHandoffLauncherCloseAsync()
    {
        using var directory = new TemporaryDirectory();
        var launcherDirectory = Path.Combine(directory.Path, "launcher");
        var childDirectory = Path.Combine(launcherDirectory, "bin");
        CopyFixtureOutput(launcherDirectory);
        CopyFixtureOutput(childDirectory);
        var childStartedSignal = Path.Combine(directory.Path, "unexpected-child.signal");
        var app = new LaunchApplication
        {
            Name = "Closeable launcher fixture",
            Path = Path.Combine(launcherDirectory, "WindowFixture.exe"),
            Arguments = $"--window-launcher-delay-ms=4000 --child-path=\"{Path.Combine(childDirectory, "WindowFixture.exe")}\" --started-file=\"{childStartedSignal}\"",
            StartMinimized = false,
            CloseOnDeactivate = true
        };
        var service = new ProcessService();
        try
        {
            await service.LaunchAsync(app, CancellationToken.None);
            var close = await service.CloseAsync(app, CancellationToken.None);
            Assert(close.Status == ProcessCloseStatus.CloseScheduled, close.Message);
            service.CancelPendingClose(app);
            Assert(!service.IsRunning(app),
                "A closed pre-handoff launcher left a pending flag that suppressed relaunch.");
            Assert(!File.Exists(childStartedSignal), "The launcher created its child after it was closed.");

            app.Arguments = string.Empty;
            await service.LaunchAsync(app, CancellationToken.None);
            Assert(service.IsRunning(app), "The same application could not be relaunched immediately.");
        }
        finally
        {
            await service.ForceCloseAsync(app, CancellationToken.None);
        }
    }

    private static async Task TestDelayedCloseOutcomeAsync()
    {
        using var directory = new TemporaryDirectory();
        var launcherDirectory = Path.Combine(directory.Path, "launcher");
        var childDirectory = Path.Combine(launcherDirectory, "bin");
        CopyFixtureOutput(launcherDirectory);
        CopyFixtureOutput(childDirectory);
        var startedSignal = Path.Combine(directory.Path, "stubborn-started.signal");
        var app = new LaunchApplication
        {
            Name = "Delayed stubborn fixture",
            Path = Path.Combine(launcherDirectory, "WindowFixture.exe"),
            Arguments = $"--launcher-delay-ms=750 --child-path=\"{Path.Combine(childDirectory, "WindowFixture.exe")}\" --started-file=\"{startedSignal}\" --ignore-close",
            StartMinimized = true,
            CloseOnDeactivate = true
        };
        var service = new ProcessService();
        var completion = new TaskCompletionSource<PendingProcessCloseOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Action<PendingProcessCloseOutcome> recordCompletion = outcome => completion.TrySetResult(outcome);
        service.PendingCloseCompleted += recordCompletion;
        try
        {
            await service.LaunchAsync(app, CancellationToken.None);
            var close = await service.CloseAsync(app, CancellationToken.None);
            Assert(close.Status == ProcessCloseStatus.CloseScheduled, close.Message);
            Assert(await WaitForFileAsync(startedSignal, TimeSpan.FromSeconds(4)),
                "The delayed stubborn child was not created.");
            var completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(completed.Result.Status == ProcessCloseStatus.ForcedClosed, completed.Result.Message);
            Assert(service.IsPendingCloseOutcomeCurrent(completed),
                "A newly published delayed-close outcome was not current.");
            Assert(!service.IsRunning(app), "The delayed unresponsive child was not force-closed.");
            service.CancelPendingClose(app);
            Assert(!service.IsPendingCloseOutcomeCurrent(completed),
                "Reactivating the application did not invalidate its queued close outcome.");
        }
        finally
        {
            service.PendingCloseCompleted -= recordCompletion;
            await service.ForceCloseAsync(app, CancellationToken.None);
        }
    }

    private static async Task TestDisplayRoundTripAsync()
    {
        using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", directory.Path);
        try
        {
            using var service = new DisplayConfigurationService();
            var snapshot = service.Capture();
            Assert(snapshot.SnapshotVersion == 3, "Unexpected display snapshot version.");
            Assert(snapshot.ActiveTargets.Count > 0, "No active display targets were captured.");
            Assert(snapshot.ActiveTargets.All(target => !string.IsNullOrWhiteSpace(target.FriendlyName)), "A display target has no friendly name.");
            Assert(snapshot.ActiveTargets.All(target => target.SourceWidth > 0 && target.SourceHeight > 0),
                "Virtual-aware CCD mode indexes did not resolve source dimensions.");
            if (snapshot.LogicalDisplayCount == 1 && snapshot.NvidiaSurround?.Enabled != true)
                Assert(snapshot.Summary.Contains("show only on", StringComparison.OrdinalIgnoreCase),
                    "A single-display Windows topology was not identified as ‘Show only on’.");
            var result = await service.RestoreAsync(snapshot, NvidiaSurroundMode.Ignore);
            Assert(File.Exists(service.RecoveryFilePath), "No emergency display recovery snapshot was written.");
            Assert(!string.IsNullOrWhiteSpace(result.Message), "Display restore returned no status.");
            var rebootLikeSnapshot = new SwitchProfile { Display = snapshot }.Clone().Display
                ?? throw new InvalidOperationException("Could not clone the display snapshot.");
            ScrambleTransientDisplayIds(rebootLikeSnapshot);
            var rejected = await service.RestoreAsync(rebootLikeSnapshot, NvidiaSurroundMode.Ignore,
                _ => Task.FromResult(false));
            Assert(!rejected.Kept, "A rejected temporary layout was reported as committed.");
        }
        finally { Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", previous); }
    }

    private static async Task TestSavedProfileSnapshotsAsync()
    {
        var document = await new ProfileStore().LoadAsync();
        var validator = typeof(DisplayConfigurationService).GetMethod("ValidateSnapshotStructures",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The display snapshot validator was not found.");
        foreach (var profile in document.Profiles.Where(profile => profile.Display is not null))
        {
            try { validator.Invoke(null, [profile.Display]); }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidOperationException($"Profile '{profile.Name}' failed: {exception.InnerException.Message}",
                    exception.InnerException);
            }
        }
    }

    private static void ScrambleTransientDisplayIds(DisplaySnapshot snapshot)
    {
        const uint adapterMask = 0x5a5a5a5a;
        const uint targetOffset = 0x10000;

        foreach (var target in snapshot.ActiveTargets)
        {
            target.AdapterLowPart ^= adapterMask;
            target.AdapterHighPart ^= unchecked((int)adapterMask);
            target.TargetId += targetOffset;
            target.SourceAdapterLowPart ^= adapterMask;
            target.SourceAdapterHighPart ^= unchecked((int)adapterMask);
        }

        snapshot.Paths = snapshot.Paths.Select(encoded =>
        {
            var bytes = Convert.FromBase64String(encoded);
            XorUInt32(bytes, 0, adapterMask);
            XorUInt32(bytes, 4, adapterMask);
            XorUInt32(bytes, 20, adapterMask);
            XorUInt32(bytes, 24, adapterMask);
            AddUInt32(bytes, 28, targetOffset);
            return Convert.ToBase64String(bytes);
        }).ToList();

        snapshot.Modes = snapshot.Modes.Select(encoded =>
        {
            var bytes = Convert.FromBase64String(encoded);
            var infoType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, sizeof(uint)));
            XorUInt32(bytes, 8, adapterMask);
            XorUInt32(bytes, 12, adapterMask);
            if (infoType is 2 or 3) AddUInt32(bytes, 4, targetOffset);
            return Convert.ToBase64String(bytes);
        }).ToList();
    }

    private static void XorUInt32(byte[] bytes, int offset, uint mask)
    {
        var span = bytes.AsSpan(offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(span,
            BinaryPrimitives.ReadUInt32LittleEndian(span) ^ mask);
    }

    private static void AddUInt32(byte[] bytes, int offset, uint amount)
    {
        var span = bytes.AsSpan(offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(span,
            unchecked(BinaryPrimitives.ReadUInt32LittleEndian(span) + amount));
    }

    private static LaunchApplication FixtureApplication(string arguments = "")
    {
        var path = Path.Combine(FixtureOutputDirectory(), "WindowFixture.exe");
        Assert(File.Exists(path), $"Fixture executable is missing at {path}");
        return new LaunchApplication
        {
            Name = "Window fixture",
            Path = path,
            ProcessName = "WindowFixture",
            Arguments = arguments,
            StartMinimized = true,
            CloseOnDeactivate = true
        };
    }

    private static string FixtureOutputDirectory() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "WindowFixture", "bin", "Release", "net8.0-windows"));

    private static void CopyFixtureOutput(string destination)
    {
        var source = FixtureOutputDirectory();
        Assert(File.Exists(Path.Combine(source, "WindowFixture.exe")), $"Fixture executable is missing at {source}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(100);
        }
        return File.Exists(path);
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return true;
            }
            catch (ArgumentException) { return true; }
            await Task.Delay(100);
        }
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static DisplayTargetSnapshot Monitor(string identity, string name, uint width, uint height,
        uint refreshHz = 60, int x = 0, int y = 0, uint rotation = 1) => new()
    {
        Identity = identity,
        FriendlyName = name,
        SourceWidth = width,
        SourceHeight = height,
        RefreshNumerator = refreshHz,
        RefreshDenominator = 1,
        SourceX = x,
        SourceY = y,
        Rotation = rotation
    };

    private static (ProfileDocument Document, SwitchProfile Target, FakeProcessService Processes,
        FakeDisplayConfigurationService Displays) PreviewFixture()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var target = new SwitchProfile { Name = "iRacing" };
        var document = new ProfileDocument { Profiles = { target } };
        return (document, target, processes, displays);
    }

    private static IReadOnlyList<PreflightItem> ItemsIn(ActivationPreflight preflight, string sectionPrefix) =>
        preflight.Sections
            .Where(section => section.Title.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => section.Items)
            .ToList();

    private static bool Mentions(IEnumerable<PreflightItem> items, PreflightSeverity severity,
        params string[] fragments) =>
        items.Any(item => item.Severity == severity &&
            fragments.All(fragment => item.Title.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    private static Task TestPreviewDisplayChangesAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        displays.CurrentSnapshot = new DisplaySnapshot
        {
            ActiveTargets =
            [
                Monitor("work-1", "Dell U2720Q", 3840, 2160),
                Monitor("side-1", "Dell P2419H", 1920, 1080, x: 3840)
            ]
        };
        target.Display = new DisplaySnapshot
        {
            ActiveTargets =
            [
                Monitor("work-1", "Dell U2720Q", 2560, 1440, refreshHz: 120),
                Monitor("sim-1", "LG 27GL850", 2560, 1440, refreshHz: 144, x: 2560)
            ]
        };

        var preflight = new ActivationPreflightService(displays, processes).Build(document, target);
        var items = ItemsIn(preflight, "Displays");

        Assert(Mentions(items, PreflightSeverity.Info, "LG 27GL850", "Enable"),
            "A monitor only in the target layout should be reported as being enabled.");
        Assert(Mentions(items, PreflightSeverity.Caution, "Dell P2419H", "Disable"),
            "A monitor dropped by the target layout should be a caution, not a silent change.");
        Assert(items.Any(item => item.Title.Contains("Dell U2720Q", StringComparison.Ordinal) &&
                                 item.Title.Contains("3840", StringComparison.Ordinal) &&
                                 item.Title.Contains("2560", StringComparison.Ordinal) &&
                                 item.Title.Contains("120", StringComparison.Ordinal)),
            "A monitor kept by both layouts should report its resolution and refresh change.");
        return Task.CompletedTask;
    }

    private static Task TestPreviewApplicationPlanAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        var previous = new SwitchProfile { Name = "Work" };
        previous.Applications.Add(new LaunchApplication { Name = "Teams", Path = @"C:\apps\teams.exe" });
        previous.Applications.Add(new LaunchApplication
        {
            Name = "Password manager", Path = @"C:\apps\vault.exe", CloseOnDeactivate = false
        });
        document.Profiles.Add(previous);
        document.ActiveProfileId = previous.Id;

        target.Applications.Add(new LaunchApplication { Name = "iRacing", Path = @"C:\sim\iracing.exe" });
        target.Applications.Add(new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" });

        processes.RunningPaths.Add(@"C:\apps\teams.exe");
        processes.RunningPaths.Add(@"C:\apps\vault.exe");
        processes.RunningPaths.Add(@"C:\sim\crew.exe");

        var preflight = new ActivationPreflightService(displays, processes).Build(document, target);
        var closing = ItemsIn(preflight, "Applications in Work");
        var starting = ItemsIn(preflight, "Applications in iRacing");

        Assert(Mentions(closing, PreflightSeverity.Caution, "Close", "Teams"),
            "A running app from the previous profile with Close on switch should be reported as closing.");
        Assert(Mentions(closing, PreflightSeverity.Info, "Password manager", "stays running"),
            "An app with Close on switch disabled should be reported as staying open.");
        Assert(Mentions(starting, PreflightSeverity.Info, "Start", "iRacing"),
            "An app that is not running should be reported as starting.");
        Assert(Mentions(starting, PreflightSeverity.Info, "Crew Chief", "already running"),
            "An app that is already running should not be reported as starting.");
        Assert(!preflight.HasProblems, "A healthy plan should report no problems.");
        return Task.CompletedTask;
    }

    private static Task TestPreviewApplicationProblemsAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        processes.ThrowOnResolvePath = @"C:\sim\missing.exe";
        target.Applications.Add(new LaunchApplication { Name = "Missing", Path = @"C:\sim\missing.exe" });
        target.Applications.Add(new LaunchApplication
        {
            Name = "Bad working directory",
            Path = @"C:\sim\ok.exe",
            WorkingDirectory = Path.Combine(Path.GetTempPath(), "SherpaManager.Tests", Guid.NewGuid().ToString("N"))
        });

        var preflight = new ActivationPreflightService(displays, processes).Build(document, target);
        var items = ItemsIn(preflight, "Applications in iRacing");

        Assert(Mentions(items, PreflightSeverity.Problem, "Missing", "cannot be found"),
            "An unresolvable executable should be a problem, not a silent failure at switch time.");
        Assert(Mentions(items, PreflightSeverity.Problem, "Bad working directory", "working directory"),
            "A missing working directory should be reported before the switch starts.");
        Assert(preflight.ProblemCount == 2, $"Expected two problems, got {preflight.ProblemCount}.");
        Assert(preflight.HasProblems, "A preview with problems should say so.");
        return Task.CompletedTask;
    }

    private static Task TestPreviewSurroundAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        target.NvidiaSurroundMode = NvidiaSurroundMode.RequireEnabled;
        displays.CurrentSnapshot = new DisplaySnapshot
        {
            NvidiaSurround = new NvidiaSurroundSnapshot
            {
                ApiAvailable = true, StatusKnown = true, Enabled = false, IsPossible = true
            }
        };

        var service = new ActivationPreflightService(displays, processes);
        var items = ItemsIn(service.Build(document, target), "NVIDIA Surround");
        Assert(Mentions(items, PreflightSeverity.Caution, "Surround will be turned on"),
            "A Surround transition should be flagged as a change needing attention.");

        displays.CurrentSnapshot = new DisplaySnapshot
        {
            NvidiaSurround = new NvidiaSurroundSnapshot { ApiAvailable = false, Description = "no NVAPI" }
        };
        var unavailable = ItemsIn(service.Build(document, target), "NVIDIA Surround");
        Assert(Mentions(unavailable, PreflightSeverity.Problem, "NVAPI is unavailable"),
            "Requiring Surround without NVAPI should be a problem the user sees before switching.");

        target.NvidiaSurroundMode = NvidiaSurroundMode.Ignore;
        var ignored = ItemsIn(service.Build(document, target), "NVIDIA Surround");
        Assert(Mentions(ignored, PreflightSeverity.Info, "will not be managed"),
            "Do not manage should say plainly that Surround is left alone.");
        return Task.CompletedTask;
    }

    private static Task TestPreviewIsReadOnlyAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        var previous = new SwitchProfile { Name = "Work" };
        previous.Applications.Add(new LaunchApplication { Name = "Teams", Path = @"C:\apps\teams.exe" });
        document.Profiles.Add(previous);
        document.ActiveProfileId = previous.Id;
        target.Applications.Add(new LaunchApplication { Name = "iRacing", Path = @"C:\sim\iracing.exe" });
        target.Display = new DisplaySnapshot { ActiveTargets = [Monitor("sim-1", "LG 27GL850", 2560, 1440)] };
        processes.RunningPaths.Add(@"C:\apps\teams.exe");

        new ActivationPreflightService(displays, processes).Build(document, target);

        Assert(processes.Events.Count == 0,
            $"The preview must not launch or close anything, but recorded: {string.Join(", ", processes.Events)}");
        Assert(processes.Launched.Count == 0 && processes.Closed.Count == 0 && processes.Forced.Count == 0,
            "The preview must not touch process lifecycle.");
        Assert(displays.RecoveryRestoreCalls == 0 && displays.ConfirmationRestoreCalls == 0,
            "The preview must not apply or roll back any display layout.");
        Assert(document.ActiveProfileId == previous.Id, "The preview must not change the active profile.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The preview is only useful if the user can actually read it, so this
    /// builds the real window and confirms every predicted item reached the
    /// visual tree with no data binding errors.
    /// </summary>
    private static Task TestPreviewWindowRendersAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        var previous = new SwitchProfile { Name = "Work" };
        previous.Applications.Add(new LaunchApplication { Name = "Teams", Path = @"C:\apps\teams.exe" });
        document.Profiles.Add(previous);
        document.ActiveProfileId = previous.Id;
        processes.RunningPaths.Add(@"C:\apps\teams.exe");
        processes.ThrowOnResolvePath = @"C:\sim\missing.exe";
        target.Applications.Add(new LaunchApplication { Name = "Missing", Path = @"C:\sim\missing.exe" });
        target.Applications.Add(new LaunchApplication { Name = "iRacing", Path = @"C:\sim\iracing.exe" });
        displays.CurrentSnapshot = new DisplaySnapshot
        {
            ActiveTargets = [Monitor("side-1", "Dell P2419H", 1920, 1080)]
        };
        target.Display = new DisplaySnapshot
        {
            ActiveTargets = [Monitor("sim-1", "LG 27GL850", 2560, 1440, refreshHz: 144)]
        };

        var preflight = new ActivationPreflightService(displays, processes).Build(document, target);
        var expected = preflight.AllItems.Select(item => item.Title).ToList();

        Exception? failure = null;
        var bindingErrors = new List<string>();
        var rendered = new List<string>();

        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var listener = new BindingErrorListener(bindingErrors);
                System.Diagnostics.PresentationTraceSources.Refresh();
                System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                    System.Diagnostics.SourceLevels.Error | System.Diagnostics.SourceLevels.Warning;

                var window = new ActivationPreflightWindow(preflight)
                {
                    ShowActivated = false,
                    Opacity = 0,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                    Left = -32000,
                    Top = -32000
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    CollectText(window, rendered);
                }
                finally
                {
                    window.Close();
                    System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60));

        if (failure is not null) throw new InvalidOperationException("The preview window failed to render.", failure);
        Assert(bindingErrors.Count == 0,
            $"The preview window reported data binding errors: {string.Join(" | ", bindingErrors)}");
        Assert(expected.Count > 0, "The fixture should produce at least one preview item.");

        var missing = expected.Where(title => !rendered.Contains(title)).ToList();
        Assert(missing.Count == 0,
            $"These preview items never reached the window: {string.Join(" | ", missing)}");
        return Task.CompletedTask;
    }

    private static void CollectText(System.Windows.DependencyObject root, List<string> into)
    {
        if (root is System.Windows.Controls.TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
            into.Add(text.Text);
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            CollectText(System.Windows.Media.VisualTreeHelper.GetChild(root, index), into);
    }

    private sealed class BindingErrorListener(List<string> errors) : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) errors.Add(message);
        }
    }

    private sealed class FakeProcessService : IProcessService
    {
        public List<string> Events { get; } = [];
        public List<Guid> Closed { get; } = [];
        public List<Guid> Forced { get; } = [];
        public List<Guid> Launched { get; } = [];
        public HashSet<string> RunningPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ProcessCloseResult> CloseResultsByPath { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public ProcessCloseResult CloseResult { get; set; } =
            new(ProcessCloseStatus.ClosedGracefully, 1, "closed");
        public string? ThrowOnLaunchPath { get; set; }
        public string? ThrowOnResolvePath { get; set; }

        public ResolvedLaunchTarget Resolve(LaunchApplication app)
        {
            if (app.Path.Equals(ThrowOnResolvePath, StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException($"Could not find {app.Name}.", app.Path);
            return new(app.Path, app.Path, Path.GetFileNameWithoutExtension(app.Path),
                "fake:" + app.Path.ToUpperInvariant(), false);
        }
        public void Validate(LaunchApplication app) { }
        public string GetIdentityKey(LaunchApplication app) => Resolve(app).IdentityKey;
        public void CancelPendingClose(LaunchApplication app) { }
        public bool IsPendingCloseOutcomeCurrent(PendingProcessCloseOutcome outcome) => true;
        public bool IsRunning(LaunchApplication app) => RunningPaths.Contains(app.Path);
        public Task<ProcessLaunchResult> LaunchAsync(LaunchApplication app, CancellationToken cancellationToken)
        {
            if (app.Path.Equals(ThrowOnLaunchPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("simulated launch failure");
            Events.Add("launch:" + app.Path);
            Launched.Add(app.Id);
            RunningPaths.Add(app.Path);
            return Task.FromResult(new ProcessLaunchResult(true, app.StartMinimized, "started"));
        }
        public Task<bool> MinimizeAsync(LaunchApplication app, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<ProcessCloseResult> CloseAsync(LaunchApplication app, CancellationToken cancellationToken)
        {
            Events.Add("close:" + app.Path);
            Closed.Add(app.Id);
            var result = CloseResultsByPath.GetValueOrDefault(app.Path, CloseResult);
            if (result.Succeeded && result.Status != ProcessCloseStatus.Superseded)
                RunningPaths.Remove(app.Path);
            return Task.FromResult(result);
        }
        public Task<ProcessCloseResult> ForceCloseAsync(LaunchApplication app, CancellationToken cancellationToken)
        {
            Forced.Add(app.Id);
            return Task.FromResult(new ProcessCloseResult(ProcessCloseStatus.ForcedClosed, 1, "force-closed"));
        }
    }

    private sealed class FakeDisplayConfigurationService(List<string> events) : IDisplayConfigurationService
    {
        public DisplayRestoreResult TargetResult { get; set; } =
            new(false, "display applied");
        public DisplayRestoreResult RecoveryResult { get; set; } =
            new(false, "display restored");
        public int RecoveryRestoreCalls { get; private set; }
        public int ConfirmationRestoreCalls { get; private set; }
        public bool ConfirmOnlyWhenVerificationChanged { get; private set; }
        public bool VerificationEnvironmentChanged { get; set; } = true;
        public DisplaySnapshot CurrentSnapshot { get; set; } = new();
        public Exception? CaptureFailure { get; set; }

        public DisplaySnapshot Capture() =>
            CaptureFailure is null ? CurrentSnapshot : throw CaptureFailure;

        public Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot,
            NvidiaSurroundMode surroundMode, CancellationToken cancellationToken = default)
        {
            events.Add("display:apply");
            return Task.FromResult(TargetResult);
        }

        public async Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot,
            NvidiaSurroundMode surroundMode, Func<DisplaySnapshot, Task<bool>> confirm,
            CancellationToken cancellationToken = default,
            bool confirmOnlyWhenVerificationChanged = false)
        {
            events.Add("display:apply");
            ConfirmationRestoreCalls++;
            ConfirmOnlyWhenVerificationChanged = confirmOnlyWhenVerificationChanged;
            if (confirmOnlyWhenVerificationChanged && !VerificationEnvironmentChanged)
                return TargetResult;
            return await confirm(snapshot) ? TargetResult : new DisplayRestoreResult(false, "reverted", Kept: false);
        }

        public Task<DisplayRestoreResult> RestoreLastRecoveryAsync(
            CancellationToken cancellationToken = default)
        {
            RecoveryRestoreCalls++;
            events.Add("display:rollback");
            return Task.FromResult(RecoveryResult);
        }
    }

    private sealed class FakeNvidiaSurroundService : INvidiaSurroundService
    {
        public int GetStatusCalls { get; private set; }
        public int ApplyConfigurationCalls { get; private set; }

        public NvidiaSurroundSnapshot GetStatus()
        {
            GetStatusCalls++;
            return new NvidiaSurroundSnapshot { ApiAvailable = true, StatusKnown = true };
        }

        public void ApplyConfiguration(NvidiaSurroundSnapshot snapshot, bool enabled) => ApplyConfigurationCalls++;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SherpaManager.Tests"));
        public string Path { get; } = System.IO.Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            var resolvedPath = System.IO.Path.GetFullPath(Path);
            if (!resolvedPath.StartsWith(TestRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            try { Directory.Delete(resolvedPath, recursive: true); }
            catch { }
        }
    }
}
