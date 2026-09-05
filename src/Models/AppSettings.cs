namespace SherpaManager.Models;

public sealed class AppSettings : ObservableObject
{
    public const int DefaultDisplaySettleDelayMs = 3_000;
    public const int MaximumDisplaySettleDelayMs = 60_000;
    public const int DefaultLaunchReadinessTimeoutMs = 15_000;
    public const int MaximumLaunchReadinessTimeoutMs = 300_000;

    private bool _minimizeToTrayOnClose = true;
    private bool _confirmDisplayChanges = true;
    private bool _showActivationPreview = true;
    private bool _startWithWindows;
    private Guid? _activateProfileOnStartup;
    private int _displaySettleDelayMs = DefaultDisplaySettleDelayMs;
    private bool _showLaunchDelays;
    private LaunchReadiness _launchReadiness = LaunchReadiness.ProcessRunning;
    private int _launchReadinessTimeoutMs = DefaultLaunchReadinessTimeoutMs;

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set => SetProperty(ref _minimizeToTrayOnClose, value);
    }

    public bool ConfirmDisplayChanges
    {
        get => _confirmDisplayChanges;
        set => SetProperty(ref _confirmDisplayChanges, value);
    }

    public bool ShowActivationPreview
    {
        get => _showActivationPreview;
        set => SetProperty(ref _showActivationPreview, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    /// <summary>
    /// How long to wait after a display layout is applied before touching
    /// applications. Monitors can still be re-syncing, changing mode, or waking
    /// after the topology call returns, and an application started into that can
    /// open on the wrong monitor or at the wrong size.
    /// </summary>
    public int DisplaySettleDelayMs
    {
        get => _displaySettleDelayMs;
        set => SetProperty(ref _displaySettleDelayMs, Math.Clamp(value, 0, MaximumDisplaySettleDelayMs));
    }

    /// <summary>
    /// What Sherpa waits for after starting an application, before starting the
    /// next one in the profile. Applies to every application, so ordering the list
    /// is the whole configuration.
    /// </summary>
    public LaunchReadiness LaunchReadiness
    {
        get => _launchReadiness;
        set => SetProperty(ref _launchReadiness, value);
    }

    /// <summary>How long to wait for readiness before giving up and continuing.</summary>
    public int LaunchReadinessTimeoutMs
    {
        get => _launchReadinessTimeoutMs;
        set => SetProperty(ref _launchReadinessTimeoutMs, Math.Clamp(value, 1_000, MaximumLaunchReadinessTimeoutMs));
    }

    /// <summary>
    /// Shows the per-application launch delay column. Off by default: launch order
    /// and readiness rules cover almost everything, and a fixed delay is only
    /// needed for targets Sherpa cannot detect, such as scripts and protocol URLs.
    /// </summary>
    public bool ShowLaunchDelays
    {
        get => _showLaunchDelays;
        set => SetProperty(ref _showLaunchDelays, value);
    }

    /// <summary>Profile activated when Sherpa Manager starts, or null for none.</summary>
    public Guid? ActivateProfileOnStartup
    {
        get => _activateProfileOnStartup;
        set => SetProperty(ref _activateProfileOnStartup, value);
    }
}
