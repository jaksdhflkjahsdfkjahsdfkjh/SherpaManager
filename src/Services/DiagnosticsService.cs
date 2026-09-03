using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SherpaManager.Models;

namespace SherpaManager.Services;

public interface IDiagnosticLog
{
    void Write(string level, string eventName, string? message = null,
        IReadOnlyDictionary<string, object?>? data = null, long? durationMs = null);
    void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? data = null,
        long? durationMs = null);
}

public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static NullDiagnosticLog Instance { get; } = new();
    private NullDiagnosticLog() { }
    public void Write(string level, string eventName, string? message = null,
        IReadOnlyDictionary<string, object?>? data = null, long? durationMs = null) { }
    public void Error(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? data = null,
        long? durationMs = null) { }
}

public sealed class DiagnosticsService : IDiagnosticLog
{
    private const int DefaultMaximumFileBytes = 1_048_576;
    private const int DefaultMaximumFileCount = 5;
    private static readonly Regex WindowsPathPattern = new(
        @"(?im)(?:[a-z]:\\|\\\\)[^\r\n]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FileUriPattern = new(
        @"(?im)file:///[^\r\n]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NvApiCodePattern = new(
        @"NVAPI\s+(-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly object _gate = new();
    private readonly int _maximumFileBytes;
    private readonly int _maximumFileCount;
    private readonly Func<DateTime> _utcNow;

    public static DiagnosticsService Current { get; } = new();

    public DiagnosticsService(string? logDirectory = null, int maximumFileBytes = DefaultMaximumFileBytes,
        int maximumFileCount = DefaultMaximumFileCount, Func<DateTime>? utcNow = null)
    {
        if (maximumFileBytes < 256) throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        if (maximumFileCount < 2) throw new ArgumentOutOfRangeException(nameof(maximumFileCount));
        LogDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SherpaManager", "Logs");
        LogFilePath = Path.Combine(LogDirectory, "sherpa.log");
        _maximumFileBytes = maximumFileBytes;
        _maximumFileCount = maximumFileCount;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        TryCreateDirectory();
    }

    public string LogDirectory { get; }
    public string LogFilePath { get; }

    public void Write(string level, string eventName, string? message = null,
        IReadOnlyDictionary<string, object?>? data = null, long? durationMs = null)
    {
        try
        {
            var safeData = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            if (data is not null)
            {
                foreach (var item in data)
                    safeData[item.Key] = SanitizeValue(item.Key, item.Value);
            }
            var entry = new DiagnosticEvent(
                _utcNow().ToString("O"),
                string.IsNullOrWhiteSpace(level) ? "info" : level.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(eventName) ? "diagnostic" : eventName.Trim(),
                RedactText(message), durationMs, safeData.Count == 0 ? null : safeData);
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            var lineBytes = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            lock (_gate)
            {
                TryCreateDirectory();
                RotateIfNeeded(lineBytes);
                using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(line);
            }
        }
        catch
        {
            // Diagnostics must never make the application operation fail.
        }
    }

    public void Error(string eventName, Exception exception,
        IReadOnlyDictionary<string, object?>? data = null, long? durationMs = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var details = data is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(data);
        var exceptions = FlattenExceptions(exception).ToList();
        var win32Codes = exceptions.OfType<Win32Exception>()
            .Select(item => item.NativeErrorCode).Distinct().ToArray();
        var nvApiCodes = exceptions.SelectMany(item => NvApiCodePattern.Matches(item.Message)
                .Select(match => int.TryParse(match.Groups[1].Value, out var code) ? (int?)code : null))
            .Where(code => code.HasValue).Select(code => code!.Value).Distinct().ToArray();
        details["exceptionType"] = exception.GetType().FullName;
        details["hResults"] = exceptions.Select(item => item.HResult).Distinct().ToArray();
        if (win32Codes.Length > 0) details["win32Codes"] = win32Codes;
        if (nvApiCodes.Length > 0) details["nvapiCodes"] = nvApiCodes;
        if (!string.IsNullOrWhiteSpace(exception.StackTrace)) details["stackTrace"] = exception.StackTrace;
        Write("error", eventName, exception.Message, details, durationMs);
    }

    public string CreateClipboardReport(DisplaySnapshot? topology = null, string? topologyError = null,
        int maximumRecentEvents = 200)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Sherpa Manager diagnostics (paths redacted)");
        builder.AppendLine($"Generated UTC: {_utcNow():O}");
        builder.AppendLine($"Sherpa version: {GetSherpaVersion()}");
        builder.AppendLine($"Windows: {RuntimeInformation.OSDescription.Trim()} ({Environment.OSVersion.Version})");
        builder.AppendLine($"Architecture: OS {RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine();
        builder.AppendLine("GPU/display drivers:");
        var drivers = GetDisplayDrivers();
        if (drivers.Count == 0) builder.AppendLine("- Unavailable");
        foreach (var driver in drivers) builder.AppendLine($"- {RedactText(driver)}");
        builder.AppendLine();
        builder.AppendLine("NVIDIA API status:");
        AppendNvidiaStatus(builder, topology?.NvidiaSurround);
        builder.AppendLine();
        builder.AppendLine("Detected monitor topology:");
        if (topology is null)
        {
            builder.AppendLine($"- Unavailable: {RedactText(topologyError) ?? "capture failed"}");
        }
        else
        {
            builder.AppendLine($"- Logical displays: {topology.LogicalDisplayCount}");
            foreach (var target in (topology.ActiveTargets ?? []).OrderBy(item => item.SourceX).ThenBy(item => item.SourceY))
            {
                var refresh = target.RefreshDenominator == 0
                    ? "unknown refresh"
                    : $"{target.RefreshNumerator / (double)target.RefreshDenominator:0.###} Hz";
                builder.AppendLine($"- {RedactText(target.FriendlyName)}: {target.SourceWidth}x{target.SourceHeight} " +
                                   $"at ({target.SourceX},{target.SourceY}), {refresh}, rotation {target.Rotation}");
            }
        }
        builder.AppendLine();
        builder.AppendLine($"Recent structured events (up to {Math.Max(0, maximumRecentEvents)}):");
        foreach (var line in ReadRecentLines(maximumRecentEvents)) builder.AppendLine(line);
        return builder.ToString().TrimEnd();
    }

    public IReadOnlyList<string> ReadRecentLines(int maximumLines)
    {
        if (maximumLines <= 0) return [];
        try
        {
            lock (_gate)
            {
                var lines = new Queue<string>(maximumLines);
                foreach (var file in EnumerateLogFilesOldestFirst())
                {
                    if (!File.Exists(file)) continue;
                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (lines.Count == maximumLines) lines.Dequeue();
                        lines.Enqueue(RedactText(line) ?? string.Empty);
                    }
                }
                return lines.ToList();
            }
        }
        catch
        {
            return [];
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(LogFilePath) || new FileInfo(LogFilePath).Length + incomingBytes <= _maximumFileBytes)
            return;
        var lastArchive = GetArchivePath(_maximumFileCount - 1);
        if (File.Exists(lastArchive)) File.Delete(lastArchive);
        for (var index = _maximumFileCount - 2; index >= 1; index--)
        {
            var source = GetArchivePath(index);
            if (File.Exists(source)) File.Move(source, GetArchivePath(index + 1));
        }
        File.Move(LogFilePath, GetArchivePath(1));
    }

