using System.CommandLine;
using System.Globalization;
using AgentSquad.Agents.DependencyInjection;
using AgentSquad.Agents.Foundry;
using AgentSquad.Agents.Implementation;
using AgentSquad.Agents.Orchestration;
using AgentSquad.Cli.Console;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Events;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Runs;
using AgentSquad.Tools.Prerequisites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace AgentSquad.Cli;

/// <summary>Console entry point for the Agent Squad software factory.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        IAnsiConsole console = AnsiConsole.Console;

        var requestOption = new Option<string>("--request", "-r")
        {
            Description = "O que você quer que seja construído, em linguagem natural.",
        };

        var transcriptOption = new Option<FileInfo?>("--transcript", "-t")
        {
            Description = "Caminho para a transcrição de uma reunião de levantamento de requisitos.",
        };

        var ownerOption = new Option<string?>("--owner")
        {
            Description = "Dono do repositório GitHub alvo. Sobrescreve a configuração.",
        };

        var repoOption = new Option<string?>("--repo")
        {
            Description = "Nome do repositório GitHub alvo. Sobrescreve a configuração.",
        };

        var parallelOption = new Option<int?>("--parallel", "-p")
        {
            Description = "Quantos agentes de codificação rodam em paralelo.",
        };

        var unattendedOption = new Option<bool>("--unattended")
        {
            Description = "Não perguntar nada: usar o padrão declarado em cada decisão humana. Para ensaio e CI.",
        };

        var planOnlyOption = new Option<bool>("--plan-only")
        {
            Description = "Parar após a aprovação do plano, sem criar issues.",
        };

        var skipChecksOption = new Option<bool>("--skip-checks")
        {
            Description = "Pular a verificação de pré-requisitos. Não recomendado.",
        };

        var runCommand = new Command("run", "Executa a fábrica de ponta a ponta: requisitos, plano, issues, agentes e pull requests.")
        {
            requestOption, transcriptOption, ownerOption, repoOption,
            parallelOption, unattendedOption, planOnlyOption, skipChecksOption,
        };

        runCommand.SetAction((parseResult, cancellationToken) => RunAsync(
            console,
            new CliOverrides(
                parseResult.GetValue(ownerOption),
                parseResult.GetValue(repoOption),
                parseResult.GetValue(parallelOption)),
            parseResult.GetValue(requestOption) ?? string.Empty,
            parseResult.GetValue(transcriptOption),
            parseResult.GetValue(unattendedOption),
            parseResult.GetValue(planOnlyOption),
            parseResult.GetValue(skipChecksOption),
            cancellationToken));

        var probeOption = new Option<bool>("--probe-models")
        {
            Description = "Fazer uma chamada real a cada deployment do Foundry para provar que o caminho inteiro funciona.",
        };

        var probeCopilotOption = new Option<bool>("--probe-copilot")
        {
            Description = "Pedir a um agente Copilot de verdade que escreva um arquivo, em um diretório descartável.",
        };

        var doctorCommand = new Command("doctor", "Verifica todos os pré-requisitos e diz exatamente como corrigir o que estiver faltando.")
        {
            ownerOption,
            repoOption,
            probeOption,
            probeCopilotOption
        };

        doctorCommand.SetAction((parseResult, cancellationToken) => DoctorAsync(
            console,
            new CliOverrides(parseResult.GetValue(ownerOption), parseResult.GetValue(repoOption), null),
            parseResult.GetValue(probeOption),
            parseResult.GetValue(probeCopilotOption),
            cancellationToken));

        var root = new RootCommand("Agent Squad — fábrica de software autônoma com .NET 10, Microsoft Agent Framework, Microsoft Foundry e GitHub Copilot.")
        {
            runCommand,
            doctorCommand,
        };

        try
        {
            return await root.Parse(args).InvokeAsync();
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("\n[yellow]Interrompido.[/]");
            return 130;
        }
