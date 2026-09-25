using System.Text.Json.Serialization;

namespace AgentSquad.Core.Validation;

/// <summary>Outcome of one step of the validation gate.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValidationOutcome>))]
public enum ValidationOutcome
{
    /// <summary>The step has not run yet.</summary>
    NotRun = 0,

    /// <summary>The step exited successfully.</summary>
    Passed = 1,

    /// <summary>The step reported a problem.</summary>
    Failed = 2,

    /// <summary>The step was deliberately not run for this work item.</summary>
    Skipped = 3,

    /// <summary>The step exceeded its time budget.</summary>
    TimedOut = 4,
}

/// <summary>The steps of the .NET validation gate, in execution order.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValidationStepKind>))]
public enum ValidationStepKind
{
    /// <summary><c>dotnet restore</c>.</summary>
    Restore = 0,

    /// <summary><c>dotnet format --verify-no-changes</c>.</summary>
    Format = 1,

    /// <summary><c>dotnet build -warnaserror</c>, which also runs the Roslyn analyzers.</summary>
    Build = 2,

    /// <summary><c>dotnet test</c>.</summary>
    Test = 3,

    /// <summary><c>dotnet list package --vulnerable --include-transitive</c>.</summary>
    Vulnerabilities = 4,

    /// <summary>Scope check: did the agent stay inside the files its issue declared?</summary>
    ScopeCompliance = 5,

    /// <summary>Secret scan over the produced diff.</summary>
    SecretScan = 6,
}

/// <summary>
/// The result of a single validation step.
/// </summary>
/// <param name="Kind">Which step this is.</param>
/// <param name="Outcome">Whether it passed.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="ExitCode">Process exit code, when a process was run.</param>
/// <param name="Summary">One line suitable for a pull-request table.</param>
/// <param name="Diagnostics">
/// The actionable output. This is fed back verbatim to the coding agent for repair,
/// so it must keep compiler and test messages intact.
/// </param>
public sealed record ValidationStep(
    ValidationStepKind Kind,
    ValidationOutcome Outcome,
    TimeSpan Duration,
    int? ExitCode,
    string Summary,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Gets a value indicating whether this step blocks the pull request.</summary>
    [JsonIgnore]
    public bool IsBlocking => Outcome is ValidationOutcome.Failed or ValidationOutcome.TimedOut;
}

/// <summary>
/// The full outcome of validating one work item's worktree.
/// </summary>
/// <remarks>
/// This report — never the coding agent's own claim — decides whether a pull request
/// may be opened (AI-008: deterministic validation is the source of truth).
/// </remarks>
/// <param name="IssueNumber">The GitHub issue the worktree implements.</param>
/// <param name="Attempt">One-based repair attempt this report belongs to.</param>
/// <param name="Steps">Every step that ran, in order.</param>
/// <param name="StartedAt">When validation began.</param>
/// <param name="Duration">Total wall-clock duration.</param>
public sealed record ValidationReport(
    int IssueNumber,
    int Attempt,
    IReadOnlyList<ValidationStep> Steps,
    DateTimeOffset StartedAt,
    TimeSpan Duration)
{
    /// <summary>Gets a value indicating whether every step passed or was deliberately skipped.</summary>
    [JsonIgnore]
    public bool Passed => Steps.All(s => s.Outcome is ValidationOutcome.Passed or ValidationOutcome.Skipped);

    /// <summary>Gets the steps that block the pull request.</summary>
    [JsonIgnore]
    public IReadOnlyList<ValidationStep> Failures => [.. Steps.Where(s => s.IsBlocking)];

    /// <summary>
    /// Renders the diagnostics that a coding agent needs in order to repair the failure.
    /// </summary>
    /// <param name="maxLinesPerStep">Cap on diagnostic lines per failed step, to bound prompt size.</param>
    /// <returns>A Markdown fragment listing each failure and its output.</returns>
    public string ToRepairBrief(int maxLinesPerStep = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLinesPerStep);

        var builder = new System.Text.StringBuilder();

        foreach (ValidationStep step in Failures)
        {
            builder.Append("### ").Append(step.Kind).Append(" — ").Append(step.Outcome).AppendLine();
            builder.AppendLine();
            builder.AppendLine("```text");

            foreach (string line in step.Diagnostics.Take(maxLinesPerStep))
            {
                builder.AppendLine(line);
            }

            if (step.Diagnostics.Count > maxLinesPerStep)
            {
                builder.Append("... (").Append(step.Diagnostics.Count - maxLinesPerStep).AppendLine(" more lines omitted)");
            }

            builder.AppendLine("```");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>Renders a compact Markdown table for the pull-request body.</summary>
    /// <returns>A Markdown table with one row per step.</returns>
    public string ToMarkdownTable()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("| Etapa | Resultado | Duração | Detalhe |");
        builder.AppendLine("|---|---|---|---|");

        foreach (ValidationStep step in Steps)
        {
            string icon = step.Outcome switch
            {
                ValidationOutcome.Passed => "✅",
                ValidationOutcome.Failed => "❌",
                ValidationOutcome.TimedOut => "⏱️",
                ValidationOutcome.Skipped => "⏭️",
                _ => "•",
            };

            builder.Append("| ").Append(step.Kind)
                   .Append(" | ").Append(icon).Append(' ').Append(step.Outcome)
                   .Append(" | ").Append(step.Duration.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append('s')
                   .Append(" | ").Append(step.Summary.Replace('|', '/'))
                   .AppendLine(" |");
        }

        return builder.ToString();
    }
}