    private IEnumerable<string> EnumerateLogFilesOldestFirst()
    {
        for (var index = _maximumFileCount - 1; index >= 1; index--)
            yield return GetArchivePath(index);
        yield return LogFilePath;
    }

    private string GetArchivePath(int index) => Path.Combine(LogDirectory, $"sherpa.{index}.log");

    private void TryCreateDirectory()
    {
        try { Directory.CreateDirectory(LogDirectory); }
        catch { /* Write and report operations handle an unavailable directory. */ }
    }

    private static object? SanitizeValue(string key, object? value)
    {
        if (value is null) return null;
        if (key.Contains("argument", StringComparison.OrdinalIgnoreCase)) return "<redacted>";
        if (value is string text)
        {
            if (IsPathField(key) && !string.IsNullOrWhiteSpace(text)) return "<redacted-path>";
            return RedactText(text);
        }
        if (value is IEnumerable<string> strings) return strings.Select(item => RedactText(item)).ToArray();
        if (value.GetType().IsPrimitive || value is decimal or DateTime or DateTimeOffset or Guid or Enum)
            return value;
        if (value is System.Collections.IEnumerable enumerable)
        {
            var safeItems = new List<object?>();
            foreach (var item in enumerable)
                safeItems.Add(item is string itemText ? RedactText(itemText) : item);
            return safeItems;
        }
        return RedactText(value.ToString());
    }

    private static bool IsPathField(string key) =>
        key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("directory", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("fileName", StringComparison.OrdinalIgnoreCase);

    private static string? RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redacted = FileUriPattern.Replace(value, "<redacted-path>");
        return WindowsPathPattern.Replace(redacted, "<redacted-path>");
    }

    private static IEnumerable<Exception> FlattenExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
            }
            else if (current.InnerException is not null) pending.Push(current.InnerException);
        }
    }

    private static string GetSherpaVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(DiagnosticsService).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
               assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static IReadOnlyList<string> GetDisplayDrivers()
    {
        var drivers = new List<string>();
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var displayClass = machine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (displayClass is null) return drivers;
            foreach (var subKeyName in displayClass.GetSubKeyNames().OrderBy(value => value, StringComparer.Ordinal))
            {
                using var driver = displayClass.OpenSubKey(subKeyName);
                var description = driver?.GetValue("DriverDesc") as string;
                var provider = driver?.GetValue("ProviderName") as string;
                var version = driver?.GetValue("DriverVersion") as string;
                if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(version)) continue;
                drivers.Add($"{description ?? "Unknown GPU"}; provider {provider ?? "unknown"}; driver {version ?? "unknown"}");
            }
        }
        catch { }
        return drivers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AppendNvidiaStatus(StringBuilder builder, NvidiaSurroundSnapshot? status)
    {
        if (status is null)
        {
            builder.AppendLine("- Unavailable because display capture failed.");
            return;
        }
        builder.AppendLine($"- API available: {status.ApiAvailable}");
        builder.AppendLine($"- Status known: {status.StatusKnown}");
        builder.AppendLine($"- Surround enabled: {status.Enabled}");
        builder.AppendLine($"- Configured topology: {status.HasConfiguredTopology}");
        builder.AppendLine($"- Grid count: {status.DisplayGrids?.Count ?? 0}; full grid captured: {status.FullGridCaptured}");
        builder.AppendLine($"- Detail: {RedactText(status.Description)}");
    }

    private sealed record DiagnosticEvent(string TimestampUtc, string Level, string Event,
        string? Message, long? DurationMs, IReadOnlyDictionary<string, object?>? Data);
}
