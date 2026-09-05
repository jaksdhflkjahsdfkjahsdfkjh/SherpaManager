using Microsoft.Win32;

namespace SherpaManager.Services;

public sealed record NvidiaLaunchTarget(string FileName, string DisplayName, string? Arguments = null);

/// <summary>
/// Finds the NVIDIA GPU settings application to open.
/// </summary>
/// <remarks>
/// NVIDIA replaced the Control Panel with the NVIDIA app, so a single hard-coded
/// target is wrong on most machines now. Nothing here is launched without first
/// confirming the file exists: launching an unresolvable
/// <c>shell:AppsFolder\...</c> identifier does not fail, it silently opens
/// Explorer at the user's Documents folder, which looks like a bug in Sherpa.
/// </remarks>
public static class NvidiaAppLocator
{
    // Observed layout: NVIDIA Corporation\NVIDIA App\CEF\NVIDIA app.exe. The
    // folder and nesting have moved between releases, so search rather than
    // assume one path.
    private static readonly string[] AppExecutableNames = ["NVIDIA app.exe", "NVIDIA App.exe"];

    /// <summary>
    /// Opens the NVIDIA app on System -> Display, where Surround is configured.
    /// The route format is NVIDIA's own: their binaries use, for example,
    /// nvidiaapp://route/#nvapp/preferences?tab=about for crash notifications.
    /// </summary>
    public const string DisplaySettingsUri = "nvidiaapp://route/#nvapp/system?tab=display";

    private const string ProtocolKey = "nvidiaapp";

    // The Store-packaged Control Panel, still present on some DCH installs.
    private const string StorePackagePrefix = "NVIDIACorp.NVIDIAControlPanel_";
    private const string StoreApplicationId = "NVIDIACorp.NVIDIAControlPanel";
    private const string PackageRepositoryKey =
        @"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static NvidiaLaunchTarget? Locate(string? nvidiaRoot = null, string? systemDirectory = null,
        bool? protocolRegistered = null)
    {
        // Prefer the deep link: it lands on the display page rather than wherever
        // the app was last left. Only used when the handler is actually
        // registered, because an unhandled URI fails in confusing ways.
        if (protocolRegistered ?? IsProtocolRegistered())
            return new NvidiaLaunchTarget(DisplaySettingsUri, "NVIDIA app display settings");

        var root = nvidiaRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation");

        var app = FindApplication(root);
        if (app is not null) return new NvidiaLaunchTarget(app, "NVIDIA app");

        // Driver-installed Control Panel. DCH drivers put it in System32; standard
        // drivers put it under the NVIDIA folder.
        var system = systemDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var controlPanel in new[]
                 {
                     Path.Combine(system, "nvcplui.exe"),
                     Path.Combine(root, "Control Panel Client", "nvcplui.exe")
                 })
            if (File.Exists(controlPanel))
                return new NvidiaLaunchTarget(controlPanel, "NVIDIA Control Panel");

        // The Store-packaged Control Panel, launched by identifier. Verified
        // against the package repository first: asking Explorer to open an
        // identifier that is not installed opens the user's Documents folder
        // instead of failing, which is indistinguishable from a bug in Sherpa.
        var storeFamily = FindStoreControlPanelFamily();
        if (storeFamily is not null)
            return new NvidiaLaunchTarget("explorer.exe", "NVIDIA Control Panel",
                $@"shell:AppsFolder\{storeFamily}!{StoreApplicationId}");

        return null;
    }

    private static string? FindStoreControlPanelFamily()
    {
        try
        {
            using var packages = Registry.ClassesRoot.OpenSubKey(PackageRepositoryKey);
            var fullName = packages?.GetSubKeyNames()
                .FirstOrDefault(name => name.StartsWith(StorePackagePrefix, StringComparison.OrdinalIgnoreCase));
            if (fullName is null) return null;

            // A package full name is Name_Version_Architecture__PublisherHash; the
            // family that AppsFolder wants is Name_PublisherHash.
            var parts = fullName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length < 2 ? null : $"{parts[0]}_{parts[^1]}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when Windows has a handler registered for the nvidiaapp: scheme.
    /// </summary>
    public static bool IsProtocolRegistered()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(ProtocolKey);
            if (key?.GetValue("URL Protocol") is null) return false;

            using var command = Registry.ClassesRoot.OpenSubKey($@"{ProtocolKey}\shell\open\command");
            if (command?.GetValue(null) is not string handler || string.IsNullOrWhiteSpace(handler)) return false;

            // Uninstalling the app can leave the key behind. A handler pointing at
            // a file that is gone would fail at launch, so fall through to the
            // other candidates instead.
            return !TryGetHandlerPath(handler, out var executable) || File.Exists(executable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Extracts the executable from a shell open command.</summary>
    private static bool TryGetHandlerPath(string handler, out string executable)
    {
        executable = string.Empty;
        var trimmed = handler.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closing = trimmed.IndexOf('"', 1);
            if (closing <= 1) return false;
            executable = trimmed[1..closing];
            return true;
        }

        var space = trimmed.IndexOf(' ');
        executable = space < 0 ? trimmed : trimmed[..space];
        return executable.Length > 0;
    }

    private static string? FindApplication(string root)
    {
        if (!Directory.Exists(root)) return null;

        foreach (var directory in EnumerateCandidateDirectories(root))
        {
            foreach (var name in AppExecutableNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// The NVIDIA folder, its "NVIDIA App"-like children, and their immediate
    /// subdirectories. Deliberately shallow: this runs on a button click, and a
    /// full recursive walk of Program Files would be slow and pointless.
    /// </summary>
    private static IEnumerable<string> EnumerateCandidateDirectories(string root)
    {
        yield return root;

        List<string> appFolders;
        try
        {
            appFolders = Directory.EnumerateDirectories(root)
                .Where(directory => Path.GetFileName(directory)
                    .StartsWith("NVIDIA App", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var appFolder in appFolders)
        {
            yield return appFolder;

            List<string> children;
            try { children = Directory.EnumerateDirectories(appFolder).ToList(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }

            foreach (var child in children) yield return child;
        }
    }
}