#pragma warning disable CA1031 // The CLI is the outermost boundary: a stack trace helps nobody in the audience.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            console.WriteLine();
            console.Write(new Panel(new Markup($"[red]{Markup.Escape(ex.Message)}[/]"))
            {
                Header = new PanelHeader(" Falha "),
                Border = BoxBorder.Heavy,
                BorderStyle = new Style(Color.Red),
            });

            if (Environment.GetEnvironmentVariable("SQUAD_DEBUG") is not null)
            {
                console.WriteException(ex, ExceptionFormats.ShortenPaths);
            }
            else
            {
                console.MarkupLine("[dim]Defina SQUAD_DEBUG=1 para ver o stack trace completo.[/]");
            }

            return 4;
        }
    }

    private static async Task<int> RunAsync(
        IAnsiConsole console,
        CliOverrides overrides,
        string request,
        FileInfo? transcript,
        bool unattended,
        bool planOnly,
        bool skipChecks,
        CancellationToken cancellationToken)
    {
        RenderBanner(console);

        if (string.IsNullOrWhiteSpace(request) && transcript is null)
        {
            console.MarkupLine("[red]Informe um pedido com --request, ou uma transcrição com --transcript.[/]");
            return 2;
        }

        if (transcript is not null && !transcript.Exists)
        {
            console.MarkupLine($"[red]A transcrição não foi encontrada: {Markup.Escape(transcript.FullName)}[/]");
            return 2;
        }

        using IHost host = BuildHost(console, overrides, unattended);

        if (!skipChecks && !await RunPrerequisiteChecksAsync(console, host, cancellationToken))
        {
            return 3;
        }

        SquadOrchestrator orchestrator = host.Services.GetRequiredService<SquadOrchestrator>();
        IRunEventStream stream = host.Services.GetRequiredService<IRunEventStream>();
        var renderer = new ConsoleRunRenderer(console);

        var runRequest = new SquadRunRequest(request, transcript?.FullName, unattended, planOnly);

        // The renderer has to be attached while the run is happening, not after it, or the
        // console shows nothing for twenty minutes and then dumps the whole history at once.
        // The orchestrator hands back the run id the moment it is assigned; the event bus
        // replays anything published in the gap, so nothing is lost either way.
        var started = new TaskCompletionSource<RunId>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<SquadRunResult> runTask = orchestrator.RunAsync(
            runRequest,
            runId => started.TrySetResult(runId),
            cancellationToken);

        RunId activeRun = await started.Task;
        Task renderTask = renderer.FollowAsync(stream, activeRun, cancellationToken);

        SquadRunResult result = await runTask;

        // The bus completes the stream when the run ends, so the renderer finishes on its
        // own; the timeout is only a guard against a stream that never closes.
        await renderTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
                        .ContinueWith(_ => { }, TaskScheduler.Default);

        RenderSummary(console, result);

        return result.Outcome switch
        {
            RunOutcome.Delivered or RunOutcome.PlanOnly => 0,
            RunOutcome.Rejected => 1,
            RunOutcome.Cancelled => 130,
            _ => 4,
        };
    }

    private static async Task<int> DoctorAsync(
        IAnsiConsole console,
        CliOverrides overrides,
        bool probeModels,
        bool probeCopilot,
        CancellationToken cancellationToken)
    {
        RenderBanner(console);

        using IHost host = BuildHost(console, overrides, unattended: true);

        bool healthy = await RunPrerequisiteChecksAsync(console, host, cancellationToken);

        if (probeModels)
        {
            healthy &= await ProbeModelsAsync(console, host, cancellationToken);
        }

        if (probeCopilot)
        {
            healthy &= await ProbeCopilotAsync(console, host, cancellationToken);
        }

        return healthy ? 0 : 3;
    }

    private static async Task<bool> ProbeCopilotAsync(
        IAnsiConsole console,
        IHost host,
        CancellationToken cancellationToken)
    {
        CopilotSmokeTest smokeTest = host.Services.GetRequiredService<CopilotSmokeTest>();

        console.Write(new Rule("[bold blue]Agente de codificação GitHub Copilot[/]").LeftJustified());
        console.WriteLine();

        CopilotProbe probe = await console
            .Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Pedindo a um agente real que escreva um arquivo…", _ => smokeTest.ProbeAsync(cancellationToken));

        Table table = new Table { Border = TableBorder.Rounded }.AddColumn(" ").AddColumn(" ");
        table.AddRow("Runtime", probe.Reachable ? "[green]iniciou e respondeu[/]" : "[red]não respondeu[/]");
        table.AddRow("Modelo", Markup.Escape(probe.Model));
        table.AddRow("Arquivo criado", probe.FileWasWritten ? "[green]sim[/]" : "[red]não[/]");
        table.AddRow("Relatório estruturado", probe.ReportParsed ? "[green]sim[/]" : "[yellow]não[/]");
        table.AddRow("Duração", $"{probe.Duration.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s");
        table.AddRow("Resultado", Markup.Escape(probe.Detail));

        if (probe.DeniedActions.Count > 0)
        {
            table.AddRow("Permissões negadas", Markup.Escape(string.Join("; ", probe.DeniedActions)));
        }

        console.Write(table);
        console.WriteLine();

        // Writing the file is the assertion that matters: a runtime that starts but cannot
        // act is worse than one that fails outright, because it looks healthy.
        bool healthy = probe.Reachable && probe.FileWasWritten;

        console.MarkupLine(healthy
            ? "[green]O agente de codificação escreveu um arquivo de verdade. O caminho do Copilot está funcionando.[/]\n"
            : "[red]O agente não conseguiu produzir o arquivo. Veja o resultado acima.[/]\n");

        return healthy;
    }

    private static async Task<bool> ProbeModelsAsync(
        IAnsiConsole console,
        IHost host,
        CancellationToken cancellationToken)
    {
        FoundrySmokeTest smokeTest = host.Services.GetRequiredService<FoundrySmokeTest>();

        console.Write(new Rule("[bold blue]Chamada real ao Microsoft Foundry[/]").LeftJustified());
        console.WriteLine();

        IReadOnlyList<ModelProbe> probes = await console
            .Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Chamando cada deployment…", _ => smokeTest.ProbeAllAsync(cancellationToken));

        Table table = new Table { Border = TableBorder.Rounded }
            .AddColumn(" ")
            .AddColumn("Deployment")
            .AddColumn("Papel")
            .AddColumn(new TableColumn("Latência").RightAligned())
            .AddColumn("Resposta");

        foreach (ModelProbe probe in probes)
        {
            table.AddRow(
                probe.Reachable ? "[green]✔[/]" : "[red]✖[/]",
                $"[bold]{Markup.Escape(probe.Deployment)}[/]",
                Markup.Escape(probe.Role),
                $"{probe.Latency.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms",
                Markup.Escape(probe.Detail));
        }

        console.Write(table);
        console.WriteLine();

        bool allReachable = probes.All(p => p.Reachable);

        console.MarkupLine(allReachable
            ? "[green]O caminho Entra ID → Foundry → Agent Framework está funcionando de ponta a ponta.[/]\n"
            : "[red]Pelo menos um deployment não respondeu. Veja a coluna Resposta.[/]\n");

        return allReachable;
    }

    private static async Task<bool> RunPrerequisiteChecksAsync(
        IAnsiConsole console,
        IHost host,
        CancellationToken cancellationToken)
    {
        PrerequisiteChecker checker = host.Services.GetRequiredService<PrerequisiteChecker>();

        IReadOnlyList<PrerequisiteResult> results = await console
            .Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Verificando pré-requisitos…", _ => checker.CheckAllAsync(cancellationToken));

        Table table = new Table { Border = TableBorder.Rounded }
            .Title("[bold]Pré-requisitos[/]")
            .AddColumn(" ")
            .AddColumn("Item")
            .AddColumn("Encontrado");

        foreach (PrerequisiteResult result in results)
        {
            string icon = result.Satisfied
                ? "[green]✔[/]"
                : result.Severity == PrerequisiteSeverity.Required ? "[red]✖[/]" : "[yellow]▲[/]";

            table.AddRow(icon, Markup.Escape(result.Name), Markup.Escape(result.Detail));
        }

        console.Write(table);

        PrerequisiteResult[] blocking =
        [
            .. results.Where(r => !r.Satisfied && r.Severity == PrerequisiteSeverity.Required),
        ];

        PrerequisiteResult[] advisory =
        [
            .. results.Where(r => !r.Satisfied && r.Severity == PrerequisiteSeverity.Recommended),
        ];

        foreach (PrerequisiteResult result in advisory)
        {
            console.MarkupLine($"[yellow]▲ {Markup.Escape(result.Name)}[/] — {Markup.Escape(result.Remedy ?? string.Empty)}");
        }

        if (blocking.Length == 0)
        {
            console.MarkupLine("\n[green]Todos os pré-requisitos obrigatórios estão satisfeitos.[/]\n");
            return true;
        }

        console.WriteLine();
        console.Write(new Rule("[red]Corrija antes de continuar[/]").LeftJustified());
        console.WriteLine();

        foreach (PrerequisiteResult result in blocking)
        {
            console.MarkupLine($"[red]✖ {Markup.Escape(result.Name)}[/]");
            console.MarkupLine($"  [dim]{Markup.Escape(result.Detail)}[/]");

            if (result.Remedy is { } remedy)
            {
                console.Write(new Padder(
                    new Panel(new Markup($"[bold]{Markup.Escape(remedy)}[/]")) { Border = BoxBorder.Rounded },
                    new Padding(2, 0, 0, 1)));
            }
        }

        return false;
    }

    private static IHost BuildHost(IAnsiConsole console, CliOverrides overrides, bool unattended)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddUserSecrets<CliOverrides>(optional: true);
        builder.Configuration.AddEnvironmentVariables("SQUAD_");

        Dictionary<string, string?> inline = [];

        if (!string.IsNullOrWhiteSpace(overrides.Owner))
        {
            inline["GitHub:Owner"] = overrides.Owner;
        }

        if (!string.IsNullOrWhiteSpace(overrides.Repository))
        {
            inline["GitHub:Repository"] = overrides.Repository;
        }

        if (overrides.Parallelism is { } parallelism)
        {
            inline["Squad:MaxParallelAgents"] = parallelism.ToString(CultureInfo.InvariantCulture);
        }

        if (inline.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(inline);
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("AgentSquad", LogLevel.Information);

        builder.Services.AddAgentSquad(builder.Configuration);
        builder.Services.AddSingleton(console);
        builder.Services.AddSingleton<Core.Abstractions.IApprovalGateway>(sp =>
            new ConsoleApprovalGateway(console, sp.GetRequiredService<TimeProvider>(), unattended));

        return builder.Build();
    }

    private static void RenderBanner(IAnsiConsole console)
    {
        console.Write(new FigletText("Agent Squad").Color(Color.Blue));
        console.MarkupLine("[dim]Fábrica de software autônoma · .NET 10 · Microsoft Agent Framework · Microsoft Foundry · GitHub Copilot[/]");
        console.WriteLine();
    }

    private static void RenderSummary(IAnsiConsole console, SquadRunResult result)
    {
        console.WriteLine();
        console.Write(new Rule("[bold]Resumo da execução[/]").LeftJustified());
        console.WriteLine();

        Table table = new Table { Border = TableBorder.None }.AddColumn(" ").AddColumn(" ");
        table.AddRow("[dim]Execução[/]", Markup.Escape(result.RunId.Value));
        table.AddRow("[dim]Resultado[/]", OutcomeMarkup(result.Outcome));
        table.AddRow("[dim]Duração[/]", $"{result.Duration.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture)} min");
        table.AddRow("[dim]Issues criadas[/]", result.Issues.Count.ToString(CultureInfo.InvariantCulture));
        table.AddRow("[dim]Aprovadas no gate[/]", $"{result.Succeeded.Count}/{result.Outcomes.Count}");
        table.AddRow("[dim]Pull requests[/]", result.PullRequests.Count.ToString(CultureInfo.InvariantCulture));
        console.Write(table);

        if (result.PullRequests.Count > 0)
        {
            console.WriteLine();
            console.MarkupLine("[bold]Pull requests para revisão humana[/]");

            foreach (PullRequestRef pullRequest in result.PullRequests)
            {
                console.MarkupLine(
                    $"  [green]#{pullRequest.Number.ToString(CultureInfo.InvariantCulture)}[/] " +
                    $"[link={pullRequest.Url}]{Markup.Escape(pullRequest.Url)}[/]" +
                    (pullRequest.IsDraft ? " [dim](draft)[/]" : string.Empty));
            }
        }

        if (result.Failed.Count > 0)
        {
            console.WriteLine();
            console.MarkupLine("[bold yellow]Work items que não passaram no gate[/]");

            foreach (ImplementationOutcome failed in result.Failed)
            {
                console.MarkupLine(
                    $"  [yellow]#{failed.IssueNumber.ToString(CultureInfo.InvariantCulture)}[/] " +
                    $"{Markup.Escape(failed.Report.Summary)}");
            }
        }

        console.WriteLine();
        console.MarkupLine($"[dim]{Markup.Escape(result.Message)}[/]");
        console.WriteLine();
    }

    private static string OutcomeMarkup(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Delivered => "[green]entregue[/]",
        RunOutcome.PlanOnly => "[blue]somente plano[/]",
        RunOutcome.Rejected => "[yellow]plano rejeitado[/]",
        RunOutcome.Cancelled => "[yellow]cancelada[/]",
        _ => "[red]falhou[/]",
    };
}

/// <summary>Command-line values that override configuration.</summary>
/// <param name="Owner">GitHub owner.</param>
/// <param name="Repository">GitHub repository.</param>
/// <param name="Parallelism">Maximum concurrent coding agents.</param>
internal sealed record CliOverrides(string? Owner, string? Repository, int? Parallelism);


