using System.Windows;
using SherpaManager.Services;

namespace SherpaManager;

public partial class App : System.Windows.Application
{
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
                var display = new DisplayConfigurationService().Capture();
                Shutdown(document.Profiles.Count >= 3 && display.Paths.Count > 0 ? 0 : 2);
            }
            catch { Shutdown(1); }
            return;
        }

        new MainWindow().Show();
    }
}
