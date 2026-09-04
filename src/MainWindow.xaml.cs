using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SherpaManager.Models;
using SherpaManager.Services;
using Forms = System.Windows.Forms;

namespace SherpaManager;

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly DiagnosticsService _diagnostics;
    private readonly DisplayConfigurationService _displays;
    private readonly ProcessService _processes;
    private readonly ProfileActivationService _activator;
    private readonly ActivationPreflightService _preflight;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly StartupRegistrationService _startup;
    private readonly ShortcutService _shortcuts;
    private readonly System.Drawing.Icon? _applicationIcon;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly HashSet<SwitchProfile> _observedProfiles = [];
    private readonly HashSet<string> _inFlightTargetIdentities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<PendingProcessCloseOutcome> _pendingCloseOutcomes = [];
    private ProfileDocument _document = new();
    private CancellationTokenSource? _busyOperationCancellation;
    private bool _isBusy;
    private bool _allowClose;
    private bool _allowBusyClose;
    private bool _loadingSettings;
    private bool _profilesLoaded;
    private bool _sessionEnding;
    private bool _resourcesDisposed;
    private bool _closeInProgress;
    private bool _activationOverridesClose;
    private bool _handlingPendingCloseOutcomes;
    private bool _openingSettings;
    private volatile bool _acceptActivation = true;
    private WindowState _lastVisibleState = WindowState.Normal;
    private SwitchProfile? _profileBeforeSettings;
    private bool _startupActivationHandled;
    private bool _capturingHotkey;

    /// <summary>Profile name requested on the command line, applied once profiles have loaded.</summary>
    public string? PendingActivationRequest { get; set; }

    public MainWindow()
    {
        _diagnostics = DiagnosticsService.Current;
        _displays = new DisplayConfigurationService(diagnostics: _diagnostics);
        _processes = new ProcessService(diagnostics: _diagnostics);
        InitializeComponent();
        SurroundModeCombo.ItemsSource = new[]
        {
            new SurroundModeOption(NvidiaSurroundMode.Ignore, "Do not manage"),
            new SurroundModeOption(NvidiaSurroundMode.RequireEnabled, "Require enabled"),
            new SurroundModeOption(NvidiaSurroundMode.RequireDisabled, "Require disabled")
        };
        _activator = new ProfileActivationService(_displays, _processes, _diagnostics);
        _preflight = new ActivationPreflightService(_displays, _processes);
        _hotkeys = new GlobalHotkeyService(_diagnostics);
        _startup = new StartupRegistrationService(_diagnostics);
        _shortcuts = new ShortcutService(_diagnostics);
        _hotkeys.HotkeyPressed += Hotkeys_HotkeyPressed;
        _processes.PendingCloseCompleted += ProcessService_PendingCloseCompleted;
        _processes.PendingMinimizationCompleted += ProcessService_PendingMinimizationCompleted;
        _applicationIcon = TryLoadApplicationIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Sherpa Manager",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        Loaded += MainWindow_Loaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        _hotkeys.Attach(handle);
        ApplyHotkeys();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var handledInterruptedRecovery = await OfferInterruptedDisplayRecoveryAsync();
        if (!_acceptActivation) return;

        ProfileDocument document;
        try
        {
            document = await _store.LoadAsync();
        }
        catch (Exception ex)
        {
            if (_acceptActivation) ShowError("Could not load profiles", ex);
            return;
        }

        if (!_acceptActivation) return;

        _document = document;
        _profilesLoaded = true;
        ProfilesList.ItemsSource = _document.Profiles;
        ProfilesList.SelectedItem = _document.Profiles.FirstOrDefault(x => x.Id == _document.ActiveProfileId)
                                    ?? _document.Profiles.FirstOrDefault();
        _document.Profiles.CollectionChanged += Profiles_CollectionChanged;
        SynchronizeProfileObservers();
        _loadingSettings = true;
        CloseToTrayCheckBox.IsChecked = _document.Settings.MinimizeToTrayOnClose;
        ConfirmDisplayChangesCheckBox.IsChecked = _document.Settings.ConfirmDisplayChanges;
        ShowActivationPreviewCheckBox.IsChecked = _document.Settings.ShowActivationPreview;
        // The registry is the truth for Windows startup: the user can remove the
        // entry from Task Manager without Sherpa ever knowing.
        _document.Settings.StartWithWindows = _startup.IsRegistered;
        StartWithWindowsCheckBox.IsChecked = _document.Settings.StartWithWindows;
        RebuildStartupProfileCombo();
        _loadingSettings = false;
        UpdateHotkeyButton();
        ApplyHotkeys();
        try { await _store.SaveAsync(_document); }
        catch (Exception ex) { ShowError("Could not save profiles", ex); }
        RefreshActiveProfile();
        RebuildTrayMenu();
        if (!handledInterruptedRecovery)
            StatusText.Text = $"Profiles are stored in {_store.FilePath}";
        await HandleStartupActivationAsync();
    }

    /// <summary>
    /// Applies a profile requested on the command line, or the configured startup
    /// profile. Runs at most once per launch so a later window activation cannot
    /// silently re-trigger a switch.
    /// </summary>
    private async Task HandleStartupActivationAsync()
    {
        if (_startupActivationHandled || !_acceptActivation) return;
        _startupActivationHandled = true;

        var requested = PendingActivationRequest;
        PendingActivationRequest = null;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            await ActivateProfileByNameAsync(requested);
            return;
        }

        if (_document.Settings.ActivateProfileOnStartup is not { } startupProfileId) return;
        if (_document.Profiles.FirstOrDefault(profile => profile.Id == startupProfileId) is not { } profile) return;
        ProfilesList.SelectedItem = profile;
        await ActivateProfileAsync(profile);
    }

    /// <summary>
    /// Handles a request from a second launch. Returns true once the request has
    /// been acted on, which is what releases the waiting process.
    /// </summary>
    public async Task<bool> HandleActivationRequestAsync(string? profileName)
    {
        var restored = TryRestoreAndActivate();
        if (string.IsNullOrWhiteSpace(profileName)) return restored;
        if (!_acceptActivation) return restored;

        if (!_profilesLoaded)
        {
            // Still starting up. Let the normal startup path apply it instead of
            // racing the profile load.
            PendingActivationRequest = profileName;
            return restored;
        }

        await ActivateProfileByNameAsync(profileName);
        return true;
    }

    private async Task ActivateProfileByNameAsync(string profileName)
    {
        var wanted = profileName.Trim();
        var profile = _document.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            _diagnostics.Write("warning", "activation.request.unknown_profile", data: new Dictionary<string, object?>
            {
                ["profileCount"] = _document.Profiles.Count
            });
            StatusText.Text = $"No profile is named \"{wanted}\".";
            return;
        }

        ProfilesList.SelectedItem = profile;
        await ActivateProfileAsync(profile);
    }

    private void Hotkeys_HotkeyPressed(Guid profileId)
    {
        if (!_acceptActivation || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (!_acceptActivation) return;
            if (_document.Profiles.FirstOrDefault(profile => profile.Id == profileId) is not { } profile) return;
            TryRestoreAndActivate();
            ProfilesList.SelectedItem = profile;
            await ActivateProfileAsync(profile);
        });
    }

    private void ApplyHotkeys()
    {
        if (!_profilesLoaded) return;
        var failures = _hotkeys.Apply(_document.Profiles
            .Select(profile => (profile.Id, profile.Name, (string?)profile.Hotkey)));
        if (failures.Count > 0) StatusText.Text = string.Join("  ", failures);
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is null) return;
        _capturingHotkey = true;
        HotkeyButton.Content = "Press keys…";
        SetHotkeyStatus("Press a combination using Ctrl, Alt, or Win. Esc cancels.");
    }

    private async void HotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey || SelectedProfile is not { } profile) return;

        // With Alt held, WPF reports Key.System and puts the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Let Tab move focus so the field is never a keyboard trap.
        if (key == Key.Tab)
        {
            EndHotkeyCapture();
            return;
        }

        e.Handled = true;

        // Ignore the modifiers themselves; wait for the key they qualify.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return;

        if (key == Key.Escape)
        {
            EndHotkeyCapture();
            SetHotkeyStatus(null);
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            await SetHotkeyAsync(profile, string.Empty, "Shortcut cleared.");
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyDefinition.TryFromInput(modifiers, virtualKey, out var hotkey, out var error))
        {
            // Stay in capture mode so the next attempt does not need another click.
            SetHotkeyStatus(error);
            return;
        }

        await SetHotkeyAsync(profile, hotkey!.Text, null);
    }

    private async Task SetHotkeyAsync(SwitchProfile profile, string hotkey, string? status)
    {
        profile.Hotkey = hotkey;
        EndHotkeyCapture();
        SetHotkeyStatus(status);
        ApplyHotkeys();
        await SaveAsync();
    }

    private async void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        if (string.IsNullOrEmpty(profile.Hotkey)) return;
        await SetHotkeyAsync(profile, string.Empty, "Shortcut cleared.");
    }

    private void HotkeyButton_LostFocus(object sender, RoutedEventArgs e) => EndHotkeyCapture();

    /// <summary>
    /// Writes the shortcut status line, collapsing it when empty so the card stays
    /// one row tall unless there is something to say.
    /// </summary>
    private void SetHotkeyStatus(string? text)
    {
        HotkeyStatusText.Text = text ?? string.Empty;
        HotkeyStatusText.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        UpdateHotkeyButton();
    }

    private void UpdateHotkeyButton()
    {
        if (_capturingHotkey) return;
        HotkeyButton.Content = SelectedProfile is { Hotkey.Length: > 0 } profile
            ? profile.Hotkey
            : "Set shortcut…";
    }

    private void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        try
        {
            var path = _shortcuts.CreateDesktopShortcut(profile.Name);
            StatusText.Text = $"Created {System.IO.Path.GetFileName(path)} on the desktop.";
        }
        catch (Exception exception)
        {
            ShowError($"Could not create a desktop shortcut for {profile.Name}", exception);
        }
    }

    private void RebuildStartupProfileCombo()
    {
        var options = new List<StartupProfileOption> { new(null, "None") };
        options.AddRange(_document.Profiles.Select(profile => new StartupProfileOption(profile.Id, profile.Name)));
        StartupProfileCombo.ItemsSource = options;
        StartupProfileCombo.SelectedValue = _document.Settings.ActivateProfileOnStartup;
        if (StartupProfileCombo.SelectedItem is null) StartupProfileCombo.SelectedIndex = 0;
    }

    private async void StartupProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || !IsLoaded || !_profilesLoaded) return;
        _document.Settings.ActivateProfileOnStartup = StartupProfileCombo.SelectedValue as Guid?;
        await SaveAsync();
    }

    private SwitchProfile? SelectedProfile => ProfilesList.SelectedItem as SwitchProfile;

    private async Task<bool> OfferInterruptedDisplayRecoveryAsync()
    {
        var interrupted = _displays.InterruptedTransaction;
        if (interrupted is null || !_acceptActivation) return false;

        EnsureWindowIsVisible();
        var started = interrupted.StartedAtUtc is { } startedAtUtc
            ? $"\n\nStarted: {startedAtUtc.ToLocalTime():g}"
            : string.Empty;
        var recoveryAvailability = interrupted.RecoveryAvailable
            ? "Sherpa can restore the display layout from before that operation."
            : "The automatic recovery snapshot is missing or damaged. If the current layout is unusable, use Win+P or Windows Display Settings.";
        var answer = System.Windows.MessageBox.Show(this,
            $"Sherpa detected a display operation that did not finish cleanly.\n\nRequested layout: {interrupted.RequestedSummary}{started}\n\n{recoveryAvailability}\n\nRestore the previous display layout now?\n\nChoose No to keep the current layout and dismiss this recovery.",
            "Interrupted display operation", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        if (answer == MessageBoxResult.No)
        {
            try
            {
                _displays.DiscardInterruptedTransaction();
                StatusText.Text = "Kept the current display layout and dismissed interrupted-operation recovery.";
                RebuildTrayMenu();
            }
            catch (Exception exception)
            {
                ShowError("Could not dismiss display recovery", exception);
            }
            return true;
        }

        var cancellationToken = BeginBusyOperation("Recovering the interrupted display operation…");
        try
        {
            var result = await _displays.RestoreInterruptedTransactionAsync(cancellationToken);
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
            RebuildTrayMenu();
        }
        catch (OperationCanceledException)
        {
            EnsureWindowIsVisible();
            StatusText.Text = "Interrupted-operation recovery was cancelled. It remains available from Restore previous.";
        }
        catch (Exception exception)
        {
            ShowError("Could not recover the interrupted display operation", exception);
        }
        finally { EndBusyOperation(); }
        return true;
    }

    private async void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        ApplicationsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ApplicationsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (SelectedProfile is { } profile) await ActivateProfileAsync(profile);
    }

    /// <summary>
    /// Shows what the switch would do and returns whether to go ahead. A preview
    /// that cannot be produced never blocks activation; the switch itself still
    /// validates everything and can still roll back.
    /// </summary>
    private async Task<bool> ConfirmActivationAsync(SwitchProfile profile)
    {
        if (!_document.Settings.ShowActivationPreview) return true;

        ActivationPreflight preflight;
        var cancellationToken = BeginBusyOperation($"Checking what switching to {profile.Name} would do…");
        try
        {
            preflight = await Task.Run(() => _preflight.Build(_document, profile), cancellationToken);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            _diagnostics.Error("activation.preview.failed", exception,
                new Dictionary<string, object?> { ["targetProfileId"] = profile.Id });
            return true;
        }
        finally { EndBusyOperation(); }

        EnsureWindowIsVisible();
        var preview = new ActivationPreflightWindow(preflight) { Owner = this };
        preview.ShowDialog();

        if (preview.SkipFuturePreviews)
        {
            _document.Settings.ShowActivationPreview = false;
            ShowActivationPreviewCheckBox.IsChecked = false;
            await SaveAsync();
        }

        return preview.Proceed;
    }

    private async Task ActivateProfileAsync(SwitchProfile profile)
    {
        if (_isBusy) return;
        if (!await ConfirmActivationAsync(profile)) return;
        _inFlightTargetIdentities.Clear();
        foreach (var app in profile.Applications.Where(app => app.Enabled))
        {
            try { _inFlightTargetIdentities.Add(_processes.GetIdentityKey(app)); }
            catch { /* Activation validation reports malformed entries to the user. */ }
        }
        var cancellationToken = BeginBusyOperation($"Switching to {profile.Name}…");
        try
        {
            var activated = await _activator.ActivateAsync(_document, profile,
                message => Dispatcher.Invoke(() => ReportBusyStatus(message)), ConfirmDisplayAsync,
                cancellationToken);
            EnsureWindowIsVisible();
            if (activated)
            {
                RefreshActiveProfile();
                RebuildTrayMenu();
            }
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
            EnsureWindowIsVisible();
            StatusText.Text = "Profile switch cancelled; the previous state was restored.";
            await SaveAsync();
        }
        catch (Exception ex) { ShowError($"Could not activate {profile.Name}", ex); }
        finally
        {
            _inFlightTargetIdentities.Clear();
            EndBusyOperation();
        }
    }

    private async void CaptureDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        try
        {
            var captured = _displays.Capture();
            if (captured.NvidiaSurround is { Enabled: true, FullGridCaptured: false } incomplete)
                throw new InvalidOperationException(
                    $"NVIDIA Surround is active, but Sherpa could not capture its complete grid. {incomplete.Description}");

            profile.Display = captured;
            if (captured.NvidiaSurround is { StatusKnown: true } surround)
                profile.NvidiaSurroundMode = (surround.HasConfiguredTopology, surround.Enabled) switch
                {
                    (true, true) => NvidiaSurroundMode.RequireEnabled,
                    (true, false) => NvidiaSurroundMode.RequireDisabled,
                    _ => NvidiaSurroundMode.Ignore
                };
            if (await SaveAsync())
                StatusText.Text = $"Captured the Windows topology and supported NVIDIA display settings for {profile.Name}.";
        }
        catch (Exception ex) { ShowError("Could not capture the display layout", ex); }
    }

    private async void ClearDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile || profile.Display is null) return;
        profile.Display = null;
        if (await SaveAsync())
            StatusText.Text = $"{profile.Name} will now keep the current display layout.";
    }

    private async void TestDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { Display: { } snapshot } profile || _isBusy) return;
        var cancellationToken = BeginBusyOperation($"Testing {profile.Name} display layout…");
        try
        {
            var result = await _displays.RestoreAsync(snapshot, profile.NvidiaSurroundMode,
                ConfirmDisplayAsync, cancellationToken);
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
            if (result.Kept)
            {
                StatusText.Text = $"{profile.Name} display layout is verified.";
            }
            else StatusText.Text = "Reverted to the previous display layout without saving the test layout.";
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
            EnsureWindowIsVisible();
            StatusText.Text = "Display test cancelled; the previous layout was restored.";
        }
        catch (Exception ex) { ShowError("Could not test the display layout", ex); }
        finally { EndBusyOperation(); }
    }

    private async void RestoreLastDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var hasInterruptedTransaction = _displays.InterruptedTransaction is not null;
        if (!hasInterruptedTransaction && !_displays.HasRecoverySnapshot)
        {
            StatusText.Text = "No previous display recovery snapshot is available.";
            return;
        }
        var cancellationToken = BeginBusyOperation(hasInterruptedTransaction
            ? "Recovering the interrupted display operation…"
            : "Restoring the previous display layout…");
        try
        {
            var result = hasInterruptedTransaction
                ? await _displays.RestoreInterruptedTransactionAsync(cancellationToken)
                : await _displays.RestoreLastRecoveryAsync(cancellationToken);
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
            RebuildTrayMenu();
        }
        catch (OperationCanceledException)
        {
            EnsureWindowIsVisible();
            StatusText.Text = hasInterruptedTransaction
                ? "Interrupted-operation recovery was cancelled. It remains available from Restore previous."
                : "Display restore cancelled; the layout from before this operation was restored.";
        }
        catch (Exception ex) { ShowError("Could not restore the previous display layout", ex); }
        finally { EndBusyOperation(); }
    }

    private Task<bool> ConfirmDisplayAsync(DisplaySnapshot snapshot)
    {
        EnsureWindowIsVisible();
        var workArea = SystemParameters.WorkArea;
        var confirmation = new DisplayConfirmationWindow(snapshot.Summary)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = workArea.Left + Math.Max(0, (workArea.Width - 520) / 2),
            Top = workArea.Top + Math.Max(0, (workArea.Height - 260) / 2)
        };
        confirmation.ShowDialog();
        return Task.FromResult(confirmation.KeepLayout);
    }

    private void ProcessService_PendingCloseCompleted(PendingProcessCloseOutcome outcome)
    {
        if (!_acceptActivation || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_acceptActivation || !_processes.IsPendingCloseOutcomeCurrent(outcome)) return;
            _pendingCloseOutcomes.Enqueue(outcome);
            _ = DrainPendingCloseOutcomesAsync();
        });
    }

    private async Task DrainPendingCloseOutcomesAsync()
    {
        if (_handlingPendingCloseOutcomes || _isBusy || _closeInProgress || !_acceptActivation) return;
        _handlingPendingCloseOutcomes = true;
        try
        {
            while (_pendingCloseOutcomes.Count > 0 && !_isBusy && !_closeInProgress && _acceptActivation)
            {
                var outcome = _pendingCloseOutcomes.Dequeue();
                await HandlePendingCloseCompletedAsync(outcome);
            }
        }
        finally { _handlingPendingCloseOutcomes = false; }
    }

    private Task HandlePendingCloseCompletedAsync(PendingProcessCloseOutcome outcome)
    {
        if (!_acceptActivation || !_processes.IsPendingCloseOutcomeCurrent(outcome) ||
            IsIdentityDesired(outcome.IdentityKey))
            return Task.CompletedTask;
        try
        {
            var result = outcome.Result;
            StatusText.Text = result.Message;
            if (!result.Succeeded) ShowTrayWarning("Application still running", result.Message);
        }
        catch (Exception exception)
        {
            if (_acceptActivation) ShowError($"Could not finish closing {outcome.ApplicationName}", exception);
        }
        return Task.CompletedTask;
    }

    private void ProcessService_PendingMinimizationCompleted(PendingProcessMinimizationOutcome outcome)
    {
        if (!_acceptActivation || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_acceptActivation || !IsIdentityDesired(outcome.IdentityKey)) return;
            StatusText.Text = outcome.Message;
            if (!outcome.Minimized) ShowTrayWarning("Application was not minimized", outcome.Message);
        });
    }

    private bool IsIdentityDesired(string identity)
    {
        if (_inFlightTargetIdentities.Contains(identity)) return true;
        if (!_profilesLoaded) return false;
        var active = _document.Profiles.FirstOrDefault(profile => profile.Id == _document.ActiveProfileId);
        if (active is null) return false;
        foreach (var app in active.Applications.Where(app => app.Enabled))
        {
            try
            {
                if (_processes.GetIdentityKey(app).Equals(identity, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { /* A malformed entry cannot match a resolved close identity. */ }
        }
        return false;
    }

    private void ShowTrayWarning(string title, string message)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message.Length <= 240 ? message : message[..237] + "...";
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _trayIcon.ShowBalloonTip(5000);
    }

    private async void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = new SwitchProfile { Name = "New profile", Description = "Describe when you use this setup" };
        _document.Profiles.Add(profile);
        ProfilesList.SelectedItem = profile;
        await SaveAsync();
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is null) return;
        MainTabs.SelectedIndex = 0;
        ProfileNameBox.Focus();
        ProfileNameBox.SelectAll();
    }

    private async void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } selected) return;
        var copy = selected.Clone();
        _document.Profiles.Add(copy);
        ProfilesList.SelectedItem = copy;
        await SaveAsync();
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } selected) return;
        if (_document.Profiles.Count == 1)
        {
            StatusText.Text = "The last profile cannot be deleted. Create another one first.";
            return;
        }
        var result = System.Windows.MessageBox.Show($"Delete the profile “{selected.Name}”?", "Sherpa Manager",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var index = _document.Profiles.IndexOf(selected);
        _document.Profiles.Remove(selected);
        if (_document.ActiveProfileId == selected.Id) _document.ActiveProfileId = null;
        ProfilesList.SelectedIndex = Math.Min(index, _document.Profiles.Count - 1);
        await SaveAsync();
        RefreshActiveProfile();
    }

    private async void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        var app = new LaunchApplication();
        profile.Applications.Add(app);
        ApplicationsGrid.SelectedItem = app;
        ApplicationsGrid.ScrollIntoView(app);
        await SaveAsync();
    }

    private async void RemoveApplication_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile || ApplicationsGrid.SelectedItem is not LaunchApplication app) return;
        profile.Applications.Remove(app);
        await SaveAsync();
    }

    private async void MoveApplicationUp_Click(object sender, RoutedEventArgs e) => await MoveSelectedApplicationAsync(-1);

    private async void MoveApplicationDown_Click(object sender, RoutedEventArgs e) => await MoveSelectedApplicationAsync(1);

    private async Task MoveSelectedApplicationAsync(int offset)
    {
        if (SelectedProfile is not { } profile || ApplicationsGrid.SelectedItem is not LaunchApplication app) return;
        var oldIndex = profile.Applications.IndexOf(app);
        var newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= profile.Applications.Count) return;
        profile.Applications.Move(oldIndex, newIndex);
        ApplicationsGrid.SelectedItem = app;
        await SaveAsync();
    }

    private async void BrowseApplication_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an application",
            Filter = "Applications and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*",
            CheckFileExists = true
        };
        // Ask first. A cancelled dialog must not leave an empty row behind, so
        // nothing is added to the profile until there is a file to put in it.
        if (dialog.ShowDialog(this) != true) return;

        var app = ApplicationsGrid.SelectedItem as LaunchApplication;
        var added = app is null;
        if (app is null)
        {
            app = new LaunchApplication();
            profile.Applications.Add(app);
            ApplicationsGrid.SelectedItem = app;
        }

        app.Path = dialog.FileName;
        if (app.Name == "New application") app.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(app.WorkingDirectory)) app.WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
        ApplicationsGrid.Items.Refresh();
        if (!await SaveAsync()) return;

        var verb = added ? "Added" : "Updated";
        try
        {
            var resolved = new LaunchTargetResolver().Resolve(app);
            StatusText.Text = resolved.IsShortcutOrProtocol && string.IsNullOrWhiteSpace(resolved.ProcessName)
                ? $"{verb} {app.Name}, but its launched process could not be inferred. Select the actual executable when possible so Sherpa can manage it."
                : resolved.IsShortcutOrProtocol && !resolved.HasExplicitProcessName
                    ? $"{verb} {app.Name}. Sherpa associated it with {resolved.ProcessName} for minimizing and closing."
                : $"{verb} {app.Name}.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void ApplicationsGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        await Dispatcher.InvokeAsync(async () => await SaveAsync());
    }

    private async void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedProfile is { } selectedProfile) _profileBeforeSettings = selectedProfile;
        EndHotkeyCapture();
        if (!IsLoaded || _openingSettings || SelectedProfile is null) return;
        MainTabs.SelectedIndex = 0;
        await SaveAsync();
    }

    private void ProfileItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: SwitchProfile profile })
            return;

        _profileBeforeSettings = profile;
        ProfilesList.SelectedItem = profile;
        MainTabs.SelectedIndex = 0;
    }

    /// <summary>
    /// A ListBox does not select on right-click, so the context menu would act on
    /// whichever profile happened to be selected instead of the one clicked.
    /// </summary>
    private void ProfileItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: SwitchProfile profile }) return;
        _profileBeforeSettings = profile;
        ProfilesList.SelectedItem = profile;
        MainTabs.SelectedIndex = 0;
    }

    private void ShowProfilePage_Click(object sender, RoutedEventArgs e)
    {
        ProfilesList.SelectedItem ??= _profileBeforeSettings ?? _document.Profiles.FirstOrDefault();
        MainTabs.SelectedIndex = 0;
    }

    private void ShowSettingsPage_Click(object sender, RoutedEventArgs e)
    {
        _profileBeforeSettings = SelectedProfile ?? _profileBeforeSettings;
        _openingSettings = true;
        ProfilesList.SelectedItem = null;
        _openingSettings = false;
        MainTabs.SelectedIndex = 1;
    }

    private async void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || !IsLoaded) return;
        _document.Settings.MinimizeToTrayOnClose = CloseToTrayCheckBox.IsChecked == true;
        _document.Settings.ConfirmDisplayChanges = ConfirmDisplayChangesCheckBox.IsChecked == true;
        _document.Settings.ShowActivationPreview = ShowActivationPreviewCheckBox.IsChecked == true;

        var wantsStartup = StartWithWindowsCheckBox.IsChecked == true;
        if (wantsStartup != _startup.IsRegistered && !_startup.SetRegistered(wantsStartup))
        {
            StatusText.Text = "Windows startup could not be changed. Check that the registry is writable.";
            _loadingSettings = true;
            StartWithWindowsCheckBox.IsChecked = _startup.IsRegistered;
            _loadingSettings = false;
        }
        _document.Settings.StartWithWindows = _startup.IsRegistered;

        await SaveAsync();
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DisplaySnapshot? topology = null;
        string? topologyError = null;
        try { topology = _displays.Capture(); }
        catch (Exception exception)
        {
            topologyError = exception.Message;
            _diagnostics.Error("diagnostics.topology_capture.failed", exception);
        }

        try
        {
            _diagnostics.Write("info", "diagnostics.copy.requested", data: new Dictionary<string, object?>
            {
                ["topologyAvailable"] = topology is not null
            });
            System.Windows.Clipboard.SetText(_diagnostics.CreateClipboardReport(topology, topologyError));
            StatusText.Text = "Redacted diagnostics copied to the clipboard.";
        }
        catch (Exception exception)
        {
            ShowError("Could not copy diagnostics", exception);
        }
    }

    private async void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SwitchProfile.NvidiaSurroundMode) || sender is not SwitchProfile profile) return;
        if (profile.Display is not null)
        {
            profile.Display.IsVerified = false;
            profile.Display.VerificationEnvironmentFingerprint = string.Empty;
            profile.Display.VerifiedAtUtc = null;
        }
        await SaveAsync();
    }

    private void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeProfileObservers();
        RebuildTrayMenu();
        RebuildStartupProfileCombo();
        ApplyHotkeys();
    }

    private void SynchronizeProfileObservers()
    {
        foreach (var removed in _observedProfiles.Except(_document.Profiles).ToList())
        {
            removed.PropertyChanged -= Profile_PropertyChanged;
            _observedProfiles.Remove(removed);
        }

        foreach (var profile in _document.Profiles.Where(profile => !_observedProfiles.Contains(profile)))
        {
            profile.PropertyChanged += Profile_PropertyChanged;
            _observedProfiles.Add(profile);
        }
    }

    private void RefreshActiveProfile()
    {
        var active = _document.Profiles.FirstOrDefault(x => x.Id == _document.ActiveProfileId);
        ActiveProfileText.Text = active?.Name ?? "None active";
    }

    private void RebuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Sherpa", null, (_, _) => ShowFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var profile in _document.Profiles)
        {
            var item = menu.Items.Add($"Switch to {profile.Name}");
            item.Font = new System.Drawing.Font(item.Font, profile.Id == _document.ActiveProfileId ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
            item.Click += async (_, _) => await Dispatcher.InvokeAsync(async () => await ActivateProfileAsync(profile));
        }
        var interruptedTransaction = _displays.InterruptedTransaction;
        if (interruptedTransaction is not null || _displays.HasRecoverySnapshot)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(interruptedTransaction is not null
                    ? "Recover interrupted display operation"
                    : "Restore previous display layout", null,
                async (_, _) => await Dispatcher.InvokeAsync(async () => await RestoreLastDisplayFromTrayAsync()));
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        var previous = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private void ShowFromTray()
    {
        Dispatcher.Invoke(RestoreAndActivate);
    }

    private async Task RestoreLastDisplayFromTrayAsync()
    {
        ShowFromTray();
        if (_isBusy) return;
        var hasInterruptedTransaction = _displays.InterruptedTransaction is not null;
        if (!hasInterruptedTransaction && !_displays.HasRecoverySnapshot)
        {
            StatusText.Text = "No previous display recovery snapshot is available.";
            return;
        }
        var cancellationToken = BeginBusyOperation(hasInterruptedTransaction
            ? "Recovering the interrupted display operation…"
            : "Restoring the previous display layout…");
        try
        {
            var result = hasInterruptedTransaction
                ? await _displays.RestoreInterruptedTransactionAsync(cancellationToken)
                : await _displays.RestoreLastRecoveryAsync(cancellationToken);
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
            RebuildTrayMenu();
        }
        catch (OperationCanceledException)
        {
            EnsureWindowIsVisible();
            StatusText.Text = hasInterruptedTransaction
                ? "Interrupted-operation recovery was cancelled. It remains available from Restore previous."
                : "Display restore cancelled; the layout from before this operation was restored.";
        }
        catch (Exception ex) { ShowError("Could not restore the previous display layout", ex); }
        finally { EndBusyOperation(); }
    }

    public void RestoreAndActivate() => TryRestoreAndActivate();

    public bool TryRestoreAndActivate()
    {
        if (!_acceptActivation || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return false;
        if (_closeInProgress)
        {
            if (_sessionEnding) return false;
            // MessageBox runs a nested dispatcher loop. If a second launch arrives
            // while a save-error prompt is open, accepting that launch guarantees
            // this interactive close will be cancelled below.
            _activationOverridesClose = true;
        }
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastVisibleState;
        EnsureWindowIsVisible();
        var target = OwnedWindows.Cast<Window>().LastOrDefault(window => window.IsVisible) ?? this;
        var activated = target.Activate();
        var targetHandle = new WindowInteropHelper(target).Handle;
        var handle = GetLastActivePopup(targetHandle);
        if (handle == IntPtr.Zero) handle = targetHandle;
        if (handle != targetHandle) activated = SetForegroundWindow(handle);
        if (!activated && !SetForegroundWindow(handle))
        {
            var flash = new FlashWindowInfo
            {
                Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                Window = handle,
                Flags = 15,
                Count = 3,
                Timeout = 0
            };
            FlashWindowEx(ref flash);
        }
        if (!_closeInProgress && _pendingCloseOutcomes.Count > 0)
            _ = DrainPendingCloseOutcomesAsync();
        return _acceptActivation;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) _lastVisibleState = WindowState;
    }

    private void EnsureWindowIsVisible()
    {
        if (!IsLoaded) return;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var virtualDesktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var windowBounds = new Rect(Left, Top, Math.Max(1, width), Math.Max(1, height));
        if (virtualDesktop.IntersectsWith(windowBounds)) return;

        var workArea = SystemParameters.WorkArea;
        WindowState = WindowState.Normal;
        Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isBusy && !_allowBusyClose)
        {
            e.Cancel = true;
            _allowClose = false;
            StatusText.Text = "Finish or revert the current profile switch before exiting Sherpa.";
            RestoreAndActivate();
            return;
        }

        Keyboard.ClearFocus();
        ApplicationsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ApplicationsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _closeInProgress = true;

        try
        {
            // Until loading has succeeded, _document is only an empty placeholder. Never
            // let an early close replace a user's profile file with that placeholder.
            if (!_profilesLoaded)
            {
                CompleteFinalClose();
                return;
            }

            var shouldHide = !_allowClose && _document.Settings.MinimizeToTrayOnClose;
            if (_sessionEnding)
            {
                try { _store.SaveAsync(_document).Wait(TimeSpan.FromSeconds(2)); }
                catch { /* Windows is ending the session; make a bounded best-effort save. */ }
            }
            else
            {
                var saveDecision = SaveBeforeCloseInteractively();
                if (_activationOverridesClose || saveDecision == CloseSaveDecision.Cancel)
                {
                    e.Cancel = true;
                    _allowClose = false;
                    _allowBusyClose = false;
                    Dispatcher.BeginInvoke(RestoreAndActivate);
                    return;
                }

                // "Exit without saving" is deliberately a full exit, even when the X
                // button would normally hide the window. This makes the destructive
                // fallback explicit and avoids silently running with unsaved state.
                if (saveDecision == CloseSaveDecision.ExitWithoutSaving) shouldHide = false;
            }

            if (shouldHide)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            CompleteFinalClose();
        }
        finally
        {
            _closeInProgress = false;
            _activationOverridesClose = false;
            if (e.Cancel && IsVisible && _pendingCloseOutcomes.Count > 0)
                Dispatcher.BeginInvoke(() => _ = DrainPendingCloseOutcomesAsync());
        }
    }

    private CloseSaveDecision SaveBeforeCloseInteractively()
    {
        while (true)
        {
            try
            {
                _store.SaveAsync(_document).GetAwaiter().GetResult();
                return CloseSaveDecision.Saved;
            }
            catch (Exception exception)
            {
                var message = $"Sherpa Manager could not save your profiles.\n\n{exception.Message}\n\n" +
                              "Yes: retry saving\nNo: exit without saving\nCancel: keep Sherpa Manager open";
                var answer = IsVisible
                    ? System.Windows.MessageBox.Show(this, message, "Could not save profiles",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Error, MessageBoxResult.Cancel)
                    : System.Windows.MessageBox.Show(message, "Could not save profiles",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Error, MessageBoxResult.Cancel);

                if (answer == MessageBoxResult.Yes) continue;
                return answer == MessageBoxResult.No
                    ? CloseSaveDecision.ExitWithoutSaving
                    : CloseSaveDecision.Cancel;
            }
        }
    }

    private void CompleteFinalClose()
    {
        _acceptActivation = false;
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        if (_profilesLoaded) _document.Profiles.CollectionChanged -= Profiles_CollectionChanged;
        foreach (var profile in _observedProfiles) profile.PropertyChanged -= Profile_PropertyChanged;
        _observedProfiles.Clear();
        _processes.PendingCloseCompleted -= ProcessService_PendingCloseCompleted;
        _processes.PendingMinimizationCompleted -= ProcessService_PendingMinimizationCompleted;
        _hotkeys.HotkeyPressed -= Hotkeys_HotkeyPressed;
        _hotkeys.Dispose();
        _displays.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _applicationIcon?.Dispose();
    }

    private static System.Drawing.Icon? TryLoadApplicationIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(executablePath)
                ? null
                : System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    public void AllowFullClose(bool includeBusyOperation = false, bool sessionEnding = false)
    {
        _allowClose = true;
        _allowBusyClose = includeBusyOperation;
        _sessionEnding = sessionEnding;
    }

    private void OpenProfileFile_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_store.FilePath}\"") { UseShellExecute = true });
    }

    private void OpenDisplaySettings_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });

    private void OpenNvidiaControlPanel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe",
                @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel")
            { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("Could not open NVIDIA Control Panel", ex); }
    }

    private async Task<bool> SaveAsync()
    {
        if (!_profilesLoaded || _document.Profiles.Count == 0) return false;
        try
        {
            await _store.SaveAsync(_document);
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save profiles: {ex.Message}";
            return false;
        }
    }

    private CancellationToken BeginBusyOperation(string message)
    {
        _busyOperationCancellation?.Dispose();
        _busyOperationCancellation = new CancellationTokenSource();
        SetBusy(true, message);
        return _busyOperationCancellation.Token;
    }

    private void EndBusyOperation()
    {
        SetBusy(false);
        _busyOperationCancellation?.Dispose();
        _busyOperationCancellation = null;
    }

    private void CancelBusyOperation_Click(object sender, RoutedEventArgs e) => RequestBusyCancellation();

    private void RequestBusyCancellation()
    {
        if (_busyOperationCancellation is not { IsCancellationRequested: false } cancellation) return;
        cancellation.Cancel();
        BusyCancelButton.IsEnabled = false;
        ReportBusyStatus("Cancelling… Sherpa is restoring the previous state.");
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isBusy || e.Key != Key.Escape) return;
        RequestBusyCancellation();
        e.Handled = true;
    }

    private void ReportBusyStatus(string message)
    {
        StatusText.Text = message;
        BusyOperationText.Text = message;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivateButton.IsEnabled = !busy;
        BusyCancelButton.IsEnabled = busy;
        if (message is not null) ReportBusyStatus(message);
        if (!busy && _pendingCloseOutcomes.Count > 0 && !_resourcesDisposed)
            Dispatcher.BeginInvoke(() => _ = DrainPendingCloseOutcomesAsync());
    }

    private void ShowError(string title, Exception exception)
    {
        _diagnostics.Error("ui.error", exception, new Dictionary<string, object?> { ["title"] = title });
        StatusText.Text = exception.Message;
        System.Windows.MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    private sealed record SurroundModeOption(NvidiaSurroundMode Value, string Label);

    private sealed record StartupProfileOption(Guid? Value, string Label);

    private enum CloseSaveDecision
    {
        Saved,
        ExitWithoutSaving,
        Cancel
    }
}
