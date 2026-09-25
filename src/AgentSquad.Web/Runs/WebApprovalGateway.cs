using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Requirements;

namespace AgentSquad.Web.Runs;

/// <summary>Something the run is waiting for a human to decide.</summary>
public abstract record PendingDecision
{
    /// <summary>Gets when the run started waiting.</summary>
    public required DateTimeOffset RequestedAt { get; init; }
}

/// <summary>The run is waiting for answers to the requirements analyst's questions.</summary>
/// <param name="Questions">The questions to answer.</param>
public sealed record PendingQuestions(IReadOnlyList<OpenQuestion> Questions) : PendingDecision;

/// <summary>The run is waiting for a human to approve, revise or abort the plan.</summary>
/// <param name="Plan">The plan under review.</param>
/// <param name="Issues">What the deterministic validator found.</param>
public sealed record PendingPlan(DeliveryPlan Plan, IReadOnlyList<PlanIssue> Issues) : PendingDecision;

/// <summary>
/// Human-in-the-loop for the web dashboard.
/// </summary>
/// <remarks>
/// <para>
/// The orchestrator calls into this from a background thread and awaits; the Blazor circuit
/// renders whatever is pending and completes the awaited task when someone clicks. The two
/// never share a thread, which is why the handoff is a
/// <see cref="TaskCompletionSource{TResult}"/> rather than anything simpler.
/// </para>
/// <para>
/// There is no timeout on purpose. A run parked on an unanswered question is visible and
/// recoverable; a run that silently proceeded on a default nobody saw is neither. The
/// console front end makes the opposite trade for unattended use, and says so.
/// </para>
/// </remarks>
public sealed class WebApprovalGateway(TimeProvider timeProvider) : IApprovalGateway
{
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly Lock _guard = new();

    private PendingDecision? _pending;
    private TaskCompletionSource<IReadOnlyList<QuestionAnswer>>? _questionsCompletion;
    private TaskCompletionSource<PlanDecision>? _planCompletion;

    /// <summary>Raised when something starts or stops waiting, so the UI can refresh.</summary>
    public event Action? Changed;

    /// <summary>Gets what the run is currently waiting for, or <see langword="null"/>.</summary>
    public PendingDecision? Pending
    {
        get
        {
            lock (_guard)
            {
                return _pending;
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QuestionAnswer>> AskAsync(
        IReadOnlyList<OpenQuestion> questions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<QuestionAnswer>>([]);
        }

        TaskCompletionSource<IReadOnlyList<QuestionAnswer>> completion;

        lock (_guard)
        {
            completion = new TaskCompletionSource<IReadOnlyList<QuestionAnswer>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _questionsCompletion = completion;
            _pending = new PendingQuestions(questions) { RequestedAt = _timeProvider.GetUtcNow() };
        }

        Changed?.Invoke();

        // A cancelled run must not leave the orchestrator awaiting forever.
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        return completion.Task;
    }

    /// <inheritdoc />
    public Task<PlanDecision> ApprovePlanAsync(
        DeliveryPlan plan,
        IReadOnlyList<PlanIssue> planIssues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(planIssues);

        TaskCompletionSource<PlanDecision> completion;

        lock (_guard)
        {
            completion = new TaskCompletionSource<PlanDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

            _planCompletion = completion;
            _pending = new PendingPlan(plan, planIssues) { RequestedAt = _timeProvider.GetUtcNow() };
        }

        Changed?.Invoke();

        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        return completion.Task;
    }

    /// <summary>
    /// Submits the human's answers to the pending questions.
    /// </summary>
    /// <param name="answers">One answer per question, keyed by question id.</param>
    /// <param name="answeredBy">Who answered, for the audit trail.</param>
    public void SubmitAnswers(IReadOnlyDictionary<string, string> answers, string answeredBy)
    {
        ArgumentNullException.ThrowIfNull(answers);

        TaskCompletionSource<IReadOnlyList<QuestionAnswer>>? completion;
        PendingQuestions? pending;

        lock (_guard)
        {
            completion = _questionsCompletion;
            pending = _pending as PendingQuestions;
            _questionsCompletion = null;
            _pending = null;
        }

        if (completion is null || pending is null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        // A question left blank falls back to the default it declared, rather than being
        // sent to the analyst as an empty string.
        IReadOnlyList<QuestionAnswer> result =
        [
            .. pending.Questions.Select(question => new QuestionAnswer(
                question.Id,
                answers.TryGetValue(question.Id, out string? value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : question.DefaultIfUnanswered,
                answeredBy,
                now)),
        ];

        completion.TrySetResult(result);
        Changed?.Invoke();
    }

    /// <summary>
    /// Submits the human's decision about the plan.
    /// </summary>
    /// <param name="verdict">Approve, revise or abort.</param>
    /// <param name="feedback">What to change, when revising.</param>
    /// <param name="decidedBy">Who decided, for the audit trail.</param>
    public void SubmitPlanDecision(PlanVerdict verdict, string? feedback, string decidedBy)
    {
        TaskCompletionSource<PlanDecision>? completion;

        lock (_guard)
        {
            completion = _planCompletion;
            _planCompletion = null;
            _pending = null;
        }

        completion?.TrySetResult(new PlanDecision(verdict, feedback, decidedBy, _timeProvider.GetUtcNow()));
        Changed?.Invoke();
    }
}
