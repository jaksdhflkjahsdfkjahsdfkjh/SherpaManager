namespace SherpaManager.Services;

/// <summary>An audio endpoint.</summary>
/// <param name="Id">
/// The Windows endpoint identifier. Stable across reboots and driver updates for
/// the same physical device, so it is what profiles store.
/// </param>
public sealed record AudioDevice(string Id, string Name);

public interface IAudioDeviceService
{
    /// <summary>True when Windows audio endpoints can be read on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Active playback endpoints, ordered by name.</summary>
    IReadOnlyList<AudioDevice> GetOutputDevices();

    /// <summary>Active recording endpoints, ordered by name.</summary>
    IReadOnlyList<AudioDevice> GetInputDevices();

    /// <summary>The current default playback endpoint, or null when there is none.</summary>
    AudioDevice? GetDefaultOutputDevice();

    /// <summary>The current default recording endpoint, or null when there is none.</summary>
    AudioDevice? GetDefaultInputDevice();

    /// <summary>
    /// Makes <paramref name="deviceId"/> the default for both ordinary use and
    /// communications. Windows derives playback or recording from the endpoint
    /// itself, so there is one call for both directions.
    /// </summary>
    void SetDefaultDevice(string deviceId);
}
