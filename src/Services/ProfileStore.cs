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
            var document = await TryLoadAsync(FilePath).ConfigureAwait(false);
            if (document is null)
                throw new InvalidDataException("The profile document is null.");
            return Normalize(document);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            TryPreserveInvalidPrimary();
        }

        try
        {
            var backupDocument = await TryLoadAsync(FilePath + ".bak").ConfigureAwait(false);
            if (backupDocument is not null) return Normalize(backupDocument);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        { /* Both copies are invalid; create safe defaults below. */ }

        return CreateDefaults();
    }

    private void TryPreserveInvalidPrimary()
    {
        try
        {
            var backup = FilePath + $".invalid-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
            File.Copy(FilePath, backup, overwrite: false);
        }
        catch { /* Keeping a diagnostic copy must never prevent .bak recovery. */ }
    }

    private static async Task<ProfileDocument?> TryLoadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProfileDocument>(stream, JsonOptions).ConfigureAwait(false);
    }

    public async Task SaveAsync(ProfileDocument document)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";

            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            var primaryIsValid = false;
            if (File.Exists(FilePath))
            {
                // Never replace the last known-good backup with a corrupt or empty
                // primary file. This matters when LoadAsync recovered from .bak and
                // the normalized document is being written back to profiles.json.
                try
                {
                    var primary = await TryLoadAsync(FilePath).ConfigureAwait(false);
                    if (primary is not null)
                    {
                        _ = Normalize(primary);
                        primaryIsValid = true;
                    }
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException) { }

            }
            if (primaryIsValid)
                File.Replace(temporaryPath, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally { _saveLock.Release(); }
    }

    private static ProfileDocument Normalize(ProfileDocument document)
    {
        document.Settings ??= new AppSettings();
        if (document.Profiles is not { Count: > 0 })
            throw new InvalidDataException("The profile document does not contain any profiles.");
        foreach (var profile in document.Profiles)
        {
            if (profile is null)
                throw new InvalidDataException("The profile document contains a null profile entry.");
            profile.Applications ??= [];
            foreach (var app in profile.Applications)
            {
                if (app is null)
                    throw new InvalidDataException($"Profile '{profile.Name}' contains a null application entry.");
                if (app.Id == Guid.Empty) app.Id = Guid.NewGuid();
            }
            if (profile.Display is { } display)
            {
                display.ActiveTargets ??= [];
                display.Paths ??= [];
                display.Modes ??= [];
                if (display.NvidiaSurround is { } surround)
                {
                    surround.GridCells ??= [];
                    surround.DisplayGrids ??= [];
                    if (surround.DisplayGrids.Any(grid => grid is null))
                        throw new InvalidDataException($"Profile '{profile.Name}' contains an invalid NVIDIA display grid.");
                    foreach (var grid in surround.DisplayGrids)
                    {
                        grid.Displays ??= [];
                        if (grid.Displays.Any(panel => panel is null))
                            throw new InvalidDataException($"Profile '{profile.Name}' contains an invalid NVIDIA grid panel.");
                    }
                }
                if (display.ActiveTargets.Any(target => target is null))
                    throw new InvalidDataException($"Profile '{profile.Name}' contains invalid display-target metadata.");
                if (display.ActiveTargets.Count == 0 && display.SnapshotVersion >= 3)
                    display.SnapshotVersion = 1;
                else if (display.SnapshotVersion >= 3 && display.ActiveTargets.All(target =>
                             target.SourceWidth == 0 && target.SourceHeight == 0))
                    display.SnapshotVersion = 2;
            }
        }
        document.SchemaVersion = 4;
        return document;
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
