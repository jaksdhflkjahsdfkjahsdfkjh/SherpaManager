using SherpaManager.Services;

namespace SherpaManager.Tests;

internal static partial class Program
{
    private static async Task TestPackageSmokeIsolationAsync()
    {
        using var directory = new TemporaryDirectory();
        var profilePath = Path.Combine(directory.Path, "profiles.json");
        const string sentinel = "Personal profile contents must never be read or replaced by a package check.";
        await File.WriteAllTextAsync(profilePath, sentinel);
        var original = Environment.GetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", directory.Path);
            await PackageSmokeTest.ValidateStorageAsync();
            Assert(await File.ReadAllTextAsync(profilePath) == sentinel, "Smoke testing changed the personal profile file.");
            Assert(Directory.GetFiles(directory.Path).Length == 1, "Smoke testing created files in the personal profile directory.");
        }
        finally { Environment.SetEnvironmentVariable("SHERPA_MANAGER_DATA_DIR", original); }
    }

    private static Task TestPackageSmokeResourcesAsync()
    {
        OnUiThread(PackageSmokeTest.ValidateResources);
        return Task.CompletedTask;
    }
}
