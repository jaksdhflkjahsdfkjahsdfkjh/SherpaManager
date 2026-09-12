using System.Windows;
using System.Windows.Media.Imaging;
using SherpaManager.Models;

namespace SherpaManager.Services;

/// <summary>Checks a packaged build without loading personal profiles or touching hardware.</summary>
internal static class PackageSmokeTest
{
    public static async Task ValidateStorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SherpaManager-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProfileStore(directory);
            var document = await store.LoadAsync().ConfigureAwait(false);
            await store.SaveAsync(document).ConfigureAwait(false);
            var reloaded = await store.LoadAsync().ConfigureAwait(false);
            if (reloaded.Profiles.Count != 3 || reloaded.Profiles.Any(profile => profile.Display is not null))
                throw new InvalidDataException("The packaged build could not round-trip safe default profiles.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    public static void ValidateResources()
    {
        foreach (var asset in new[] { "SherpaManager.png", "iRacing-Stacked-Color-Blue.png", "ACC-logo.png" })
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri($"pack://application:,,,/SherpaManager;component/Assets/{asset}"))
                ?? throw new InvalidDataException($"Missing packaged asset: {asset}");
            using var stream = resource.Stream;
            var image = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (image.Frames.Count == 0) throw new InvalidDataException($"Invalid packaged asset: {asset}");
        }

        // These constructors load the actual compiled XAML and shared templates.
        // MainWindow is deliberately excluded: it creates tray and hardware services.
        Func<Window>[] windows =
        [
            () => new CaptureConfirmationWindow("Smoke check", "Saved layout", "Current layout"),
            () => new DisplayConfirmationWindow("Packaged display confirmation"),
            () => new ActivationPreflightWindow(new ActivationPreflight { ProfileName = "Smoke check" }),
            () => new DisplayLayoutWindow("Smoke check", null),
            () => new ActivationHistoryWindow([]),
            () => new ApplicationPickerWindow([])
        ];
        foreach (var create in windows)
        {
            var window = create();
            try
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new System.Windows.Size(1200, 900));
                content.Arrange(new Rect(0, 0, 1200, 900));
                content.UpdateLayout();
            }
            finally { window.Close(); }
        }
    }
}
