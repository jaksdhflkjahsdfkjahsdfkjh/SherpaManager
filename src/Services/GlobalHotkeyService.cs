using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SherpaManager.Services;

/// <summary>
/// Registers system-wide hotkeys against a window handle. Registration is
/// best effort: a hotkey another application already owns is reported and
/// skipped rather than failing anything, because losing a shortcut must never
/// stop Sherpa Manager from starting.
/// </summary>
public sealed class GlobalHotkeyService(IDiagnosticLog? diagnostics = null) : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly IDiagnosticLog _diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
    private readonly Dictionary<int, Guid> _profilesByHotkeyId = [];
    private HwndSource? _source;
    private nint _handle;
    private int _nextId = 0xB000;
    private bool _disposed;

    /// <summary>Raised on the UI thread when a registered hotkey is pressed.</summary>
    public event Action<Guid>? HotkeyPressed;

    public void Attach(nint windowHandle)
    {
        if (_disposed || _source is not null || windowHandle == 0) return;
        _handle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WindowProc);
    }

    /// <summary>
    /// Replaces every registration with <paramref name="assignments"/>, returning
    /// a message per hotkey that could not be registered.
    /// </summary>
    public IReadOnlyList<string> Apply(IEnumerable<(Guid ProfileId, string ProfileName, string? Hotkey)> assignments)
    {
        var failures = new List<string>();
        if (_disposed || _handle == 0) return failures;

        UnregisterAll();
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (profileId, profileName, text) in assignments)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!HotkeyDefinition.TryParse(text, out var hotkey, out var error))
            {
                failures.Add(error ?? $"{profileName}: '{text}' is not a valid shortcut.");
                continue;
            }

            // Two profiles claiming one combination is a profile problem, not a
            // Windows one, so report it here rather than letting the second
            // registration fail with a less useful message.
            if (claimed.TryGetValue(hotkey!.Text, out var owner))
            {
                failures.Add($"{profileName}: {hotkey.Text} is already used by {owner}.");
                continue;
            }

            var id = _nextId++;
            if (!RegisterHotKey(_handle, id, (uint)hotkey.Modifiers | ModNoRepeat, hotkey.VirtualKey))
            {
                var code = Marshal.GetLastWin32Error();
                _diagnostics.Write("warning", "hotkey.register.failed", hotkey.Text,
                    new Dictionary<string, object?> { ["win32"] = code, ["profileId"] = profileId });
                failures.Add(code == 1409
                    ? $"{profileName}: {hotkey.Text} is already taken by another application."
                    : $"{profileName}: {hotkey.Text} could not be registered (Windows error {code}).");
                continue;
            }

            _profilesByHotkeyId[id] = profileId;
            claimed[hotkey.Text] = profileName;
        }

        _diagnostics.Write("info", "hotkey.applied", data: new Dictionary<string, object?>
        {
            ["registeredCount"] = _profilesByHotkeyId.Count,
            ["failureCount"] = failures.Count
        });
        return failures;
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmHotkey || !_profilesByHotkeyId.TryGetValue((int)wParam, out var profileId))
            return 0;
        handled = true;
        HotkeyPressed?.Invoke(profileId);
        return 0;
    }

    private void UnregisterAll()
    {
        foreach (var id in _profilesByHotkeyId.Keys.ToList()) UnregisterHotKey(_handle, id);
        _profilesByHotkeyId.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0) UnregisterAll();
        _source?.RemoveHook(WindowProc);
        _source = null;
        _handle = 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
