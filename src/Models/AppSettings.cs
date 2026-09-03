namespace SherpaManager.Models;

public sealed class AppSettings : ObservableObject
{
    private bool _minimizeToTrayOnClose = true;
    private bool _confirmDisplayChanges = true;

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
}
