using System.Collections.ObjectModel;

namespace SherpaManager.Models;

public sealed class ProfileDocument
{
    public int SchemaVersion { get; set; } = 5;
    public Guid? ActiveProfileId { get; set; }
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<SwitchProfile> Profiles { get; set; } = [];
}
