using SherpaManager.Models;

namespace SherpaManager.Services;

public interface IProcessService
{
    ResolvedLaunchTarget Resolve(LaunchApplication app);
    void Validate(LaunchApplication app);
    string GetIdentityKey(LaunchApplication app);
    void CancelPendingClose(LaunchApplication app);
    bool IsPendingCloseOutcomeCurrent(PendingProcessCloseOutcome outcome);
    bool IsRunning(LaunchApplication app);
    Task<ProcessLaunchResult> LaunchAsync(LaunchApplication app, CancellationToken cancellationToken);

    /// <summary>Waits until <paramref name="app"/> meets its readiness rule, or the timeout passes.</summary>
    Task<ProcessReadinessResult> WaitUntilReadyAsync(LaunchApplication app, LaunchReadiness readiness,
        TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> MinimizeAsync(LaunchApplication app, TimeSpan timeout, CancellationToken cancellationToken);
    Task<ProcessCloseResult> CloseAsync(LaunchApplication app, CancellationToken cancellationToken);
    Task<ProcessCloseResult> ForceCloseAsync(LaunchApplication app, CancellationToken cancellationToken);
}
