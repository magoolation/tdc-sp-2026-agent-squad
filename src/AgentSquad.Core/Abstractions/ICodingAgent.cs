using AgentSquad.Core.Implementation;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Validation;

namespace AgentSquad.Core.Abstractions;

/// <summary>
/// The brief handed to a coding agent for one attempt at one work item.
/// </summary>
/// <param name="Worktree">The isolated checkout to work in.</param>
/// <param name="Item">The work item being implemented.</param>
/// <param name="IssueNumber">The GitHub issue number.</param>
/// <param name="IssueBody">The rendered issue body, which is the agent's specification.</param>
/// <param name="Attempt">One-based attempt number. Attempt 1 is the initial implementation.</param>
/// <param name="PreviousFailure">
/// The validation report from the previous attempt, when this is a repair round.
/// Its diagnostics are fed back verbatim so the agent fixes the real error.
/// </param>
/// <param name="PreviousFindings">Blocking review findings from the previous attempt, when repairing.</param>
/// <param name="SessionId">
/// Stable session identifier. Must be a real UUID: the Copilot runtime rejects
/// hand-written non-UUID identifiers. Reusing it across attempts preserves context.
/// </param>
public sealed record CodingAssignment(
    AgentWorktree Worktree,
    WorkItem Item,
    int IssueNumber,
    string IssueBody,
    int Attempt,
    ValidationReport? PreviousFailure,
    IReadOnlyList<Review.ReviewFinding> PreviousFindings,
    Guid SessionId);

/// <summary>
/// The raw outcome of one coding-agent attempt, before validation.
/// </summary>
/// <param name="Report">The agent's own structured report, parsed from its output.</param>
/// <param name="RawOutput">Everything the agent said, for the transcript.</param>
/// <param name="SessionId">The provider session id, for replay and resume.</param>
/// <param name="Model">The model that produced the work.</param>
/// <param name="ToolCalls">Number of tool invocations, for the cost and behaviour record.</param>
/// <param name="Duration">Wall-clock duration of the attempt.</param>
/// <param name="DeniedActions">Actions the permission policy refused, for the security audit.</param>
public sealed record CodingAttempt(
    AgentReport Report,
    string RawOutput,
    string? SessionId,
    string Model,
    int ToolCalls,
    TimeSpan Duration,
    IReadOnlyList<string> DeniedActions);

/// <summary>
/// Something that can implement a work item inside a worktree.
/// </summary>
/// <remarks>
/// The production implementation drives the GitHub Copilot SDK. The abstraction exists
/// so the orchestration, the validation gate and the delivery pipeline can be tested
/// without invoking a model (TST-005), not to support swapping vendors at runtime.
/// </remarks>
public interface ICodingAgent : IAsyncDisposable
{
    /// <summary>Gets the display name used in events and in the pull-request transparency note.</summary>
    string Name { get; }

    /// <summary>
    /// Implements one work item.
    /// </summary>
    /// <param name="assignment">What to implement and where.</param>
    /// <param name="progress">Receives a line of agent output as it is produced, for live display.</param>
    /// <param name="cancellationToken">Cancels the attempt. Implementations must enforce their own timeout too.</param>
    /// <returns>The attempt outcome. Never throws for an agent-level failure; returns a failed report instead.</returns>
    Task<CodingAttempt> ImplementAsync(
        CodingAssignment assignment,
        Func<string, ValueTask> progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Creates a coding agent bound to a specific worktree.
/// </summary>
/// <remarks>
/// A factory rather than a singleton because each agent needs its own working directory,
/// its own <c>COPILOT_HOME</c> and its own permission policy scoped to one worktree (AI-004).
/// </remarks>
public interface ICodingAgentFactory
{
    /// <summary>Creates an agent for one worktree.</summary>
    /// <param name="worktree">The worktree the agent is confined to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A started agent. The caller disposes it.</returns>
    Task<ICodingAgent> CreateAsync(AgentWorktree worktree, CancellationToken cancellationToken);
}
