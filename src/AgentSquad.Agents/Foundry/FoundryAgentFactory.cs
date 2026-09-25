using AgentSquad.Core.Configuration;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Agents.Foundry;

/// <summary>
/// Which model tier an agent needs.
/// </summary>
/// <remarks>
/// Deliberately a small, meaningful set rather than a free-form model name at every call
/// site: it keeps model choice a configuration decision and makes the cost profile of the
/// pipeline legible in one place.
/// </remarks>
public enum ModelTier
{
    /// <summary>Reasoning-heavy work: requirements analysis and planning.</summary>
    Planning = 0,

    /// <summary>High-volume structured work: critique, rendering, classification.</summary>
    Workhorse = 1,

    /// <summary>Reading and judging code diffs.</summary>
    Review = 2,
}

/// <summary>
/// Builds Microsoft Agent Framework agents backed by Microsoft Foundry.
/// </summary>
/// <remarks>
/// <para>
/// Authentication is Microsoft Entra ID through <see cref="DefaultAzureCredential"/>
/// (SEC-002). There is no key path: the provisioned Foundry account sets
/// <c>disableLocalAuth</c>, so one would not work.
/// </para>
/// <para>
/// Every agent is wrapped in OpenTelemetry middleware at the agent layer, which produces
/// correctly nested <c>invoke_agent</c>, <c>chat</c> and <c>execute_tool</c> spans. The
/// underlying chat client is deliberately left uninstrumented: instrumenting both layers
/// duplicates every span.
/// </para>
/// </remarks>
public sealed partial class FoundryAgentFactory : IDisposable
{
    /// <summary>
    /// The OpenTelemetry source and meter name for every agent the factory produces.
    /// </summary>
    /// <remarks>
    /// The meter name is always identical to the activity source name, so a host must
    /// register this string with both <c>AddSource</c> and <c>AddMeter</c>.
    /// </remarks>
    public const string ActivitySourceName = "AgentSquad.Agents";

    private readonly FoundryOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FoundryAgentFactory> _logger;
    private readonly Lazy<AIProjectClient> _projectClient;

    /// <summary>Initializes a new instance of the <see cref="FoundryAgentFactory"/> class.</summary>
    /// <param name="options">Foundry options.</param>
    /// <param name="loggerFactory">Logger factory handed to each agent.</param>
    /// <param name="logger">Logger.</param>
    public FoundryAgentFactory(
        IOptions<FoundryOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<FoundryAgentFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;

        _projectClient = new Lazy<AIProjectClient>(CreateProjectClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the Foundry project client, created on first use.</summary>
    public AIProjectClient ProjectClient => _projectClient.Value;

    /// <summary>
    /// Creates an agent.
    /// </summary>
    /// <param name="name">
    /// Agent name. Foundry validates it against <c>^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$</c>,
    /// so it must be alphanumeric with interior hyphens only.
    /// </param>
    /// <param name="instructions">The system instructions.</param>
    /// <param name="tier">Which model tier to use.</param>
    /// <param name="description">A one-line description, used when the agent is exposed as a tool.</param>
    /// <param name="tools">Function tools the agent may call.</param>
    /// <returns>An instrumented agent.</returns>
    public AIAgent Create(
        string name,
        string instructions,
        ModelTier tier,
        string? description = null,
        IList<AITool>? tools = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);

        string model = ResolveModel(tier);

        ChatClientAgent agent = ProjectClient.AsAIAgent(
            model: model,
            instructions: instructions,
            name: name,
            description: description,
            tools: tools,
            loggerFactory: _loggerFactory);

        LogAgentCreated(name, model, tier);

        return agent
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: ActivitySourceName,
                configure: telemetry => telemetry.EnableSensitiveData = _options.CaptureMessageContent)
            .Build();
    }

    /// <summary>Gets the deployment name behind a tier, for display and for the pull-request transparency note.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The deployment name.</returns>
    public string ResolveModel(ModelTier tier) => tier switch
    {
        ModelTier.Planning => _options.PlanningModel,
        ModelTier.Review => _options.ReviewModel,
        _ => _options.WorkhorseModel,
    };

    /// <summary>
    /// Builds the chat options used for every structured-output call.
    /// </summary>
    /// <remarks>
    /// Temperature is sent only when it is explicitly configured. Reasoning models reject
    /// the parameter with an HTTP 400, so sending it unconditionally would couple the
    /// sampling setting to the model choice and break the pipeline the moment someone
    /// swaps in a stronger deployment. See <see cref="FoundryOptions.Temperature"/>.
    /// </remarks>
    /// <returns>Chat options.</returns>
    public ChatOptions StructuredOptions()
    {
        var options = new ChatOptions();

        if (_options.Temperature is { } temperature)
        {
            options.Temperature = (float)temperature;
        }

        return options;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_projectClient.IsValueCreated && _projectClient.Value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private AIProjectClient CreateProjectClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ProjectEndpoint))
        {
            throw new InvalidOperationException(
                "Foundry:ProjectEndpoint is not configured. Run 'azd up' and copy the projectEndpoint output.");
        }

        LogConnecting(_options.ProjectEndpoint);

        // The parameter is tokenProvider, typed AuthenticationTokenProvider.
        // TokenCredential derives from it, so DefaultAzureCredential binds directly.
        return new AIProjectClient(new Uri(_options.ProjectEndpoint), new DefaultAzureCredential());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to Microsoft Foundry at {Endpoint} with Entra ID")]
    private partial void LogConnecting(string endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {Name} created on {Model} ({Tier})")]
    private partial void LogAgentCreated(string name, string model, ModelTier tier);
}
