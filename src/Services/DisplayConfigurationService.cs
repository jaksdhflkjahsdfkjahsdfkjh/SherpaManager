using System.ComponentModel;
using System.Runtime.InteropServices;
using SherpaManager.Models;
using Forms = System.Windows.Forms;

namespace SherpaManager.Services;

public sealed class DisplayConfigurationService
{
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    private const uint SdcValidate = 0x00000040;
    private const uint SdcApply = 0x00000080;
    private const uint SdcSaveToDatabase = 0x00000200;
    private const uint SdcAllowChanges = 0x00000400;
    private const int ErrorInsufficientBuffer = 122;

    public DisplaySnapshot Capture()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ThrowIfFailed(GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount));
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var result = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ErrorInsufficientBuffer) continue;
            ThrowIfFailed(result);

            var screens = Forms.Screen.AllScreens;
            var summary = string.Join("  •  ", screens.Select(screen =>
                $"{screen.DeviceName.Replace(@"\\.\", string.Empty)} {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " (primary)" : string.Empty)}"));

            return new DisplaySnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                Summary = $"{screens.Length} display{(screens.Length == 1 ? string.Empty : "s")}: {summary}",
                PathStructureSize = Marshal.SizeOf<DisplayConfigPathInfo>(),
                ModeStructureSize = Marshal.SizeOf<DisplayConfigModeInfo>(),
                Paths = paths.Take((int)pathCount).Select(ToBase64).ToList(),
                Modes = modes.Take((int)modeCount).Select(ToBase64).ToList()
            };
        }

        throw new InvalidOperationException("The connected displays changed while capturing the layout. Try again.");
    }

    public void Restore(DisplaySnapshot snapshot)
    {
        if (snapshot.Paths.Count == 0) throw new InvalidOperationException("This display snapshot is empty.");
        if (snapshot.PathStructureSize != Marshal.SizeOf<DisplayConfigPathInfo>() ||
            snapshot.ModeStructureSize != Marshal.SizeOf<DisplayConfigModeInfo>())
            throw new InvalidOperationException("This display snapshot was created by an incompatible Sherpa Manager version. Capture it again.");

        var paths = snapshot.Paths.Select(FromBase64<DisplayConfigPathInfo>).ToArray();
        var modes = snapshot.Modes.Select(FromBase64<DisplayConfigModeInfo>).ToArray();
        var baseFlags = SdcUseSuppliedDisplayConfig | SdcAllowChanges;

        ThrowIfFailed(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, baseFlags | SdcValidate));
        ThrowIfFailed(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            baseFlags | SdcApply | SdcSaveToDatabase));
    }

    private static string ToBase64<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return Convert.ToBase64String(bytes);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static T FromBase64<T>(string value) where T : struct
    {
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length != Marshal.SizeOf<T>()) throw new InvalidOperationException("A display snapshot contains invalid data.");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return Marshal.PtrToStructure<T>(pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != 0) throw new Win32Exception(result);
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray, ref uint numModeInfoArrayElements,
        [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray, uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[] modeInfoArray, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion { public uint Cx; public uint Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode { public DisplayConfigVideoSignalInfo TargetVideoSignalInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }
}
