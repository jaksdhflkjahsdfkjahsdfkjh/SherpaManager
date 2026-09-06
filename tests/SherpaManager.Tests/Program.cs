using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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
            ("Activation preview window renders every item", TestPreviewWindowRendersAsync),
            ("Command line parses --activate in both forms", TestCommandLineParsingAsync),
            ("Hotkeys parse, canonicalise, and reject unusable combinations", TestHotkeyParsingAsync),
            ("A second launch hands its profile to the running instance", TestSingleInstanceProfileHandoffAsync),
            ("Desktop shortcut names survive awkward profile names", TestShortcutFileNameAsync),
            ("A duplicated profile does not inherit the shortcut", TestProfileCloneDropsHotkeyAsync),
            ("A restored window is not minimized again", TestRestoredWindowStaysRestoredAsync),
            ("Applications wait for the displays to settle", TestDisplaySettleDelayAsync),
            ("The display settle delay is bounded", TestDisplaySettleDelayBoundsAsync),
            ("The NVIDIA app is found where it is actually installed", TestNvidiaAppLocatorAsync),
            ("Display layouts scale and place every monitor", TestDisplayLayoutGeometryAsync),
            ("Audio endpoints can be enumerated", TestAudioEnumerationAsync),
            ("Audio output switches before applications start", TestAudioAppliedBeforeLaunchAsync),
            ("A disconnected audio device warns without failing the switch", TestAudioMissingDeviceAsync),
            ("An audio change that silently does nothing is reported", TestAudioSilentlyIgnoredAsync),
            ("Audio preview reports the pending change", TestAudioPreviewAsync),
            ("A monitor audio endpoint is waited for after the display comes up", TestAudioEndpointAppearsLateAsync),
            ("Audio input switches independently of output", TestAudioInputAsync),
            ("The displayed version drops build metadata", TestAppVersionFormatAsync),
            ("An application is waited for before the next one starts", TestReadinessGatesNextLaunchAsync),
            ("Applications Sherpa cannot track are not waited for", TestReadinessSkipsUntrackableAsync),
            ("Applications start in the order they are listed", TestLaunchOrderAsync),
            ("A readiness timeout warns without failing the switch", TestReadinessTimeoutAsync),
            ("Readiness detects a real fixture window", TestReadinessDetectsWindowAsync),
            ("Waiting for an app to start outlasts a slow startup", TestReadinessOutlastsSlowStartupAsync),
            ("Display layouts survive layouts that cannot be drawn", TestDisplayLayoutEdgeCasesAsync),
            ("The display layout window renders every monitor", TestDisplayLayoutWindowRendersAsync),
            ("Application rows are numbered", TestApplicationRowNumbersRenderAsync),
            ("The launch order skips entries that will not start", TestOrderLabelsAsync),
            ("Row headers show the order, and a warning instead of it", TestRowHeaderStyleRendersAsync),
            ("Editing the list reflags and renumbers it without being asked", TestIssueWatcherAsync),
            ("The lock button shows the state dragging is actually in", TestDragLockButtonAsync),
            ("The row number column is present when a profile has no applications", TestRowHeaderOnEmptyGridAsync),
            ("The row number column is present on the first profile selection", TestRowHeaderOnFirstSelectionAsync),
            ("A switch is recorded exactly as it was reported", TestActivationRecordedAsync),
            ("A cancelled switch is still recorded", TestCancelledActivationRecordedAsync),
            ("The switch history window renders a recorded switch", TestActivationHistoryWindowRendersAsync),
            ("Switches are numbered and labelled in the history", TestActivationHistoryLabellingAsync),
            ("Unusable applications are flagged in the editor", TestApplicationIssuesAsync),
            ("Editor flags match what the activation preview reports", TestApplicationIssuesMatchPreviewAsync),
            ("Tooltips use the Sherpa theme", TestToolTipThemedAsync),
            ("HDR follows the profile across changing hardware", TestAdvancedColorPlanAsync),
            ("Advanced colour flags are read at the right bits", TestAdvancedColorFlagsAsync),
            ("Advanced colour is readable from real displays", TestAdvancedColorReadsRealDisplaysAsync),
            ("The graphics vendor is read from the adapter path", TestAdapterVendorParsingAsync),
            ("Surround is refused when no display is on the NVIDIA card", TestHybridGraphicsPreviewAsync),
            ("Adapters are read from the real machine", TestAdaptersReadFromRealHardwareAsync),
            ("A single ultrawide is not mistaken for combined monitors", TestUltrawideIsNotCombinedAsync),
            ("Installed applications are read from the Start menu", TestInstalledCatalogAsync),
            ("Searching finds an app by name, publisher, or path", TestInstalledCatalogSearchAsync),
            ("The application picker lists, filters, and chooses", TestApplicationPickerRendersAsync)
        };

        var selectedTests = tests.ToList();
        if (Environment.GetEnvironmentVariable("SHERPA_HARDWARE_TESTS") == "1")
            selectedTests.Add(("Display capture and same-topology recovery round trip", TestDisplayRoundTripAsync));
        if (Environment.GetEnvironmentVariable("SHERPA_VALIDATE_SAVED_PROFILES") == "1")
            selectedTests.Add(("Saved profile display snapshots pass preflight", TestSavedProfileSnapshotsAsync));

        // SHERPA_TEST_FILTER runs a subset by name substring, which is how a
        // fixture test gets isolated from the watchers other fixture tests leave
        // running behind them.
        var filter = Environment.GetEnvironmentVariable("SHERPA_TEST_FILTER");
        if (!string.IsNullOrWhiteSpace(filter))
        {
            selectedTests = selectedTests
                .Where(test => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Console.WriteLine($"Filter '{filter}' selected {selectedTests.Count} test(s).");
        }

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

    /// <summary>
    /// The catalog is read from real Windows shortcuts, so it is checked against
    /// real ones: a fabricated Start menu, built the same way an installer builds
    /// the real one.
    /// </summary>
    private static Task TestInstalledCatalogAsync()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "Programs");
        var publisher = Path.Combine(root, "Rhinode LLC");
        var nested = Path.Combine(root, "MOZA", "Tools");
        Directory.CreateDirectory(publisher);
        Directory.CreateDirectory(nested);

        // Real executables to point at, so resolution has something to resolve.
        var paints = Path.Combine(directory.Path, "TradingPaints.exe");
        var pit = Path.Combine(directory.Path, "PitHouse.exe");
        var gone = Path.Combine(directory.Path, "Removed.exe");
        var notAnExe = Path.Combine(directory.Path, "Manual.pdf");
        foreach (var file in new[] { paints, pit, gone, notAnExe }) File.WriteAllText(file, "x");

        CreateShortcut(Path.Combine(publisher, "Trading Paints.lnk"), paints, "--tray", directory.Path);
        CreateShortcut(Path.Combine(root, "Pit House.lnk"), pit);
        // The same executable reached by a longer name: one application, not two.
        CreateShortcut(Path.Combine(nested, "Pit House (64-bit).lnk"), pit);
        // Never a thing a profile launches.
        CreateShortcut(Path.Combine(publisher, "Uninstall Trading Paints.lnk"), paints);
        CreateShortcut(Path.Combine(root, "Trading Paints Help.lnk"), paints);
        // A target that no longer exists, and one that is not a program at all.
        CreateShortcut(Path.Combine(root, "Removed App.lnk"), gone);
        File.Delete(gone);
        CreateShortcut(Path.Combine(root, "Manual.lnk"), notAnExe);

        var found = InstalledApplicationCatalog.Scan([root]);
        var names = found.Select(app => app.Name).ToList();

        Assert(names.Count == 2, $"Expected two applications, got: {string.Join(", ", names)}");
        // Ordered by name, so the list reads the way the Start menu reads.
        Assert(names[0] == "Pit House" && names[1] == "Trading Paints",
            $"Unexpected order: {string.Join(", ", names)}");

        var trading = found.Single(app => app.Name == "Trading Paints");
        Assert(trading.Path == paints, $"Wrong target: {trading.Path}");
        Assert(trading.Arguments == "--tray", $"Shortcut arguments were lost: '{trading.Arguments}'");
        Assert(trading.WorkingDirectory == directory.Path, $"Working directory was lost: '{trading.WorkingDirectory}'");
        Assert(trading.Group == "Rhinode LLC", $"The publisher folder should be the group; got '{trading.Group}'.");

        // The shorter of two names for the same executable, and no group because
        // it sits at the top level.
        var pitHouse = found.Single(app => app.Name == "Pit House");
        Assert(pitHouse.Group.Length == 0, $"A top-level shortcut should have no group; got '{pitHouse.Group}'.");

        Assert(!names.Any(name => name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)),
            "An uninstaller is not an application to launch.");
        Assert(!names.Any(name => name.Contains("Help", StringComparison.OrdinalIgnoreCase)),
            "A help link is not an application to launch.");
        Assert(!names.Contains("Removed App"), "A shortcut whose target is gone must not be offered.");
        Assert(!names.Contains("Manual"), "A shortcut to a document is not an application.");

        // A folder that does not exist is normal: not every machine has both.
        Assert(InstalledApplicationCatalog.Scan([Path.Combine(directory.Path, "absent")]).Count == 0,
            "Scanning a missing folder should find nothing rather than fail.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Searching has to find an application by any of the things the user might
    /// remember about it, and by more than one of them at once.
    /// </summary>
    private static Task TestInstalledCatalogSearchAsync()
    {
        var app = new InstalledApplication("MOZA Pit House",
            Path.Combine("C:", "Program Files (x86)", "MOZA Pit House", "MOZA Pit House.exe"),
            string.Empty, string.Empty, "MOZA Racing");

        Assert(InstalledApplicationCatalog.Matches(app, string.Empty), "An empty search should match everything.");
        Assert(InstalledApplicationCatalog.Matches(app, "moza"), "Searching by name should match.");
        Assert(InstalledApplicationCatalog.Matches(app, "PIT house"), "Searching should ignore case.");
        Assert(InstalledApplicationCatalog.Matches(app, "racing"), "Searching by publisher should match.");
        Assert(InstalledApplicationCatalog.Matches(app, "program files"), "Searching by path should match.");
        // Words are matched separately, so a half-remembered name still finds it.
        Assert(InstalledApplicationCatalog.Matches(app, "house moza"), "Words in any order should match.");
        Assert(InstalledApplicationCatalog.Matches(app, "moza racing house"), "Words from name and publisher should match.");
        Assert(!InstalledApplicationCatalog.Matches(app, "iracing"), "An unrelated search must not match.");
        Assert(!InstalledApplicationCatalog.Matches(app, "moza iracing"), "One missing word should reject the entry.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The picker is what the user actually sees, so the list, the search box, and
    /// the state of the Add button are rendered rather than assumed.
    /// </summary>
    private static Task TestApplicationPickerRendersAsync()
    {
        var catalog = new List<InstalledApplication>
        {
            new("MOZA Pit House", Path.Combine("C:", "moza", "MOZA Pit House.exe"), string.Empty, string.Empty, "MOZA Racing"),
            new("Trading Paints", Path.Combine("C:", "paints", "Trading Paints.exe"), string.Empty, string.Empty, "Rhinode LLC"),
            new("Visual Studio Code", Path.Combine("C:", "code", "Code.exe"), string.Empty, string.Empty, string.Empty)
        };

        var rendered = new List<string>();
        var filtered = new List<string>();
        var addEnabledAtRest = false;
        var addLabel = string.Empty;
        var chosen = new List<string>();

        OnUiThread(() =>
        {
            var window = new ApplicationPickerWindow(catalog)
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

                var list = (System.Windows.Controls.ListBox)window.FindName("ResultList");
                var search = (System.Windows.Controls.TextBox)window.FindName("SearchBox");
                var add = (System.Windows.Controls.Button)window.FindName("AddButton");

                // The first result is selected, so Enter works without touching
                // the mouse.
                addEnabledAtRest = add.IsEnabled;

                search.Text = "paints";
                window.UpdateLayout();
                filtered.AddRange(list.Items.OfType<ApplicationPickerWindow.PickerRow>().Select(row => row.Name));

                search.Text = string.Empty;
                window.UpdateLayout();
                list.SelectAll();
                window.UpdateLayout();
                addLabel = add.Content as string ?? string.Empty;
                chosen.AddRange(list.SelectedItems.OfType<ApplicationPickerWindow.PickerRow>().Select(row => row.Name));
            }
            finally { window.Close(); }
        });

        foreach (var name in new[] { "MOZA Pit House", "Trading Paints", "Visual Studio Code" })
            Assert(rendered.Contains(name), $"'{name}' was not drawn. Rendered: {string.Join(" | ", rendered)}");
        Assert(rendered.Any(text => text.Contains("MOZA Racing", StringComparison.Ordinal)),
            "The publisher should be shown so two similar entries can be told apart.");

        Assert(addEnabledAtRest, "The first result should be selected, so Add works straight away.");
        Assert(filtered is ["Trading Paints"], $"Searching did not filter: {string.Join(", ", filtered)}");
        Assert(chosen.Count == 3, $"The list should allow choosing several; got {chosen.Count}.");
        Assert(addLabel.Contains('3'), $"The button should say how many will be added; it said '{addLabel}'.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a real Windows shortcut, the same way an installer does.
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string target, string arguments = "",
        string workingDirectory = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("WScript.Shell could not be created.");
        object? shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                null, shell, [shortcutPath]);
            if (shortcut is null) throw new InvalidOperationException("The shortcut could not be created.");

            var type = shortcut.GetType();
            void Set(string name, string value) => type.InvokeMember(name,
                System.Reflection.BindingFlags.SetProperty, null, shortcut, [value]);

            Set("TargetPath", target);
            if (arguments.Length > 0) Set("Arguments", arguments);
            if (workingDirectory.Length > 0) Set("WorkingDirectory", workingDirectory);
            type.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>
    /// The rules that decide what happens to HDR when a profile meets hardware it
    /// was not captured on.
    /// </summary>
    /// <remarks>
    /// Written on a machine with no HDR display, which is exactly why the decision
    /// is separated from the Windows call: the call can only be checked here for
    /// not failing, but the rules are what turn someone's HDR off by mistake.
    /// </remarks>
    private static Task TestAdvancedColorPlanAsync()
    {
        const string port = @"\\?\DISPLAY#AUS2421#5&2b36889e&0&UID4352";

        static DisplayTargetSnapshot Saved(bool captured, bool supported, bool enabled) => new()
        {
            FriendlyName = "OLED ultrawide",
            MonitorDevicePath = port,
            // Deliberately stale: the ids a profile was saved with are not the ids
            // it comes back as, so nothing may match on them.
            AdapterLowPart = 111,
            AdapterHighPart = 222,
            TargetId = 333,
            AdvancedColorCaptured = captured,
            AdvancedColorSupported = supported,
            AdvancedColorEnabled = enabled
        };

        static Dictionary<string, (string Display, DisplayConfigurationService.LiveAdvancedColor State)>
            Live(bool readable, bool supported, bool enabled, bool forceDisabled = false) => new()
            {
                [port] = ("OLED ultrawide",
                    new DisplayConfigurationService.LiveAdvancedColor(readable, supported, enabled, forceDisabled))
            };

        // A profile saved before Sherpa read HDR knows nothing; it must not be
        // read as "HDR was off" and switch off a display someone set up by hand.
        var plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(captured: false, supported: false, enabled: false)], Live(true, true, true));
        Assert(plan.Changes.Count == 0,
            "A profile captured before Sherpa understood HDR must leave HDR alone.");
        Assert(plan.Warnings.Count == 0, "Leaving HDR alone is not worth a warning.");

        // The same rule on its own, with the other guards deliberately out of the
        // way: not having read HDR is decisive by itself.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(captured: false, supported: true, enabled: false)], Live(true, true, enabled: true));
        Assert(plan.Changes.Count == 0,
            "Never having read a display's HDR must be enough on its own to leave it alone.");

        // The ordinary cases, in both directions.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: true)], Live(true, true, enabled: false));
        Assert(plan.Changes is [{ Enable: true }],
            $"HDR should be turned back on; planned {plan.Changes.Count} changes.");
        Assert(plan.Changes[0].Display == "OLED ultrawide", "The change should name the display.");

        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: false)], Live(true, true, enabled: true));
        Assert(plan.Changes is [{ Enable: false }], "HDR should be turned off for a profile that had it off.");

        // Already right: touching it would flicker the display for nothing.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: true)], Live(true, true, enabled: true));
        Assert(plan.Changes.Count == 0 && plan.Warnings.Count == 0,
            "A display already in the right state should be left alone.");

        // The profile moved to another PC, or the monitor was replaced by one that
        // cannot do HDR. Ordinary, and not a warning.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: true)], Live(true, supported: false, enabled: false));
        Assert(plan.Changes.Count == 0, "A display that cannot do HDR must not be asked to.");
        Assert(plan.Warnings.Count == 0, "Hardware that cannot do HDR is not a failure.");

        // Windows older than 1709, or a driver that refuses the request.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: true)], Live(readable: false, supported: false, enabled: false));
        Assert(plan.Changes.Count == 0, "Nothing can be planned for a display Windows will not describe.");
        Assert(plan.Warnings is [var unreadable] && unreadable.Contains("could not be read", StringComparison.Ordinal),
            $"An unreadable display should say so. Warnings: {string.Join(" | ", plan.Warnings)}");

        // Windows itself is holding HDR off, usually because the mode cannot carry
        // it. Saying why beats silently doing nothing.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: true)], Live(true, true, enabled: false, forceDisabled: true));
        Assert(plan.Changes.Count == 0, "HDR that Windows is forcing off cannot be turned on.");
        Assert(plan.Warnings is [var forced] && forced.Contains("holding HDR off", StringComparison.Ordinal),
            $"A forced-off display should explain itself. Warnings: {string.Join(" | ", plan.Warnings)}");

        // Turning HDR off is still allowed while Windows is forcing it off, since
        // that is where the display already is.
        plan = DisplayConfigurationService.PlanAdvancedColor(
            [Saved(true, true, enabled: false)], Live(true, true, enabled: false, forceDisabled: true));
        Assert(plan.Changes.Count == 0 && plan.Warnings.Count == 0,
            "A display already off needs nothing, forced or not.");

        // The monitor is not connected any more.
        plan = DisplayConfigurationService.PlanAdvancedColor([Saved(true, true, enabled: true)],
            new Dictionary<string, (string, DisplayConfigurationService.LiveAdvancedColor)>());
        Assert(plan.Changes.Count == 0 && plan.Warnings.Count == 0,
            "A display that is no longer attached is not a problem.");

        // Matching is by monitor device path. The saved adapter and target ids are
        // stale on purpose above, and every case so far matched anyway.
        plan = DisplayConfigurationService.PlanAdvancedColor([Saved(true, true, enabled: true)],
            new Dictionary<string, (string Display, DisplayConfigurationService.LiveAdvancedColor State)>
            {
                [@"\\?\DISPLAY#OTHER#1&abcdef&0&UID1"] =
                    ("Some other panel", new DisplayConfigurationService.LiveAdvancedColor(true, true, false, false))
            });
        Assert(plan.Changes.Count == 0, "A different monitor must not inherit this profile's HDR setting.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The order of the flags inside the advanced-colour bitfield.
    /// </summary>
    /// <remarks>
    /// Every bit reads zero on a display without HDR, so hardware here cannot tell
    /// a correct decoder from one with the bits transposed. This can.
    /// </remarks>
    private static Task TestAdvancedColorFlagsAsync()
    {
        var none = DisplayConfigurationService.DecodeAdvancedColorFlags(0);
        Assert(!none.Supported && !none.Enabled && !none.ForceDisabled, "Zero means nothing is set.");
        Assert(none.Readable, "A value that was read back is readable by definition.");

        // Bit 0 alone: the display can do HDR, and it is off. This is the pairing a
        // transposed decoder turns into "enabled but unsupported".
        var supported = DisplayConfigurationService.DecodeAdvancedColorFlags(0b0001);
        Assert(supported.Supported && !supported.Enabled,
            "Bit 0 is advancedColorSupported; reading it as enabled would switch HDR on where it is not supported.");

        var on = DisplayConfigurationService.DecodeAdvancedColorFlags(0b0011);
        Assert(on.Supported && on.Enabled && !on.ForceDisabled, "Bits 0 and 1 are supported and enabled.");

        // Bit 2 is wideColorEnforced, which Sherpa does not act on. Reading it as
        // forceDisabled would make every wide-gamut display look blocked.
        var wideColor = DisplayConfigurationService.DecodeAdvancedColorFlags(0b0101);
        Assert(wideColor.Supported && !wideColor.ForceDisabled,
            "Bit 2 is wideColorEnforced, not advancedColorForceDisabled.");

        var forced = DisplayConfigurationService.DecodeAdvancedColorFlags(0b1001);
        Assert(forced.Supported && !forced.Enabled && forced.ForceDisabled, "Bit 3 is advancedColorForceDisabled.");

        // The upper bits are reserved and must not leak into any flag.
        var reserved = DisplayConfigurationService.DecodeAdvancedColorFlags(0xFFFF_FFF0);
        Assert(!reserved.Supported && !reserved.Enabled && !reserved.ForceDisabled,
            "Reserved bits must not be read as flags.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The advanced-colour request has to be one Windows accepts. A wrong struct
    /// size or field order is rejected, so a real capture that comes back marked
    /// as read is the evidence the layout is right.
    /// </summary>
    private static Task TestAdvancedColorReadsRealDisplaysAsync()
    {
        var snapshot = new DisplayConfigurationService().Capture();
        Assert(snapshot.ActiveTargets.Count > 0, "The machine running the tests has no active display.");

        foreach (var target in snapshot.ActiveTargets)
        {
            Assert(target.AdvancedColorCaptured,
                $"Windows refused the advanced colour request for {target.FriendlyName}, " +
                "which usually means the struct size or field order is wrong.");
            // Reported by the same call, so implausible values would mean the
            // fields are being read at the wrong offsets.
            Assert(target.BitsPerColorChannel is > 0 and <= 16,
                $"{target.FriendlyName} reported {target.BitsPerColorChannel} bits per channel.");
            Assert(target.ColorEncoding <= 4, $"{target.FriendlyName} reported colour encoding {target.ColorEncoding}.");
            // HDR cannot be on where it is not supported; that pairing would mean
            // the two flag bits are swapped.
            Assert(target.AdvancedColorSupported || !target.AdvancedColorEnabled,
                $"{target.FriendlyName} reports HDR enabled but unsupported, so the flag bits are misread.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reading the graphics vendor out of an adapter device path.
    /// </summary>
    /// <remarks>
    /// The shapes here are real: the first is this machine's own adapter path,
    /// and the rest are the forms Windows produces for virtual and non-PCI
    /// adapters, which have to come back as "no vendor" rather than as a wrong
    /// one.
    /// </remarks>
    private static Task TestAdapterVendorParsingAsync()
    {
        static (uint Id, string Name) Parse(string path) =>
            DisplayConfigurationService.ParseAdapterVendor(path);

        var nvidia = Parse(@"\\?\PCI#VEN_10DE&DEV_1F06&SUBSYS_C7531462&REV_A1#4&8FC2AB9&0&0009#{5B45201D-F2F2-4F3B-85BB-30FF1F953599}");
        Assert(nvidia == (0x10DE, "NVIDIA"), $"Expected NVIDIA, got 0x{nvidia.Id:X4} '{nvidia.Name}'.");

        // The processor graphics on this same machine, which drive no displays but
        // would if a monitor were plugged into the motherboard.
        var amd = Parse(@"\\?\PCI#VEN_1002&DEV_164E&SUBSYS_7E261462&REV_CB#4&16ADCFD2&0&0041#{5B45201D-F2F2-4F3B-85BB-30FF1F953599}");
        Assert(amd == (0x1002, "AMD"), $"Expected AMD, got 0x{amd.Id:X4} '{amd.Name}'.");

        Assert(Parse(@"\\?\PCI#VEN_8086&DEV_9BC4#...") == (0x8086, "Intel"), "Intel should be recognised.");

        // Windows writes these paths in either case.
        Assert(Parse(@"\\?\pci#ven_10de&dev_1f06#...") == (0x10DE, "NVIDIA"), "Parsing must ignore case.");

        // A VM is the only way to reach a Windows version you do not own, so its
        // adapters are worth naming too.
        Assert(Parse(@"\\?\PCI#VEN_15AD&DEV_0405#...").Name == "VMware", "VMware should be recognised.");
        Assert(Parse(@"\\?\PCI#VEN_1414&DEV_008E#...").Name == "Microsoft", "The Microsoft adapter should be recognised.");

        // A real vendor id Sherpa has no name for: the id is still worth keeping.
        var unknown = Parse(@"\\?\PCI#VEN_1AF4&DEV_1050#...");
        Assert(unknown == (0x1AF4, string.Empty), $"An unknown vendor should keep its id; got 0x{unknown.Id:X4} '{unknown.Name}'.");

        // Nothing to read: a remote session, and the LUID fallback the service
        // uses when Windows will not name the adapter at all.
        Assert(Parse(@"\\?\ROOT#BasicDisplay#0000#{...}") == (0u, string.Empty), "A non-PCI path has no vendor.");
        Assert(Parse("00000000:0000C3F1") == (0u, string.Empty), "The LUID fallback has no vendor.");
        Assert(Parse(string.Empty) == (0u, string.Empty), "An empty path has no vendor.");
        Assert(Parse("VEN_") == (0u, string.Empty), "A truncated marker has no vendor.");
        Assert(Parse("VEN_ZZZZ") == (0u, string.Empty), "Non-hex digits are not a vendor id.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A profile that manages Surround on a PC whose displays are not on the
    /// NVIDIA card.
    /// </summary>
    /// <remarks>
    /// The case this exists for cannot be produced on the development machine
    /// without physically moving a cable, so the preview is driven with the
    /// snapshot such a machine would produce. NVAPI answers for the card whether
    /// or not it is driving anything, so without this the profile looks fine and
    /// then fails at the display step with a bare driver error.
    /// </remarks>
    private static Task TestHybridGraphicsPreviewAsync()
    {
        static ActivationPreflight Build(List<DisplayAdapterSnapshot> adapters)
        {
            var (document, target, processes, displays) = PreviewFixture();
            target.NvidiaSurroundMode = NvidiaSurroundMode.RequireEnabled;
            displays.CurrentSnapshot = new DisplaySnapshot
            {
                ActiveTargets = [Monitor("side-1", "Dell P2419H", 1920, 1080)],
                Adapters = adapters,
                NvidiaSurround = new NvidiaSurroundSnapshot
                {
                    ApiAvailable = true,
                    StatusKnown = true,
                    HasConfiguredTopology = true,
                    Enabled = false,
                    Description = "Surround is off."
                }
            };
            return new ActivationPreflightService(displays, processes).Build(document, target);
        }

        static DisplayAdapterSnapshot Adapter(string vendor, uint id, int displays) =>
            new() { Vendor = vendor, VendorId = id, DisplayCount = displays, DevicePath = $"path-{id:X4}" };

        // The screens are on the processor's graphics; the card is idle.
        var onboard = Build([Adapter("AMD", 0x1002, 1)]);
        var problem = onboard.AllItems.FirstOrDefault(item => item.Title.Contains("no displays are on the NVIDIA card",
            StringComparison.OrdinalIgnoreCase));
        Assert(problem is not null,
            $"The mismatch was not reported. Items: {string.Join(" | ", onboard.AllItems.Select(i => i.Title))}");
        Assert(problem!.Severity == PreflightSeverity.Problem, "It stops Surround working, so it is a problem.");

        var detail = problem.Detail ?? string.Empty;
        Assert(detail.Contains("AMD", StringComparison.Ordinal),
            $"The detail should name what is driving the displays: {detail}");
        Assert(detail.Contains("motherboard", StringComparison.OrdinalIgnoreCase),
            "The detail should say what to actually check.");

        static bool Reports(ActivationPreflight preflight) => preflight.AllItems.Any(item =>
            item.Title.Contains("no displays are on the NVIDIA card", StringComparison.OrdinalIgnoreCase));

        // The normal case: displays on the card, whatever else is installed.
        Assert(!Reports(Build([Adapter("NVIDIA", 0x10DE, 3)])), "Displays on the card are not a mismatch.");

        // Both adapters driving screens. The card can still do its part.
        Assert(!Reports(Build([Adapter("NVIDIA", 0x10DE, 2), Adapter("Intel", 0x8086, 1)])),
            "A second adapter alongside the card is not a mismatch.");

        // Nothing recorded means unknown, not "no NVIDIA". Inventing a problem
        // here would break every profile captured before adapters were recorded.
        Assert(!Reports(Build([])), "An empty adapter list must not be read as having no NVIDIA card.");

        // An adapter Sherpa cannot name is still not the NVIDIA card.
        var unnamed = Build([Adapter(string.Empty, 0x1AF4, 1)]);
        Assert(Reports(unnamed), "An unrecognised adapter driving the displays is still not the card.");
        Assert(unnamed.AllItems.Any(item =>
                item.Detail?.Contains("unrecognised", StringComparison.OrdinalIgnoreCase) == true),
            "An adapter with no name should be described as unrecognised rather than left blank.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The adapter inventory read from the machine actually running the tests.
    /// </summary>
    private static Task TestAdaptersReadFromRealHardwareAsync()
    {
        var snapshot = new DisplayConfigurationService().Capture();
        Assert(snapshot.Adapters.Count > 0, "A machine with an active display has an adapter driving it.");
        Assert(snapshot.Adapters.Sum(adapter => adapter.DisplayCount) == snapshot.ActiveTargets.Count,
            "Every active display belongs to exactly one adapter.");

        foreach (var adapter in snapshot.Adapters)
        {
            Assert(!string.IsNullOrWhiteSpace(adapter.DevicePath), "An adapter should be identifiable.");
            Assert(adapter.DisplayCount > 0, "An adapter with no displays does not belong in the list.");
            // The path either carries a vendor id or it does not; what must not
            // happen is a name without the id it came from.
            Assert(adapter.Vendor.Length == 0 || adapter.VendorId != 0,
                $"'{adapter.Vendor}' was named without a vendor id, from {adapter.DevicePath}.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Telling one very wide monitor apart from several combined into one.
    /// </summary>
    /// <remarks>
    /// The threshold used to sit at 3.0, which described a 49-inch 32:9 monitor
    /// to its owner as possibly several monitors combined by the driver. Those
    /// panels are staples of sim rigs, so the mistake would have been common.
    /// </remarks>
    private static Task TestUltrawideIsNotCombinedAsync()
    {
        static bool Wide(int width, int height) => DisplayConfigurationService.IsWiderThanAnyPanel(width, height);

        // Single panels, up to the widest made. None of these is combined.
        Assert(!Wide(1920, 1080), "16:9 is an ordinary monitor.");
        Assert(!Wide(2560, 1440), "1440p is an ordinary monitor.");
        Assert(!Wide(3440, 1440), "21:9 ultrawide is one monitor.");
        Assert(!Wide(5120, 1440), "32:9 at 5120x1440, the Samsung Odyssey G9, is one monitor.");
        Assert(!Wide(3840, 1080), "32:9 at 3840x1080 is one monitor.");

        // Panels combined by the driver into a single logical display.
        Assert(Wide(5760, 1080), "Three 1080p panels side by side are combined.");
        Assert(Wide(7680, 1440), "Three 1440p panels side by side are combined.");
        Assert(Wide(11520, 1080), "Six panels are combined.");

        // Portrait triples, which sim rigs also use: 3 x 1080 wide, 1920 tall.
        Assert(!Wide(3240, 1920), "Three portrait panels are not wider than the threshold, and are not claimed to be.");

        // Nothing to divide by. A zero height must not throw or divide.
        Assert(!Wide(1920, 0), "A display with no height cannot be measured.");
        Assert(!Wide(0, 0), "An empty bounds cannot be measured.");
        return Task.CompletedTask;
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
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target], Settings = { DisplaySettleDelayMs = 0 } };

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
        var document = new ProfileDocument { Profiles = [target], Settings = { DisplaySettleDelayMs = 0 } };
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
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target], Settings = { DisplaySettleDelayMs = 0 } };

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
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target], Settings = { DisplaySettleDelayMs = 0 } };

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
        var document = new ProfileDocument { ActiveProfileId = previous.Id, Profiles = [previous, target], Settings = { DisplaySettleDelayMs = 0 } };
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

    private static Task TestCommandLineParsingAsync()
    {
        var separate = CommandLineOptions.Parse(["--activate", "iRacing"]);
        Assert(separate.ActivateProfile == "iRacing", $"Expected iRacing, got '{separate.ActivateProfile}'.");

        var inline = CommandLineOptions.Parse(["--activate=Work Setup"]);
        Assert(inline.ActivateProfile == "Work Setup", $"Expected 'Work Setup', got '{inline.ActivateProfile}'.");

        var quoted = CommandLineOptions.Parse(["--activate=\"Work Setup\""]);
        Assert(quoted.ActivateProfile == "Work Setup", "A quoted inline value should keep its spaces and lose its quotes.");

        var cased = CommandLineOptions.Parse(["--ACTIVATE", "ACC"]);
        Assert(cased.ActivateProfile == "ACC", "The switch itself should be case insensitive.");

        // A profile name is never silently taken from the next switch.
        var missingValue = CommandLineOptions.Parse(["--activate", "--smoke-test"]);
        Assert(missingValue.ActivateProfile is null, "A missing profile name must not consume the following switch.");
        Assert(missingValue.SmokeTest, "The following switch should still be parsed.");

        var help = CommandLineOptions.Parse(["--help"]);
        Assert(help.ShowHelp, "--help should be recognised.");

        // Unknown arguments must never stop the application starting.
        var unknown = CommandLineOptions.Parse(["--future-flag", "--activate", "Work"]);
        Assert(unknown.ActivateProfile == "Work", "An unknown argument should not break later parsing.");
        Assert(unknown.Unknown.Count == 1, $"Expected one unknown argument, got {unknown.Unknown.Count}.");

        var none = CommandLineOptions.Parse([]);
        Assert(none.ActivateProfile is null && !none.ShowHelp && !none.SmokeTest,
            "No arguments should produce no requests.");
        return Task.CompletedTask;
    }

    private static Task TestHotkeyParsingAsync()
    {
        Assert(HotkeyDefinition.TryParse("ctrl+alt+i", out var parsed, out _), "Ctrl+Alt+I should parse.");
        Assert(parsed!.Text == "Ctrl+Alt+I", $"Expected canonical 'Ctrl+Alt+I', got '{parsed.Text}'.");
        Assert(parsed.VirtualKey == 'I', "The virtual key for I should be its upper-case code.");
        Assert(parsed.Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt), "Both modifiers should be set.");

        Assert(HotkeyDefinition.TryParse("WIN + SHIFT + f12", out var functionKey, out _), "Win+Shift+F12 should parse.");
        Assert(functionKey!.Text == "Shift+Win+F12", $"Modifier order should be canonical, got '{functionKey.Text}'.");
        Assert(functionKey.VirtualKey == 0x7B, "F12 should map to VK_F12.");

        // Shift alone would capture ordinary typing.
        Assert(!HotkeyDefinition.TryParse("Shift+A", out _, out var shiftError), "Shift+A should be rejected.");
        Assert(shiftError is not null && shiftError.Contains("Ctrl", StringComparison.Ordinal),
            "The rejection should say which modifiers are acceptable.");

        Assert(!HotkeyDefinition.TryParse("Ctrl+Alt", out _, out var noKeyError), "A combination with no key should be rejected.");
        Assert(noKeyError is not null, "A missing key should explain itself.");

        Assert(!HotkeyDefinition.TryParse("Ctrl+Alt+I+J", out _, out _), "Two keys should be rejected.");
        Assert(!HotkeyDefinition.TryParse("Ctrl+Alt+F25", out _, out _), "F25 does not exist.");
        Assert(!HotkeyDefinition.TryParse("Ctrl+Alt+;", out _, out _), "Punctuation is not registerable here.");
        Assert(!HotkeyDefinition.TryParse("", out _, out _), "Empty text is not a hotkey.");
        Assert(!HotkeyDefinition.TryParse(null, out _, out _), "Null is not a hotkey.");

        Assert(HotkeyDefinition.Canonicalize("  alt + ctrl + 5 ") == "Ctrl+Alt+5",
            "Canonicalize should tidy spacing and ordering.");
        Assert(HotkeyDefinition.Canonicalize("nonsense") is null, "Canonicalize should return null for bad input.");

        // Keys captured from the keyboard must obey exactly the same rules as typed text.
        Assert(HotkeyDefinition.TryFromInput(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'W',
            out var captured, out _), "Ctrl+Alt+W should be accepted from a key press.");
        Assert(captured!.Text == "Ctrl+Alt+W", $"Expected 'Ctrl+Alt+W', got '{captured.Text}'.");
        Assert(HotkeyDefinition.TryFromInput(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x70,
            out var capturedFunction, out _), "Ctrl+Alt+F1 should be accepted from a key press.");
        Assert(capturedFunction!.Text == "Ctrl+Alt+F1", $"Expected 'Ctrl+Alt+F1', got '{capturedFunction.Text}'.");
        Assert(!HotkeyDefinition.TryFromInput(HotkeyModifiers.Shift, 'A', out _, out var capturedShiftError),
            "Shift alone should be rejected from a key press too.");
        Assert(capturedShiftError is not null && capturedShiftError.Contains("Ctrl", StringComparison.Ordinal),
            "The rejection should name the modifiers that would work.");
        Assert(!HotkeyDefinition.TryFromInput(HotkeyModifiers.Control, 0x09, out _, out _),
            "Tab is not a key Sherpa can register.");

        // Typed and captured paths must agree, or a shortcut could be accepted one
        // way and rejected the other.
        Assert(HotkeyDefinition.TryParse("Ctrl+Alt+W", out var typed, out _) &&
               typed!.Text == captured.Text && typed.VirtualKey == captured.VirtualKey &&
               typed.Modifiers == captured.Modifiers,
            "Typing a shortcut and pressing it should produce the same definition.");
        return Task.CompletedTask;
    }

    private static async Task TestSingleInstanceProfileHandoffAsync()
    {
        var key = "Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(key);
        SingleInstanceService? secondary = null;
        var secondaryThread = new Thread(() => secondary = new SingleInstanceService(key));
        secondaryThread.Start();
        secondaryThread.Join();
        using var secondaryInstance = secondary ?? throw new InvalidOperationException("Secondary coordinator was not created.");

        var payloads = new List<string?>();
        using var delivered = new SemaphoreSlim(0);
        primary.StartListening(payload =>
        {
            lock (payloads) payloads.Add(payload);
            delivered.Release();
            return Task.FromResult(true);
        });

        Assert(secondaryInstance.SignalPrimaryInstance("iRacing Rig"), "The secondary was not acknowledged.");
        Assert(await delivered.WaitAsync(TimeSpan.FromSeconds(5)), "The primary never saw the request.");
        lock (payloads)
            Assert(payloads[0] == "iRacing Rig",
                $"The primary received '{payloads[0]}' instead of the requested profile.");

        // A plain window activation must not replay the previous request. The
        // coordinator has to live on its own thread: a mutex is reentrant for the
        // thread that owns it, so a same-thread secondary would wrongly look primary.
        var plainAcknowledged = false;
        var plainWasSecondary = false;
        var plainThread = new Thread(() =>
        {
            using var plain = new SingleInstanceService(key);
            plainWasSecondary = !plain.IsPrimaryInstance;
            if (plainWasSecondary) plainAcknowledged = plain.SignalPrimaryInstance();
        });
        plainThread.Start();
        plainThread.Join(TimeSpan.FromSeconds(10));

        Assert(plainWasSecondary, "The third coordinator should not have become primary.");
        Assert(plainAcknowledged, "The plain activation was not acknowledged.");
        Assert(await delivered.WaitAsync(TimeSpan.FromSeconds(5)), "The plain activation never arrived.");
        lock (payloads)
            Assert(payloads[1] is null,
                $"A plain activation delivered a stale request: '{payloads[1]}'.");
    }

    private static Task TestShortcutFileNameAsync()
    {
        Assert(ShortcutService.BuildFileName("iRacing") == "iRacing (Sherpa).lnk",
            "A simple name should be used as-is.");
        var awkward = ShortcutService.BuildFileName("Work: sim/rig?");
        Assert(!awkward.Any(character => Path.GetInvalidFileNameChars().Contains(character)),
            $"'{awkward}' still contains characters Windows rejects.");
        Assert(ShortcutService.BuildFileName("   ") == "Profile (Sherpa).lnk",
            "A blank name should fall back rather than produce an unnamed file.");
        Assert(ShortcutService.BuildFileName(new string('x', 200)).Length < 120,
            "A very long profile name should be trimmed so the path stays usable.");
        Assert(ShortcutService.BuildFileName("Trailing dots...").EndsWith("(Sherpa).lnk", StringComparison.Ordinal),
            "Trailing dots should not break the extension.");
        return Task.CompletedTask;
    }

    private static Task TestProfileCloneDropsHotkeyAsync()
    {
        var profile = new SwitchProfile { Name = "iRacing", Hotkey = "Ctrl+Alt+I" };
        var copy = profile.Clone();
        Assert(string.IsNullOrEmpty(copy.Hotkey),
            "A duplicated profile must not inherit the shortcut; two profiles cannot own one combination.");
        Assert(profile.Hotkey == "Ctrl+Alt+I", "Duplicating must not disturb the original's shortcut.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The minimize watcher polls for the whole launch timeout so a launcher that
    /// opens its window late still gets minimized. It must not fight the user:
    /// restoring a window it has already minimized has to stick.
    /// </summary>
    private static async Task TestRestoredWindowStaysRestoredAsync()
    {
        using var directory = new TemporaryDirectory();
        // Its own copy of the fixture. Earlier tests leave minimize watchers running
        // for up to 45 seconds against the shared fixture path, and those would
        // minimize this window using their own handle sets.
        var fixtureDirectory = Path.Combine(directory.Path, "fixture");
        CopyFixtureOutput(fixtureDirectory);

        // Renamed, not just relocated. Other watchers match on the explicit process
        // name "WindowFixture", so a copy that keeps that name is still matched by
        // them no matter where it lives.
        const string processName = "RestoreFixture";
        var executable = Path.Combine(fixtureDirectory, processName + ".exe");
        File.Move(Path.Combine(fixtureDirectory, "WindowFixture.exe"), executable);

        var minimizedSignal = Path.Combine(directory.Path, "minimized.signal");
        var app = new LaunchApplication
        {
            Name = "Restore fixture",
            Path = executable,
            ProcessName = processName,
            Arguments = $"--state-file=\"{minimizedSignal}\"",
            StartMinimized = true
        };

        var existing = Process.GetProcessesByName(processName).Select(process =>
        {
            var id = process.Id;
            process.Dispose();
            return id;
        }).ToHashSet();

        var service = new ProcessService();
        try
        {
            var launch = await service.LaunchAsync(app, CancellationToken.None);
            Assert(launch.Started, "Fixture did not start.");
            Assert(await WaitForFileAsync(minimizedSignal, TimeSpan.FromSeconds(10)),
                "The fixture did not reach the minimized state.");

            var window = await WaitForFixtureWindowAsync(processName, existing, TimeSpan.FromSeconds(5));
            Assert(window != IntPtr.Zero, "The fixture window handle could not be found.");
            Assert(IsIconic(window), "The fixture window should start minimized.");

            // Let the watcher complete a few 250 ms polls first. The process is
            // started with a minimized window style, so the window is already
            // minimized when the watcher first sights it; restoring before that
            // first sighting is a genuine race, and not what a user can hit.
            await Task.Delay(1000);

            // The user opens the application again while the watcher is still running.
            ShowWindow(window, SwRestore);
            Assert(await WaitForConditionAsync(() => !IsIconic(window), TimeSpan.FromSeconds(3)),
                "The fixture window did not restore.");

            // Several watcher polls (250 ms each) must pass without it being undone.
            await Task.Delay(1500);
            Assert(!IsIconic(window),
                "The minimize watcher re-minimized a window the user had restored.");
        }
        finally { await service.CloseAsync(app, CancellationToken.None); }
    }

    /// <summary>Finds the window of a fixture process that did not exist before.</summary>
    private static async Task<IntPtr> WaitForFixtureWindowAsync(string processName,
        HashSet<int> excludedProcessIds, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (excludedProcessIds.Contains(process.Id)) continue;
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
                catch { /* The fixture may exit between enumeration and inspection. */ }
                finally { process.Dispose(); }
            }
            await Task.Delay(100);
        }
        return IntPtr.Zero;
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }

    private static async Task TestDisplaySettleDelayAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var target = new SwitchProfile { Name = "iRacing", Display = new DisplaySnapshot() };
        target.Applications.Add(new LaunchApplication { Name = "Sim", Path = @"C:\sim\app.exe" });
        var document = new ProfileDocument { Profiles = { target } };
        document.Settings.DisplaySettleDelayMs = 120;

        var activator = new ProfileActivationService(displays, processes);
        await activator.ActivateAsync(document, target, message =>
        {
            if (message.Contains("settle", StringComparison.OrdinalIgnoreCase))
                processes.Events.Add("settle");
        });

        var order = processes.Events;
        var settle = order.IndexOf("settle");
        Assert(settle >= 0, $"The settle delay was never reported. Events: {string.Join(", ", order)}");
        Assert(settle > order.IndexOf("display:apply"),
            "The settle delay must come after the display layout is applied.");
        Assert(settle < order.IndexOf(@"launch:C:\sim\app.exe"),
            "Applications must not start until the displays have settled.");

        // A profile with no captured layout changes no displays, so there is
        // nothing to settle and no reason to make the user wait.
        var noDisplay = new SwitchProfile { Name = "Work" };
        noDisplay.Applications.Add(new LaunchApplication { Name = "Mail", Path = @"C:\work\mail.exe" });
        document.Profiles.Add(noDisplay);
        var processesWithoutDisplay = new FakeProcessService();
        var displaysWithoutDisplay = new FakeDisplayConfigurationService(processesWithoutDisplay.Events);
        var settleReported = false;
        await new ProfileActivationService(displaysWithoutDisplay, processesWithoutDisplay)
            .ActivateAsync(document, noDisplay, message =>
            {
                if (message.Contains("settle", StringComparison.OrdinalIgnoreCase)) settleReported = true;
            });
        Assert(!settleReported, "A profile without a display layout should not wait for displays to settle.");
    }

    private static Task TestDisplaySettleDelayBoundsAsync()
    {
        var settings = new AppSettings();
        Assert(settings.DisplaySettleDelayMs == AppSettings.DefaultDisplaySettleDelayMs,
            "A new profile document should carry the default settle delay.");

        settings.DisplaySettleDelayMs = -500;
        Assert(settings.DisplaySettleDelayMs == 0, "A negative delay should clamp to zero, not wait forever.");

        settings.DisplaySettleDelayMs = int.MaxValue;
        Assert(settings.DisplaySettleDelayMs == AppSettings.MaximumDisplaySettleDelayMs,
            "A huge delay should be capped so a bad value cannot appear to hang a profile switch.");
        return Task.CompletedTask;
    }

    private static Task TestNvidiaAppLocatorAsync()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "NVIDIA Corporation");
        var system = Path.Combine(directory.Path, "System32");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(system);

        // The deep link wins whenever Windows has a handler for the scheme: it
        // opens the display page rather than wherever the app was last left.
        var deepLink = NvidiaAppLocator.Locate(root, system, protocolRegistered: true);
        Assert(deepLink is not null, "A registered protocol should always yield a target.");
        Assert(deepLink!.FileName == NvidiaAppLocator.DisplaySettingsUri,
            $"Expected the display deep link, got '{deepLink.FileName}'.");
        Assert(NvidiaAppLocator.DisplaySettingsUri.StartsWith("nvidiaapp://route/#nvapp/", StringComparison.Ordinal),
            "The deep link must use NVIDIA's own route format.");

        // Everything below is the fallback path, with no protocol handler.
        Assert(NvidiaAppLocator.Locate(root, system, protocolRegistered: false) is null,
            "Nothing should be reported when no NVIDIA application is installed.");

        // The observed layout: NVIDIA Corporation\NVIDIA App\CEF\NVIDIA app.exe.
        var cef = Path.Combine(root, "NVIDIA App", "CEF");
        Directory.CreateDirectory(cef);
        var appExecutable = Path.Combine(cef, "NVIDIA app.exe");
        File.WriteAllText(appExecutable, string.Empty);

        var located = NvidiaAppLocator.Locate(root, system, protocolRegistered: false);
        Assert(located is not null, "The NVIDIA app was not found in its nested folder.");
        Assert(located!.FileName.Equals(appExecutable, StringComparison.OrdinalIgnoreCase),
            $"Located '{located.FileName}' instead of '{appExecutable}'.");
        Assert(located.DisplayName == "NVIDIA app", $"Unexpected display name '{located.DisplayName}'.");

        // The driver-installed Control Panel is the fallback for older systems.
        Directory.Delete(Path.Combine(root, "NVIDIA App"), recursive: true);
        var controlPanel = Path.Combine(system, "nvcplui.exe");
        File.WriteAllText(controlPanel, string.Empty);
        var legacy = NvidiaAppLocator.Locate(root, system, protocolRegistered: false);
        Assert(legacy is not null && legacy.FileName.Equals(controlPanel, StringComparison.OrdinalIgnoreCase),
            "The legacy Control Panel should be used when the NVIDIA app is absent.");

        // The app wins when both are present.
        Directory.CreateDirectory(cef);
        File.WriteAllText(appExecutable, string.Empty);
        var preferred = NvidiaAppLocator.Locate(root, system, protocolRegistered: false);
        Assert(preferred is not null && preferred.FileName.Equals(appExecutable, StringComparison.OrdinalIgnoreCase),
            "The NVIDIA app should be preferred over the legacy Control Panel.");

        // Standard (non-DCH) drivers put the Control Panel under the NVIDIA folder
        // rather than System32.
        Directory.Delete(Path.Combine(root, "NVIDIA App"), recursive: true);
        File.Delete(controlPanel);
        var clientDirectory = Path.Combine(root, "Control Panel Client");
        Directory.CreateDirectory(clientDirectory);
        var clientExecutable = Path.Combine(clientDirectory, "nvcplui.exe");
        File.WriteAllText(clientExecutable, string.Empty);
        var client = NvidiaAppLocator.Locate(root, system, protocolRegistered: false);
        Assert(client is not null && client.FileName.Equals(clientExecutable, StringComparison.OrdinalIgnoreCase),
            $"The Control Panel Client location was not found; got '{client?.FileName}'.");

        // A missing root must not throw on a button click.
        Assert(NvidiaAppLocator.Locate(Path.Combine(directory.Path, "absent"),
            Path.Combine(directory.Path, "absent"), protocolRegistered: false) is null,
            "A missing NVIDIA folder should be handled without throwing.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A monitor's audio endpoint does not exist until Windows has finished
    /// enabling that monitor, so it appears after the display step returns.
    /// </summary>
    private static async Task TestAudioEndpointAppearsLateAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var monitorSpeakers = new AudioDevice("monitor", "ASUS VG249 (NVIDIA High Definition Audio)");
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("usb", "Speakers (USB Audio Device)") },
            DefaultId = "usb",
            // The endpoint shows up only on the third enumeration, well after the
            // display call has returned.
            AppearAfterQueries = (3, monitorSpeakers)
        };

        var target = new SwitchProfile
        {
            Name = "Work",
            Display = new DisplaySnapshot(),
            AudioOutputDeviceId = "monitor",
            AudioOutputDeviceName = monitorSpeakers.Name
        };
        target.Applications.Add(new LaunchApplication { Name = "Mail", Path = @"C:\work\mail.exe" });
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        var reports = new List<string>();
        Assert(await new ProfileActivationService(displays, processes, null, audio)
                .ActivateAsync(document, target, reports.Add),
            "The switch should succeed.");

        Assert(audio.DefaultId == "monitor",
            $"The monitor endpoint should have become default once it appeared; got '{audio.DefaultId}'.");
        Assert(reports.Any(message => message.Contains("Waiting for", StringComparison.OrdinalIgnoreCase)),
            $"The wait should be reported. Reports: {string.Join(" | ", reports)}");

        var order = processes.Events;
        Assert(order.IndexOf("audio:set:monitor") < order.IndexOf(@"launch:C:\work\mail.exe"),
            "Audio must still be switched before applications start.");

        // With no display change there is nothing that could bring the endpoint,
        // so the switch must not stall waiting for it.
        var noDisplay = new SwitchProfile
        {
            Name = "Portable",
            AudioOutputDeviceId = "absent",
            AudioOutputDeviceName = "Headset"
        };
        document.Profiles.Add(noDisplay);
        var quiet = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("usb", "Speakers (USB Audio Device)") },
            DefaultId = "usb"
        };
        var started = DateTime.UtcNow;
        var quietReports = new List<string>();
        Assert(await new ProfileActivationService(displays, processes, null, quiet)
                .ActivateAsync(document, noDisplay, quietReports.Add),
            "A profile with no display layout should still activate.");
        Assert(DateTime.UtcNow - started < TimeSpan.FromSeconds(5),
            "A profile that changes no displays must not wait for an absent audio device.");
        Assert(quietReports.All(message => !message.Contains("Waiting for", StringComparison.OrdinalIgnoreCase)),
            "No wait should be reported when no display change could produce the device.");
    }

    /// <summary>
    /// Order is the mechanism now: "start after X" means "be below X in the list",
    /// with a readiness rule on X deciding when that is.
    /// </summary>
    private static async Task TestLaunchOrderAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);

        var target = new SwitchProfile { Name = "iRacing" };
        foreach (var name in new[] { "first", "second", "third" })
            target.Applications.Add(new LaunchApplication { Name = name, Path = $@"C:\sim\{name}.exe" });
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        Assert(await new ProfileActivationService(displays, processes).ActivateAsync(document, target, _ => { }),
            "The switch should succeed.");

        var launches = processes.Events.Where(item => item.StartsWith("launch:", StringComparison.Ordinal)).ToList();
        Assert(launches.SequenceEqual([@"launch:C:\sim\first.exe", @"launch:C:\sim\second.exe", @"launch:C:\sim\third.exe"]),
            $"Applications started out of order: {string.Join(", ", launches)}");

        // Reordering the list reorders the launches, with no other setting changed.
        var moved = target.Applications[2];
        target.Applications.Move(2, 0);
        var reordered = new FakeProcessService();
        var reorderedDisplays = new FakeDisplayConfigurationService(reordered.Events);
        var second = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };
        await new ProfileActivationService(reorderedDisplays, reordered).ActivateAsync(second, target, _ => { });

        var afterMove = reordered.Events.Where(item => item.StartsWith("launch:", StringComparison.Ordinal)).ToList();
        Assert(afterMove.FirstOrDefault() == "launch:" + moved.Path,
            $"The moved application should start first; got {afterMove.FirstOrDefault()}.");
        return;
    }

    private static async Task TestReadinessGatesNextLaunchAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);

        var launcher = new LaunchApplication { Name = "Launcher", Path = @"C:\sim\launcher.exe" };
        var second = new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" };
        var target = new SwitchProfile { Name = "iRacing", Applications = { launcher, second } };
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        // Waiting is the default: ordering the list is the whole configuration.
        Assert(document.Settings.LaunchReadiness == LaunchReadiness.ProcessRunning,
            "New documents should wait for each application to start.");

        Assert(await new ProfileActivationService(displays, processes).ActivateAsync(document, target, _ => { }),
            "The switch should succeed.");

        var order = processes.Events;
        var launched = order.IndexOf(@"launch:C:\sim\launcher.exe");
        var waited = order.IndexOf(@"ready:C:\sim\launcher.exe");
        var next = order.IndexOf(@"launch:C:\sim\crew.exe");

        Assert(waited > launched, "Readiness must be checked after the application is started.");
        Assert(next > waited, "The next application must not start until the previous one is ready.");
        Assert(processes.ReadinessWaits.Count == 2, "Every trackable application should be waited for.");

        // Turning it off restores straight-through launching.
        var off = new FakeProcessService();
        var offDisplays = new FakeDisplayConfigurationService(off.Events);
        document.Settings.LaunchReadiness = LaunchReadiness.None;
        await new ProfileActivationService(offDisplays, off).ActivateAsync(document, target, _ => { });
        Assert(off.ReadinessWaits.Count == 0, "Nothing should be waited for when the rule is off.");
    }

    /// <summary>
    /// Scripts, protocol URLs, and launchers whose real process cannot be matched
    /// can never satisfy a rule, so waiting for them only costs the timeout.
    /// </summary>
    private static async Task TestReadinessSkipsUntrackableAsync()
    {
        var processes = new FakeProcessService();
        processes.UntrackablePaths.Add(@"C:\sim\launch.url");
        var displays = new FakeDisplayConfigurationService(processes.Events);

        var shortcut = new LaunchApplication { Name = "Steam shortcut", Path = @"C:\sim\launch.url" };
        var tracked = new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" };
        var target = new SwitchProfile { Name = "iRacing", Applications = { shortcut, tracked } };
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        var reports = new List<string>();
        Assert(await new ProfileActivationService(displays, processes).ActivateAsync(document, target, reports.Add),
            "The switch should succeed.");

        Assert(!processes.ReadinessWaits.Contains(shortcut.Id),
            "An untrackable target must not be waited for.");
        Assert(processes.ReadinessWaits.Contains(tracked.Id),
            "A trackable application should still be waited for.");
        Assert(reports.Any(message => message.Contains("cannot track", StringComparison.OrdinalIgnoreCase)),
            $"The skip should be reported. Reports: {string.Join(" | ", reports)}");
    }

    private static async Task TestReadinessTimeoutAsync()
    {
        var processes = new FakeProcessService { ReadinessSucceeds = false };
        var displays = new FakeDisplayConfigurationService(processes.Events);

        var slow = new LaunchApplication { Name = "Slow app", Path = @"C:\sim\slow.exe" };
        var second = new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" };
        var target = new SwitchProfile { Name = "iRacing", Applications = { slow, second } };
        var document = new ProfileDocument
        {
            Profiles = { target },
            Settings = { DisplaySettleDelayMs = 0, LaunchReadiness = LaunchReadiness.WindowResponsive }
        };

        var reports = new List<string>();
        // A slow application must not cost the rest of the profile.
        Assert(await new ProfileActivationService(displays, processes).ActivateAsync(document, target, reports.Add),
            "A readiness timeout must not fail the switch.");
        Assert(processes.Launched.Count == 2, "Later applications must still start after a timeout.");
        Assert(reports.Any(message => message.Contains("did not reach", StringComparison.OrdinalIgnoreCase)),
            $"The timeout was not reported. Reports: {string.Join(" | ", reports)}");
    }

    /// <summary>
    /// The default rule has to survive an application that exists long before it
    /// is usable.
    /// </summary>
    /// <remarks>
    /// This is the regression for the rule doing nothing at all: a process exists
    /// the moment it is started, so "is it running" was satisfied within
    /// milliseconds and every application in a profile launched at once. Measured
    /// against this fixture, the process was there after 7ms and ready after
    /// 3,042ms.
    /// </remarks>
    private static async Task TestReadinessOutlastsSlowStartupAsync()
    {
        using var directory = new TemporaryDirectory();
        var fixtureDirectory = Path.Combine(directory.Path, "fixture");
        CopyFixtureOutput(fixtureDirectory);
        const string processName = "SlowFixture";
        var executable = Path.Combine(fixtureDirectory, processName + ".exe");
        File.Move(Path.Combine(fixtureDirectory, "WindowFixture.exe"), executable);

        const int startupMs = 2000;
        var service = new ProcessService();
        var app = new LaunchApplication
        {
            Name = "Slow fixture",
            Path = executable,
            Arguments = $"--startup-delay-ms={startupMs}",
            ProcessName = processName,
            StartMinimized = true
        };

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert((await service.LaunchAsync(app, CancellationToken.None)).Started, "The slow fixture did not start.");
            var afterLaunch = clock.ElapsedMilliseconds;

            var ready = await service.WaitUntilReadyAsync(app, LaunchReadiness.ProcessRunning,
                TimeSpan.FromSeconds(20), CancellationToken.None);
            var afterWait = clock.ElapsedMilliseconds;

            Assert(ready.Ready, $"The fixture never became ready: {ready.Message}");
            // Generous, because the point is that it waited at all rather than
            // returning the instant the process appeared.
            Assert(afterWait >= startupMs * 0.7,
                $"The wait returned after {afterWait}ms, so it did not outlast a {startupMs}ms startup. " +
                $"Launching alone took {afterLaunch}ms.");
        }
        finally { await service.CloseAsync(app, CancellationToken.None); }
    }

    /// <summary>
    /// The window and process rules run against real Win32 state, so they are
    /// checked against a real fixture rather than only through the fake.
    /// </summary>
    private static async Task TestReadinessDetectsWindowAsync()
    {
        using var directory = new TemporaryDirectory();
        var fixtureDirectory = Path.Combine(directory.Path, "fixture");
        CopyFixtureOutput(fixtureDirectory);
        const string processName = "ReadyFixture";
        var executable = Path.Combine(fixtureDirectory, processName + ".exe");
        File.Move(Path.Combine(fixtureDirectory, "WindowFixture.exe"), executable);

        var service = new ProcessService();
        var app = new LaunchApplication
        {
            Name = "Ready fixture",
            Path = executable,
            ProcessName = processName,
            StartMinimized = true
        };

        try
        {
            Assert((await service.LaunchAsync(app, CancellationToken.None)).Started, "Fixture did not start.");
            var ready = await service.WaitUntilReadyAsync(app, LaunchReadiness.WindowVisible,
                TimeSpan.FromSeconds(15), CancellationToken.None);
            Assert(ready.Ready, $"A real window was not detected: {ready.Message}");

            // A minimized window still counts: applications that start minimized
            // would otherwise never satisfy the rule.
            Assert((await service.WaitUntilReadyAsync(app, LaunchReadiness.WindowResponsive,
                    TimeSpan.FromSeconds(15), CancellationToken.None)).Ready,
                "A running fixture should be reported as responsive.");
        }
        finally { await service.CloseAsync(app, CancellationToken.None); }

        // Nothing running: the rule must time out rather than report ready.
        var absent = new LaunchApplication { Name = "Absent", Path = executable, ProcessName = processName };
        var timedOut = await new ProcessService().WaitUntilReadyAsync(absent, LaunchReadiness.ProcessRunning,
            TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert(!timedOut.Ready, "An application that is not running must not be reported ready.");
        Assert(timedOut.Message.Contains("did not reach", StringComparison.OrdinalIgnoreCase),
            $"Unexpected timeout message: {timedOut.Message}");
    }

    private static Task TestAppVersionFormatAsync()
    {
        // The SDK appends the commit to the informational version; the window
        // corner should show the human version, not a 40-character hash.
        Assert(AppVersion.Format("0.5.0+c3c37dae280bc0e440c8feb0a69d4f568e5d15c8", null) == "v0.5.0",
            "Build metadata should be dropped.");
        Assert(AppVersion.Format("0.5.0", null) == "v0.5.0", "A plain informational version should be used as-is.");
        Assert(AppVersion.Format("  0.5.1  ", null) == "v0.5.1", "Surrounding space should be trimmed.");

        // Falling back to the assembly version drops the revision, which is always
        // zero for this project.
        Assert(AppVersion.Format(null, new Version(0, 5, 0, 0)) == "v0.5.0",
            "The assembly version should render without its revision.");
        Assert(AppVersion.Format("", new Version(1, 2, 3, 4)) == "v1.2.3",
            "An empty informational version should fall back to the assembly version.");
        Assert(AppVersion.Format(null, new Version(1, 2)) == "v1.2.0",
            "A two-part assembly version should not produce a negative build number.");
        Assert(AppVersion.Format(null, null) == "v0.0.0", "There should always be something to show.");

        // The real value has to be usable in the corner of a window.
        Assert(AppVersion.Display.StartsWith('v') && AppVersion.Display.Length is > 1 and < 20,
            $"'{AppVersion.Display}' is not a sensible label.");
        return Task.CompletedTask;
    }

    private static async Task TestAudioInputAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("speakers", "Desk speakers"), new AudioDevice("headset-out", "Headset") },
            InputDevices = { new AudioDevice("desk-mic", "Desk mic"), new AudioDevice("headset-mic", "Headset mic") },
            DefaultId = "speakers",
            DefaultInputId = "desk-mic"
        };

        // Output and input are independent: a profile may set either, both, or
        // neither.
        var target = new SwitchProfile
        {
            Name = "iRacing",
            AudioInputDeviceId = "headset-mic",
            AudioInputDeviceName = "Headset mic"
        };
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        Assert(await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, target, _ => { }), "The switch should succeed.");
        Assert(audio.DefaultInputId == "headset-mic", $"The input was left as '{audio.DefaultInputId}'.");
        Assert(audio.DefaultId == "speakers", "Setting only the input must not disturb the output.");

        // Both at once.
        var both = new SwitchProfile
        {
            Name = "ACC",
            AudioOutputDeviceId = "headset-out",
            AudioOutputDeviceName = "Headset",
            AudioInputDeviceId = "desk-mic",
            AudioInputDeviceName = "Desk mic"
        };
        document.Profiles.Add(both);
        Assert(await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, both, _ => { }), "The second switch should succeed.");
        Assert(audio.DefaultId == "headset-out" && audio.DefaultInputId == "desk-mic",
            $"Expected headset-out/desk-mic, got {audio.DefaultId}/{audio.DefaultInputId}.");

        // A missing microphone is a warning, exactly like a missing speaker.
        var missing = new SwitchProfile
        {
            Name = "Work",
            AudioInputDeviceId = "absent-mic",
            AudioInputDeviceName = "Studio mic"
        };
        document.Profiles.Add(missing);
        var reports = new List<string>();
        Assert(await new ProfileActivationService(displays, processes, null, audio)
                .ActivateAsync(document, missing, reports.Add),
            "A missing microphone must not fail the switch.");
        Assert(reports.Any(message => message.Contains("not connected", StringComparison.OrdinalIgnoreCase)),
            $"The missing microphone was not reported. Reports: {string.Join(" | ", reports)}");

        // The preview describes both directions.
        var preview = new ActivationPreflightService(displays, processes, audio);
        var items = ItemsIn(preview.Build(document, both), "Audio");
        Assert(Mentions(items, PreflightSeverity.Info, "Switch audio input", "Desk mic") ||
               Mentions(items, PreflightSeverity.Info, "already", "Desk mic"),
            "The preview should describe the input device.");
        Assert(items.Any(item => item.Title.Contains("output", StringComparison.OrdinalIgnoreCase)),
            "The preview should describe the output device too.");
    }

    private static async Task TestAudioAppliedBeforeLaunchAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("headset", "Sim headset"), new AudioDevice("speakers", "Desk speakers") },
            DefaultId = "speakers"
        };

        var target = new SwitchProfile
        {
            Name = "iRacing",
            AudioOutputDeviceId = "headset",
            AudioOutputDeviceName = "Sim headset"
        };
        target.Applications.Add(new LaunchApplication { Name = "Sim", Path = @"C:\sim\app.exe" });
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        var activated = await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, target, _ => { });

        Assert(activated, "The switch should succeed.");
        Assert(audio.DefaultId == "headset", $"The default output was left as '{audio.DefaultId}'.");

        // Applications read the default output once at launch, so the switch has
        // to happen first.
        var order = processes.Events;
        var applied = order.IndexOf("audio:set:headset");
        Assert(applied >= 0, $"The audio switch was never applied. Events: {string.Join(", ", order)}");
        Assert(applied < order.IndexOf(@"launch:C:\sim\app.exe"),
            "Audio must be switched before applications are started.");

        // Activating again must not re-apply what is already correct.
        audio.SetCalls = 0;
        await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, target, _ => { });
        Assert(audio.SetCalls == 0, "An audio device that is already default should not be set again.");
    }

    /// <summary>
    /// Windows accepting the audio change and not making it.
    /// </summary>
    /// <remarks>
    /// The only way to set a default endpoint is IPolicyConfig, which Microsoft
    /// never documented. Sherpa calls one method at a fixed position in that
    /// interface; if a Windows version ever shifts the layout, the call lands on
    /// a different method, which can return success having done nothing. That is
    /// indistinguishable from working unless the answer is read back, and it
    /// cannot be reproduced on the Windows this was written on, so it is
    /// reproduced here instead.
    /// </remarks>
    private static async Task TestAudioSilentlyIgnoredAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices =
            {
                new AudioDevice("speakers", "Desk speakers"),
                new AudioDevice("headset", "Sim headset")
            },
            DefaultId = "speakers",
            // Accepts the call, changes nothing.
            IgnoreSet = true
        };

        var target = new SwitchProfile
        {
            Name = "iRacing",
            AudioOutputDeviceId = "headset",
            AudioOutputDeviceName = "Sim headset"
        };
        target.Applications.Add(new LaunchApplication { Name = "Sim", Path = @"C:\sim\app.exe" });
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        var reports = new List<string>();
        var activated = await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, target, reports.Add);

        Assert(audio.SetCalls == 1, $"The device should have been set once; it was set {audio.SetCalls} times.");
        Assert(reports.Any(message => message.Contains("still Desk speakers", StringComparison.OrdinalIgnoreCase)),
            $"The user was not told the change did not take. Reports: {string.Join(" | ", reports)}");

        // Audio that will not change is worth saying, but it is not worth losing
        // the display layout and the applications over.
        Assert(activated, "A refused audio change must not fail the switch.");
        Assert(processes.Launched.Count == 1, "Applications should still start.");

        // And the ordinary case still passes silently, so the check is not simply
        // warning every time.
        audio.IgnoreSet = false;
        audio.DefaultId = "speakers";
        var second = new List<string>();
        Assert(await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, target, second.Add), "The switch should succeed when audio applies.");
        Assert(!second.Any(message => message.Contains("still", StringComparison.OrdinalIgnoreCase)),
            $"A change that worked must not be reported as ignored. Reports: {string.Join(" | ", second)}");
    }

    private static async Task TestAudioMissingDeviceAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("speakers", "Desk speakers") },
            DefaultId = "speakers"
        };

        var target = new SwitchProfile
        {
            Name = "iRacing",
            AudioOutputDeviceId = "headset",
            AudioOutputDeviceName = "Sim headset"
        };
        target.Applications.Add(new LaunchApplication { Name = "Sim", Path = @"C:\sim\app.exe" });
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        var reports = new List<string>();
        var activated = await new ProfileActivationService(displays, processes, null, audio)
            .ActivateAsync(document, target, reports.Add);

        // An unplugged headset must not cost the user their profile switch.
        Assert(activated, "A missing audio device must not fail the switch.");
        Assert(audio.SetCalls == 0, "A device that is not connected should never be set.");
        Assert(processes.Launched.Count == 1, "Applications should still start.");
        Assert(reports.Any(message => message.Contains("not connected", StringComparison.OrdinalIgnoreCase)),
            $"The user was not told the device was missing. Reports: {string.Join(" | ", reports)}");

        // A failing device switch is also only a warning.
        audio.Devices.Add(new AudioDevice("headset", "Sim headset"));
        audio.ThrowOnSet = true;
        reports.Clear();
        var second = new SwitchProfile
        {
            Name = "ACC",
            AudioOutputDeviceId = "headset",
            AudioOutputDeviceName = "Sim headset"
        };
        document.Profiles.Add(second);
        Assert(await new ProfileActivationService(displays, processes, null, audio)
                .ActivateAsync(document, second, reports.Add),
            "A failing audio switch must not fail the profile switch.");
        Assert(reports.Any(message => message.Contains("Could not switch the audio", StringComparison.OrdinalIgnoreCase)),
            $"The failure was not reported. Reports: {string.Join(" | ", reports)}");
    }

    private static Task TestAudioPreviewAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("headset", "Sim headset"), new AudioDevice("speakers", "Desk speakers") },
            DefaultId = "speakers"
        };

        var service = new ActivationPreflightService(displays, processes, audio);

        var unchanged = ItemsIn(service.Build(document, target), "Audio");
        Assert(Mentions(unchanged, PreflightSeverity.Info, "will not change"),
            "A profile with no audio device should say the output is left alone.");

        target.AudioOutputDeviceId = "headset";
        target.AudioOutputDeviceName = "Sim headset";
        var switching = ItemsIn(service.Build(document, target), "Audio");
        Assert(Mentions(switching, PreflightSeverity.Info, "Switch audio output", "Sim headset"),
            "A pending audio change should be listed.");

        audio.DefaultId = "headset";
        var already = ItemsIn(service.Build(document, target), "Audio");
        Assert(Mentions(already, PreflightSeverity.Info, "already"),
            "No change should be reported when the device is already default.");

        audio.Devices.RemoveAll(device => device.Id == "headset");
        audio.DefaultId = "speakers";
        var missing = ItemsIn(service.Build(document, target), "Audio");
        Assert(Mentions(missing, PreflightSeverity.Problem, "not connected"),
            "A disconnected device should be a problem the user sees before switching.");
        return Task.CompletedTask;
    }

    private static Task TestAudioEnumerationAsync()
    {
        var service = new AudioDeviceService();
        if (!service.IsAvailable)
        {
            // A build agent may have no audio stack at all. Enumeration must then
            // degrade to empty rather than throwing.
            Assert(service.GetOutputDevices().Count == 0,
                "An unavailable audio stack should report no devices, not throw.");
            Assert(service.GetDefaultOutputDevice() is null,
                "An unavailable audio stack should report no default device.");
            return Task.CompletedTask;
        }

        var devices = service.GetOutputDevices();
        var inputs = service.GetInputDevices();
        foreach (var device in devices.Concat(inputs))
        {
            Assert(!string.IsNullOrWhiteSpace(device.Id), "Every endpoint needs an identifier.");
            Assert(!string.IsNullOrWhiteSpace(device.Name), "Every endpoint needs a name.");
        }
        Assert(devices.Select(device => device.Id).Distinct().Count() == devices.Count,
            "Playback endpoint identifiers must be unique.");
        Assert(inputs.Select(device => device.Id).Distinct().Count() == inputs.Count,
            "Recording endpoint identifiers must be unique.");
        Assert(devices.All(output => inputs.All(input => input.Id != output.Id)),
            "Playback and recording endpoints must not share identifiers.");

        var current = service.GetDefaultOutputDevice();
        if (current is not null)
            Assert(devices.Any(device => device.Id == current.Id),
                $"The default playback endpoint '{current.Name}' was not in the enumerated list.");

        var currentInput = service.GetDefaultInputDevice();
        if (currentInput is not null)
            Assert(inputs.Any(device => device.Id == currentInput.Id),
                $"The default recording endpoint '{currentInput.Name}' was not in the enumerated list.");

        if (Environment.GetEnvironmentVariable("SHERPA_PROBE_AUDIO") == "1")
        {
            Console.WriteLine($"[probe] default output = {current?.Name ?? "(none)"}");
            foreach (var device in devices) Console.WriteLine($"[probe]   out {device.Name}");
            Console.WriteLine($"[probe] default input  = {currentInput?.Name ?? "(none)"}");
            foreach (var device in inputs) Console.WriteLine($"[probe]   in  {device.Name}");
        }
        return Task.CompletedTask;
    }

    private static Task TestDisplayLayoutGeometryAsync()
    {
        // Two 1920x1080 monitors side by side, the right one primary at the origin.
        var snapshot = new DisplaySnapshot
        {
            ActiveTargets =
            [
                Monitor("left", "Dell P2419H", 1920, 1080, x: -1920),
                Monitor("right", "AOC 27B36X", 1920, 1080, refreshHz: 144)
            ]
        };

        var view = DisplayLayoutBuilder.Build(snapshot, 384, 216);
        Assert(view.Tiles.Count == 2, $"Expected two tiles, got {view.Tiles.Count}.");

        // 3840x1080 of desktop into 384x216 scales by 0.1, limited by width.
        Assert(Math.Abs(view.Width - 384) < 0.01, $"Expected a width of 384, got {view.Width}.");
        Assert(Math.Abs(view.Height - 108) < 0.01, $"Expected a height of 108, got {view.Height}.");

        // Negative desktop coordinates must be shifted so drawing starts at zero.
        var left = view.Tiles.Single(tile => tile.Name == "Dell P2419H");
        var right = view.Tiles.Single(tile => tile.Name == "AOC 27B36X");
        Assert(Math.Abs(left.X) < 0.01, $"The leftmost monitor should start at zero, got {left.X}.");
        Assert(Math.Abs(right.X - 192) < 0.01, $"The right monitor should start at 192, got {right.X}.");
        Assert(Math.Abs(left.Width - 192) < 0.01 && Math.Abs(left.Height - 108) < 0.01,
            "Monitors should scale by the same factor as the desktop.");

        Assert(right.IsPrimary, "The monitor at the desktop origin is the primary.");
        Assert(!left.IsPrimary, "A monitor away from the origin is not the primary.");
        Assert(right.Detail.Contains("144", StringComparison.Ordinal),
            $"The refresh rate should be shown, got '{right.Detail}'.");
        Assert(view.Summary.Contains("2 monitors", StringComparison.Ordinal),
            $"Unexpected summary '{view.Summary}'.");
        return Task.CompletedTask;
    }

    private static Task TestDisplayLayoutEdgeCasesAsync()
    {
        Assert(!DisplayLayoutBuilder.Build(null, 400, 300).HasTiles,
            "A profile with no captured layout has nothing to draw.");
        Assert(!DisplayLayoutBuilder.Build(new DisplaySnapshot(), 400, 300).HasTiles,
            "A snapshot with no monitors has nothing to draw.");

        // A monitor reported with no size cannot be placed. It must be left out
        // and accounted for, not silently dropped or allowed to divide by zero.
        var partial = new DisplaySnapshot
        {
            ActiveTargets =
            [
                Monitor("good", "AOC 27B36X", 2560, 1440),
                Monitor("broken", "Ghost", 0, 0, x: 5000)
            ]
        };
        var view = DisplayLayoutBuilder.Build(partial, 400, 300);
        Assert(view.Tiles.Count == 1, $"Expected the unusable monitor to be left out, got {view.Tiles.Count} tiles.");
        Assert(view.Summary.Contains("no usable size", StringComparison.OrdinalIgnoreCase),
            $"The summary should account for the skipped monitor, got '{view.Summary}'.");

        var unusable = DisplayLayoutBuilder.Build(
            new DisplaySnapshot { ActiveTargets = [Monitor("broken", "Ghost", 0, 0)] }, 400, 300);
        Assert(!unusable.HasTiles && unusable.Summary.Length > 0,
            "A layout with only unusable monitors should explain itself rather than draw nothing.");

        // A small layout must not be blown up to fill the dialog.
        var small = DisplayLayoutBuilder.Build(
            new DisplaySnapshot { ActiveTargets = [Monitor("one", "Small", 800, 600)] }, 4000, 3000);
        Assert(Math.Abs(small.Width - 800) < 0.01 && Math.Abs(small.Height - 600) < 0.01,
            $"A layout smaller than the canvas should not be scaled up; got {small.Width}x{small.Height}.");

        // Degenerate canvas sizes must not divide by zero or produce NaN.
        var tiny = DisplayLayoutBuilder.Build(
            new DisplaySnapshot { ActiveTargets = [Monitor("one", "Big", 3840, 2160)] }, 0, 0);
        Assert(tiny.HasTiles && !double.IsNaN(tiny.Width) && tiny.Width > 0,
            $"A zero-sized canvas should still produce a drawable tile; got width {tiny.Width}.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The launch order is only useful if it is visible, and the application-wide
    /// DataGrid style sets HeadersVisibility to Column, which hides row headers
    /// however wide they are. This renders a grid under those same styles to
    /// confirm a local override still draws the numbers.
    /// </summary>
    /// <summary>
    /// Profiles load after the window is already open, so the grid receives its
    /// items late. LoadingRow alone left the launch order blank until something
    /// regenerated the rows, which is why the numbers only appeared after clicking
    /// between profiles.
    /// </summary>
    /// <remarks>
    /// This mirrors the wiring in MainWindow rather than calling it: constructing
    /// the real window would start the tray icon, the profile store, and the
    /// display and process services.
    /// </remarks>
    /// <summary>
    /// Reproduces the applications grid as the window actually builds it: inside a
    /// TabControl that presents only its selected content, with the DataContext
    /// bound to the profile list's selection and the items bound to that profile's
    /// Applications. The selection is made after the window is up, as it is when
    /// profiles load.
    /// </summary>
    /// <summary>
    /// A profile with no applications still has to show the launch order column,
    /// otherwise the grid changes shape when a profile is selected and it looks
    /// like the column only exists sometimes.
    /// </summary>
    /// <summary>
    /// The startup sequence: the window opens with nothing selected, then the
    /// profile list makes its first selection once profiles have loaded. The
    /// launch order column must be there straight away, not only after the
    /// selection changes a second time.
    /// </summary>
    private static async Task TestActivationRecordedAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var audio = new FakeAudioDeviceService(processes.Events)
        {
            Devices = { new AudioDevice("speakers", "Desk speakers") },
            DefaultId = "speakers"
        };

        var target = new SwitchProfile { Name = "iRacing", Display = new DisplaySnapshot() };
        target.Applications.Add(new LaunchApplication { Name = "Sim", Path = @"C:\sim\app.exe" });
        // A device that is not connected produces a warning without failing.
        target.AudioOutputDeviceId = "absent";
        target.AudioOutputDeviceName = "Headset";
        var document = new ProfileDocument { Profiles = { target }, Settings = { DisplaySettleDelayMs = 0 } };

        var activator = new ProfileActivationService(displays, processes, null, audio);
        ActivationRecord? recorded = null;
        activator.ActivationRecorded += record => recorded = record;

        var seen = new List<string>();
        Assert(await activator.ActivateAsync(document, target, seen.Add), "The switch should succeed.");

        Assert(recorded is not null, "The switch was not recorded.");
        Assert(recorded!.ProfileName == "iRacing", $"Recorded the wrong profile: {recorded.ProfileName}.");
        Assert(recorded.Outcome == "succeeded_with_warnings", $"Unexpected outcome '{recorded.Outcome}'.");
        Assert(recorded.DurationMs >= 0, "The switch should have a duration.");

        // The history must be the messages the user actually saw, in order, or it
        // is a second description that can drift away from the truth.
        Assert(recorded.Steps.Select(step => step.Message).SequenceEqual(seen),
            "The recorded steps do not match what was reported to the user.");

        Assert(recorded.Warnings.Count == 1, $"Expected one warning, got {recorded.Warnings.Count}.");
        Assert(recorded.Steps.Count(step => step.IsWarning) == 1,
            "The warning should be marked on the step the user saw it on.");
        Assert(recorded.DescribeOutcome().Contains("warning", StringComparison.OrdinalIgnoreCase),
            $"Unexpected description '{recorded.DescribeOutcome()}'.");

        // The closing message is what the status bar shows, so it summarises rather
        // than repeating every warning already reported and recorded above.
        var closing = seen[^1];
        Assert(closing.Contains("1 warning", StringComparison.Ordinal),
            $"The closing message should count the warnings; got '{closing}'.");
        Assert(!closing.Contains("Headset", StringComparison.Ordinal),
            $"The closing message should not repeat the warning text; got '{closing}'.");
        Assert(closing.Length < 80, $"The closing message is too long for a status bar: '{closing}'.");

        // An application may share a name with the profile, so the closing message
        // has to be distinguishable from an application's own readiness message.
        Assert(closing.StartsWith("Switched to ", StringComparison.Ordinal),
            $"The closing message should name the switch, not read like an application's; got '{closing}'.");
        var appReady = $"{target.Name} is ready.";
        Assert(closing != appReady,
            "The profile's closing message must differ from the message an application of the same name produces.");
    }

    private static async Task TestCancelledActivationRecordedAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);

        var previousApp = new LaunchApplication { Name = "Previous", Path = "previous.exe" };
        processes.RunningPaths.Add(previousApp.Path);
        var previous = new SwitchProfile { Name = "Work", Applications = { previousApp } };
        var target = new SwitchProfile { Name = "iRacing", Display = new DisplaySnapshot { IsVerified = true } };
        target.Applications.Add(new LaunchApplication { Name = "Started", Path = "target-started.exe" });
        target.Applications.Add(new LaunchApplication
        {
            Name = "Delayed",
            Path = "target-delayed.exe",
            LaunchDelayMs = LaunchApplication.MaximumLaunchDelayMs
        });
        var document = new ProfileDocument
        {
            ActiveProfileId = previous.Id,
            Profiles = { previous, target },
            Settings = { DisplaySettleDelayMs = 0 }
        };

        var activator = new ProfileActivationService(displays, processes);
        ActivationRecord? recorded = null;
        activator.ActivationRecorded += record => recorded = record;

        using var cancellation = new CancellationTokenSource();
        var activation = activator.ActivateAsync(document, target, _ => { },
            cancellationToken: cancellation.Token);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (processes.Launched.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        cancellation.Cancel();
        try { await activation; } catch (OperationCanceledException) { }

        // A switch that went wrong is exactly the one worth looking at afterwards.
        Assert(recorded is not null, "A cancelled switch was not recorded.");
        Assert(recorded!.Outcome == "cancelled", $"Unexpected outcome '{recorded.Outcome}'.");
        Assert(!recorded.Succeeded, "A cancelled switch must not be reported as succeeded.");
        Assert(recorded.Steps.Count > 0, "A cancelled switch should still have steps.");
    }

    private static Task TestApplicationIssuesAsync()
    {
        using var directory = new TemporaryDirectory();
        var processes = new FakeProcessService();
        processes.ThrowOnResolvePath = @"C:\sim\missing.exe";

        var good = new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" };
        var missing = new LaunchApplication { Name = "Missing", Path = @"C:\sim\missing.exe" };
        var duplicate = new LaunchApplication { Name = "Crew Chief copy", Path = @"C:\sim\crew.exe" };
        var badDirectory = new LaunchApplication
        {
            Name = "Bad directory",
            Path = @"C:\sim\ok.exe",
            WorkingDirectory = Path.Combine(directory.Path, "absent")
        };
        var profile = new SwitchProfile
        {
            Name = "iRacing",
            Applications = { good, missing, duplicate, badDirectory }
        };

        var issues = ApplicationIssueScanner.Scan(profile, processes);

        Assert(!issues.ContainsKey(good.Id), "A usable entry should not be flagged.");
        Assert(issues[missing.Id].Kind == ApplicationIssueKind.NotFound,
            $"Expected NotFound, got {issues[missing.Id].Kind}.");
        Assert(issues[badDirectory.Id].Kind == ApplicationIssueKind.WorkingDirectoryMissing,
            $"Expected WorkingDirectoryMissing, got {issues[badDirectory.Id].Kind}.");

        // The first entry keeps the slot; the later one is what gets skipped.
        Assert(issues[duplicate.Id].Kind == ApplicationIssueKind.Duplicate,
            $"Expected Duplicate, got {issues[duplicate.Id].Kind}.");
        Assert(issues[duplicate.Id].Message.Contains("Crew Chief", StringComparison.Ordinal),
            $"The duplicate should name what it collides with; got '{issues[duplicate.Id].Message}'.");

        // A disabled spare is a deliberate choice, not a collision: it is never
        // launched, so it cannot conflict with anything.
        duplicate.Enabled = false;
        var withDisabled = ApplicationIssueScanner.Scan(profile, processes);
        Assert(!withDisabled.ContainsKey(duplicate.Id),
            "A disabled entry must not be reported as a duplicate.");

        // A broken path still matters when disabled, because enabling it later
        // would not re-announce the problem.
        missing.Enabled = false;
        Assert(ApplicationIssueScanner.Scan(profile, processes)[missing.Id].Kind == ApplicationIssueKind.NotFound,
            "A disabled entry with a broken path should still be flagged.");

        Assert(ApplicationIssueScanner.Scan(new SwitchProfile { Name = "Empty" }, processes).Count == 0,
            "A profile with no applications has nothing to flag.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// The flags and the launch order used to be refreshed by each editor action
    /// calling the scanner, and actions added later forgot to. This is the
    /// regression: nothing here asks for a rescan.
    /// </summary>
    private static Task TestIssueWatcherAsync()
    {
        var processes = new FakeProcessService();
        var crew = new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" };
        var iracing = new LaunchApplication { Name = "iRacing", Path = @"C:\sim\iracing.exe" };
        var profile = new SwitchProfile { Name = "iRacing", Applications = { crew, iracing } };

        var counts = new List<int>();
        using var watcher = new ApplicationIssueWatcher(processes, counts.Add);
        watcher.Watch(profile);

        Assert(counts is [0], $"Watching should scan once and find nothing; got [{string.Join(", ", counts)}].");
        Assert(crew.OrderLabel == "1" && iracing.OrderLabel == "2", "The initial scan should have numbered the list.");

        // Browsing for a file adds an entry and then sets its path. Neither step
        // told the watcher anything.
        var added = new LaunchApplication { Name = "New application" };
        profile.Applications.Add(added);
        Assert(added.OrderLabel == "3", $"An added entry should take the next number; got '{added.OrderLabel}'.");

        added.Path = @"C:\sim\crew.exe";
        Assert(added.HasIssue, "An entry that duplicates another should have been flagged as it was edited.");
        Assert(added.OrderLabel.Length == 0, "A flagged entry should give up its number.");
        Assert(counts[^1] == 1, $"The count should have reached the heading; got {counts[^1]}.");

        // A duplicate that is switched off is a deliberate spare, not a problem.
        added.Enabled = false;
        Assert(!added.HasIssue, "Disabling a duplicate should clear its flag.");
        Assert(added.OrderLabel == "3", $"Clearing the flag should restore the number; got '{added.OrderLabel}'.");

        // The up/down buttons and dragging both just move the item.
        profile.Applications.Move(2, 0);
        Assert(added.OrderLabel == "1" && crew.OrderLabel == "2",
            $"Moving should renumber; got '{added.OrderLabel}' and '{crew.OrderLabel}'.");

        profile.Applications.Remove(added);
        Assert(crew.OrderLabel == "1" && iracing.OrderLabel == "2", "Removing should renumber the rest.");

        // Watching something else must stop the old profile driving the heading.
        var before = counts.Count;
        watcher.Watch(null);
        crew.Path = @"C:\sim\other.exe";
        Assert(counts.Count == before + 1 && counts[^1] == 0,
            "Releasing the profile should report an empty heading and then stay quiet.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The open and closed padlocks are nearly identical at button size, so the
    /// state has to be legible in more than the glyph, and it has to follow the
    /// setting rather than be assigned when the button is clicked.
    /// </summary>
    private static Task TestDragLockButtonAsync()
    {
        var settings = new AppSettings();
        string lockedGlyph = string.Empty, unlockedGlyph = string.Empty;
        System.Windows.Media.Color lockedColour = default, unlockedColour = default;
        string lockedTip = string.Empty, unlockedTip = string.Empty;
        var missingStyle = false;

        OnUiThread(() =>
        {
            var style = System.Windows.Application.Current?.TryFindResource("DragLockButtonStyle")
                as System.Windows.Style;
            if (style is null) { missingStyle = true; return; }

            var button = new System.Windows.Controls.Button { Style = style, DataContext = settings };
            var window = new Window
            {
                Width = 200,
                Height = 120,
                Content = button,
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

                lockedGlyph = button.Content as string ?? string.Empty;
                lockedColour = ((System.Windows.Media.SolidColorBrush)button.Foreground).Color;
                lockedTip = button.ToolTip as string ?? string.Empty;

                // Only the setting changes. Nothing touches the button.
                settings.AllowApplicationDragReorder = true;
                window.UpdateLayout();

                unlockedGlyph = button.Content as string ?? string.Empty;
                unlockedColour = ((System.Windows.Media.SolidColorBrush)button.Foreground).Color;
                unlockedTip = button.ToolTip as string ?? string.Empty;
            }
            finally { window.Close(); }
        });

        Assert(!missingStyle, "DragLockButtonStyle is not in App.xaml.");
        Assert(lockedGlyph.Length > 0 && unlockedGlyph.Length > 0, "The button should carry a glyph in both states.");
        Assert(lockedGlyph != unlockedGlyph,
            $"Unlocking should change the glyph; it stayed '{lockedGlyph}'.");
        Assert(lockedColour != unlockedColour,
            $"The two states should differ in colour as well as glyph; both were {lockedColour}.");
        Assert(lockedTip != unlockedTip, "The tooltip should say which state the button is in.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Renders the shipped row header style, not a copy of it: a retemplated
    /// header with a DataTrigger fails silently when it fails, so what each header
    /// really shows has to be read back out of App.xaml's own template.
    /// </summary>
    private static Task TestRowHeaderStyleRendersAsync()
    {
        var processes = new FakeProcessService();
        processes.ThrowOnResolvePath = @"C:\sim\missing.exe";
        var profile = new SwitchProfile
        {
            Name = "iRacing",
            Applications =
            {
                new LaunchApplication { Name = "MOZA Pit House", Path = @"C:\sim\moza.exe" },
                new LaunchApplication { Name = "Missing", Path = @"C:\sim\missing.exe" },
                new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" }
            }
        };
        ApplicationIssueScanner.Apply(profile, processes);

        // What each row header actually shows: its number, whether the warning is
        // visible, and the reason behind it.
        var shown = new List<(string Order, bool Warning, string ToolTip)>();
        var missingStyle = false;

        OnUiThread(() =>
        {
            var style = System.Windows.Application.Current?.TryFindResource("ApplicationRowHeaderStyle")
                as System.Windows.Style;
            if (style is null) { missingStyle = true; return; }

            var grid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserSortColumns = false,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.All,
                RowHeaderWidth = 30,
                RowHeaderStyle = style,
                ItemsSource = profile.Applications
            };
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Name",
                Binding = new System.Windows.Data.Binding("Name")
            });

            var window = new Window
            {
                Width = 520,
                Height = 300,
                Content = grid,
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
                grid.UpdateLayout();

                foreach (var app in profile.Applications)
                {
                    if (grid.ItemContainerGenerator.ContainerFromItem(app) is not System.Windows.Controls.DataGridRow row)
                        continue;
                    if (FindVisualChild<System.Windows.Controls.Primitives.DataGridRowHeader>(row) is not { } header)
                        continue;

                    header.ApplyTemplate();
                    var order = (System.Windows.Controls.TextBlock)header.Template.FindName("Order", header);
                    var warning = (System.Windows.Controls.TextBlock)header.Template.FindName("Warning", header);
                    var cell = (System.Windows.Controls.Border)header.Template.FindName("HeaderCell", header);
                    shown.Add((
                        order.Visibility == System.Windows.Visibility.Visible ? order.Text : string.Empty,
                        warning.Visibility == System.Windows.Visibility.Visible,
                        cell.ToolTip as string ?? string.Empty));
                }
            }
            finally { window.Close(); }
        });

        Assert(!missingStyle,
            "ApplicationRowHeaderStyle is not in App.xaml; the grid would fall back to the Windows header.");
        Assert(shown.Count == 3, $"Expected three row headers, found {shown.Count}.");

        Assert(shown[0] == ("1", false, string.Empty), $"The first header showed {shown[0]}.");
        Assert(shown[2] == ("2", false, string.Empty), $"The third header showed {shown[2]}.");

        // The entry that will not start shows why, in place of a number.
        Assert(shown[1].Order.Length == 0, $"A flagged entry should show no number; it showed '{shown[1].Order}'.");
        Assert(shown[1].Warning, "A flagged entry should show the warning marker.");
        Assert(shown[1].ToolTip.Length > 0, "The warning marker needs the reason in its tooltip.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The row header shows the launch order, and an entry that will not start
    /// takes no number, so what remains reads as the sequence that will run.
    /// </summary>
    private static Task TestOrderLabelsAsync()
    {
        var processes = new FakeProcessService();
        processes.ThrowOnResolvePath = @"C:\sim\missing.exe";

        var first = new LaunchApplication { Name = "MOZA Pit House", Path = @"C:\sim\moza.exe" };
        var broken = new LaunchApplication { Name = "Missing", Path = @"C:\sim\missing.exe" };
        var second = new LaunchApplication { Name = "Crew Chief", Path = @"C:\sim\crew.exe" };
        var duplicate = new LaunchApplication { Name = "Crew Chief copy", Path = @"C:\sim\crew.exe" };
        var third = new LaunchApplication { Name = "iRacing", Path = @"C:\sim\iracing.exe" };
        var profile = new SwitchProfile
        {
            Name = "iRacing",
            Applications = { first, broken, second, duplicate, third }
        };

        var count = ApplicationIssueScanner.Apply(profile, processes);
        Assert(count == 2, $"Expected two flagged entries, got {count}.");

        Assert(first.OrderLabel == "1", $"Expected 1, got '{first.OrderLabel}'.");
        Assert(broken.OrderLabel.Length == 0, "A broken entry should carry no number.");
        Assert(second.OrderLabel == "2", $"Expected 2, got '{second.OrderLabel}'.");
        Assert(duplicate.OrderLabel.Length == 0, "A duplicate entry should carry no number.");
        Assert(third.OrderLabel == "3", $"Expected 3, got '{third.OrderLabel}'.");

        // Fixing the broken entry renumbers everything after it.
        processes.ThrowOnResolvePath = null;
        Assert(ApplicationIssueScanner.Apply(profile, processes) == 1, "Only the duplicate should stay flagged.");
        Assert(broken.OrderLabel == "2" && second.OrderLabel == "3" && third.OrderLabel == "4",
            $"Renumbering failed: {broken.OrderLabel}, {second.OrderLabel}, {third.OrderLabel}.");

        // Reordering the list reorders the numbers, which is what dragging does.
        profile.Applications.Move(4, 0);
        ApplicationIssueScanner.Apply(profile, processes);
        Assert(third.OrderLabel == "1", $"The moved entry should now be first, got '{third.OrderLabel}'.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tooltips are drawn from WPF's own default style rather than the window's, so
    /// they stay light in a dark application unless an implicit style overrides
    /// them. They are popups in their own window, so this checks the style is
    /// applied rather than photographing one.
    /// </summary>
    private static Task TestToolTipThemedAsync()
    {
        System.Windows.Media.Color background = default;
        System.Windows.Media.Color foreground = default;
        var templated = false;
        double maxWidth = 0;

        OnUiThread(() =>
        {
            var tip = new System.Windows.Controls.ToolTip { Content = "Starts the same thing as Crew Chief." };
            tip.ApplyTemplate();

            background = ((System.Windows.Media.SolidColorBrush)tip.Background).Color;
            foreground = ((System.Windows.Media.SolidColorBrush)tip.Foreground).Color;
            templated = tip.Template is not null;
            maxWidth = tip.MaxWidth;
        });

        var panel = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1B1A22");
        var text = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F0EFF5");

        Assert(background == panel, $"Tooltips should use the panel colour; got {background}.");
        Assert(foreground == text, $"Tooltips should use the light text colour; got {foreground}.");
        Assert(templated, "The tooltip should use Sherpa's template, not the Windows default.");
        Assert(maxWidth is > 0 and <= 600,
            $"Tooltips need a width bound so a long path does not stretch off screen; got {maxWidth}.");
        return Task.CompletedTask;
    }

    private static Task TestApplicationIssuesMatchPreviewAsync()
    {
        var (document, target, processes, displays) = PreviewFixture();
        processes.ThrowOnResolvePath = @"C:\sim\missing.exe";
        target.Applications.Add(new LaunchApplication { Name = "Good", Path = @"C:\sim\good.exe" });
        target.Applications.Add(new LaunchApplication { Name = "Missing", Path = @"C:\sim\missing.exe" });
        target.Applications.Add(new LaunchApplication { Name = "Copy", Path = @"C:\sim\good.exe" });

        var flagged = ApplicationIssueScanner.Scan(target, processes);
        var preview = ItemsIn(new ActivationPreflightService(displays, processes).Build(document, target),
            "Applications in iRacing");

        Assert(flagged.Count == 2, $"Expected two flagged entries, got {flagged.Count}.");
        Assert(Mentions(preview, PreflightSeverity.Problem, "Missing", "cannot be found"),
            "The preview should report the missing executable.");
        Assert(Mentions(preview, PreflightSeverity.Info, "Copy", "duplicate"),
            "The preview should report the duplicate.");
        return Task.CompletedTask;
    }

    private static async Task TestActivationHistoryLabellingAsync()
    {
        var processes = new FakeProcessService();
        var displays = new FakeDisplayConfigurationService(processes.Events);
        var activator = new ProfileActivationService(displays, processes);

        var recorded = new List<ActivationRecord>();
        activator.ActivationRecorded += recorded.Add;

        var work = new SwitchProfile { Name = "Work" };
        var sim = new SwitchProfile { Name = "iRacing" };
        var document = new ProfileDocument { Profiles = { work, sim }, Settings = { DisplaySettleDelayMs = 0 } };

        // Numbers must keep counting up across a session, so a switch can be named.
        await activator.ActivateAsync(document, work, _ => { });
        await activator.ActivateAsync(document, sim, _ => { });
        await activator.ActivateAsync(document, work, _ => { });

        Assert(recorded.Select(record => record.Sequence).SequenceEqual([1, 2, 3]),
            $"Switches were numbered {string.Join(", ", recorded.Select(record => record.Sequence))}.");
        Assert(recorded[1].ProfileName == "iRacing", "The second switch should be the sim profile.");

        // A separate service starts its own count rather than continuing another's.
        var second = new ProfileActivationService(displays, processes);
        ActivationRecord? fresh = null;
        second.ActivationRecorded += record => fresh = record;
        await second.ActivateAsync(document, work, _ => { });
        Assert(fresh?.Sequence == 1, $"A new session should start at 1, got {fresh?.Sequence}.");

        // The age label has to stay readable at every scale a long session reaches.
        var describe = typeof(ActivationHistoryWindow.SwitchRow)
            .GetMethod("DescribeAge", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DescribeAge was not found.");
        string Age(TimeSpan span) => (string)describe.Invoke(null, [span])!;

        Assert(Age(TimeSpan.FromSeconds(5)) == "just now", "Seconds should read as just now.");
        Assert(Age(TimeSpan.FromMinutes(3)) == "3 min ago", "Minutes should be counted.");
        Assert(Age(TimeSpan.FromMinutes(90)) == "1 h ago", "An hour and a half should read in hours.");
        Assert(Age(TimeSpan.FromHours(30)) == "1 d ago", "Over a day should read in days.");
        Assert(Age(TimeSpan.FromSeconds(50)) == "1 min ago",
            "Just under a minute should not round down to zero.");
        Assert(Age(TimeSpan.FromSeconds(-5)) == "just now",
            "A clock adjustment must not produce a negative age.");
    }

    private static Task TestActivationHistoryWindowRendersAsync()
    {
        var record = new ActivationRecord { ProfileName = "iRacing" };
        record.Steps.Add(new ActivationStep(record.StartedUtc, "Applying display layout…", false));
        record.Steps.Add(new ActivationStep(record.StartedUtc.AddSeconds(3.6), "Switching audio output to Headset…", false));
        record.Steps.Add(new ActivationStep(record.StartedUtc.AddSeconds(4.1), "Crew Chief did not reach 'window appears' in time.", true));
        record.Warnings.Add("Crew Chief did not reach 'window appears' in time.");
        record.Outcome = "succeeded_with_warnings";
        record.DurationMs = 4200;

        var bindingErrors = new List<string>();
        var rendered = RenderOffScreen(() => new ActivationHistoryWindow([record]), bindingErrors);

        Assert(bindingErrors.Count == 0,
            $"The history window reported data binding errors: {string.Join(" | ", bindingErrors)}");
        foreach (var expected in new[] { "iRacing", "Applying display layout…", "Switching audio output to Headset…" })
            Assert(rendered.Any(text => text.Contains(expected, StringComparison.Ordinal)),
                $"'{expected}' never reached the window.");
        Assert(rendered.Any(text => text.Contains("3.60s", StringComparison.Ordinal)),
            $"Step timings were not shown. Rendered: {string.Join(" | ", rendered)}");
        Assert(rendered.Any(text => text.Contains("warning", StringComparison.OrdinalIgnoreCase)),
            "The outcome should say the switch had warnings.");
        return Task.CompletedTask;
    }

    private static Task TestRowHeaderOnFirstSelectionAsync()
    {
        var empty = new SwitchProfile { Name = "Work" };
        var rendered = new List<string>();

        OnUiThread(() =>
        {
            var profiles = new System.Windows.Controls.ListBox
            {
                ItemsSource = new[] { empty },
                DisplayMemberPath = "Name"
            };

            var grid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.All,
                RowHeaderWidth = 30,
                AlternationCount = 2
            };
            grid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("Applications"));
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Name",
                Binding = new System.Windows.Data.Binding("Name")
            });

            var page = new System.Windows.Controls.Grid();
            page.SetBinding(System.Windows.FrameworkElement.DataContextProperty,
                new System.Windows.Data.Binding("SelectedItem") { Source = profiles });
            page.Children.Add(grid);

            var tabs = new System.Windows.Controls.TabControl { SelectedIndex = 0 };
            tabs.Items.Add(new System.Windows.Controls.TabItem { Content = page });

            var window = new Window
            {
                Width = 700,
                Height = 420,
                Content = tabs,
                ShowActivated = false,
                Opacity = 0,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000
            };
            try
            {
                // Opens with nothing selected, exactly as the window does before
                // profiles have loaded.
                window.Show();
                window.UpdateLayout();

                profiles.SelectedItem = empty;
                window.UpdateLayout();
                CollectText(window, rendered);
            }
            finally { window.Close(); }
        });

        Assert(rendered.Contains("\u2116"),
            $"The launch order column was missing on the first selection. Rendered: {string.Join(" | ", rendered)}");
        return Task.CompletedTask;
    }

    private static Task TestRowHeaderOnEmptyGridAsync()
    {
        var rendered = new List<string>();
        OnUiThread(() =>
        {
            var grid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.All,
                RowHeaderWidth = 30,
                ItemsSource = Array.Empty<LaunchApplication>()
            };
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Name",
                Binding = new System.Windows.Data.Binding("Name")
            });

            var window = new Window
            {
                Width = 520,
                Height = 300,
                Content = grid,
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
            finally { window.Close(); }
        });

        Assert(rendered.Contains("\u2116"),
            $"The row number column disappeared on an empty grid. Rendered: {string.Join(" | ", rendered)}");
        return Task.CompletedTask;
    }

    private static Task TestApplicationRowNumbersRenderAsync()
    {
        var bindingErrors = new List<string>();
        var rendered = RenderOffScreen(() =>
        {
            var grid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.All,
                RowHeaderWidth = 34,
                ItemsSource = new[] { "first", "second", "third" }
            };
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Value",
                Binding = new System.Windows.Data.Binding(".")
            });
            grid.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

            return new Window { Width = 420, Height = 320, Content = grid };
        }, bindingErrors);

        Assert(bindingErrors.Count == 0,
            $"The grid reported data binding errors: {string.Join(" | ", bindingErrors)}");
        foreach (var number in new[] { "1", "2", "3" })
            Assert(rendered.Contains(number),
                $"Row number {number} was not drawn. Rendered: {string.Join(" | ", rendered)}");

        // The corner between the row and column headers labels the column. It is a
        // button inside the DataGrid template and stays the Windows default white
        // square unless its component resource is replaced, so confirm ours drew.
        Assert(rendered.Contains("№"),
            $"The row number column header was not drawn. Rendered: {string.Join(" | ", rendered)}");
        return Task.CompletedTask;
    }

    private static Task TestDisplayLayoutWindowRendersAsync()
    {
        var snapshot = new DisplaySnapshot
        {
            ActiveTargets =
            [
                Monitor("left", "Dell P2419H", 1920, 1080, x: -1920),
                Monitor("centre", "AOC 27B36X", 2560, 1440, refreshHz: 144),
                Monitor("right", "LG 27GL850", 1920, 1080, x: 2560, rotation: 2)
            ],
            NvidiaSurround = new NvidiaSurroundSnapshot
            {
                ApiAvailable = true,
                StatusKnown = true,
                Enabled = true,
                FullGridCaptured = true,
                DisplayGrids =
                [
                    new NvidiaSurroundDisplayGridSnapshot
                    {
                        Rows = 1,
                        Columns = 3,
                        PerDisplayWidth = 1920,
                        PerDisplayHeight = 1080,
                        RefreshRate = 144,
                        ApplyWithBezelCorrection = true,
                        BitsPerPixel = 32
                    }
                ]
            }
        };

        var bindingErrors = new List<string>();
        var rendered = RenderOffScreen(() => new DisplayLayoutWindow("iRacing", snapshot), bindingErrors);

        Assert(bindingErrors.Count == 0,
            $"The layout window reported data binding errors: {string.Join(" | ", bindingErrors)}");

        foreach (var expected in new[] { "Dell P2419H", "AOC 27B36X", "LG 27GL850", "iRacing display layout" })
            Assert(rendered.Any(text => text.Contains(expected, StringComparison.Ordinal)),
                $"'{expected}' never reached the window.");

        // The Surround card has to show the grid, not just say it is enabled.
        foreach (var expected in new[] { "1 × 3", "Applied" })
            Assert(rendered.Any(text => text.Contains(expected, StringComparison.Ordinal)),
                $"The Surround detail '{expected}' was not rendered.");
        return Task.CompletedTask;
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

        var bindingErrors = new List<string>();
        var rendered = RenderOffScreen(() => new ActivationPreflightWindow(preflight), bindingErrors);

        Assert(bindingErrors.Count == 0,
            $"The preview window reported data binding errors: {string.Join(" | ", bindingErrors)}");
        Assert(expected.Count > 0, "The fixture should produce at least one preview item.");

        var missing = expected.Where(title => !rendered.Contains(title)).ToList();
        Assert(missing.Count == 0,
            $"These preview items never reached the window: {string.Join(" | ", missing)}");
        return Task.CompletedTask;
    }

    private static System.Collections.Concurrent.BlockingCollection<Action>? _uiWork;
    private static readonly object UiGate = new();

    /// <summary>
    /// One STA thread owning a single WPF Application, running test delegates
    /// directly rather than through a dispatcher message loop.
    /// </summary>
    /// <remarks>
    /// Two things are load-bearing here. Application.Current is per process but
    /// has thread affinity, so a second test creating its own thread finds it
    /// already owned and its window then builds no visual tree at all, silently.
    /// And the thread must not run Dispatcher.Run(): with a running dispatcher,
    /// Window.Show() defers its layout pass onto the queue behind the callback
    /// that called it, so the window reports 0x0 and an empty tree no matter how
    /// the queue is pumped from inside that callback.
    /// </remarks>
    private static void EnsureUiThread()
    {
        lock (UiGate)
        {
            if (_uiWork is not null) return;

            var work = new System.Collections.Concurrent.BlockingCollection<Action>();
            using var ready = new ManualResetEventSlim();
            Exception? startupFailure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var app = new App();
                    app.InitializeComponent();

                    // App.xaml declares OnMainWindowClose. The first window shown
                    // becomes MainWindow, so closing it shuts the Application down
                    // and every later window then builds no visual tree at all.
                    app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                }
                catch (Exception exception) { startupFailure = exception; }
                finally { ready.Set(); }

                if (startupFailure is not null) return;
                foreach (var item in work.GetConsumingEnumerable()) item();
            }) { IsBackground = true, Name = "SherpaManager.Tests UI" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The WPF test host did not start.");
            if (startupFailure is not null)
                throw new InvalidOperationException("The WPF test host failed to start.", startupFailure);

            _uiWork = work;
        }
    }

    private static void OnUiThread(Action action)
    {
        EnsureUiThread();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiWork!.Add(() =>
        {
            try
            {
                action();
                completed.TrySetResult();
            }
            catch (Exception exception) { completed.TrySetException(exception); }
        });

        if (!completed.Task.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("A UI test operation timed out.");
        completed.Task.GetAwaiter().GetResult();
    }

    /// <summary>Renders a window off-screen and returns every text node in it.</summary>
    private static List<string> RenderOffScreen(Func<Window> create, List<string> bindingErrors)
    {
        var rendered = new List<string>();
        OnUiThread(() =>
        {
            var listener = new BindingErrorListener(bindingErrors);
            System.Diagnostics.PresentationTraceSources.Refresh();
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Error | System.Diagnostics.SourceLevels.Warning;

            var window = create();
            window.ShowActivated = false;
            window.Opacity = 0;
            window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
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
        });

        if (rendered.Count == 0)
            throw new InvalidOperationException(
                "The window produced no visual tree at all, which means it never laid out rather than that its content was wrong.");
        return rendered;
    }

    /// <summary>Finds the first child of a type in a rendered visual tree.</summary>
    private static T? FindVisualChild<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } deeper) return deeper;
        }
        return null;
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

    private sealed class FakeAudioDeviceService(List<string> events) : IAudioDeviceService
    {
        public List<AudioDevice> Devices { get; } = [];
        public List<AudioDevice> InputDevices { get; } = [];
        public string? DefaultId { get; set; }
        public string? DefaultInputId { get; set; }
        public int SetCalls { get; set; }
        public bool ThrowOnSet { get; set; }

        /// <summary>
        /// Accept the call and change nothing, which is what setting a default
        /// through a shifted vtable would look like: a success code, no effect.
        /// </summary>
        public bool IgnoreSet { get; set; }
        public bool Available { get; set; } = true;

        public bool IsAvailable => Available;

        /// <summary>Adds a device once enumeration has been called this many times.</summary>
        public (int AfterQueries, AudioDevice Device)? AppearAfterQueries { get; set; }

        private int _queries;

        public IReadOnlyList<AudioDevice> GetOutputDevices()
        {
            _queries++;
            if (AppearAfterQueries is { } pending && _queries >= pending.AfterQueries &&
                Devices.All(device => device.Id != pending.Device.Id))
                Devices.Add(pending.Device);
            return Devices;
        }

        public IReadOnlyList<AudioDevice> GetInputDevices() => InputDevices;

        public AudioDevice? GetDefaultOutputDevice() =>
            Devices.FirstOrDefault(device => device.Id == DefaultId);

        public AudioDevice? GetDefaultInputDevice() =>
            InputDevices.FirstOrDefault(device => device.Id == DefaultInputId);

        public void SetDefaultDevice(string deviceId)
        {
            SetCalls++;
            if (ThrowOnSet) throw new InvalidOperationException("simulated audio failure");
            events.Add("audio:set:" + deviceId);
            if (IgnoreSet) return;
            // Windows derives the direction from the endpoint itself.
            if (InputDevices.Any(device => device.Id == deviceId)) DefaultInputId = deviceId;
            else DefaultId = deviceId;
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
        public HashSet<string> UntrackablePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ProcessLaunchResult> LaunchAsync(LaunchApplication app, CancellationToken cancellationToken)
        {
            if (app.Path.Equals(ThrowOnLaunchPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("simulated launch failure");
            Events.Add("launch:" + app.Path);
            Launched.Add(app.Id);
            RunningPaths.Add(app.Path);
            return Task.FromResult(new ProcessLaunchResult(true, app.StartMinimized, "started",
                LifecycleManageable: !UntrackablePaths.Contains(app.Path)));
        }
        public Task<bool> MinimizeAsync(LaunchApplication app, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public List<Guid> ReadinessWaits { get; } = [];
        public bool ReadinessSucceeds { get; set; } = true;
        public Task<ProcessReadinessResult> WaitUntilReadyAsync(LaunchApplication app, LaunchReadiness readiness,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            ReadinessWaits.Add(app.Id);
            Events.Add("ready:" + app.Path);
            return Task.FromResult(ReadinessSucceeds
                ? new ProcessReadinessResult(true, $"{app.Name} is ready.")
                : new ProcessReadinessResult(false, $"{app.Name} did not reach '{readiness.Describe()}' in time; the profile continued anyway."));
        }
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

    private const int SwRestore = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

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
