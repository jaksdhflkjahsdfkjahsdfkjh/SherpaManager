namespace SherpaManager.Models;

/// <summary>
/// How Sherpa decides an application has finished starting, before it moves on
/// to the next one in the profile.
/// </summary>
public enum LaunchReadiness
{
    /// <summary>Move on immediately. Only the per-application delay applies.</summary>
    None,

    /// <summary>Wait until a matching process exists.</summary>
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
        LaunchReadiness.ProcessRunning => "process starts",
        LaunchReadiness.WindowVisible => "window appears",
        LaunchReadiness.WindowResponsive => "window responds",
        _ => "no wait"
    };
}
