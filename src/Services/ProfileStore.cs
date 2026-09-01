using System.Collections.ObjectModel;
using System.Text.Json;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class ProfileStore
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string FilePath { get; }

    public ProfileStore()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        var directory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SherpaManager")
            : Path.GetFullPath(overrideDirectory);
        FilePath = Path.Combine(directory, "profiles.json");
    }

    public async Task<ProfileDocument> LoadAsync()
    {
        if (!File.Exists(FilePath)) return CreateDefaults();

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(stream, JsonOptions).ConfigureAwait(false);
            if (document?.Profiles.Count > 0) return document;
        }
        catch (JsonException)
        {
            var backup = FilePath + $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(FilePath, backup, overwrite: false);
        }

        return CreateDefaults();
    }

    public async Task SaveAsync(ProfileDocument document)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";

            using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions).ConfigureAwait(false);

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally { _saveLock.Release(); }
    }

    private static ProfileDocument CreateDefaults() => new()
    {
        Profiles = new ObservableCollection<SwitchProfile>
        {
            new()
            {
                Name = "Work",
                Description = "Desk displays and everyday tools",
                AccentColor = "#9B8DFF"
            },
            new()
            {
                Name = "iRacing",
                Description = "Your iRacing display layout and companion apps",
                AccentColor = "#EC9A5C"
            },
            new()
            {
                Name = "ACC",
                Description = "Assetto Corsa Competizione setup",
                AccentColor = "#F0B649"
            }
        }
    };
}
