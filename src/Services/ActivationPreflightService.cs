using SherpaManager.Models;

namespace SherpaManager.Services;

/// <summary>
/// Predicts what <see cref="ProfileActivationService"/> would do, without
/// applying anything. It reads the live display topology and asks the process
/// service what is currently running, but it never changes either.
/// </summary>
public sealed class ActivationPreflightService(IDisplayConfigurationService displays, IProcessService processes)
{
    public ActivationPreflight Build(ProfileDocument document, SwitchProfile target)
    {
        var preflight = new ActivationPreflight { ProfileName = target.Name };

        DisplaySnapshot? current = null;
        string? captureFailure = null;
        try { current = displays.Capture(); }
        catch (Exception exception) { captureFailure = exception.Message; }

        preflight.Sections.Add(BuildDisplaySection(document, target, current, captureFailure));
        preflight.Sections.Add(BuildSurroundSection(target, current, captureFailure));

        var startSection = BuildStartSection(target, out var targetIdentities);
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
                $"{live.Description} Capture the sim profile while Surround is on, or enable it in NVIDIA Control Panel first."));
            return section;
        }

        section.Items.Add(new PreflightItem(PreflightSeverity.Caution,
            $"Surround will be turned {(wantEnabled ? "on" : "off")}.",
            "Screens go black briefly while the grid is rebuilt. A change that would require a driver reload is refused rather than forced."));
        return section;
    }

    private PreflightSection BuildStartSection(SwitchProfile target, out HashSet<string> targetIdentities)
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

            var detail = app.LaunchDelayMs > 0
                ? $"After a {app.LaunchDelayMs} ms delay{(app.StartMinimized ? ", minimized" : string.Empty)}."
                : app.StartMinimized ? "Minimized." : null;
            section.Items.Add(new PreflightItem(PreflightSeverity.Info, $"Start {app.Name}.", detail));
        }

        if (section.Items.Count == 0)
            section.Items.Add(new PreflightItem(PreflightSeverity.Info,
                "This profile has no enabled applications.",
                "Activation will only change displays."));

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
