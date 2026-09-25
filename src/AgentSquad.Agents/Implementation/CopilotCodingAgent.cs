using System.Diagnostics;
using System.Text;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Implementation;
using AgentSquad.Tools.Processes;
using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentSquad.Agents.Implementation;

/// <summary>
/// Implements a work item by driving the GitHub Copilot SDK inside one isolated worktree.
/// </summary>
/// <remarks>
/// <para>
/// The Copilot client is exposed to the rest of the factory as a Microsoft Agent Framework
/// <see cref="AIAgent"/>, so the coding agent composes with the same middleware, telemetry
/// and session model as the planning and review agents.
/// </para>
/// <para>
/// Each instance owns a private <c>COPILOT_HOME</c>. Without that, concurrent agents share
/// one session store, one permission-approval file and one log directory, and their state
/// interferes.
/// </para>
/// </remarks>
public sealed partial class CopilotCodingAgent : ICodingAgent
{
    private readonly CopilotClient _client;
    private readonly AgentWorktree _worktree;
    private readonly CopilotOptions _options;
    private readonly CopilotPermissionPolicy _policy;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private int _toolCalls;

    private CopilotCodingAgent(
        CopilotClient client,
        AgentWorktree worktree,
        CopilotOptions options,
        CopilotPermissionPolicy policy,
        TimeSpan timeout,
        ILogger logger)
    {
        _client = client;
        _worktree = worktree;
        _options = options;
        _policy = policy;
        _timeout = timeout;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => $"copilot/{_options.Model}";

    /// <summary>
    /// Starts a Copilot runtime bound to one worktree.
    /// </summary>
    /// <param name="worktree">The worktree the agent is confined to.</param>
    /// <param name="options">Copilot options.</param>
    /// <param name="copilotHome">Private configuration and session directory for this agent.</param>
    /// <param name="timeout">Time budget for a single attempt.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A started agent.</returns>
    public static async Task<CopilotCodingAgent> StartAsync(
        AgentWorktree worktree,
        CopilotOptions options,
        string copilotHome,
        TimeSpan timeout,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Directory.CreateDirectory(copilotHome);

        ILogger logger = loggerFactory.CreateLogger<CopilotCodingAgent>();

        string runtime = ResolveRuntimePath(options);

        var clientOptions = new CopilotClientOptions
        {
            WorkingDirectory = worktree.Path,

            // Becomes COPILOT_HOME for this runtime: its own session store, permission
            // approvals and logs, isolated from every sibling agent.
            BaseDirectory = copilotHome,
            UseLoggedInUser = true,
            Logger = logger,

            // Always an explicit stdio connection to a real copilot binary, never the SDK's
            // bundled-runtime path. That path looks for a `copilot-runtime.exe` wrapper
            // beside a `runtime.node`, and no released CLI ships that layout — the published
            // packages contain a single `copilot.exe`. Letting the SDK choose fails at the
            // first call with "Copilot runtime wrapper not found".
            Connection = RuntimeConnection.ForStdio(runtime),
        };

        var client = new CopilotClient(clientOptions);
        await client.StartAsync(cancellationToken);

        var policy = new CopilotPermissionPolicy(options, worktree.Path, worktree.ArtifactsPath, logger);

        return new CopilotCodingAgent(client, worktree, options, policy, timeout, logger);
    }

    /// <inheritdoc />
    public async Task<CodingAttempt> ImplementAsync(
        CodingAssignment assignment,
        Func<string, ValueTask> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(progress);

        var stopwatch = Stopwatch.StartNew();
        var transcript = new StringBuilder();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_timeout);

        try
        {
            AIAgent agent = CreateAgent(assignment);
            string prompt = CopilotPromptBuilder.Build(assignment);

            LogStarting(assignment.IssueNumber, assignment.Attempt, _options.Model);

            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(prompt, cancellationToken: budget.Token))
            {
                string text = update.Text;

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                transcript.Append(text);
                await progress(text);
            }

            string output = transcript.ToString();
            AgentReport report = AgentReportParser.Parse(output, assignment.IssueNumber);

            LogFinished(assignment.IssueNumber, report.Status, stopwatch.Elapsed.TotalSeconds);

            return new CodingAttempt(
                report,
                output,
                assignment.SessionId.ToString(),
                _options.Model,
                Volatile.Read(ref _toolCalls),
                stopwatch.Elapsed,
                _policy.Denials);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(assignment.IssueNumber, _timeout.TotalMinutes);

            return Failure(
                assignment,
                transcript.ToString(),
                $"The agent exceeded its {_timeout.TotalMinutes:F0}-minute budget.",
                stopwatch.Elapsed);
        }
#pragma warning disable CA1031 // An agent failure must become a reportable outcome, never an unhandled exception that aborts the wave.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(ex, assignment.IssueNumber);

