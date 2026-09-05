namespace SherpaManager.Models;

/// <summary>
/// How Sherpa decides an application has finished starting, before it moves on
/// to the next one in the profile.
/// </summary>
public enum LaunchReadiness
{
    /// <summary>Move on immediately. Only the per-application delay applies.</summary>
    None,

    /// <summary>
    /// Wait until a matching process has finished starting and is waiting for
    /// input. Kept under its original name so saved settings still load.
    /// </summary>
    ProcessRunning,

    /// <summary>Wait until a matching process owns a visible window.</summary>
    WindowVisible,

    /// <summary>Wait until a matching process owns a window and is answering messages.</summary>
    WindowResponsive
}

public static class LaunchReadinessExtensions
{
    public static string Describe(this LaunchReadiness readiness) => readiness switch
    {
        LaunchReadiness.ProcessRunning => "finishes starting",
        LaunchReadiness.WindowVisible => "window appears",
        LaunchReadiness.WindowResponsive => "window responds",
        _ => "no wait"
    };
}
