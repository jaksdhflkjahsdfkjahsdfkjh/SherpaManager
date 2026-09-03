using System.Windows;
using System.Windows.Threading;
using SherpaManager.Services;

namespace SherpaManager;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private readonly DiagnosticsService _diagnostics = DiagnosticsService.Current;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _diagnostics.Write("info", "application.start", data: new Dictionary<string, object?>
        {
            ["arguments"] = e.Args,
            ["sherpaVersion"] = typeof(App).Assembly.GetName().Version?.ToString(),
            ["windowsVersion"] = Environment.OSVersion.Version.ToString(),
            ["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
        });
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var store = new ProfileStore();
                var document = await store.LoadAsync();
                await store.SaveAsync(document);
                using var displayService = new DisplayConfigurationService(diagnostics: _diagnostics);
                var display = displayService.Capture();
                Shutdown(document.Profiles.Count >= 3 && display.Paths.Count > 0 ? 0 : 2);
            }
            catch (Exception exception)
            {
                _diagnostics.Error("application.smoke_test.failed", exception);
                Shutdown(1);
            }
            return;
        }

        try { _singleInstance = new SingleInstanceService(); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            _diagnostics.Error("application.single_instance.failed", exception);
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
                {
                    _diagnostics.Write("warning", "application.single_instance.unresponsive");
                    System.Windows.MessageBox.Show("The existing Sherpa Manager instance did not respond. Try opening it from the notification area.",
                        "Sherpa Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Shutdown(signalled ? 0 : 4);
                return;
            }
        }

        var window = new MainWindow();
        MainWindow = window;
        _singleInstance.StartListening(() =>
            window.Dispatcher.InvokeAsync(window.TryRestoreAndActivate).Task);
        window.Show();
        _diagnostics.Write("info", "application.ready");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diagnostics.Write("info", "application.exit", data: new Dictionary<string, object?>
        {
            ["exitCode"] = e.ApplicationExitCode
        });
        _singleInstance?.Dispose();
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is MainWindow window)
            window.AllowFullClose(includeBusyOperation: true, sessionEnding: true);
        base.OnSessionEnding(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        _diagnostics.Error("crash.dispatcher", e.Exception);

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _diagnostics.Error("crash.app_domain", exception, new Dictionary<string, object?>
            {
                ["isTerminating"] = e.IsTerminating
            });
        else
            _diagnostics.Write("error", "crash.app_domain", "A non-Exception object was thrown.",
                new Dictionary<string, object?> { ["isTerminating"] = e.IsTerminating });
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _diagnostics.Error("crash.unobserved_task", e.Exception);
        e.SetObserved();
    }
}
