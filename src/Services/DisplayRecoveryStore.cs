using System.Text.Json;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class DisplayRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }

    public DisplayRecoveryStore()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        var directory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SherpaManager")
            : Path.GetFullPath(overrideDirectory);
        FilePath = Path.Combine(directory, "last-display-recovery.json");
    }

    public void Save(DisplaySnapshot snapshot)
    {
        DisplayConfigurationService.ValidateSnapshotStructures(snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                   FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }
        // Preserve the last known-good backup if the primary recovery file was
        // damaged. A failed write must not leave both recovery copies corrupt.
        if (File.Exists(FilePath) && TryLoad(FilePath) is not null)
            File.Replace(temporaryPath, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public DisplaySnapshot? Load()
    {
        var primary = TryLoad(FilePath);
        return primary ?? TryLoad(FilePath + ".bak");
    }

    private static DisplaySnapshot? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<DisplaySnapshot>(File.ReadAllText(path), JsonOptions);
            if (snapshot is null) return null;
            DisplayConfigurationService.ValidateSnapshotStructures(snapshot);
            return snapshot;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
