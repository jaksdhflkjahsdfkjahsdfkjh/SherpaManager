namespace SherpaManager.Models;

public enum PreflightSeverity
{
    /// <summary>Expected, reversible, or no change at all.</summary>
    Info,

    /// <summary>Allowed to proceed, but it can lose work or leave a display unusable.</summary>
    Caution,

    /// <summary>Will fail or be skipped during activation.</summary>
    Problem
}

public sealed record PreflightItem(PreflightSeverity Severity, string Title, string? Detail = null);

public sealed class PreflightSection(string title)
{
    public string Title { get; } = title;
    public List<PreflightItem> Items { get; } = [];
}

/// <summary>
/// A read-only description of what activating a profile would do, produced
/// without touching displays or processes. Everything here is a prediction from
/// the current machine state, so an item can still turn out differently if the
/// environment changes between the preview and the switch.
/// </summary>
public sealed class ActivationPreflight
{
    public string ProfileName { get; init; } = string.Empty;
    public List<PreflightSection> Sections { get; } = [];

    public IEnumerable<PreflightItem> AllItems => Sections.SelectMany(section => section.Items);
    public int ProblemCount => AllItems.Count(item => item.Severity == PreflightSeverity.Problem);
    public int CautionCount => AllItems.Count(item => item.Severity == PreflightSeverity.Caution);
    public bool HasProblems => ProblemCount > 0;

    public string Headline
    {
        get
        {
            if (ProblemCount > 0 && CautionCount > 0)
                return $"{Describe(ProblemCount, "problem")} and {Describe(CautionCount, "change needing attention")}.";
            if (ProblemCount > 0) return $"{Describe(ProblemCount, "problem")}. Activation can continue, but these entries will not work.";
            if (CautionCount > 0) return $"{Describe(CautionCount, "change needing attention")}.";
            return "Nothing here needs your attention.";
        }
    }

    private static string Describe(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}
