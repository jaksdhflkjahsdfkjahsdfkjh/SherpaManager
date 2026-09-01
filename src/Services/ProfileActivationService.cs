using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProfileActivationService(DisplayConfigurationService displays, ProcessService processes)
{
    private readonly SemaphoreSlim _activationLock = new(1, 1);

    public async Task ActivateAsync(ProfileDocument document, SwitchProfile target,
        Action<string> report, CancellationToken cancellationToken = default)
    {
        if (!await _activationLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another profile switch is already in progress.");

        try
        {
            foreach (var app in target.Applications.Where(x => x.Enabled)) processes.Validate(app);

            var previous = document.Profiles.FirstOrDefault(x => x.Id == document.ActiveProfileId);
            if (target.Display is not null)
            {
                report("Applying display layout…");
                displays.Restore(target.Display);
                await Task.Delay(1000, cancellationToken);
            }
            else report("Keeping the current display layout.");

            if (previous is not null && previous.Id != target.Id)
            {
                var targetProcessNames = target.Applications.Select(ProcessKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var app in previous.Applications.Where(x => x.Enabled && x.CloseOnDeactivate))
                {
                    if (targetProcessNames.Contains(ProcessKey(app))) continue;
                    report($"Closing {app.Name}…");
                    await processes.CloseAsync(app, cancellationToken);
                }
            }

            foreach (var app in target.Applications.Where(x => x.Enabled))
            {
                if (processes.IsRunning(app))
                {
                    report($"{app.Name} is already running.");
                    continue;
                }
                if (app.LaunchDelayMs > 0) await Task.Delay(app.LaunchDelayMs, cancellationToken);
                report($"Starting {app.Name}…");
                processes.Launch(app);
            }

            target.LastActivatedUtc = DateTime.UtcNow;
            document.ActiveProfileId = target.Id;
            report($"{target.Name} is ready.");
        }
        finally { _activationLock.Release(); }
    }

    private static string ProcessKey(LaunchApplication app) =>
        string.IsNullOrWhiteSpace(app.ProcessName)
            ? Path.GetFileNameWithoutExtension(app.Path)
            : Path.GetFileNameWithoutExtension(app.ProcessName);
}
