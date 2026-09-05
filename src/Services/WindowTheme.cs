using System.Runtime.InteropServices;

namespace SherpaManager.Services;

/// <summary>
/// Applies the dark title bar to a window.
/// </summary>
/// <remarks>
/// WPF draws the window contents but Windows draws the title bar, so a dark
/// application still gets a white caption unless it opts in. The attribute was
/// numbered 19 before Windows 10 20H1 and 20 afterwards, and setting the wrong
/// one silently does nothing, so try the current one and fall back.
/// </remarks>
public static class WindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBeforeTwentyH1 = 19;

    public static void ApplyDarkTitleBar(nint windowHandle)
    {
        if (windowHandle == 0) return;

        var enabled = 1;
        if (DwmSetWindowAttribute(windowHandle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(windowHandle, UseImmersiveDarkModeBeforeTwentyH1, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
