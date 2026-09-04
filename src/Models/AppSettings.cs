namespace SherpaManager.Models;

public sealed class AppSettings : ObservableObject
{
    private bool _minimizeToTrayOnClose = true;
    private bool _confirmDisplayChanges = true;
    private bool _showActivationPreview = true;
    private bool _startWithWindows;
    private Guid? _activateProfileOnStartup;

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

    /// <summary>Profile activated when Sherpa Manager starts, or null for none.</summary>
    public Guid? ActivateProfileOnStartup
    {
        get => _activateProfileOnStartup;
        set => SetProperty(ref _activateProfileOnStartup, value);
    }
}
