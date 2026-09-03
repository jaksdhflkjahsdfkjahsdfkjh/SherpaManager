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

    private static string? TryResolveShellLink(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)(object)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var target = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var result = Environment.ExpandEnvironmentVariables(target.ToString());
            return File.Exists(result) ? Path.GetFullPath(result) : null;
        }
        catch (COMException) { return null; }
    }

    private static string NormalizeProcessName(string processName) =>
        string.IsNullOrWhiteSpace(processName) ? string.Empty : Path.GetFileNameWithoutExtension(processName.Trim());

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SearchPath(string? path, string fileName, string? extension, int bufferLength,
        StringBuilder buffer, IntPtr filePart);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
