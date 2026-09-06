using System.Runtime.InteropServices;
using System.Text;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class LaunchTargetResolver
{
    private static readonly string[] HiddenExtensions = [".exe", ".lnk", ".url", ".bat", ".cmd"];
    private static readonly IReadOnlyDictionary<string, string> KnownProtocolProcesses =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["steam://rungameid/266410"] = "iRacingUI"
        };

    public ResolvedLaunchTarget Resolve(LaunchApplication app)
    {
        var enteredPath = Environment.ExpandEnvironmentVariables(app.Path.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(enteredPath))
            throw new InvalidOperationException($"{app.Name} has no executable or shortcut path.");

        if (Uri.TryCreate(enteredPath, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            var explicitProcess = NormalizeProcessName(app.ProcessName);
            var protocolProcess = string.IsNullOrWhiteSpace(explicitProcess)
                ? InferProtocolProcess(enteredPath)
                : explicitProcess;
            return new ResolvedLaunchTarget(enteredPath, null, protocolProcess,
                string.IsNullOrWhiteSpace(protocolProcess) ? $"protocol:{enteredPath}" : $"process:{protocolProcess}", true,
                !string.IsNullOrWhiteSpace(explicitProcess));
        }

        var resolvedPath = ResolveFilePath(enteredPath, app.Name);
        var extension = Path.GetExtension(resolvedPath);
        var executablePath = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? resolvedPath
            : extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                ? TryResolveShellLink(resolvedPath)
                : null;

        var explicitProcessName = NormalizeProcessName(app.ProcessName);
        var processName = explicitProcessName;
        if (string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(executablePath))
            processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName) && extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            processName = InferProtocolProcess(ReadInternetShortcutUrl(resolvedPath));

        var identity = !string.IsNullOrWhiteSpace(explicitProcessName)
            ? $"process:{processName}"
            : !string.IsNullOrWhiteSpace(executablePath)
                ? $"file:{Path.GetFullPath(executablePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()}"
                : !string.IsNullOrWhiteSpace(processName)
                    ? $"process:{processName}"
                : $"launch:{Path.GetFullPath(resolvedPath).ToUpperInvariant()}";

        var managedExecutablePaths = GetManagedExecutablePaths(executablePath);

        return new ResolvedLaunchTarget(resolvedPath, executablePath, processName, identity,
            !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase),
            !string.IsNullOrWhiteSpace(explicitProcessName), managedExecutablePaths);
    }

    private static IReadOnlyList<string>? GetManagedExecutablePaths(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        var canonicalPath = Path.GetFullPath(executablePath);
        var paths = new List<string> { canonicalPath };
        var directory = Path.GetDirectoryName(canonicalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            // A number of launchers (including MOZA Pit House) hand off to a same-named
            // executable in a bin folder. Keep the association path-based and narrow.
            var binTarget = Path.Combine(directory, "bin", Path.GetFileName(canonicalPath));
            if (File.Exists(binTarget)) paths.Add(Path.GetFullPath(binTarget));
        }
        return paths;
    }

    private static string ReadInternetShortcutUrl(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..].Trim();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return string.Empty;
    }

    private static string InferProtocolProcess(string protocol)
    {
        var normalized = protocol.Trim().TrimEnd('/');
        return KnownProtocolProcesses.TryGetValue(normalized, out var processName) ? processName : string.Empty;
    }

    private static string ResolveFilePath(string enteredPath, string appName)
    {
        if (File.Exists(enteredPath)) return Path.GetFullPath(enteredPath);

        if (string.IsNullOrEmpty(Path.GetExtension(enteredPath)))
        {
            var matches = HiddenExtensions
                .Select(extension => enteredPath + extension)
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
                throw new InvalidOperationException($"More than one application matches {appName}. Select the exact file with Browse.");
        }

        if (!Path.IsPathFullyQualified(enteredPath) && TryFindOnPath(enteredPath, out var commandPath))
            return commandPath;

        var shortcutHint = string.IsNullOrEmpty(Path.GetExtension(enteredPath))
            ? " Windows may be hiding a .url or .lnk extension; use Browse to select the item."
            : string.Empty;
        throw new FileNotFoundException($"Could not find {appName}.{shortcutHint}", enteredPath);
    }

    private static bool TryFindOnPath(string command, out string path)
    {
        var buffer = new StringBuilder(32768);
        var length = SearchPath(null, command, null, buffer.Capacity, buffer, IntPtr.Zero);
        if (length > 0 && length < buffer.Capacity)
        {
            path = buffer.ToString();
            return true;
        }
        path = string.Empty;
        return false;
    }

    private static string? TryResolveShellLink(string shortcutPath) => ReadShortcut(shortcutPath)?.Target;

    /// <summary>
    /// Reads what a Windows shortcut points at, without launching it. Null when
    /// the shortcut cannot be read or points at something that is no longer
    /// there.
    /// </summary>
    public static ShortcutTarget? ReadShortcut(string shortcutPath) => ShellLink.Read(shortcutPath);

    private static string NormalizeProcessName(string processName) =>
        string.IsNullOrWhiteSpace(processName) ? string.Empty : Path.GetFileNameWithoutExtension(processName.Trim());

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SearchPath(string? path, string fileName, string? extension, int bufferLength,
        StringBuilder buffer, IntPtr filePart);

}
