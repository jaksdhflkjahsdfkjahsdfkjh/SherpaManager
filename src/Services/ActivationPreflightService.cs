using SherpaManager.Models;

namespace SherpaManager.Services;

/// <summary>
/// Predicts what <see cref="ProfileActivationService"/> would do, without
/// applying anything. It reads the live display topology and asks the process
/// service what is currently running, but it never changes either.
/// </summary>
public sealed class ActivationPreflightService(IDisplayConfigurationService displays, IProcessService processes,
    IAudioDeviceService? audio = null)
{
    private const uint NvidiaVendorId = 0x10DE;

    public ActivationPreflight Build(ProfileDocument document, SwitchProfile target)
    {
        var preflight = new ActivationPreflight { ProfileName = target.Name };

        DisplaySnapshot? current = null;
        string? captureFailure = null;
        try { current = displays.Capture(); }
        catch (Exception exception) { captureFailure = exception.Message; }

        preflight.Sections.Add(BuildDisplaySection(document, target, current, captureFailure));
        preflight.Sections.Add(BuildSurroundSection(target, current, captureFailure));

        preflight.Sections.Add(BuildAudioSection(target, target.Display is not null));

        var startSection = BuildStartSection(document, target, out var targetIdentities);
        var previous = document.Profiles.FirstOrDefault(profile => profile.Id == document.ActiveProfileId);
        preflight.Sections.Add(BuildCurrentApplicationsSection(previous, target, targetIdentities));
        preflight.Sections.Add(startSection);

        return preflight;
    }

    private static PreflightSection BuildDisplaySection(ProfileDocument document, SwitchProfile target,
        DisplaySnapshot? current, string? captureFailure)
    {
        var section = new PreflightSection("Displays");

        if (target.Display is null)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                "The display layout will not change.",
                "This profile has no captured layout, so activation leaves your monitors exactly as they are."));
            return section;
        }

        if (current is null)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                "The current display layout could not be read.",
                captureFailure ?? "Windows did not return a display configuration."));
            return section;
        }

        var currentByIdentity = IndexByIdentity(current.ActiveTargets);
        var targetByIdentity = IndexByIdentity(target.Display.ActiveTargets);

        foreach (var (identity, desired) in targetByIdentity)
        {
            if (currentByIdentity.TryGetValue(identity, out var live))
            {
                foreach (var change in DescribeChanges(live, desired)) section.Items.Add(change);
                continue;
            }

            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                $"Enable {Describe(desired)}.", DescribeMode(desired)));
        }

        foreach (var (identity, live) in currentByIdentity)
        {
            if (targetByIdentity.ContainsKey(identity)) continue;
            section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
                $"Disable {Describe(live)}.",
                "It is removed from the Windows desktop, not powered off. Anything on it moves to a remaining monitor."));
        }

        if (targetByIdentity.Count == 0)
            section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                "This layout has no monitors in it.",
                "Activation would leave no usable desktop, so the display step will fail safely instead."));

        if (section.Items.Count == 0)
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                "Your monitors already match this layout.",
                "The layout is still reapplied, but nothing should visibly change."));

        section.Items.Add(DescribeConfirmation(document, target.Display));

        var settleDelay = document.Settings.DisplaySettleDelayMs;
        if (settleDelay > 0)
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                $"Applications wait {settleDelay / 1000.0:0.#} seconds after the displays change.",
                "This gives the monitors time to finish switching before anything is started or closed."));

        return section;
    }

    private PreflightSection BuildAudioSection(SwitchProfile target, bool displayWillChange)
    {
        var section = new PreflightSection("Audio");

        DescribeAudioEndpoint(section, "output", target.AudioOutputDeviceId, target.AudioOutputDeviceName,
            audio is null ? null : audio.GetOutputDevices, audio is null ? null : audio.GetDefaultOutputDevice,
            displayWillChange);
        DescribeAudioEndpoint(section, "input", target.AudioInputDeviceId, target.AudioInputDeviceName,
            audio is null ? null : audio.GetInputDevices, audio is null ? null : audio.GetDefaultInputDevice,
            displayWillChange);
        return section;
    }

    private void DescribeAudioEndpoint(PreflightSection section, string kind, string deviceId, string deviceName,
        Func<IReadOnlyList<AudioDevice>>? list, Func<AudioDevice?>? current, bool displayWillChange)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                $"The audio {kind} will not change.",
                $"This profile is set to leave the default {(kind == "output" ? "playback" : "recording")} device alone."));
            return;
        }

        var wanted = deviceName is { Length: > 0 } name ? name : "the saved device";
        if (audio is null || !audio.IsAvailable || list is null || current is null)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
                $"The audio {kind} should switch to {wanted}, but Windows audio could not be read.",
                "The switch will continue and report a warning."));
            return;
        }

        var active = current();
        if (active is not null && active.Id == deviceId)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info, $"Audio {kind} is already {active.Name}."));
            return;
        }

        if (list().All(device => device.Id != deviceId))
        {
            // A monitor's audio endpoint only exists once that monitor is on, so a
            // device missing now may well arrive as part of this very switch.
            section.Items.Add(displayWillChange
                ? new PreflightItem(PreflightSeverity.Caution,
                    $"The audio {kind} device {wanted} is not connected yet.",
                    "If it belongs to a monitor this profile enables, Sherpa waits for it to appear after the displays change. Otherwise it is left as it is.")
                : new PreflightItem(PreflightSeverity.Problem,
                    $"The audio {kind} device {wanted} is not connected.",
                    "It will be left as it is and the switch will report a warning."));
            return;
        }

        section.Items.Add(new PreflightItem(PreflightSeverity.Info,
            $"Switch audio {kind} to {wanted}.",
            active is null ? null : $"Currently {active.Name}. Ordinary use and communications are both changed."));
    }

    private static PreflightItem DescribeConfirmation(ProfileDocument document, DisplaySnapshot desired)
    {
        if (!document.Settings.ConfirmDisplayChanges)
            return new PreflightItem(PreflightSeverity.Caution,
                "No confirmation countdown will appear.",
                "Confirm display changes is turned off in Settings, so the layout is applied without asking. If you cannot see the result, use Win+P or Windows Display Settings.");

        return desired.IsVerified
            ? new PreflightItem(PreflightSeverity.Info,
                "You confirmed this layout before.",
                "The 10-second countdown reappears only if your monitors, GPU, ports, or driver have changed since then.")
            : new PreflightItem(PreflightSeverity.Info,
                "You will get a 10-second countdown to confirm.",
                "If you do not keep the layout, Sherpa restores the previous one automatically.");
    }

    /// <summary>
    /// Reports when the screens are not on the NVIDIA card, so a Surround change
    /// cannot do anything.
    /// </summary>
    /// <remarks>
    /// Common on laptops with switchable graphics, and on desktops where the
    /// processor's graphics are still enabled and a monitor is plugged into the
    /// motherboard rather than the card. NVAPI reports the card in both cases, so
    /// the profile looks fine right up until it fails.
    /// </remarks>
    /// <returns>The item to show, or null when there is nothing to say.</returns>
    private static PreflightItem? DescribeGraphicsMismatch(DisplaySnapshot? current)
    {
        // Empty means the adapters were never recorded, which is not the same as
        // there being no NVIDIA adapter.
        if (current is null || current.Adapters.Count == 0) return null;
        if (current.Adapters.Any(adapter => adapter.VendorId == NvidiaVendorId)) return null;

        var driving = string.Join(" and ", current.Adapters.Select(adapter =>
            string.IsNullOrEmpty(adapter.Vendor) ? "an unrecognised adapter" : adapter.Vendor + " graphics"));
        var detail = $"The displays are being driven by {driving}, not by the NVIDIA card. " +
                     "Surround only applies to displays connected to the NVIDIA card itself. " +
                     "On a desktop, check the monitors are plugged into the card rather than the motherboard; " +
                     "on a laptop with switchable graphics, Surround is usually unavailable.";
        return new PreflightItem(PreflightSeverity.Problem,
            "Surround cannot be changed: no displays are on the NVIDIA card.", detail);
    }

    private static PreflightSection BuildSurroundSection(SwitchProfile target, DisplaySnapshot? current,
        string? captureFailure)
    {
        var section = new PreflightSection("NVIDIA Surround");
        var live = current?.NvidiaSurround;

        if (target.NvidiaSurroundMode == NvidiaSurroundMode.Ignore)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                "Surround will not be managed.",
                "This profile is set to Do not manage, so whatever Surround state you are in now is left alone."));
            return section;
        }

        var wantEnabled = target.NvidiaSurroundMode == NvidiaSurroundMode.RequireEnabled;
        var verb = wantEnabled ? "enabled" : "disabled";

        if (live is null)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                $"Surround must be {verb}, but its current state is unknown.",
                captureFailure ?? "The NVIDIA API did not report a status."));
            return section;
        }

        if (!live.ApiAvailable)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                $"Surround must be {verb}, but NVAPI is unavailable.",
                $"{live.Description} Activation will fail at the display step."));
            return section;
        }

        if (!live.StatusKnown)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
                $"Surround must be {verb}, but its current state could not be read.",
                live.Description));
            return section;
        }

        // NVAPI answers for the card whether or not the card is driving anything.
        // On a machine with switchable or processor graphics the monitors can be
        // on the other adapter, and the Surround change then fails at the display
        // step with a bare driver error.
        if (DescribeGraphicsMismatch(current) is { } mismatch)
        {
            section.Items.Add(mismatch);
            return section;
        }

        if (live.Enabled == wantEnabled)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                $"Surround is already {verb}.", "No Surround change is needed."));
            return section;
        }

        if (wantEnabled && !live.IsPossible)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                "Surround must be enabled, but the driver reports no usable grid.",
                $"{live.Description} Capture the sim profile while Surround is on, or enable it in the NVIDIA app first."));
            return section;
        }

        section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
            $"Surround will be turned {(wantEnabled ? "on" : "off")}.",
            "Screens go black briefly while the grid is rebuilt. A change that would require a driver reload is refused rather than forced."));
        return section;
    }

    private PreflightSection BuildStartSection(ProfileDocument document, SwitchProfile target,
        out HashSet<string> targetIdentities)
    {
        var section = new PreflightSection($"Applications in {target.Name}");
        targetIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in target.Applications.Where(app => app.Enabled))
        {
            string identity;
            try
            {
                identity = processes.GetIdentityKey(app);
                targetIdentities.Add(identity);
            }
            catch (Exception exception)
            {
                section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                    $"{app.Name} cannot be found.", exception.Message));
                continue;
            }

            var workingDirectory = Environment.ExpandEnvironmentVariables(app.WorkingDirectory.Trim());
            if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            {
                section.Items.Add(new PreflightItem(PreflightSeverity.Problem,
                    $"{app.Name} has a working directory that does not exist.", workingDirectory));
                continue;
            }

            if (!seen.Add(identity))
            {
                section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                    $"{app.Name} is a duplicate entry and will be skipped.",
                    "Another enabled entry in this profile already starts the same thing."));
                continue;
            }

            bool running;
            try { running = processes.IsRunning(app); }
            catch (Exception exception)
            {
                section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
                    $"Could not tell whether {app.Name} is running.", exception.Message));
                continue;
            }

            if (running)
            {
                section.Items.Add(app.StartMinimized
                    ? new PreflightItem(PreflightSeverity.Info,
                        $"{app.Name} is already running and will be minimized.")
                    : new PreflightItem(PreflightSeverity.Info,
                        $"{app.Name} is already running and will be left alone."));
                continue;
            }

            var notes = new List<string>();
            if (app.LaunchDelayMs > 0) notes.Add($"after a {app.LaunchDelayMs} ms delay");
            if (app.StartMinimized) notes.Add("minimized");

            var detail = notes.Count == 0
                ? null
                : char.ToUpperInvariant(notes[0][0]) + string.Join(", ", notes)[1..] + ".";
            section.Items.Add(new PreflightItem(PreflightSeverity.Info, $"Start {app.Name}.", detail));
        }

        if (section.Items.Count == 0)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                "This profile has no enabled applications.",
                "Activation will only change displays."));
            return section;
        }

        var rule = document.Settings.LaunchReadiness;
        section.Items.Add(rule == LaunchReadiness.None
            ? new PreflightItem(PreflightSeverity.Info, "Applications start one after another without waiting.")
            : new PreflightItem(PreflightSeverity.Info,
                $"Each application is started only once the previous one's {rule.Describe()}.",
                $"Up to {document.Settings.LaunchReadinessTimeoutMs / 1000.0:0.#}s each. Applications Sherpa cannot track are not waited for."));

        return section;
    }

    private PreflightSection BuildCurrentApplicationsSection(SwitchProfile? previous, SwitchProfile target,
        IReadOnlySet<string> targetIdentities)
    {
        var name = previous is null ? "the current profile" : previous.Name;
        var section = new PreflightSection($"Applications in {name}");

        if (previous is null || previous.Id == target.Id)
        {
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                previous is null
                    ? "No profile is active, so nothing will be closed."
                    : $"{target.Name} is already the active profile, so nothing will be closed."));
            return section;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in previous.Applications.Where(app => app.Enabled))
        {
            string identity;
            try { identity = processes.GetIdentityKey(app); }
            catch (Exception exception)
            {
                section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
                    $"{app.Name} could not be identified and its close step will be skipped.", exception.Message));
                continue;
            }

            if (targetIdentities.Contains(identity) || !seen.Add(identity)) continue;

            bool running;
            try { running = processes.IsRunning(app); }
            catch (Exception exception)
            {
                section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
                    $"Could not tell whether {app.Name} is running.", exception.Message));
                continue;
            }

            if (!running) continue;

            section.Items.Add(app.CloseOnDeactivate
                ? new PreflightItem(PreflightSeverity.Caution,
                    $"Close {app.Name}.",
                    "It is asked to exit normally first, then force-closed if it does not. Unsaved work in it can be lost.")
                : new PreflightItem(PreflightSeverity.Info,
                    $"{app.Name} stays running.",
                    "Close on switch is turned off for this entry."));
        }

        if (section.Items.Count == 0)
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                $"Nothing from {name} is running, so nothing will be closed."));

        return section;
    }

    private static Dictionary<string, DisplayTargetSnapshot> IndexByIdentity(
        IEnumerable<DisplayTargetSnapshot> targets)
    {
        var index = new Dictionary<string, DisplayTargetSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            // Identity is the durable monitor key. Fall back to the path index so a
            // snapshot that predates identities still produces a readable preview
            // instead of collapsing every monitor onto one empty key.
            var key = !string.IsNullOrWhiteSpace(target.Identity)
                ? target.Identity
                : $"path:{target.PathIndex}";
            index.TryAdd(key, target);
        }
        return index;
    }

    private static IEnumerable<PreflightItem> DescribeChanges(DisplayTargetSnapshot live,
        DisplayTargetSnapshot desired)
    {
        var name = Describe(desired);

        if (live.SourceWidth != desired.SourceWidth || live.SourceHeight != desired.SourceHeight ||
            !RefreshMatches(live, desired))
            yield return new PreflightItem(PreflightSeverity.Info,
                $"{name}: {DescribeMode(live)} to {DescribeMode(desired)}.");

        if (live.Rotation != desired.Rotation)
            yield return new PreflightItem(PreflightSeverity.Caution,
                $"{name}: rotation {DescribeRotation(live.Rotation)} to {DescribeRotation(desired.Rotation)}.");

        var wasPrimary = live is { SourceX: 0, SourceY: 0 };
        var willBePrimary = desired is { SourceX: 0, SourceY: 0 };
        if (!wasPrimary && willBePrimary)
            yield return new PreflightItem(PreflightSeverity.Info, $"{name} becomes the primary display.");
        else if (wasPrimary && !willBePrimary)
            yield return new PreflightItem(PreflightSeverity.Info, $"{name} is no longer the primary display.");
        else if (live.SourceX != desired.SourceX || live.SourceY != desired.SourceY)
            yield return new PreflightItem(PreflightSeverity.Info,
                $"{name} moves to {desired.SourceX}, {desired.SourceY}.");
    }

    private static bool RefreshMatches(DisplayTargetSnapshot live, DisplayTargetSnapshot desired) =>
        Math.Abs(RefreshHz(live) - RefreshHz(desired)) < 0.5;

    private static double RefreshHz(DisplayTargetSnapshot target) =>
        target.RefreshDenominator == 0 ? 0 : target.RefreshNumerator / (double)target.RefreshDenominator;

    private static string Describe(DisplayTargetSnapshot target)
    {
        if (!string.IsNullOrWhiteSpace(target.FriendlyName)) return target.FriendlyName;
        if (!string.IsNullOrWhiteSpace(target.Identity)) return target.Identity;
        return $"display {target.PathIndex + 1}";
    }

    private static string DescribeMode(DisplayTargetSnapshot target)
    {
        var resolution = $"{target.SourceWidth}×{target.SourceHeight}";
        var hz = RefreshHz(target);
        return hz > 0 ? $"{resolution} at {hz:0.##} Hz" : resolution;
    }

    private static string DescribeRotation(uint rotation) => rotation switch
    {
        2 => "90°",
        3 => "180°",
        4 => "270°",
        _ => "none"
    };
}
