using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgentSquad.Core.Implementation;

/// <summary>How a coding agent's run ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImplementationStatus>))]
public enum ImplementationStatus
{
    /// <summary>The work item was implemented and the local gate passed.</summary>
    Completed = 0,

    /// <summary>Some of the work was done, but not all acceptance criteria are met.</summary>
    Partial = 1,

    /// <summary>The agent stopped and explained why. This is a valid, useful outcome.</summary>
    Blocked = 2,

    /// <summary>The agent run failed outside its own control: timeout, crash, cancellation.</summary>
    Failed = 3,
}

/// <summary>
/// The structured report a coding agent emits at the end of its run.
/// </summary>
/// <remarks>
/// The agent is asked to print this as a fenced <c>json AGENT_REPORT</c> block
/// (see <c>AGENTS.md</c> §8). It is advisory: the orchestrator trusts
/// <see cref="Validation.ValidationReport"/> for the pass/fail decision, and uses this
/// for the pull-request narrative, the audit trail and the human reviewer's attention list.
/// </remarks>
/// <param name="Issue">The GitHub issue number.</param>
/// <param name="Status">How the run ended.</param>
/// <param name="Summary">One sentence about what was delivered.</param>
/// <param name="FilesChanged">Files the agent believes it changed.</param>
/// <param name="TestsAdded">Tests the agent added.</param>
/// <param name="OutOfScopeNeeded">Work the agent found necessary but correctly refused to do.</param>
/// <param name="Risks">What a human reviewer should look at closely.</param>
/// <param name="BlockedReason">Why the agent stopped, when <see cref="Status"/> is <see cref="ImplementationStatus.Blocked"/>.</param>
public sealed record AgentReport(
    [property: Description("GitHub issue number.")] int Issue,
    [property: Description("completed | partial | blocked | failed")] ImplementationStatus Status,
    [property: Description("One sentence on what was delivered.")] string Summary,
    [property: Description("Files the agent changed.")] IReadOnlyList<string> FilesChanged,
    [property: Description("Tests the agent added.")] IReadOnlyList<string> TestsAdded,
    [property: Description("Necessary work the agent refused to do because it was out of scope.")] IReadOnlyList<string> OutOfScopeNeeded,
    [property: Description("What a human reviewer should look at closely.")] IReadOnlyList<string> Risks,
    [property: Description("Why the agent stopped, when blocked.")] string? BlockedReason)
{
    /// <summary>Creates the report used when a run fails outside the agent's control.</summary>
    /// <param name="issue">The issue number.</param>
    /// <param name="reason">What went wrong.</param>
    /// <returns>A report with <see cref="ImplementationStatus.Failed"/>.</returns>
    public static AgentReport ForFailure(int issue, string reason) =>
        new(issue, ImplementationStatus.Failed, reason, [], [], [], [], reason);
}

/// <summary>
/// Everything the orchestrator knows about one coding agent's attempt at one work item.
/// </summary>
/// <param name="IssueNumber">The GitHub issue.</param>
/// <param name="WorkItemKey">The plan key, for traceability back to the plan.</param>
/// <param name="BranchName">The branch the agent committed to.</param>
/// <param name="WorktreePath">Where the agent worked.</param>
/// <param name="SessionId">The Copilot session id, so the run can be replayed or resumed.</param>
/// <param name="Model">The model that produced the code, for the transparency note in the PR (AI-009).</param>
/// <param name="Attempts">Each attempt, including repair rounds.</param>
/// <param name="Report">The agent's own final report.</param>
/// <param name="Validation">The authoritative validation result.</param>
/// <param name="Diff">Unified diff produced, used by the reviewer agent.</param>
/// <param name="Duration">Total wall-clock duration including repairs.</param>
/// <param name="TranscriptPath">Where the full transcript was persisted (AI-006).</param>
public sealed record ImplementationOutcome(
    int IssueNumber,
    string WorkItemKey,
    string BranchName,
    string WorktreePath,
    string? SessionId,
    string Model,
    int Attempts,
    AgentReport Report,
    Validation.ValidationReport Validation,
    string Diff,
    TimeSpan Duration,
    string TranscriptPath)
{
    /// <summary>Gets a value indicating whether a pull request may be opened for this outcome.</summary>
    [JsonIgnore]
    public bool IsPullRequestReady =>
        Validation.Passed && Report.Status is ImplementationStatus.Completed or ImplementationStatus.Partial;
}
