namespace SherpaManager.Services;

public sealed record ResolvedLaunchTarget(
    string LaunchPath,
    string? ExecutablePath,
    string ProcessName,
    string IdentityKey,
    bool IsShortcutOrProtocol,
    bool HasExplicitProcessName = false,
    IReadOnlyList<string>? ManagedExecutablePaths = null);
