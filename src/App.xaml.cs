using System.Windows;
using SherpaManager.Services;

namespace SherpaManager;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var store = new ProfileStore();
                var document = await store.LoadAsync();
                await store.SaveAsync(document);
                using var displayService = new DisplayConfigurationService();
                var display = displayService.Capture();
                Shutdown(document.Profiles.Count >= 3 && display.Paths.Count > 0 ? 0 : 2);
            }
            catch { Shutdown(1); }
            return;
        }

        try { _singleInstance = new SingleInstanceService(); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            System.Windows.MessageBox.Show($"Sherpa Manager could not coordinate with another copy. Close the other copy, or run both with the same administrator setting.\n\n{exception.Message}",
                "Sherpa Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(3);
            return;
        }
        if (!_singleInstance.IsPrimaryInstance)
        {
            var signalled = _singleInstance.SignalPrimaryInstance();
            if (!_singleInstance.IsPrimaryInstance)
            {
                if (!signalled)
                    System.Windows.MessageBox.Show("The existing Sherpa Manager instance did not respond. Try opening it from the notification area.",
                        "Sherpa Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(signalled ? 0 : 4);
                return;
            }
        }

        var window = new MainWindow();
        MainWindow = window;
        _singleInstance.StartListening(() =>
            window.Dispatcher.InvokeAsync(window.TryRestoreAndActivate).Task);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is MainWindow window)
            window.AllowFullClose(includeBusyOperation: true, sessionEnding: true);
        base.OnSessionEnding(e);
    }
}
