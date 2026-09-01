using System.Collections.ObjectModel;

namespace SherpaManager.Models;

public sealed class SwitchProfile : ObservableObject
{
    private string _name = "New profile";
    private string _description = string.Empty;
    private string _accentColor = "#6C57FF";
    private DisplaySnapshot? _display;
    private DateTime? _lastActivatedUtc;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string AccentColor { get => _accentColor; set => SetProperty(ref _accentColor, value); }
    public DisplaySnapshot? Display { get => _display; set { if (SetProperty(ref _display, value)) OnPropertyChanged(nameof(DisplaySummary)); } }
    public DateTime? LastActivatedUtc { get => _lastActivatedUtc; set => SetProperty(ref _lastActivatedUtc, value); }
    public ObservableCollection<LaunchApplication> Applications { get; set; } = [];

    public string DisplaySummary => Display?.Summary ?? "No display layout captured — activation will keep the current layout.";

    public SwitchProfile Clone(string? name = null) => new()
    {
        Name = name ?? $"{Name} copy",
        Description = Description,
        AccentColor = AccentColor,
        Display = Display is null ? null : new DisplaySnapshot
        {
            CapturedAtUtc = Display.CapturedAtUtc,
            Summary = Display.Summary,
            PathStructureSize = Display.PathStructureSize,
            ModeStructureSize = Display.ModeStructureSize,
            Paths = [.. Display.Paths],
            Modes = [.. Display.Modes]
        },
        Applications = new ObservableCollection<LaunchApplication>(Applications.Select(x => x.Clone()))
    };
}
