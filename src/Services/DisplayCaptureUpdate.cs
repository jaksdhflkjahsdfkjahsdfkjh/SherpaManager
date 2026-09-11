using SherpaManager.Models;

namespace SherpaManager.Services;

internal static class DisplayCaptureUpdate
{
    // A recapture replaces verification metadata too, even when its summary looks
    // identical. Always ask before replacing an existing saved snapshot.
    public static bool Apply(SwitchProfile profile, DisplaySnapshot captured, Func<bool> confirmReplacement)
    {
        if (profile.Display is not null && !confirmReplacement()) return false;
        profile.Display = captured;
        if (captured.NvidiaSurround is { StatusKnown: true } surround)
            profile.NvidiaSurroundMode = (surround.HasConfiguredTopology, surround.Enabled) switch
            {
                (true, true) => NvidiaSurroundMode.RequireEnabled,
                (true, false) => NvidiaSurroundMode.RequireDisabled,
                _ => NvidiaSurroundMode.Ignore
            };
        return true;
    }
}