            return Failure(assignment, transcript.ToString(), ex.Message, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.StopAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            LogShutdownFailed(ex);
        }
        finally
        {
            await _client.DisposeAsync();
        }
    }

    /// <summary>
    /// Finds the Copilot binary to drive.
    /// </summary>
    /// <remarks>
    /// Configuration first, then the copy the SDK's build targets staged next to this
    /// assembly, then whatever is on PATH. Resolving rather than requiring configuration
    /// matters for a demo machine: the factory works out of the box if Copilot is installed
    /// at all, and says precisely what is missing when it is not.
    /// </remarks>
    /// <param name="options">Copilot options.</param>
    /// <returns>An absolute path to a Copilot executable.</returns>
    private static string ResolveRuntimePath(CopilotOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RuntimePath))
        {
            return File.Exists(options.RuntimePath)
                ? options.RuntimePath
                : throw new FileNotFoundException(
                    $"Copilot:RuntimePath aponta para '{options.RuntimePath}', que não existe.",
                    options.RuntimePath);
        }

        string executable = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
        string? assemblyDirectory = Path.GetDirectoryName(typeof(CopilotCodingAgent).Assembly.Location);

        if (assemblyDirectory is not null)
        {
            string rid = OperatingSystem.IsWindows() ? "win-x64"
                       : OperatingSystem.IsMacOS() ? "osx-x64"
                       : "linux-x64";

            string staged = Path.Combine(assemblyDirectory, "runtimes", rid, "native", executable);

            if (File.Exists(staged))
            {
                return staged;
            }
        }

        LaunchPlan onPath = ExecutableResolver.Resolve("copilot");

        if (Path.IsPathRooted(onPath.FileName) && File.Exists(onPath.FileName))
        {
            return onPath.FileName;
        }

        throw new FileNotFoundException(
            "Nenhum executável do GitHub Copilot foi encontrado. Instale com " +
            "'npm install -g @github/copilot' ou 'winget install GitHub.Copilot', " +
            "ou aponte Copilot:RuntimePath para um binário existente.",
            executable);
    }

    private AIAgent CreateAgent(CodingAssignment assignment)
    {
        var sessionConfig = new SessionConfig
        {
            // A real UUID: the runtime rejects hand-written non-UUID identifiers. Reusing it
            // across repair attempts keeps the agent's own context from the previous round.
            SessionId = assignment.SessionId.ToString(),
            ClientName = "AgentSquad",
            Model = _options.Model,
            ReasoningEffort = _options.ReasoningEffort,
            WorkingDirectory = _worktree.Path,
            Streaming = _options.Streaming,

            // The repository's own governance, loaded as first-class configuration rather
            // than pasted into the prompt: skills, path-scoped instructions, AGENTS.md.
            SkillDirectories = [.. _options.SkillDirectories.Select(d => Path.Combine(_worktree.Path, d))],
            InstructionDirectories = [.. _options.InstructionDirectories.Select(d => Path.Combine(_worktree.Path, d))],
            OrganizationCustomInstructions = CopilotPromptBuilder.OrganizationInstructions,

            OnPermissionRequest = _policy.EvaluateAsync,
            OnEvent = OnSessionEvent,
        };

        if (!_options.AllowAskUser)
        {
            // A wave of parallel agents cannot block on interactive questions. An agent that
            // needs an answer must stop and report instead (AGENTS.md §3.10).
            sessionConfig.ExcludedTools = ["ask_user"];
        }

        return _client.AsAIAgent(
            sessionConfig,
            ownsClient: false,
            name: $"implementer-{assignment.IssueNumber}");
    }

    private void OnSessionEvent(SessionEvent sessionEvent)
    {
        if (sessionEvent is ToolExecutionStartEvent)
        {
            Interlocked.Increment(ref _toolCalls);
        }
    }

    private CodingAttempt Failure(CodingAssignment assignment, string output, string reason, TimeSpan elapsed) =>
        new(
            AgentReport.ForFailure(assignment.IssueNumber, reason),
            output,
            assignment.SessionId.ToString(),
            _options.Model,
            Volatile.Read(ref _toolCalls),
            elapsed,
            _policy.Denials);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copilot agent starting on issue #{IssueNumber} (attempt {Attempt}, model {Model})")]
    private partial void LogStarting(int issueNumber, int attempt, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copilot agent finished issue #{IssueNumber} with status {Status} in {Seconds:F0}s")]
    private partial void LogFinished(int issueNumber, ImplementationStatus status, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Copilot agent on issue #{IssueNumber} exceeded its {Minutes:F0}-minute budget")]
    private partial void LogTimedOut(int issueNumber, double minutes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Copilot agent on issue #{IssueNumber} failed")]
    private partial void LogFailed(Exception exception, int issueNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Copilot runtime did not shut down cleanly")]
    private partial void LogShutdownFailed(Exception exception);
}

