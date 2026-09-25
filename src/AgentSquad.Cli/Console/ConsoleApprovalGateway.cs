using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Requirements;
using Spectre.Console;

namespace AgentSquad.Cli.Console;

/// <summary>
/// Asks the human for decisions through the terminal.
/// </summary>
/// <remarks>
/// <para>
/// This is where AI-005 becomes concrete. The factory can produce a plan on its own, but it
/// cannot publish issues or open pull requests until a person says so, and this type is the
/// only thing standing between the two.
/// </para>
/// <para>
/// When no terminal is attached — CI, a background service — every question falls back to
/// the default it declared, and the fallback is announced rather than silent.
/// </para>
/// </remarks>
public sealed class ConsoleApprovalGateway(IAnsiConsole console, TimeProvider timeProvider, bool unattended) : IApprovalGateway
{
    private readonly IAnsiConsole _console = console;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly bool _unattended = unattended;

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

        if (_unattended || !_console.Profile.Capabilities.Interactive)
        {
            return Task.FromResult(UseDefaults(questions));
        }

        var answers = new List<QuestionAnswer>(questions.Count);

        _console.WriteLine();
        _console.Write(new Rule("[yellow]Perguntas do analista de requisitos[/]").LeftJustified());
        _console.WriteLine();

        foreach (OpenQuestion question in questions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var panel = new Panel(new Markup($"[bold]{Markup.Escape(question.Question)}[/]\n\n[dim]{Markup.Escape(question.Why)}[/]"))
            {
                Header = new PanelHeader($" {question.Id}{(question.Blocking ? " · bloqueante" : string.Empty)} "),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(question.Blocking ? Color.Red : Color.Yellow),
            };

            _console.Write(panel);

            // The impact of each option is shown before the prompt, so the human is choosing
            // between consequences rather than between labels.
            Table table = new Table { Border = TableBorder.Simple }
                .AddColumn("Opção")
                .AddColumn("O que isso significa para o plano");

            foreach (QuestionOption option in question.Options)
            {
                table.AddRow(Markup.Escape(option.Label), Markup.Escape(option.Impact));
            }

            _console.Write(table);

            string[] choices = [.. question.Options.Select(o => o.Label), "Outro (digitar)"];

            string chosen = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[green]Sua resposta para {question.Id}?[/]")
                    .AddChoices(choices));

            if (chosen == "Outro (digitar)")
            {
                chosen = _console.Prompt(new TextPrompt<string>("[green]Descreva:[/]"));
            }

