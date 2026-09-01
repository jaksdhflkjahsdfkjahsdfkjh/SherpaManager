using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SherpaManager.Models;
using SherpaManager.Services;
using Forms = System.Windows.Forms;

namespace SherpaManager;

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly DisplayConfigurationService _displays = new();
    private readonly ProfileActivationService _activator;
    private readonly Forms.NotifyIcon _trayIcon;
    private ProfileDocument _document = new();
    private bool _isBusy;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _activator = new ProfileActivationService(_displays, new ProcessService());
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
        try
        {
            _document = await _store.LoadAsync();
            ProfilesList.ItemsSource = _document.Profiles;
            ProfilesList.SelectedItem = _document.Profiles.FirstOrDefault(x => x.Id == _document.ActiveProfileId)
                                        ?? _document.Profiles.FirstOrDefault();
            _document.Profiles.CollectionChanged += Profiles_CollectionChanged;
            await _store.SaveAsync(_document);
            RefreshActiveProfile();
            RebuildTrayMenu();
            StatusText.Text = $"Profiles are stored in {_store.FilePath}";
        }
        catch (Exception ex) { ShowError("Could not load profiles", ex); }
    }

    private SwitchProfile? SelectedProfile => ProfilesList.SelectedItem as SwitchProfile;

    private async void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is { } profile) await ActivateProfileAsync(profile);
    }

    private async Task ActivateProfileAsync(SwitchProfile profile)
    {
        if (_isBusy) return;
        SetBusy(true, $"Switching to {profile.Name}…");
        try
        {
            await _activator.ActivateAsync(_document, profile, message => Dispatcher.Invoke(() => StatusText.Text = message));
            await _store.SaveAsync(_document);
            RefreshActiveProfile();
            RebuildTrayMenu();
        }
        catch (Exception ex) { ShowError($"Could not activate {profile.Name}", ex); }
        finally { SetBusy(false); }
    }

    private async void CaptureDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        try
        {
            profile.Display = _displays.Capture();
            await SaveAsync();
            StatusText.Text = $"Captured the current layout for {profile.Name}.";
        }
        catch (Exception ex) { ShowError("Could not capture the display layout", ex); }
    }

    private async void ClearDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile || profile.Display is null) return;
        profile.Display = null;
        await SaveAsync();
        StatusText.Text = $"{profile.Name} will now keep the current display layout.";
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
            Filter = "Applications (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        app.Path = dialog.FileName;
        if (app.Name == "New application") app.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(app.WorkingDirectory)) app.WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
        ApplicationsGrid.Items.Refresh();
        await SaveAsync();
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

    private void Profiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTrayMenu();

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
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        var previous = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        try { _store.SaveAsync(_document).GetAwaiter().GetResult(); }
        catch { /* The main UI reports save failures while the app is running. */ }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OpenProfileFile_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_store.FilePath}\"") { UseShellExecute = true });
    }

    private async Task SaveAsync()
    {
        if (_document.Profiles.Count == 0) return;
        try { await _store.SaveAsync(_document); }
        catch (Exception ex) { StatusText.Text = $"Could not save profiles: {ex.Message}"; }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivateButton.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }

    private void ShowError(string title, Exception exception)
    {
        StatusText.Text = exception.Message;
        System.Windows.MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
