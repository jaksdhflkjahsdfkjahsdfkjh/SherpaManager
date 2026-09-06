using System.Runtime.InteropServices;
using System.Text;
using SherpaManager.Models;

namespace SherpaManager.Services;

/// <summary>
/// Reads and writes Windows shortcuts.
/// </summary>
/// <remarks>
/// Uses the shell's own Unicode interface rather than the WScript.Shell scripting
/// object that most examples reach for. The scripting object goes through the
/// system's ANSI code page, so a name it cannot represent is silently turned into
/// question marks: writing a shortcut for a profile named in Cyrillic on a
/// Windows set to a Western code page produced "??????? ?????????.lnk", and the
/// profile name embedded in the shortcut's arguments was destroyed the same way,
/// leaving a shortcut that matched no profile.
/// </remarks>
internal static class ShellLink
{
    /// <summary>
    /// Reads what a shortcut points at. Null when it cannot be read, or points at
    /// something that is no longer there.
    /// </summary>
    public static ShortcutTarget? Read(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)(object)new ShellLinkCoClass();
            ((IPersistFile)link).Load(shortcutPath, 0);

            var target = new StringBuilder(MaxPath);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var resolved = Environment.ExpandEnvironmentVariables(target.ToString());
            if (!File.Exists(resolved)) return null;

            var arguments = new StringBuilder(MaxPath);
            link.GetArguments(arguments, arguments.Capacity);
            var workingDirectory = new StringBuilder(MaxPath);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            return new ShortcutTarget(Path.GetFullPath(resolved), arguments.ToString().Trim(),
                Environment.ExpandEnvironmentVariables(workingDirectory.ToString()).Trim());
        }
        catch (COMException) { return null; }
    }

    /// <summary>Writes a shortcut, replacing any file already at that path.</summary>
    public static void Write(string shortcutPath, string target, string arguments = "",
        string workingDirectory = "", string iconLocation = "", string description = "")
    {
        var link = (IShellLinkW)(object)new ShellLinkCoClass();
        link.SetPath(target);
        if (arguments.Length > 0) link.SetArguments(arguments);
        if (workingDirectory.Length > 0) link.SetWorkingDirectory(workingDirectory);
        if (iconLocation.Length > 0) link.SetIconLocation(iconLocation, 0);
        if (description.Length > 0) link.SetDescription(description);
        ((IPersistFile)link).Save(shortcutPath, true);
    }

    private const int MaxPath = 32768;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkCoClass { }

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
