using System.Text.Json;
using SherpaManager.Models;

namespace SherpaManager.Services;

public sealed class DisplayTransactionStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _markerPath;
    private readonly string _recoveryPath;

    public DisplayTransactionStore()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        var directory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SherpaManager")
            : Path.GetFullPath(overrideDirectory);
        _markerPath = Path.Combine(directory, "display-transaction.json");
        _recoveryPath = Path.Combine(directory, "display-transaction-recovery.json");
    }

    public bool HasPendingTransaction => File.Exists(_markerPath);

    public InterruptedDisplayTransaction? GetPendingTransaction()
    {
        if (!File.Exists(_markerPath)) return null;

        DisplayTransactionMarker? marker = null;
        try
        {
            marker = JsonSerializer.Deserialize<DisplayTransactionMarker>(
                File.ReadAllText(_markerPath), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // The marker's presence is authoritative. A malformed description must
            // not hide a potentially recoverable interrupted display operation.
        }

        return new InterruptedDisplayTransaction(
            marker is { SchemaVersion: CurrentSchemaVersion } ? marker.StartedAtUtc : null,
            marker is { SchemaVersion: CurrentSchemaVersion } && !string.IsNullOrWhiteSpace(marker.RequestedSummary)
                ? marker.RequestedSummary
                : "Unknown display layout",
            TryLoadRecovery() is not null);
    }

    public void Begin(DisplaySnapshot recoverySnapshot, string requestedSummary)
    {
        ArgumentNullException.ThrowIfNull(recoverySnapshot);
        DisplayConfigurationService.ValidateSnapshotStructures(recoverySnapshot);
        if (File.Exists(_markerPath))
            throw new InvalidOperationException(
                "An interrupted display change is still waiting for recovery. Restore or dismiss it before applying another layout.");

        var directory = Path.GetDirectoryName(_markerPath)!;
        Directory.CreateDirectory(directory);
        WriteAtomically(_recoveryPath,
            JsonSerializer.SerializeToUtf8Bytes(recoverySnapshot, JsonOptions));

        var marker = new DisplayTransactionMarker
        {
            SchemaVersion = CurrentSchemaVersion,
            StartedAtUtc = DateTime.UtcNow,
            RequestedSummary = string.IsNullOrWhiteSpace(requestedSummary)
                ? "Unknown display layout"
                : requestedSummary
        };
        try
        {
            WriteAtomically(_markerPath, JsonSerializer.SerializeToUtf8Bytes(marker, JsonOptions));
        }
        catch
        {
            TryDelete(_recoveryPath);
            throw;
        }
    }

    public DisplaySnapshot LoadRecovery() => TryLoadRecovery()
        ?? throw new InvalidOperationException(
            "The interrupted display transaction exists, but its recovery snapshot is missing or damaged. Use Win+P or Windows Display Settings to recover, then dismiss the recovery prompt.");

    public void Complete()
    {
        // Removing the marker first makes a leftover recovery snapshot harmless if
        // the process exits between these two operations.
        if (File.Exists(_markerPath)) File.Delete(_markerPath);
        TryDelete(_recoveryPath);
        TryDelete(_markerPath + ".tmp");
        TryDelete(_recoveryPath + ".tmp");
    }

    private DisplaySnapshot? TryLoadRecovery()
    {
        if (!File.Exists(_recoveryPath)) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<DisplaySnapshot>(
                File.ReadAllText(_recoveryPath), JsonOptions);
            if (snapshot is null) return null;
            DisplayConfigurationService.ValidateSnapshotStructures(snapshot);
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
                                          or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporaryPath = path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                   FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class DisplayTransactionMarker
    {
        public int SchemaVersion { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public string RequestedSummary { get; set; } = string.Empty;
    }
}

public sealed record InterruptedDisplayTransaction(
    DateTime? StartedAtUtc,
    string RequestedSummary,
    bool RecoveryAvailable);
