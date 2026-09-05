using System.Reflection;

namespace SherpaManager.Services;

/// <summary>The application version, formatted for display.</summary>
public static class AppVersion
{
    /// <summary>For example <c>v0.5.0</c>.</summary>
    public static string Display { get; } = Format(
        (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly).GetName().Version);

    /// <summary>
    /// Prefers the informational version, which is the human one, and drops the
    /// build metadata the SDK appends after a '+'. Falls back to the assembly
    /// version without its trailing revision, which is always zero here.
    /// </summary>
    public static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadata = informationalVersion.IndexOf('+');
            var trimmed = (metadata < 0 ? informationalVersion : informationalVersion[..metadata]).Trim();
            if (trimmed.Length > 0) return "v" + trimmed;
        }

        return assemblyVersion is null
            ? "v0.0.0"
            : $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(0, assemblyVersion.Build)}";
    }
}
