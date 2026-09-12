using Microsoft.Win32;

namespace SherpaManager.Services;

/// <summary>
/// Registers Sherpa Manager to start with Windows through the per-user Run key.
/// Deliberately per-user and unelevated: nothing here needs administrator rights,
/// and a machine-wide entry would be harder for the user to remove.
/// </summary>
public sealed class StartupRegistrationService(IDiagnosticLog? diagnostics = null)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SherpaManager";

    private readonly IDiagnosticLog _diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception exception)
            {
                _diagnostics.Error("startup.read.failed", exception);
                return false;
            }
        }
    }

    /// <summary>Returns whether the registration now matches <paramref name="enabled"/>.</summary>
    /// <param name="minimized">Whether Windows should start Sherpa minimized.</param>
    public bool SetRegistered(bool enabled, bool minimized = false)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _diagnostics.Write("info", "startup.unregistered");
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                _diagnostics.Write("warning", "startup.register.skipped",
                    "The running executable path could not be determined.");
                return false;
            }

            key.SetValue(ValueName, BuildCommand(executable, minimized), RegistryValueKind.String);
            _diagnostics.Write("info", "startup.registered", data: new Dictionary<string, object?>
            {
                ["minimized"] = minimized
            });
            return true;
        }
        catch (Exception exception)
        {
            _diagnostics.Error("startup.write.failed", exception, new Dictionary<string, object?>
            {
                ["enabled"] = enabled
            });
            return false;
        }
    }

    /// <summary>
    /// The command Windows runs at sign-in: the quoted executable, and the
    /// minimized switch when asked for.
    /// </summary>
    /// <remarks>
    /// The installer's uninstaller recognises its own entry by this quoted path at
    /// the start of the command, so anything added here has to come after it.
    /// </remarks>
    internal static string BuildCommand(string executable, bool minimized) =>
        minimized ? $"\"{executable}\" {CommandLineOptions.MinimizedSwitch}" : $"\"{executable}\"";
}
