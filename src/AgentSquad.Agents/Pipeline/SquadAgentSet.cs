using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentSquad.Agents.Foundry;
using AgentSquad.Agents.Prompts;
using AgentSquad.Core.Intake;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Requirements;
using AgentSquad.Core.Review;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentSquad.Agents.Pipeline;

/// <summary>What the transcript analyst extracts from a meeting.</summary>
/// <param name="Title">A short name for what the meeting was about.</param>
/// <param name="Participants">Who spoke, and their role where stated.</param>
/// <param name="Decisions">What was settled, and by whom.</param>
/// <param name="StatedRequirements">Requirements as they were expressed, with attribution.</param>
/// <param name="OpenItems">Explicitly deferred, and implicitly unresolved, questions.</param>
/// <param name="Constraints">Deadlines, budgets, mandated technology, compliance obligations.</param>
/// <param name="NonGoals">What somebody explicitly ruled out of scope.</param>
/// <param name="UnsharedAssumptions">Things said as "obviously" that are probably not shared.</param>
public sealed record TranscriptAnalysis(
    [property: Description("Short name for the subject of the meeting.")] string Title,
    [property: Description("Who spoke, with their role when stated.")] IReadOnlyList<string> Participants,
    [property: Description("What was settled, and by whom.")] IReadOnlyList<string> Decisions,
    [property: Description("Requirements as expressed, attributed to a speaker and timestamp.")] IReadOnlyList<string> StatedRequirements,
    [property: Description("Deferred and unresolved questions.")] IReadOnlyList<string> OpenItems,
    [property: Description("Deadlines, budgets, mandated technology, compliance.")] IReadOnlyList<string> Constraints,
    [property: Description("What was explicitly ruled out of scope.")] IReadOnlyList<string> NonGoals,
    [property: Description("Assumptions stated as obvious that are probably not shared.")] IReadOnlyList<string> UnsharedAssumptions);

/// <summary>The intake analyst's reading of the request and the target repository.</summary>
/// <param name="Mode">Greenfield, Brownfield or BrownfieldNewModule.</param>
/// <param name="PrimaryLanguage">Dominant language, or null for an empty repository.</param>
/// <param name="Stack">Frameworks, test libraries and tools detected.</param>
/// <param name="Conventions">Conventions worth following, each citing the file that establishes it.</param>
/// <param name="Summary">Two or three sentences a planner could act on.</param>
public sealed record IntakeAssessment(
    [property: Description("Greenfield | Brownfield | BrownfieldNewModule")] DeliveryMode Mode,
    [property: Description("Dominant language, or null when the repository is empty.")] string? PrimaryLanguage,
    [property: Description("Frameworks, test libraries and tools detected.")] IReadOnlyList<string> Stack,
    [property: Description("Conventions to follow, each citing the file that establishes it.")] IReadOnlyList<string> Conventions,
    [property: Description("Two or three sentences a planner could act on.")] string Summary);

/// <summary>The plan critic's verdict.</summary>
/// <param name="Approved">Whether the plan is executable as written.</param>
/// <param name="Summary">One paragraph on the plan's quality.</param>
/// <param name="Problems">What must change, each naming the work-item key.</param>
/// <param name="MissingWork">Requirements or criteria no work item delivers.</param>
public sealed record PlanCritique(
    [property: Description("True when the plan is executable as written.")] bool Approved,
    [property: Description("One paragraph on the plan's quality.")] string Summary,
    [property: Description("What must change, each naming the work-item key.")] IReadOnlyList<string> Problems,
    [property: Description("Requirements or criteria no work item delivers.")] IReadOnlyList<string> MissingWork);

/// <summary>
/// The Microsoft Agent Framework agents that run the orchestration pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Each agent has one job and returns schema-bound JSON. Structured output is what lets a
/// deterministic pipeline consume a model's judgement without parsing prose, and it is why
/// <see cref="PlanValidator"/> can check a plan that a model produced.
/// </para>
/// <para>
/// The agents are created once and reused. They are stateless between calls: conversation
/// state lives in an <see cref="AgentSession"/> that the caller owns, so two concurrent
/// runs never share context.
/// </para>
/// </remarks>
public sealed partial class SquadAgentSet
{
    private static readonly JsonSerializerOptions StructuredJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly FoundryAgentFactory _factory;
    private readonly ILogger<SquadAgentSet> _logger;

