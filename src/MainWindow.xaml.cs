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
    private readonly DisplayConfigurationService _displays = new();
    private readonly ProcessService _processes = new();
    private readonly ProfileActivationService _activator;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly HashSet<SwitchProfile> _observedProfiles = [];
    private readonly HashSet<string> _inFlightTargetIdentities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<PendingProcessCloseOutcome> _pendingCloseOutcomes = [];
    private ProfileDocument _document = new();
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
    private volatile bool _acceptActivation = true;
    private WindowState _lastVisibleState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        SurroundModeCombo.ItemsSource = new[]
        {
            new SurroundModeOption(NvidiaSurroundMode.Ignore, "Do not manage"),
            new SurroundModeOption(NvidiaSurroundMode.RequireEnabled, "Require enabled"),
            new SurroundModeOption(NvidiaSurroundMode.RequireDisabled, "Require disabled")
        };
        _activator = new ProfileActivationService(_displays, _processes);
        _processes.PendingCloseCompleted += ProcessService_PendingCloseCompleted;
        _processes.PendingMinimizationCompleted += ProcessService_PendingMinimizationCompleted;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
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
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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
        _loadingSettings = false;
        try { await _store.SaveAsync(_document); }
        catch (Exception ex) { ShowError("Could not save profiles", ex); }
        RefreshActiveProfile();
        RebuildTrayMenu();
        StatusText.Text = $"Profiles are stored in {_store.FilePath}";
    }

    private SwitchProfile? SelectedProfile => ProfilesList.SelectedItem as SwitchProfile;

    private async void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        ApplicationsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ApplicationsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (SelectedProfile is { } profile) await ActivateProfileAsync(profile);
    }

    private async Task ActivateProfileAsync(SwitchProfile profile)
    {
        if (_isBusy) return;
        _inFlightTargetIdentities.Clear();
        foreach (var app in profile.Applications.Where(app => app.Enabled))
        {
            try { _inFlightTargetIdentities.Add(_processes.GetIdentityKey(app)); }
            catch { /* Activation validation reports malformed entries to the user. */ }
        }
        SetBusy(true, $"Switching to {profile.Name}…");
        try
        {
            var activated = await _activator.ActivateAsync(_document, profile,
                message => Dispatcher.Invoke(() => StatusText.Text = message), ConfirmDisplayAsync,
                ConfirmForceCloseAsync);
            EnsureWindowIsVisible();
            if (activated)
            {
                RefreshActiveProfile();
                RebuildTrayMenu();
            }
            await SaveAsync();
        }
        catch (Exception ex) { ShowError($"Could not activate {profile.Name}", ex); }
        finally
        {
            _inFlightTargetIdentities.Clear();
            SetBusy(false);
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
        SetBusy(true, $"Testing {profile.Name} display layout…");
        try
        {
            var result = await _displays.RestoreAsync(snapshot, profile.NvidiaSurroundMode, ConfirmDisplayAsync);
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
            if (result.Kept)
            {
                snapshot.IsVerified = true;
                StatusText.Text = $"{profile.Name} display layout is verified.";
            }
            else StatusText.Text = "Reverted to the previous display layout without saving the test layout.";
            await SaveAsync();
        }
        catch (Exception ex) { ShowError("Could not test the display layout", ex); }
        finally { SetBusy(false); }
    }

    private async void RestoreLastDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (!_displays.HasRecoverySnapshot || _isBusy)
        {
            StatusText.Text = "No previous display recovery snapshot is available.";
            return;
        }
        SetBusy(true, "Restoring the previous display layout…");
        try
        {
            var result = await _displays.RestoreLastRecoveryAsync();
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
        }
        catch (Exception ex) { ShowError("Could not restore the previous display layout", ex); }
        finally { SetBusy(false); }
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

    private Task<bool> ConfirmForceCloseAsync(LaunchApplication app, ProcessCloseResult result)
    {
        var answer = System.Windows.MessageBox.Show(this,
            $"{app.Name} ignored the normal close request.\n\nForce close it now and remember this choice for future profile switches? This can discard unsaved work. Choosing No cancels the switch when the app is still part of the current profile.",
            "App is still running", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        return Task.FromResult(answer == MessageBoxResult.Yes);
    }

    private Task<bool> ConfirmDelayedForceCloseAsync(LaunchApplication app)
    {
        var answer = System.Windows.MessageBox.Show(this,
            $"A delayed {app.Name} process ignored the normal close request.\n\nForce close it now and remember this choice for future profile switches? This can discard unsaved work. Choosing No leaves it running.",
            "App is still running", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        return Task.FromResult(answer == MessageBoxResult.Yes);
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

    private async Task HandlePendingCloseCompletedAsync(PendingProcessCloseOutcome outcome)
    {
        if (!_acceptActivation || !_processes.IsPendingCloseOutcomeCurrent(outcome) ||
            IsIdentityDesired(outcome.IdentityKey))
            return;
        try
        {
            var result = outcome.Result;
            var app = _profilesLoaded
                ? _document.Profiles.SelectMany(profile => profile.Applications)
                    .FirstOrDefault(candidate => candidate.Id == outcome.ApplicationId)
                : null;

            if (outcome.ForcePromptRecommended && app is not null && !app.ForceCloseAfterTimeout)
            {
                TryRestoreAndActivate();
                if (await ConfirmDelayedForceCloseAsync(app))
                {
                    if (!_processes.IsPendingCloseOutcomeCurrent(outcome) ||
                        IsIdentityDesired(outcome.IdentityKey))
                        return;
                    app.ForceCloseAfterTimeout = true;
                    var preferenceSaved = await SaveAsync();
                    result = await _processes.ForceCloseAsync(app, outcome, CancellationToken.None);
                    if (!preferenceSaved)
                        result = result with
                        {
                            Message = result.Message + " The force-close preference could not be saved."
                        };
                }
            }

            StatusText.Text = result.Message;
            if (!result.Succeeded) ShowTrayWarning("Application still running", result.Message);
        }
        catch (Exception exception)
        {
            if (_acceptActivation) ShowError($"Could not finish closing {outcome.ApplicationName}", exception);
        }
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
        if (SelectedProfile is not { } selected || _document.Profiles.Count == 1) return;
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
        var app = ApplicationsGrid.SelectedItem as LaunchApplication;
        if (app is null)
        {
            app = new LaunchApplication();
            profile.Applications.Add(app);
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an application",
            Filter = "Applications and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        app.Path = dialog.FileName;
        if (app.Name == "New application") app.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(app.WorkingDirectory)) app.WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
        ApplicationsGrid.Items.Refresh();
        if (!await SaveAsync()) return;
        try
        {
            var resolved = new LaunchTargetResolver().Resolve(app);
            StatusText.Text = resolved.IsShortcutOrProtocol && string.IsNullOrWhiteSpace(resolved.ProcessName)
                ? $"Added {app.Name}. Set Process name to support already-running detection, minimizing, and closing for this shortcut."
                : resolved.IsShortcutOrProtocol && !resolved.HasExplicitProcessName
                    ? $"Added {app.Name}. Sherpa associated it with {resolved.ProcessName} for minimizing and closing."
                : $"Added {app.Name}.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void ApplicationsGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        await Dispatcher.InvokeAsync(async () => await SaveAsync());
    }

    private async void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        await SaveAsync();
    }

    private async void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || !IsLoaded) return;
        _document.Settings.MinimizeToTrayOnClose = CloseToTrayCheckBox.IsChecked == true;
        _document.Settings.ConfirmDisplayChanges = ConfirmDisplayChangesCheckBox.IsChecked == true;
        await SaveAsync();
    }

    private async void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SwitchProfile.NvidiaSurroundMode) || sender is not SwitchProfile profile) return;
        if (profile.Display is not null) profile.Display.IsVerified = false;
        await SaveAsync();
    }

    private void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeProfileObservers();
        RebuildTrayMenu();
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
        if (_displays.HasRecoverySnapshot)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Restore previous display layout", null,
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
        SetBusy(true, "Restoring the previous display layout…");
        try
        {
            var result = await _displays.RestoreLastRecoveryAsync();
            EnsureWindowIsVisible();
            StatusText.Text = result.Message;
        }
        catch (Exception ex) { ShowError("Could not restore the previous display layout", ex); }
        finally { SetBusy(false); }
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
        _displays.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
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

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivateButton.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
        if (!busy && _pendingCloseOutcomes.Count > 0 && !_resourcesDisposed)
            Dispatcher.BeginInvoke(() => _ = DrainPendingCloseOutcomesAsync());
    }

    private void ShowError(string title, Exception exception)
    {
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

    private enum CloseSaveDecision
    {
        Saved,
        ExitWithoutSaving,
        Cancel
    }
}
