using System.Diagnostics;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProcessService
{
    public void Validate(LaunchApplication app)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(app.Path.Trim());
        if (string.IsNullOrWhiteSpace(expandedPath)) throw new InvalidOperationException($"{app.Name} has no executable path.");
        if ((!Uri.TryCreate(expandedPath, UriKind.Absolute, out var uri) || uri.IsFile) && !File.Exists(expandedPath))
            throw new FileNotFoundException($"Could not find {app.Name}.", expandedPath);
    }

    public bool IsRunning(LaunchApplication app)
    {
        var processName = GetProcessName(app);
        return !string.IsNullOrWhiteSpace(processName) && Process.GetProcessesByName(processName).Length > 0;
    }

    public void Launch(LaunchApplication app)
    {
        Validate(app);
        var expandedPath = Environment.ExpandEnvironmentVariables(app.Path.Trim());

        var workingDirectory = Environment.ExpandEnvironmentVariables(app.WorkingDirectory.Trim());
        if (string.IsNullOrWhiteSpace(workingDirectory) && File.Exists(expandedPath))
            workingDirectory = Path.GetDirectoryName(expandedPath) ?? string.Empty;

        Process.Start(new ProcessStartInfo
        {
            FileName = expandedPath,
            Arguments = Environment.ExpandEnvironmentVariables(app.Arguments),
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = app.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal
        });
    }

    public async Task<bool> CloseAsync(LaunchApplication app, CancellationToken cancellationToken)
    {
        var processName = GetProcessName(app);
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var closedAny = false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.HasExited) continue;
                if (!process.CloseMainWindow()) continue;
                closedAny = true;
                try { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
                catch (TimeoutException) { /* Never force-kill user applications. */ }
            }
        }
        return closedAny;
    }

    private static string GetProcessName(LaunchApplication app)
    {
        if (!string.IsNullOrWhiteSpace(app.ProcessName)) return Path.GetFileNameWithoutExtension(app.ProcessName.Trim());
        var expandedPath = Environment.ExpandEnvironmentVariables(app.Path.Trim());
        if (Uri.TryCreate(expandedPath, UriKind.Absolute, out var uri) && !uri.IsFile) return string.Empty;
        return Path.GetFileNameWithoutExtension(expandedPath);
    }
}
