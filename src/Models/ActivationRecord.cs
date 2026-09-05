namespace SherpaManager.Models;

/// <summary>One reported step of a profile switch, in the order it happened.</summary>
public sealed record ActivationStep(DateTime TimestampUtc, string Message, bool IsWarning);

/// <summary>
/// What one profile switch actually did. Recorded from the same messages the user
/// sees during the switch, so the history and the live status can never disagree.
/// </summary>
public sealed class ActivationRecord
{
    /// <summary>
    /// Counts switches within this run, so one can be referred to by number in a
    /// long session instead of by "the third one down".
    /// </summary>
    public int Sequence { get; init; }

    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The activation outcome, matching what diagnostics records.</summary>
    public string Outcome { get; set; } = "in progress";
    public long DurationMs { get; set; }

    public List<ActivationStep> Steps { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool Succeeded => Outcome.StartsWith("succeeded", StringComparison.Ordinal);
    public DateTime StartedLocal => StartedUtc.ToLocalTime();

    public string Headline =>
        $"{ProfileName} · {StartedLocal:HH:mm:ss} · {DurationMs / 1000.0:0.#}s · {DescribeOutcome()}";

    public string DescribeOutcome() => Outcome switch
    {
        "succeeded" => "succeeded",
        "succeeded_with_warnings" => $"{Warnings.Count} warning{(Warnings.Count == 1 ? "" : "s")}",
        "display_reverted" => "display reverted",
        "previous_application_close_failed" => "an application would not close",
        "cancelled" => "cancelled",
        "failed" => "failed",
        _ => Outcome
    };

    /// <summary>
    /// Marks the steps that were also raised as warnings, so the history shows the
    /// same emphasis the user saw at the time.
    /// </summary>
    public void MarkWarnings()
    {
        if (Warnings.Count == 0) return;
        var warned = new HashSet<string>(Warnings, StringComparer.Ordinal);
        for (var index = 0; index < Steps.Count; index++)
            if (warned.Contains(Steps[index].Message))
                Steps[index] = Steps[index] with { IsWarning = true };
    }
}
