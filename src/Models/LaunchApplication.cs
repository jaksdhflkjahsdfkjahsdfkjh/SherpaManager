namespace SherpaManager.Models;

public sealed class LaunchApplication : ObservableObject
{
    public const int MaximumLaunchDelayMs = 60_000;

    private string _name = "New application";
    private string _path = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private string _processName = string.Empty;
    private int _launchDelayMs;
    private bool _closeOnDeactivate = true;
    private bool _startMinimized = true;
    private bool _enabled = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Path { get => _path; set => SetProperty(ref _path, value); }
    public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value); }
    public string WorkingDirectory { get => _workingDirectory; set => SetProperty(ref _workingDirectory, value); }
    public string ProcessName { get => _processName; set => SetProperty(ref _processName, value); }
    public int LaunchDelayMs
    {
        get => _launchDelayMs;
        set => SetProperty(ref _launchDelayMs, Math.Clamp(value, 0, MaximumLaunchDelayMs));
    }
    public bool CloseOnDeactivate { get => _closeOnDeactivate; set => SetProperty(ref _closeOnDeactivate, value); }
    public bool StartMinimized { get => _startMinimized; set => SetProperty(ref _startMinimized, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public LaunchApplication Clone() => new()
    {
        Name = Name,
        Path = Path,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        ProcessName = ProcessName,
        LaunchDelayMs = LaunchDelayMs,
        CloseOnDeactivate = CloseOnDeactivate,
        StartMinimized = StartMinimized,
        Enabled = Enabled
    };
}
