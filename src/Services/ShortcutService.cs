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

        return CreateShortcut(desktop, profileName, executable);
    }

    /// <summary>
    /// Writes an activation shortcut into a folder. Separate from the desktop so
    /// it can be written somewhere a test can read back.
    /// </summary>
    internal string CreateShortcut(string directory, string profileName, string executable)
    {
        var path = Path.Combine(directory, BuildFileName(profileName));
        try
        {
            // The profile name is both the file name and part of the arguments,
            // and --activate matches on it exactly, so it has to be written
            // verbatim rather than through the system code page.
            ShellLink.Write(path, executable,
                arguments: $"{CommandLineOptions.ActivateSwitch} \"{profileName}\"",
                workingDirectory: Path.GetDirectoryName(executable) ?? string.Empty,
                iconLocation: executable + ",0",
                description: $"Switch Sherpa Manager to {profileName}");

            _diagnostics.Write("info", "shortcut.created", data: new Dictionary<string, object?>
            {
                ["profileName"] = profileName
            });
            return path;
        }
        catch (Exception exception)
        {
            _diagnostics.Error("shortcut.create.failed", exception);
            throw;
        }
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
