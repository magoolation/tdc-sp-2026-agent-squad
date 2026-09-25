using System.Globalization;
using AgentSquad.Core.Events;
using AgentSquad.Core.Runs;
using Spectre.Console;

namespace AgentSquad.Cli.Console;

/// <summary>
/// Renders a run's event stream to the terminal as it happens.
/// </summary>
/// <remarks>
/// It consumes exactly the same <see cref="IRunEventStream"/> the web dashboard consumes,
/// so the terminal and the browser can never tell different stories about a run.
/// </remarks>
public sealed class ConsoleRunRenderer(IAnsiConsole console)
{
    private readonly IAnsiConsole _console = console;

    /// <summary>
    /// Follows a run until it ends or the token is cancelled.
    /// </summary>
    /// <param name="stream">The event stream.</param>
    /// <param name="runId">The run to follow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the run's stream ends.</returns>
    public async Task FollowAsync(IRunEventStream stream, RunId runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        RunPhase? currentPhase = null;

        await foreach (RunEvent runEvent in stream.SubscribeAsync(runId, cancellationToken))
        {
            if (currentPhase != runEvent.Phase)
            {
                currentPhase = runEvent.Phase;
                _console.WriteLine();
                _console.Write(new Rule($"[bold blue]{PhaseLabel(runEvent.Phase)}[/]").LeftJustified());
            }

            Render(runEvent);
        }
    }

    private void Render(RunEvent runEvent)
    {
        string time = runEvent.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string issue = runEvent.IssueNumber is { } number
            ? $"[dim]#{number.ToString(CultureInfo.InvariantCulture)}[/] "
            : string.Empty;

        (string icon, string colour) = runEvent.Level switch
        {
            RunEventLevel.Milestone => ("◆", "green"),
            RunEventLevel.Error => ("✖", "red"),
            RunEventLevel.Warning => ("▲", "yellow"),
            RunEventLevel.Trace => ("·", "grey"),
            _ => ("•", "white"),
        };

        _console.MarkupLine(
            $"[dim]{time}[/] [{colour}]{icon}[/] {issue}[dim]{Markup.Escape(runEvent.Source)}[/]  " +
            $"[{colour}]{Markup.Escape(runEvent.Message)}[/]");

        if (runEvent.Data?.TryGetValue("url", out string? url) == true)
        {
            _console.MarkupLine($"           [link={url}]{Markup.Escape(url)}[/]");
        }
    }

    private static string PhaseLabel(RunPhase phase) => phase switch
    {
        RunPhase.Intake => "1. Intake — entendendo o pedido e o repositório",
        RunPhase.Requirements => "2. Requisitos — o que precisa ser verdade",
        RunPhase.Clarification => "3. Esclarecimento — perguntas para o humano",
        RunPhase.Planning => "4. Planejamento — work items e ondas de paralelismo",
        RunPhase.PlanApproval => "5. Aprovação — a decisão é humana",
        RunPhase.Publishing => "6. Publicação — issues no GitHub",
        RunPhase.Implementation => "7. Implementação — agentes em paralelo",
        RunPhase.Validation => "8. Validação — linters, analisadores e testes",
        RunPhase.Review => "9. Revisão — leitura automatizada do diff",
        RunPhase.Delivery => "10. Entrega — pull requests",
        _ => "Concluído",
    };
}
