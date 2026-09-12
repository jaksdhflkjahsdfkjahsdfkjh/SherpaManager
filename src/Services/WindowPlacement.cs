using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SherpaManager.Services;

/// <summary>Keeps windows inside the nearest monitor's work area, including after DPI or topology changes.</summary>
internal sealed class WindowPlacement
{
    internal static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(WindowPlacement), new PropertyMetadata(true));
    private static readonly DependencyProperty InstanceProperty = DependencyProperty.RegisterAttached(
        "Instance", typeof(WindowPlacement), typeof(WindowPlacement));
    private readonly Window _window;
    private readonly nint _handle;
    private readonly double _minimumWidth, _minimumHeight, _maximumWidth, _maximumHeight;
    private bool _queued;

    private WindowPlacement(Window window, nint handle)
    {
        _window = window;
        _handle = handle;
        _minimumWidth = window.MinWidth;
        _minimumHeight = window.MinHeight;
        _maximumWidth = window.MaxWidth;
        _maximumHeight = window.MaxHeight;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        window.Loaded += (_, _) => QueueFit();
        window.Closed += (_, _) => HwndSource.FromHwnd(handle)?.RemoveHook(WindowMessage);
        Fit();
    }

    public static void Attach(Window window, nint handle)
    {
        if (!(bool)window.GetValue(EnabledProperty) || window.GetValue(InstanceProperty) is not null) return;
        window.SetValue(InstanceProperty, new WindowPlacement(window, handle));
    }

    public static void EnsureVisible(Window window)
    {
        if (window.GetValue(InstanceProperty) is WindowPlacement placement) placement.QueueFit();
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WPF applies the DPI-suggested bounds first; fit them afterwards. Also handle
        // a disconnected monitor, a changed taskbar, and the end of a window drag.
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0232) QueueFit();
        return 0;
    }

    private void QueueFit()
    {
        if (_queued || _window.Dispatcher.HasShutdownStarted) return;
        _queued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { _queued = false; Fit(); }));
    }

    private void Fit()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(_handle, 2), ref info)) return;
        var work = info.Work.ToRect();
        var dpi = GetDpiForWindow(_handle);
        var scale = dpi > 0 ? dpi / 96d : 1;
        _window.MinWidth = Math.Min(_minimumWidth, work.Width / scale);
        _window.MinHeight = Math.Min(_minimumHeight, work.Height / scale);
        _window.MaxWidth = Math.Min(_maximumWidth, work.Width / scale);
        _window.MaxHeight = Math.Min(_maximumHeight, work.Height / scale);
        if (!_window.IsLoaded || _window.WindowState != WindowState.Normal || !GetWindowRect(_handle, out var rect)) return;
        var current = rect.ToRect();
        var fitted = FitBounds(current, work);
        if (current != fitted)
            SetWindowPos(_handle, 0, (int)fitted.X, (int)fitted.Y, (int)fitted.Width, (int)fitted.Height, 0x0014);
    }

    internal static Rect FitBounds(Rect window, Rect work)
    {
        var width = Math.Min(window.Width, work.Width);
        var height = Math.Min(window.Height, work.Height);
        return new Rect(Math.Clamp(window.X, work.Left, work.Right - width),
            Math.Clamp(window.Y, work.Top, work.Bottom - height), width, height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
