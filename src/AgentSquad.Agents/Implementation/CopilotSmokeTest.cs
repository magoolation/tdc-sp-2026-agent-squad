using System.Diagnostics;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Agents.Implementation;

/// <summary>The outcome of exercising the Copilot coding agent end to end.</summary>
/// <param name="Reachable">Whether the runtime started and produced a result.</param>
/// <param name="Model">The model the agent used.</param>
/// <param name="Duration">How long the whole exercise took.</param>
/// <param name="FileWasWritten">Whether the file the agent was asked to create actually exists.</param>
/// <param name="ReportParsed">Whether the agent emitted a parseable <c>AGENT_REPORT</c>.</param>
/// <param name="DeniedActions">Actions the permission policy refused.</param>
/// <param name="Detail">A short human-readable outcome, or the failure message.</param>
public sealed record CopilotProbe(
    bool Reachable,
    string Model,
    TimeSpan Duration,
    bool FileWasWritten,
    bool ReportParsed,
    IReadOnlyList<string> DeniedActions,
    string Detail);

/// <summary>
/// Exercises the whole GitHub Copilot path in a throwaway directory.
/// </summary>
/// <remarks>
/// <para>
/// This is the riskiest integration in the factory and the last one to run in a normal
/// execution — roughly ten minutes of planning happens before a coding agent is ever
/// started. Discovering a broken runtime there costs the whole run, and on stage it costs
/// the talk. So it is testable on its own, in seconds.
/// </para>
/// <para>
/// It exercises the real thing: starting the runtime, the permission policy, streaming, and
/// the report contract. The only thing it fakes is the work item.
/// </para>
/// </remarks>
public sealed partial class CopilotSmokeTest(
    IOptions<CopilotOptions> copilotOptions,
    IOptions<SquadOptions> squadOptions,
    ILoggerFactory loggerFactory,
    ILogger<CopilotSmokeTest> logger)
{
    private readonly CopilotOptions _copilot = copilotOptions.Value;
    private readonly SquadOptions _squad = squadOptions.Value;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILogger<CopilotSmokeTest> _logger = logger;

    /// <summary>
    /// Asks a real coding agent to create one small file in a scratch directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened.</returns>
    public async Task<CopilotProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        string root = Path.Combine(_squad.WorkRoot, "probe", $"copilot-{Guid.NewGuid():N}"[..20]);
        string artifacts = Path.Combine(root, ".artifacts");
        string copilotHome = Path.Combine(root, ".copilot");

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(artifacts);

        var worktree = new AgentWorktree(IssueNumber: 0, Branch: "probe", Path: root, ArtifactsPath: artifacts);

        try
        {
            LogStarting(_copilot.Model, root);

            await using CopilotCodingAgent agent = await CopilotCodingAgent.StartAsync(
                worktree,
                _copilot,
                copilotHome,
                TimeSpan.FromMinutes(3),
                _loggerFactory,
                cancellationToken);

            var assignment = new CodingAssignment(
                worktree,
                ProbeItem,
                IssueNumber: 0,
                IssueBody: ProbeIssueBody,
                Attempt: 1,
                PreviousFailure: null,
                PreviousFindings: [],
                SessionId: Guid.NewGuid());

            CodingAttempt attempt = await agent.ImplementAsync(
                assignment,
                _ => ValueTask.CompletedTask,
                cancellationToken);

            stopwatch.Stop();

            bool fileWritten = File.Exists(Path.Combine(root, "PROBE.md"));
            bool reportParsed = attempt.Report.Status != ImplementationStatus.Partial ||
                                attempt.Report.Risks.Count == 0;

            string detail = attempt.Report.Status == ImplementationStatus.Failed
                ? attempt.Report.BlockedReason ?? attempt.Report.Summary
                : $"{attempt.Report.Status}: {Truncate(attempt.Report.Summary, 90)} ({attempt.ToolCalls} tool calls)";

            LogFinished(attempt.Report.Status, fileWritten, stopwatch.Elapsed.TotalSeconds);

            return new CopilotProbe(
                Reachable: attempt.Report.Status != ImplementationStatus.Failed,
                attempt.Model,
                stopwatch.Elapsed,
                fileWritten,
                reportParsed,
                attempt.DeniedActions,
                detail);
        }
#pragma warning disable CA1031 // A probe reports every failure as a result; it must never throw.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            stopwatch.Stop();
            LogFailed(ex);

            return new CopilotProbe(false, _copilot.Model, stopwatch.Elapsed, false, false, [], Explain(ex));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string Explain(Exception exception)
    {
        string message = exception.Message;

        if (message.Contains("not a valid Win32", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
        {
            return "O runtime do Copilot não foi encontrado. Compile AgentSquad.Agents (os targets do " +
                   "GitHub.Copilot.SDK baixam o binário), ou aponte Copilot:RuntimePath para um copilot instalado.";
        }

        if (message.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return "O Copilot não está autenticado. Rode 'copilot login', ou exporte GH_TOKEN com a permissão " +
                   "'Copilot Requests'.";
        }

        if (message.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return $"O modelo '{message}'. Confira Copilot:Model contra 'copilot --help'.";
        }

        return Truncate(message, 200);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover probe directory is harmless.
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private const string ProbeIssueBody = """
        ## Objetivo

        Criar um arquivo `PROBE.md` na raiz do worktree.

        ## Escopo — arquivos previstos

        | Arquivo | Ação |
        |---|---|
        | `PROBE.md` | criar |

        **Fora de escopo:** qualquer outro arquivo.

        ## Critérios de aceite

        - [ ] `PROBE.md` existe na raiz e contém exatamente a linha `agent squad probe ok`

        ## Notas de implementação

        Não rode build nem testes: não há projeto aqui. Não use git. Apenas crie o arquivo
        e emita o relatório final.
        """;

    private static readonly WorkItem ProbeItem = new(
        Key: "PROBE",
        Title: "create the probe file",
        Goal: "Provar que o agente de codificação consegue escrever um arquivo.",
        Context: "Diagnóstico da fábrica. Não há projeto nem código neste diretório.",
        Files: [new PlannedFile("PROBE.md", FileAction.Create)],
        OutOfScope: ["qualquer outro arquivo"],
        AcceptanceCriteria: ["PROBE.md existe e contém a linha 'agent squad probe ok'"],
        ImplementationNotes: ["Não rode build nem testes.", "Não use git."],
        RequirementIds: [],
        DependsOn: [],
        Wave: 1,
        Area: "docs",
        Size: WorkItemSize.Small,
        NeedsHuman: false);

    [LoggerMessage(Level = LogLevel.Information, Message = "Probing the Copilot coding agent with {Model} in {Path}")]
    private partial void LogStarting(string model, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copilot probe finished: {Status}, file written={FileWritten}, {Seconds:F0}s")]
    private partial void LogFinished(ImplementationStatus status, bool fileWritten, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Copilot probe failed")]
    private partial void LogFailed(Exception exception);
}