    /// <summary>Initializes a new instance of the <see cref="SquadAgentSet"/> class.</summary>
    /// <param name="factory">Agent factory.</param>
    /// <param name="logger">Logger.</param>
    public SquadAgentSet(FoundryAgentFactory factory, ILogger<SquadAgentSet> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
        _logger = logger;

        TranscriptAnalyst = factory.Create(
            "transcript-analyst",
            AgentPrompts.TranscriptAnalyst,
            ModelTier.Planning,
            "Extracts decisions, requirements and open items from a meeting transcript.");

        Intake = factory.Create(
            "intake-analyst",
            AgentPrompts.Intake,
            ModelTier.Workhorse,
            "Classifies the delivery and describes the target repository.");

        RequirementsAnalyst = factory.Create(
            "requirements-analyst",
            AgentPrompts.Requirements,
            ModelTier.Planning,
            "Turns a request into verifiable requirements and open questions.");

        Architect = factory.Create(
            "architect",
            AgentPrompts.Architect,
            ModelTier.Planning,
            "Produces the delivery plan: work items, dependencies and parallelism waves.");

        PlanCritic = factory.Create(
            "plan-critic",
            AgentPrompts.PlanCritic,
            ModelTier.Planning,
            "Reviews the plan before any issue is created.");

        CodeReviewer = factory.Create(
            "code-reviewer",
            AgentPrompts.CodeReviewer,
            ModelTier.Review,
            "Reviews an agent-produced diff against the engineering rules.");
    }

    /// <summary>Gets the agent that reads meeting transcripts.</summary>
    public AIAgent TranscriptAnalyst { get; }

    /// <summary>Gets the agent that classifies the delivery.</summary>
    public AIAgent Intake { get; }

    /// <summary>Gets the agent that produces requirements.</summary>
    public AIAgent RequirementsAnalyst { get; }

    /// <summary>Gets the agent that produces the plan.</summary>
    public AIAgent Architect { get; }

    /// <summary>Gets the agent that critiques the plan.</summary>
    public AIAgent PlanCritic { get; }

    /// <summary>Gets the agent that reviews diffs.</summary>
    public AIAgent CodeReviewer { get; }

    /// <summary>Gets the deployment name used for code review, for the transparency note in pull requests.</summary>
    public string ReviewModel => _factory.ResolveModel(ModelTier.Review);

    /// <summary>Gets the deployment name used for planning.</summary>
    public string PlanningModel => _factory.ResolveModel(ModelTier.Planning);

    /// <summary>
    /// Runs an agent and deserializes its schema-bound response.
    /// </summary>
    /// <typeparam name="TResult">The expected shape.</typeparam>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The user message.</param>
    /// <param name="session">Conversation state, when the call continues an exchange.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized result.</returns>
    public async Task<TResult> RunStructuredAsync<TResult>(
        AIAgent agent,
        string prompt,
        AgentSession? session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var runOptions = new ChatClientAgentRunOptions(_factory.StructuredOptions());

        AgentResponse<TResult> response = await agent.RunAsync<TResult>(
            prompt,
            session,
            StructuredJson,
            runOptions,
            cancellationToken);

        LogStructuredRun(agent.Name ?? "agent", typeof(TResult).Name, response.Usage?.TotalTokenCount ?? 0);

        return response.Result;
    }

    /// <summary>
    /// Wraps external content so it is unambiguously data rather than instruction (AI-001).
    /// </summary>
    /// <param name="label">What the content is, for example <c>TRANSCRIPT</c> or <c>DIFF</c>.</param>
    /// <param name="content">The untrusted text.</param>
    /// <returns>The delimited block.</returns>
    public static string Untrusted(string label, string content) =>
        $"<<<UNTRUSTED {label}\n{content}\nUNTRUSTED>>>";

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{AgentName} returned {ResultType} using {TokenCount} tokens")]
    private partial void LogStructuredRun(string agentName, string resultType, long tokenCount);
}

/// <summary>
/// JSON contracts for the structured agent payloads (ENG-029).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TranscriptAnalysis))]
[JsonSerializable(typeof(IntakeAssessment))]
[JsonSerializable(typeof(RequirementsDocument))]
[JsonSerializable(typeof(DeliveryPlan))]
[JsonSerializable(typeof(PlanCritique))]
[JsonSerializable(typeof(ReviewResult))]
[JsonSerializable(typeof(ProjectContext))]
public sealed partial class SquadJsonContext : JsonSerializerContext;
