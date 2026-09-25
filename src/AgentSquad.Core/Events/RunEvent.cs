using System.Text.Json.Serialization;
using AgentSquad.Core.Runs;

namespace AgentSquad.Core.Events;

/// <summary>The phases a run moves through.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunPhase>))]
public enum RunPhase
{
    /// <summary>Reading the request, the transcript and the target repository.</summary>
    Intake = 0,

    /// <summary>Turning the request into verifiable requirements.</summary>
    Requirements = 1,

    /// <summary>Waiting for a human to answer open questions.</summary>
    Clarification = 2,

    /// <summary>Producing and critiquing the delivery plan.</summary>
    Planning = 3,

    /// <summary>Waiting for a human to approve the plan.</summary>
    PlanApproval = 4,

    /// <summary>Creating labels, milestone and issues on GitHub.</summary>
    Publishing = 5,

    /// <summary>Coding agents working in parallel worktrees.</summary>
    Implementation = 6,

    /// <summary>Running linters, analyzers and tests.</summary>
    Validation = 7,

    /// <summary>Automated code review of the diffs.</summary>
    Review = 8,

    /// <summary>Pushing branches and opening pull requests.</summary>
    Delivery = 9,

    /// <summary>The run has ended.</summary>
    Completed = 10,
}

/// <summary>How prominent an event is in the console and the web dashboard.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunEventLevel>))]
public enum RunEventLevel
{
    /// <summary>Fine-grained detail: agent tokens, individual tool calls.</summary>
    Trace = 0,

    /// <summary>Normal progress.</summary>
    Info = 1,

    /// <summary>Something recoverable went wrong.</summary>
    Warning = 2,

    /// <summary>An operation failed.</summary>
    Error = 3,

    /// <summary>A milestone worth highlighting on stage.</summary>
    Milestone = 4,
}

/// <summary>
/// One observable thing that happened during a run.
/// </summary>
/// <remarks>
/// This is the single stream that feeds the console renderer, the Blazor dashboard
/// and the on-disk journal. Every consumer subscribes to the same sequence, so the
/// terminal and the web UI can never disagree about what happened.
/// </remarks>
/// <param name="RunId">The run this belongs to.</param>
/// <param name="Sequence">Monotonic, gap-free ordinal within the run.</param>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Phase">Which phase the run was in.</param>
/// <param name="Level">How prominent it is.</param>
/// <param name="Source">Who emitted it: an agent name, <c>orchestrator</c>, or a tool name.</param>
/// <param name="Message">Human-readable text, already localized for the audience.</param>
/// <param name="IssueNumber">The GitHub issue this concerns, when the event is per-item.</param>
/// <param name="Data">Optional structured payload for the dashboard.</param>
public sealed record RunEvent(
    string RunId,
    long Sequence,
    DateTimeOffset Timestamp,
    RunPhase Phase,
    RunEventLevel Level,
    string Source,
    string Message,
    int? IssueNumber = null,
    IReadOnlyDictionary<string, string>? Data = null)
{
    /// <summary>Creates an informational event.</summary>
    /// <param name="runId">Owning run.</param>
    /// <param name="sequence">Ordinal within the run.</param>
    /// <param name="timestamp">When it happened.</param>
    /// <param name="phase">Current phase.</param>
    /// <param name="source">Emitter.</param>
    /// <param name="message">Text for the operator.</param>
    /// <param name="issueNumber">Related issue, if any.</param>
    /// <returns>The event.</returns>
    public static RunEvent Info(
        RunId runId,
        long sequence,
        DateTimeOffset timestamp,
        RunPhase phase,
        string source,
        string message,
        int? issueNumber = null) =>
        new(runId.Value, sequence, timestamp, phase, RunEventLevel.Info, source, message, issueNumber);
}

/// <summary>
/// Publishes run events to every interested consumer.
/// </summary>
public interface IRunEventPublisher
{
    /// <summary>Publishes one event.</summary>
    /// <param name="phase">Current phase.</param>
    /// <param name="level">Prominence.</param>
    /// <param name="source">Emitter name.</param>
    /// <param name="message">Human-readable text.</param>
    /// <param name="issueNumber">Related issue, if any.</param>
    /// <param name="data">Optional structured payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the event has been accepted for delivery.</returns>
    ValueTask PublishAsync(
        RunPhase phase,
        RunEventLevel level,
        string source,
        string message,
        int? issueNumber = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Streams the events of a run to a subscriber.
/// </summary>
public interface IRunEventStream
{
    /// <summary>
    /// Subscribes to a run's events, replaying what already happened and then following live.
    /// </summary>
    /// <param name="runId">The run to watch.</param>
    /// <param name="cancellationToken">Stops the subscription.</param>
    /// <returns>An asynchronous sequence that ends when the run ends or the token is cancelled.</returns>
    IAsyncEnumerable<RunEvent> SubscribeAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>Gets the identifiers of runs the process currently knows about, newest first.</summary>
    /// <returns>Run identifiers.</returns>
    IReadOnlyList<RunId> KnownRuns();
}
