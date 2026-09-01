using System.Collections.ObjectModel;

namespace SherpaManager.Models;

public sealed class ProfileDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid? ActiveProfileId { get; set; }
    public ObservableCollection<SwitchProfile> Profiles { get; set; } = [];
}
