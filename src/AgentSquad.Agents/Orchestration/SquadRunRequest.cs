using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Intake;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Requirements;
using AgentSquad.Core.Runs;

namespace AgentSquad.Agents.Orchestration;

/// <summary>
/// What the factory was asked to build.
/// </summary>
/// <param name="Request">The request in natural language. Empty when only a transcript is supplied.</param>
/// <param name="TranscriptPath">Path to a requirements-meeting transcript, when one is supplied.</param>
/// <param name="Unattended">
/// When <see langword="true"/>, every human decision falls back to its declared default
/// instead of waiting. Intended for rehearsal and for CI, never for a delivery that matters.
/// </param>
/// <param name="StopAfterPlan">Stop once the plan is approved, without creating issues. Useful for a dry read-through.</param>
public sealed record SquadRunRequest(
    string Request,
    string? TranscriptPath = null,
    bool Unattended = false,
    bool StopAfterPlan = false)
{
    /// <summary>Gets a value indicating whether a meeting transcript was supplied.</summary>
    public bool HasTranscript => !string.IsNullOrWhiteSpace(TranscriptPath);
}

/// <summary>Why a run ended.</summary>
public enum RunOutcome
{
    /// <summary>Every wave completed and pull requests were opened.</summary>
    Delivered = 0,

    /// <summary>The plan was produced and approved, but the run was asked to stop there.</summary>
    PlanOnly = 1,

    /// <summary>A human rejected the plan.</summary>
    Rejected = 2,

    /// <summary>The run stopped because something could not be recovered from.</summary>
    Failed = 3,

    /// <summary>The run was cancelled.</summary>
    Cancelled = 4,
}

/// <summary>
/// Everything a run produced.
/// </summary>
/// <param name="RunId">The run identifier.</param>
/// <param name="Outcome">Why the run ended.</param>
/// <param name="Context">What the intake phase established.</param>
/// <param name="Requirements">The requirements the plan was built from.</param>
/// <param name="Plan">The approved plan.</param>
/// <param name="Issues">The issues created.</param>
/// <param name="Outcomes">One entry per work item that an agent attempted.</param>
/// <param name="PullRequests">The pull requests opened.</param>
/// <param name="Duration">Total wall-clock duration.</param>
/// <param name="Message">A human-readable explanation, used when the run did not deliver.</param>
public sealed record SquadRunResult(
    RunId RunId,
    RunOutcome Outcome,
    ProjectContext? Context,
    RequirementsDocument? Requirements,
    DeliveryPlan? Plan,
    IReadOnlyList<TrackedIssue> Issues,
    IReadOnlyList<ImplementationOutcome> Outcomes,
    IReadOnlyList<PullRequestRef> PullRequests,
    TimeSpan Duration,
    string Message)
{
    /// <summary>Gets the work items that passed the validation gate.</summary>
    public IReadOnlyList<ImplementationOutcome> Succeeded =>
        [.. Outcomes.Where(o => o.Validation.Passed)];

    /// <summary>Gets the work items that did not pass.</summary>
    public IReadOnlyList<ImplementationOutcome> Failed =>
        [.. Outcomes.Where(o => !o.Validation.Passed)];
}
