namespace SherpaManager.Services;

/// <summary>An audio output endpoint.</summary>
/// <param name="Id">
/// The Windows endpoint identifier. Stable across reboots and driver updates for
/// the same physical device, so it is what profiles store.
/// </param>
public sealed record AudioDevice(string Id, string Name);

public interface IAudioDeviceService
{
    /// <summary>True when Windows audio endpoints can be read on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Active output endpoints, ordered by name.</summary>
    IReadOnlyList<AudioDevice> GetOutputDevices();

    /// <summary>The current default output endpoint, or null when there is none.</summary>
    AudioDevice? GetDefaultOutputDevice();

    /// <summary>
    /// Makes <paramref name="deviceId"/> the default output for both ordinary
    /// playback and communications.
    /// </summary>
    void SetDefaultOutputDevice(string deviceId);
}
