namespace SherpaManager.Services;

public enum ProcessCloseStatus
{
    NotRunning,
    ClosedGracefully,
    ForcedClosed,
    Superseded,
    CloseScheduled,
    StillRunning,
    AccessDenied,
    MonitoringFailed
}

public sealed record ProcessCloseResult(ProcessCloseStatus Status, int MatchedCount, string Message)
{
    public bool Succeeded => Status is ProcessCloseStatus.NotRunning
        or ProcessCloseStatus.ClosedGracefully
        or ProcessCloseStatus.ForcedClosed
        or ProcessCloseStatus.Superseded;
}

public sealed record ProcessLaunchResult(
    bool Started,
    bool Minimized,
    string Message,
    bool LifecycleManageable = true,
    bool MinimizationPending = false)
{
    public bool HasWarning => !Started || !LifecycleManageable;
}

/// <summary>
/// Reports the eventual result of a non-blocking close monitor. Handlers run on
/// a worker thread and should marshal UI work to the dispatcher.
/// </summary>
public sealed record PendingProcessCloseOutcome(
    Guid ApplicationId,
    string ApplicationName,
    string IdentityKey,
    ProcessCloseResult Result,
    long Generation);

/// <summary>
/// Reports the verified result of an asynchronous minimization observer. Handlers
/// run on a worker thread and should marshal UI work to the dispatcher.
/// </summary>
public sealed record PendingProcessMinimizationOutcome(
    Guid ApplicationId,
    string ApplicationName,
    string IdentityKey,
    bool Minimized,
    string Message);
