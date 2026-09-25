using AgentSquad.Core.Planning;
using AgentSquad.Core.Validation;

namespace AgentSquad.Core.Abstractions;

/// <summary>
/// Runs the deterministic quality gate over a worktree.
/// </summary>
/// <remarks>
/// The gate — not the coding agent's self-assessment — decides whether a pull request
/// may be opened (AI-008). Implementations run the steps in
/// <see cref="ValidationStepKind"/> order and stop at the first blocking failure,
/// because a build failure makes every later step meaningless.
/// </remarks>
public interface IValidationGate
{
    /// <summary>
    /// Validates one worktree.
    /// </summary>
    /// <param name="worktree">The worktree to validate.</param>
    /// <param name="item">The work item, used for the scope-compliance check.</param>
    /// <param name="baseRef">Base commit-ish, used to compute the diff under review.</param>
    /// <param name="attempt">One-based repair attempt, recorded in the report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation report.</returns>
    Task<ValidationReport> ValidateAsync(
        AgentWorktree worktree,
        WorkItem item,
        string baseRef,
        int attempt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Asks a human a question and waits for the answer.
/// </summary>
/// <remarks>
/// Backed by the console in the CLI and by a form in the Blazor dashboard. Both record
/// who answered and when, because plan approval is an auditable control (AI-005, AI-006).
/// </remarks>
public interface IApprovalGateway
{
    /// <summary>
    /// Asks the human to answer the open questions raised during requirements gathering.
    /// </summary>
    /// <param name="questions">Questions to ask. Implementations should present all of them at once.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The answers. An implementation running unattended returns each question's
    /// declared default rather than blocking forever.
    /// </returns>
    Task<IReadOnlyList<Requirements.QuestionAnswer>> AskAsync(
        IReadOnlyList<Requirements.OpenQuestion> questions,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks the human to approve, reject or revise the delivery plan.
    /// </summary>
    /// <param name="plan">The plan under review.</param>
    /// <param name="planIssues">Problems the deterministic validator found, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decision.</returns>
    Task<PlanDecision> ApprovePlanAsync(
        DeliveryPlan plan,
        IReadOnlyList<PlanIssue> planIssues,
        CancellationToken cancellationToken);
}

/// <summary>What the human decided about a plan.</summary>
public enum PlanVerdict
{
    /// <summary>Publish the issues and start implementing.</summary>
    Approve = 0,

    /// <summary>Send the plan back to the architect with feedback.</summary>
    Revise = 1,

    /// <summary>Stop the run.</summary>
    Abort = 2,
}

/// <summary>
/// A human's decision about a delivery plan.
/// </summary>
/// <param name="Verdict">What to do next.</param>
/// <param name="Feedback">What to change, when revising.</param>
/// <param name="DecidedBy">Who decided, for the audit trail.</param>
/// <param name="DecidedAt">When the decision was made.</param>
public sealed record PlanDecision(
    PlanVerdict Verdict,
    string? Feedback,
    string DecidedBy,
    DateTimeOffset DecidedAt);
