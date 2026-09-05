using SherpaManager.Models;

namespace SherpaManager.Services;

public enum ApplicationIssueKind
{
    None,

    /// <summary>Another enabled entry in the profile starts the same thing.</summary>
    Duplicate,

    /// <summary>The executable, shortcut, or protocol target could not be resolved.</summary>
    NotFound,

    /// <summary>The working directory does not exist.</summary>
    WorkingDirectoryMissing
}

public sealed record ApplicationIssue(ApplicationIssueKind Kind, string Message);

/// <summary>
/// Finds the problems in a profile's application list that would otherwise only
/// appear during a switch.
/// </summary>
/// <remarks>
/// Deliberately the same rules the activation preview applies, so the editor and
/// the preview cannot disagree about whether an entry is usable.
/// </remarks>
public static class ApplicationIssueScanner
{
    /// <summary>
    /// Scans the profile and writes the results onto each entry: the reason it
    /// will not work, and its place in the launch order. An entry that will not
    /// start takes no number, so the numbers that remain read as the sequence
    /// that will actually run.
    /// </summary>
    /// <returns>How many entries need attention.</returns>
    public static int Apply(SwitchProfile profile, IProcessService processes)
    {
        var issues = Scan(profile, processes);
        var position = 0;
        foreach (var app in profile.Applications)
        {
            app.Issue = issues.TryGetValue(app.Id, out var issue) ? issue.Message : string.Empty;
            app.OrderLabel = app.HasIssue ? string.Empty : (++position).ToString();
        }
        return issues.Count;
    }

    public static IReadOnlyDictionary<Guid, ApplicationIssue> Scan(SwitchProfile profile, IProcessService processes)
    {
        var issues = new Dictionary<Guid, ApplicationIssue>();
        if (profile.Applications.Count == 0) return issues;

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in profile.Applications)
        {
            string identity;
            try
            {
                identity = processes.GetIdentityKey(app);
            }
            catch (Exception exception)
            {
                issues[app.Id] = new ApplicationIssue(ApplicationIssueKind.NotFound, exception.Message);
                continue;
            }

            var workingDirectory = Environment.ExpandEnvironmentVariables(app.WorkingDirectory.Trim());
            if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
            {
                issues[app.Id] = new ApplicationIssue(ApplicationIssueKind.WorkingDirectoryMissing,
                    $"The working directory does not exist: {workingDirectory}");
                continue;
            }

            // Only enabled entries are launched, so only they can collide. A
            // disabled duplicate is a deliberate spare, not a problem.
            if (!app.Enabled) continue;

            if (seen.TryGetValue(identity, out var owner))
            {
                issues[app.Id] = new ApplicationIssue(ApplicationIssueKind.Duplicate,
                    $"Starts the same thing as {owner}, so it will be skipped.");
                continue;
            }

            seen[identity] = app.Name;
        }

        return issues;
    }
}