            answers.Add(new QuestionAnswer(question.Id, chosen, Environment.UserName, _timeProvider.GetUtcNow()));
        }

        return Task.FromResult<IReadOnlyList<QuestionAnswer>>(answers);
    }

    /// <inheritdoc />
    public Task<PlanDecision> ApprovePlanAsync(
        DeliveryPlan plan,
        IReadOnlyList<PlanIssue> planIssues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(planIssues);

        RenderPlan(plan, planIssues);

        if (_unattended || !_console.Profile.Capabilities.Interactive)
        {
            _console.MarkupLine("[yellow]Modo não interativo: o plano foi aprovado automaticamente.[/]");
            return Task.FromResult(new PlanDecision(PlanVerdict.Approve, null, "unattended", _timeProvider.GetUtcNow()));
        }

        cancellationToken.ThrowIfCancellationRequested();

        const string Approve = "Aprovar — criar as issues e disparar os agentes";
        const string Revise = "Revisar — devolver ao arquiteto com um comentário";
        const string Abort = "Abortar — encerrar a execução";

        string choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("\n[bold green]O que fazemos com este plano?[/]")
                .AddChoices(Approve, Revise, Abort));

        if (choice == Approve)
        {
            return Task.FromResult(new PlanDecision(PlanVerdict.Approve, null, Environment.UserName, _timeProvider.GetUtcNow()));
        }

        if (choice == Abort)
        {
            return Task.FromResult(new PlanDecision(PlanVerdict.Abort, null, Environment.UserName, _timeProvider.GetUtcNow()));
        }

        string feedback = _console.Prompt(
            new TextPrompt<string>("[green]O que precisa mudar?[/]")
                .Validate(text => string.IsNullOrWhiteSpace(text)
                    ? ValidationResult.Error("Descreva o que deve mudar — um pedido de revisão sem motivo não ajuda o arquiteto.")
                    : ValidationResult.Success()));

        return Task.FromResult(new PlanDecision(PlanVerdict.Revise, feedback, Environment.UserName, _timeProvider.GetUtcNow()));
    }

    private IReadOnlyList<QuestionAnswer> UseDefaults(IReadOnlyList<OpenQuestion> questions)
    {
        _console.MarkupLine("[yellow]Modo não interativo: usando o padrão declarado para cada pergunta.[/]");

        foreach (OpenQuestion question in questions)
        {
            _console.MarkupLine($"  [dim]{Markup.Escape(question.Id)}[/] → [yellow]{Markup.Escape(question.DefaultIfUnanswered)}[/]");
        }

        return
        [
            .. questions.Select(q => new QuestionAnswer(
                q.Id, q.DefaultIfUnanswered, "unattended (default)", _timeProvider.GetUtcNow())),
        ];
    }

    private void RenderPlan(DeliveryPlan plan, IReadOnlyList<PlanIssue> planIssues)
    {
        _console.WriteLine();
        _console.Write(new Rule($"[bold blue]Plano: {Markup.Escape(plan.Title)}[/]").LeftJustified());
        _console.WriteLine();
        _console.Write(new Padder(new Markup(Markup.Escape(plan.Overview)), new Padding(2, 0, 2, 1)));

        if (plan.Architecture.Count > 0)
        {
            _console.MarkupLine("[bold]Decisões de arquitetura[/]");

            foreach (string decision in plan.Architecture)
            {
                _console.MarkupLine($"  • {Markup.Escape(decision)}");
            }

            _console.WriteLine();
        }

        IReadOnlyList<IReadOnlyList<WorkItem>> waves = plan.Waves();

        for (int i = 0; i < waves.Count; i++)
        {
            Table table = new Table { Border = TableBorder.Rounded }
                .Title($"[bold]Onda {i + 1}[/] — {waves[i].Count} agente(s) em paralelo")
                .AddColumn("Key")
                .AddColumn("Título")
                .AddColumn("Área")
                .AddColumn(new TableColumn("Arquivos").RightAligned())
                .AddColumn("Depende de");

            foreach (WorkItem item in waves[i])
            {
                table.AddRow(
                    $"[bold]{Markup.Escape(item.Key)}[/]",
                    Markup.Escape(item.Title),
                    Markup.Escape(item.Area),
                    item.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.DependsOn.Count == 0 ? "[dim]—[/]" : Markup.Escape(string.Join(", ", item.DependsOn)));
            }

            _console.Write(table);
        }

        if (plan.DefaultDecisions.Count > 0)
        {
            _console.WriteLine();
            _console.MarkupLine("[bold yellow]Decisões tomadas por padrão — conteste se discordar[/]");

            foreach (string decision in plan.DefaultDecisions)
            {
                _console.MarkupLine($"  • {Markup.Escape(decision)}");
            }
        }

        if (plan.Risks.Count > 0)
        {
            _console.WriteLine();
            _console.MarkupLine("[bold]Riscos[/]");

            foreach (string risk in plan.Risks)
            {
                _console.MarkupLine($"  • {Markup.Escape(risk)}");
            }
        }

        if (planIssues.Count > 0)
        {
            _console.WriteLine();

            Table table = new Table { Border = TableBorder.Heavy }
                .Title("[bold red]Problemas encontrados pelo validador determinístico[/]")
                .AddColumn("Severidade")
                .AddColumn("Código")
                .AddColumn("Problema");

            foreach (PlanIssue issue in planIssues)
            {
                string colour = issue.Severity == PlanIssueSeverity.Error ? "red" : "yellow";

                table.AddRow(
                    $"[{colour}]{issue.Severity}[/]",
                    Markup.Escape(issue.Code),
                    Markup.Escape(issue.Message));
            }

            _console.Write(table);
        }

        _console.WriteLine();
    }
}
