using System.Runtime.InteropServices;

namespace SherpaManager.Services;

/// <summary>
/// Reads and sets the default Windows audio output endpoint.
/// </summary>
/// <remarks>
/// Enumeration uses the documented Core Audio MMDevice API. Changing the default
/// endpoint has no documented API at all: every tool that does it, Sherpa
/// included, uses the undocumented IPolicyConfig interface. It has been stable
/// since Windows Vista, but it is not contractual, so every call here is guarded
/// and a failure is reported rather than thrown at the user as a crash.
/// </remarks>
public sealed class AudioDeviceService(IDiagnosticLog? diagnostics = null) : IAudioDeviceService
{
    private const int RenderDataFlow = 0;     // eRender
    private const int CaptureDataFlow = 1;    // eCapture
    private const int DeviceStateActive = 0x1;
    private const int RoleConsole = 0;
    private const int RoleMultimedia = 1;
    private const int RoleCommunications = 2;
    private const int VtLpwstr = 31;

    private static readonly PropertyKey FriendlyNameKey =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    private readonly IDiagnosticLog _diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    public bool IsAvailable
    {
        get
        {
            try
            {
                var enumerator = CreateEnumerator();
                Release(enumerator);
                return true;
            }
            catch (Exception exception)
            {
                _diagnostics.Error("audio.unavailable", exception);
                return false;
            }
        }
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices() => GetDevices(RenderDataFlow);

    public IReadOnlyList<AudioDevice> GetInputDevices() => GetDevices(CaptureDataFlow);

    public AudioDevice? GetDefaultOutputDevice() => GetDefaultDevice(RenderDataFlow);

    public AudioDevice? GetDefaultInputDevice() => GetDefaultDevice(CaptureDataFlow);

    private IReadOnlyList<AudioDevice> GetDevices(int dataFlow)
    {
        object? enumeratorObject = null;
        try
        {
            enumeratorObject = CreateEnumerator();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Check(enumerator.EnumAudioEndpoints(dataFlow, DeviceStateActive, out var collection),
                "enumerate audio endpoints");

            try
            {
                Check(collection.GetCount(out var count), "count audio endpoints");
                var devices = new List<AudioDevice>((int)count);
                for (uint index = 0; index < count; index++)
                {
                    if (collection.Item(index, out var device) != 0) continue;
                    try
                    {
                        var read = TryRead(device);
                        if (read is not null) devices.Add(read);
                    }
                    finally { Release(device); }
                }

                return devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            finally { Release(collection); }
        }
        catch (Exception exception)
        {
            _diagnostics.Error("audio.enumerate.failed", exception);
            return [];
        }
        finally { Release(enumeratorObject); }
    }

    private AudioDevice? GetDefaultDevice(int dataFlow)
    {
        object? enumeratorObject = null;
        try
        {
            enumeratorObject = CreateEnumerator();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            // A machine with no endpoint of this kind returns a failure here rather
            // than a null device, so treat any failure as "no default".
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, RoleConsole, out var device) != 0) return null;
            try { return TryRead(device); }
            finally { Release(device); }
        }
        catch (Exception exception)
        {
            _diagnostics.Error("audio.default.read_failed", exception);
            return null;
        }
        finally { Release(enumeratorObject); }
    }

    public void SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("An audio device identifier is required.", nameof(deviceId));

        object? policyObject = null;
        try
        {
            policyObject = Activator.CreateInstance(Type.GetTypeFromCLSID(PolicyConfigClassId, throwOnError: true)!)
                ?? throw new InvalidOperationException("Windows did not provide the audio policy configuration object.");
            var policy = (IPolicyConfig)policyObject;

            // Console and Multimedia cover ordinary use; Communications is the
            // separate device Windows uses for calls. Setting only one leaves the
            // machine half-switched, which is worse than not switching at all.
            // The endpoint id carries its own direction, so this is identical for
            // playback and recording.
            foreach (var role in new[] { RoleConsole, RoleMultimedia, RoleCommunications })
                Check(policy.SetDefaultEndpoint(deviceId, role), $"set the default audio endpoint for role {role}");

            _diagnostics.Write("info", "audio.default.applied");
        }
        catch (Exception exception)
        {
            _diagnostics.Error("audio.default.apply_failed", exception);
            throw new InvalidOperationException(
                $"Windows would not change the default audio device: {exception.Message}", exception);
        }
        finally { Release(policyObject); }
    }

    private AudioDevice? TryRead(IMMDevice device)
    {
        try
        {
            if (device.GetId(out var id) != 0 || string.IsNullOrWhiteSpace(id)) return null;
            return new AudioDevice(id, ReadFriendlyName(device) ?? id);
        }
        catch (Exception exception)
        {
            _diagnostics.Error("audio.device.read_failed", exception);
            return null;
        }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return null;
        try
        {
            var key = FriendlyNameKey;
            if (store.GetValue(ref key, out var value) != 0) return null;
            try
            {
                return value.VarType == VtLpwstr && value.PointerValue != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.PointerValue)
                    : null;
            }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    private static object CreateEnumerator() =>
        Activator.CreateInstance(Type.GetTypeFromCLSID(MmDeviceEnumeratorClassId, throwOnError: true)!)
        ?? throw new InvalidOperationException("Windows did not provide the audio endpoint enumerator.");

    private static void Check(int result, string what)
    {
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        _ = what;
    }

    private static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject)) return;
        try { Marshal.FinalReleaseComObject(comObject); }
        catch (ArgumentException) { }
    }

    private static readonly Guid MmDeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid PolicyConfigClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    // x64 layout: the union follows the type and reserved fields at offset 8.
    // Sherpa is x64 only, so this does not need a 32-bit variant.
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public short VarType;
        [FieldOffset(8)] public IntPtr PointerValue;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    // Undocumented. Only SetDefaultEndpoint is used; the earlier members exist to
    // put it at the right position in the interface, and are never called.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility();
    }
}
