using System.Reflection;
using System.Runtime.InteropServices;

namespace SherpaManager.Services;

/// <summary>
/// Creates desktop shortcuts that activate a profile directly. Windows exposes
/// shortcut creation through the shell scripting object, which is bound late here
/// so no COM reference or extra package is needed.
/// </summary>
public sealed class ShortcutService(IDiagnosticLog? diagnostics = null)
{
    private readonly IDiagnosticLog _diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    /// <summary>Returns the created shortcut path.</summary>
    public string CreateDesktopShortcut(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("A profile name is required.", nameof(profileName));

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("The running Sherpa Manager executable could not be located.");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new InvalidOperationException("The desktop folder could not be located.");

        var path = Path.Combine(desktop, BuildFileName(profileName));
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("The Windows shell scripting object is not available.");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("The Windows shell scripting object could not be created.");
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path])
                ?? throw new InvalidOperationException("The shortcut could not be created.");

            var type = shortcut.GetType();
            Set(type, shortcut, "TargetPath", executable);
            Set(type, shortcut, "Arguments", $"{CommandLineOptions.ActivateSwitch} \"{profileName}\"");
            Set(type, shortcut, "WorkingDirectory", Path.GetDirectoryName(executable) ?? string.Empty);
            Set(type, shortcut, "IconLocation", executable + ",0");
            Set(type, shortcut, "Description", $"Switch Sherpa Manager to {profileName}");
            type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

            _diagnostics.Write("info", "shortcut.created", data: new Dictionary<string, object?>
            {
                ["profileName"] = profileName
            });
            return path;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            _diagnostics.Error("shortcut.create.failed", exception.InnerException);
            throw exception.InnerException;
        }
        finally
        {
            Release(shortcut);
            Release(shell);
        }
    }

    private static void Set(Type type, object instance, string property, string value) =>
        type.InvokeMember(property, BindingFlags.SetProperty, null, instance, [value]);

    private static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject)) return;
        try { Marshal.FinalReleaseComObject(comObject); }
        catch (ArgumentException) { }
    }

    public static string BuildFileName(string profileName)
    {
        var cleaned = new string(profileName
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Profile";
        // Keep well under MAX_PATH once the desktop folder and suffix are added.
        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd();
        return $"{cleaned} (Sherpa).lnk";
    }
}
