using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AgentSquad.Agents.Pipeline;
using AgentSquad.Agents.Rendering;
using AgentSquad.Agents.Runs;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Events;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Intake;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Requirements;
using AgentSquad.Core.Review;
using AgentSquad.Core.Runs;
using AgentSquad.Core.Validation;
using AgentSquad.Tools.Git;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Agents.Orchestration;

/// <summary>
/// Drives one end-to-end run of the software factory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the parallel wave is hand-written rather than a workflow graph.</b> The planning
/// half of the pipeline is a clean sequence with a human gate, and
/// <c>Microsoft.Agents.AI.Workflows</c> models that well. The implementation wave is a
/// different problem: bounded concurrency, a per-agent time budget, a repair loop with a
/// retry ceiling, and worktree lifecycle that must be cleaned up even when an agent dies.
/// Expressing that as a graph would hide the control the operator most needs, so it is
/// written as explicit, testable C# with <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>.
/// See <c>docs/adr/0001-orchestration-model.md</c>.
/// </para>
/// <para>
/// Every irreversible action — creating issues, pushing a branch, opening a pull request —
/// happens after a gate: human approval for the plan, deterministic validation for the code
/// (AI-004, AI-005).
/// </para>
/// </remarks>
public sealed partial class SquadOrchestrator(
    SquadAgentSet agents,
    IIssueTracker issueTracker,
    IGitClient gitClient,
    GitWorktreeManager worktreeManager,
    IValidationGate validationGate,
    ICodingAgentFactory codingAgentFactory,
    IApprovalGateway approvals,
    RunEventBus eventBus,
    IOptions<SquadOptions> squadOptions,
    IOptions<GitHubOptions> gitHubOptions,
    TimeProvider timeProvider,
    ILogger<SquadOrchestrator> logger)
{
    private readonly SquadAgentSet _agents = agents;
    private readonly IIssueTracker _issueTracker = issueTracker;
    private readonly IGitClient _gitClient = gitClient;
    private readonly GitWorktreeManager _worktrees = worktreeManager;
    private readonly IValidationGate _validationGate = validationGate;
    private readonly ICodingAgentFactory _codingAgents = codingAgentFactory;
    private readonly IApprovalGateway _approvals = approvals;
    private readonly RunEventBus _eventBus = eventBus;
    private readonly SquadOptions _options = squadOptions.Value;
    private readonly GitHubOptions _gitHub = gitHubOptions.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<SquadOrchestrator> _logger = logger;

    /// <summary>
    /// Runs the factory end to end.
    /// </summary>
    /// <param name="request">What to build.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>Everything the run produced.</returns>
    public async Task<SquadRunResult> RunAsync(SquadRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = RunId.New(_timeProvider);
        IRunEventPublisher events = _eventBus.PublisherFor(runId);
        var stopwatch = Stopwatch.StartNew();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.RunTimeout);
        CancellationToken token = budget.Token;

        ProjectContext? context = null;
        RequirementsDocument? requirements = null;
        DeliveryPlan? plan = null;
        IReadOnlyList<TrackedIssue> issues = [];
        IReadOnlyList<ImplementationOutcome> outcomes = [];
        IReadOnlyList<PullRequestRef> pullRequests = [];

        try
        {
            LogRunStarted(runId.Value, _gitHub.Slug);
            await events.PublishAsync(RunPhase.Intake, RunEventLevel.Milestone, "orchestrator",
                $"Execução {runId} iniciada em {_gitHub.Slug}.", cancellationToken: token);

            // ---- Intake -------------------------------------------------------------
            context = await IntakeAsync(request, events, token);

            // ---- Requirements, with a human clarification loop ----------------------
            requirements = await GatherRequirementsAsync(request, context, events, token);

            // ---- Plan, validated deterministically and critiqued ---------------------
            plan = await BuildPlanAsync(requirements, context, events, token);

            // ---- Human approval ------------------------------------------------------
            PlanDecision decision = await ApprovePlanAsync(plan, requirements, request, events, token);

            if (decision.Verdict != PlanVerdict.Approve)
            {
                return Finish(runId, RunOutcome.Rejected, context, requirements, plan, issues, outcomes, pullRequests,
                    stopwatch.Elapsed, $"O plano foi rejeitado por {decision.DecidedBy}: {decision.Feedback ?? "sem comentário"}.");
            }

            if (request.StopAfterPlan)
            {
                return Finish(runId, RunOutcome.PlanOnly, context, requirements, plan, issues, outcomes, pullRequests,
                    stopwatch.Elapsed, "Plano aprovado. A execução foi encerrada antes de criar issues, conforme solicitado.");
            }

            // ---- Publish the work ----------------------------------------------------
            issues = await PublishIssuesAsync(plan, runId, events, token);

            // ---- Implement, wave by wave ---------------------------------------------
            (outcomes, pullRequests) = await ImplementAllWavesAsync(plan, issues, runId, events, token);

            string summary = BuildSummary(outcomes, pullRequests);
            await events.PublishAsync(RunPhase.Completed, RunEventLevel.Milestone, "orchestrator", summary, cancellationToken: token);

            return Finish(runId, RunOutcome.Delivered, context, requirements, plan, issues, outcomes, pullRequests,
                stopwatch.Elapsed, summary);
        }
        catch (OperationCanceledException)
        {
            bool timedOut = !cancellationToken.IsCancellationRequested;
            string message = timedOut
                ? $"A execução excedeu o orçamento de {_options.RunTimeout.TotalHours:F1}h."
                : "A execução foi cancelada.";

            await SafePublishAsync(events, RunPhase.Completed, RunEventLevel.Error, message);

            return Finish(runId, RunOutcome.Cancelled, context, requirements, plan, issues, outcomes, pullRequests,
                stopwatch.Elapsed, message);
        }
#pragma warning disable CA1031 // A run must always produce a result the caller can render, never an unhandled exception.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRunFailed(ex, runId.Value);
            await SafePublishAsync(events, RunPhase.Completed, RunEventLevel.Error, $"A execução falhou: {ex.Message}");

            return Finish(runId, RunOutcome.Failed, context, requirements, plan, issues, outcomes, pullRequests,
                stopwatch.Elapsed, ex.Message);
        }
        finally
        {
            if (_options.CleanupWorktrees)
            {
                await _worktrees.CleanupAllAsync(CancellationToken.None);
            }

            _eventBus.Complete(runId);
        }
    }

    // =====================================================================================
    // Intake
    // =====================================================================================

    private async Task<ProjectContext> IntakeAsync(
        SquadRunRequest request,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        await events.PublishAsync(RunPhase.Intake, RunEventLevel.Info, "orchestrator",
            "Clonando o repositório alvo e preparando-o para worktrees paralelos…", cancellationToken: cancellationToken);

        string localPath = Path.Combine(_options.ReposDirectory, _gitHub.Repository);
        await _gitClient.EnsureCloneAsync(_gitHub.CloneUrl, localPath, cancellationToken);

        _worktrees.RepositoryPath = localPath;
        await _worktrees.PrepareRepositoryAsync(cancellationToken);

        string defaultBranch = await _gitClient.GetDefaultBranchAsync(localPath, cancellationToken);
        string inventory = RepositoryInventory.Describe(localPath);

        await events.PublishAsync(RunPhase.Intake, RunEventLevel.Info, "intake-analyst",
            "Analisando o repositório para decidir se é greenfield ou brownfield…", cancellationToken: cancellationToken);

        var prompt = new StringBuilder();
        prompt.AppendLine("## Pedido do usuário").AppendLine();
        prompt.AppendLine(SquadAgentSet.Untrusted("REQUEST", request.Request)).AppendLine();
        prompt.AppendLine("## Repositório alvo").AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"`{_gitHub.Slug}`, branch padrão `{defaultBranch}`.").AppendLine();
        prompt.AppendLine("Inventário do repositório:").AppendLine();
        prompt.AppendLine(SquadAgentSet.Untrusted("REPOSITORY", inventory));

        IntakeAssessment assessment = await _agents.RunStructuredAsync<IntakeAssessment>(
            _agents.Intake, prompt.ToString(), session: null, cancellationToken);

        var context = new ProjectContext(
            assessment.Mode,
            _gitHub.Owner,
            _gitHub.Repository,
            defaultBranch,
            localPath,
            assessment.PrimaryLanguage,
            assessment.Stack,
            [],
            assessment.Conventions,
            assessment.Summary);

        await events.PublishAsync(RunPhase.Intake, RunEventLevel.Milestone, "intake-analyst",
            $"Modo de entrega: **{assessment.Mode}**. {assessment.Summary}", cancellationToken: cancellationToken);

        return context;
    }

    // =====================================================================================
    // Requirements
    // =====================================================================================

    private async Task<RequirementsDocument> GatherRequirementsAsync(
        SquadRunRequest request,
        ProjectContext context,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        string? transcriptDigest = null;

        if (request.HasTranscript)
        {
            transcriptDigest = await AnalyzeTranscriptAsync(request.TranscriptPath!, events, cancellationToken);
        }

        await events.PublishAsync(RunPhase.Requirements, RunEventLevel.Info, "requirements-analyst",
            "Transformando o pedido em requisitos verificáveis…", cancellationToken: cancellationToken);

        AgentSession session = await _agents.RequirementsAnalyst.CreateSessionAsync(cancellationToken);
        string prompt = BuildRequirementsPrompt(request, context, transcriptDigest);

        RequirementsDocument document = await _agents.RunStructuredAsync<RequirementsDocument>(
            _agents.RequirementsAnalyst, prompt, session, cancellationToken);

        for (int round = 1; round <= _options.MaxClarificationRounds; round++)
        {
            IReadOnlyList<OpenQuestion> pending = document.OpenQuestions;

            if (pending.Count == 0)
            {
                break;
            }

            await events.PublishAsync(RunPhase.Clarification, RunEventLevel.Milestone, "requirements-analyst",
                $"{pending.Count} pergunta(s) para o humano (rodada {round}).", cancellationToken: cancellationToken);

            IReadOnlyList<QuestionAnswer> answers = await _approvals.AskAsync(pending, cancellationToken);

            if (answers.Count == 0)
            {
                break;
            }

            var followUp = new StringBuilder();
            followUp.AppendLine("O humano respondeu às suas perguntas. Incorpore as respostas e devolva o documento");
            followUp.AppendLine("de requisitos atualizado. Remova as perguntas respondidas e converta cada resposta");
            followUp.AppendLine("em requisito ou restrição, com `confidence: \"stated\"`.").AppendLine();

            foreach (QuestionAnswer answer in answers)
            {
                OpenQuestion? question = pending.FirstOrDefault(q =>
                    string.Equals(q.Id, answer.QuestionId, StringComparison.OrdinalIgnoreCase));

                followUp.Append("- **").Append(answer.QuestionId).Append("** ")
                        .Append(question?.Question ?? string.Empty)
                        .Append(" → **").Append(answer.Answer).AppendLine("**");
            }

            document = await _agents.RunStructuredAsync<RequirementsDocument>(
                _agents.RequirementsAnalyst, followUp.ToString(), session, cancellationToken);
        }

        await events.PublishAsync(RunPhase.Requirements, RunEventLevel.Milestone, "requirements-analyst",
            $"{document.Requirements.Count} requisito(s), {document.Assumptions.Count} suposição(ões) declarada(s).",
            cancellationToken: cancellationToken);

        return document;
    }

    private async Task<string> AnalyzeTranscriptAsync(
        string transcriptPath,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        await events.PublishAsync(RunPhase.Intake, RunEventLevel.Info, "transcript-analyst",
            $"Lendo a transcrição da reunião: {Path.GetFileName(transcriptPath)}", cancellationToken: cancellationToken);

        string transcript = await File.ReadAllTextAsync(transcriptPath, cancellationToken);

        string prompt = "Analise a transcrição abaixo e extraia o que foi efetivamente estabelecido.\n\n"
                      + SquadAgentSet.Untrusted("TRANSCRIPT", transcript);

        TranscriptAnalysis analysis = await _agents.RunStructuredAsync<TranscriptAnalysis>(
            _agents.TranscriptAnalyst, prompt, session: null, cancellationToken);

        await events.PublishAsync(RunPhase.Intake, RunEventLevel.Milestone, "transcript-analyst",
            $"Reunião \"{analysis.Title}\": {analysis.Decisions.Count} decisão(ões), " +
            $"{analysis.StatedRequirements.Count} requisito(s) declarado(s), {analysis.OpenItems.Count} pendência(s).",
            cancellationToken: cancellationToken);

        foreach (string item in analysis.OpenItems)
        {
            await events.PublishAsync(RunPhase.Intake, RunEventLevel.Warning, "transcript-analyst",
                $"Pendência da reunião: {item}", cancellationToken: cancellationToken);
        }

        var digest = new StringBuilder();
        digest.AppendLine("### Decisões").AppendLine();
        AppendBullets(digest, analysis.Decisions);
        digest.AppendLine("### Requisitos declarados na reunião").AppendLine();
        AppendBullets(digest, analysis.StatedRequirements);
        digest.AppendLine("### Restrições").AppendLine();
        AppendBullets(digest, analysis.Constraints);
        digest.AppendLine("### Explicitamente fora de escopo").AppendLine();
        AppendBullets(digest, analysis.NonGoals);
        digest.AppendLine("### Pendências e divergências não resolvidas").AppendLine();
        AppendBullets(digest, analysis.OpenItems);
        digest.AppendLine("### Suposições provavelmente não compartilhadas").AppendLine();
        AppendBullets(digest, analysis.UnsharedAssumptions);

        return digest.ToString();

        static void AppendBullets(StringBuilder builder, IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                builder.AppendLine("_(nenhum)_").AppendLine();
                return;
            }

            foreach (string value in values)
            {
                builder.Append("- ").AppendLine(value);
            }

            builder.AppendLine();
        }
    }

    private static string BuildRequirementsPrompt(SquadRunRequest request, ProjectContext context, string? transcriptDigest)
    {
        var prompt = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.Request))
        {
            prompt.AppendLine("## Pedido do usuário").AppendLine();
            prompt.AppendLine(SquadAgentSet.Untrusted("REQUEST", request.Request)).AppendLine();
        }

        if (transcriptDigest is not null)
        {
            prompt.AppendLine("## O que a reunião de levantamento estabeleceu").AppendLine();
            prompt.AppendLine(transcriptDigest).AppendLine();
            prompt.AppendLine("Trate as pendências acima como candidatas naturais a perguntas — mas só as que realmente");
            prompt.AppendLine("mudam o plano, e no máximo cinco no total.").AppendLine();
        }

        prompt.AppendLine("## Contexto do repositório alvo").AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Modo: **{context.Mode}**");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Linguagem: {context.PrimaryLanguage ?? "(repositório vazio)"}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Stack: {string.Join(", ", context.Stack)}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Resumo: {context.Summary}");

        if (context.Conventions.Count > 0)
        {
            prompt.AppendLine().AppendLine("Convenções já estabelecidas no repositório:").AppendLine();

            foreach (string convention in context.Conventions)
            {
                prompt.Append("- ").AppendLine(convention);
            }
        }

        return prompt.ToString();
    }

    private static string BuildArchitectPrompt(RequirementsDocument requirements, ProjectContext context)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("## Requisitos aprovados").AppendLine();
        prompt.AppendLine(SquadAgentSet.Untrusted("REQUIREMENTS", PlanFormatter.Requirements(requirements))).AppendLine();

        prompt.AppendLine("## Repositório alvo").AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Modo: **{context.Mode}**");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Linguagem: {context.PrimaryLanguage ?? "(repositório vazio)"}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Stack: {string.Join(", ", context.Stack)}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Branch base: `{context.DefaultBranch}`");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Resumo: {context.Summary}").AppendLine();

        if (context.Conventions.Count > 0)
        {
            prompt.AppendLine("Convenções a seguir (já estabelecidas no repositório):").AppendLine();

            foreach (string convention in context.Conventions)
            {
                prompt.Append("- ").AppendLine(convention);
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("## Lembretes operacionais").AppendLine();
        prompt.AppendLine("- Cada work item vira uma issue implementada por **um** agente em **um** worktree isolado.");
        prompt.AppendLine("- Dois itens da **mesma onda** nunca podem declarar o mesmo arquivo.");
        prompt.AppendLine("- Toda dependência aponta para uma onda **estritamente anterior**.");
        prompt.AppendLine("- Código de produção sempre vem acompanhado do seu arquivo de teste, no mesmo item.");
        prompt.AppendLine("- Os critérios de aceite precisam ser decidíveis por `dotnet format`, `dotnet build -warnaserror` e `dotnet test`.");

        return prompt.ToString();
    }

    // =====================================================================================
    // Planning
    // =====================================================================================

    private async Task<DeliveryPlan> BuildPlanAsync(
        RequirementsDocument requirements,
        ProjectContext context,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        await events.PublishAsync(RunPhase.Planning, RunEventLevel.Info, "architect",
            "Montando o plano de execução e as ondas de paralelismo…", cancellationToken: cancellationToken);

        AgentSession session = await _agents.Architect.CreateSessionAsync(cancellationToken);
        string prompt = BuildArchitectPrompt(requirements, context);

        DeliveryPlan plan = await _agents.RunStructuredAsync<DeliveryPlan>(
            _agents.Architect, prompt, session, cancellationToken);

        string[] requirementIds = [.. requirements.Requirements.Select(r => r.Id)];

        for (int revision = 0; revision <= _options.MaxPlanRevisions; revision++)
        {
            // The deterministic validator runs first. It settles the questions that have a
            // right answer — file conflicts, cycles, wave ordering — so the critic's budget
            // is spent on the questions that need judgement (AI-008).
            IReadOnlyList<PlanIssue> planIssues = PlanValidator.Validate(plan, requirementIds);
            IReadOnlyList<PlanIssue> blocking = [.. planIssues.Where(i => i.Severity == PlanIssueSeverity.Error)];

            foreach (PlanIssue issue in planIssues)
            {
                await events.PublishAsync(
                    RunPhase.Planning,
                    issue.Severity == PlanIssueSeverity.Error ? RunEventLevel.Warning : RunEventLevel.Info,
                    "plan-validator",
                    $"{issue.Code}: {issue.Message}",
                    cancellationToken: cancellationToken);
            }

            PlanCritique critique = await CritiqueAsync(plan, requirements, planIssues, cancellationToken);

            await events.PublishAsync(RunPhase.Planning, RunEventLevel.Info, "plan-critic",
                critique.Summary, cancellationToken: cancellationToken);

            bool acceptable = blocking.Count == 0 && critique.Approved;

            if (acceptable || revision == _options.MaxPlanRevisions)
            {
                if (!acceptable)
                {
                    await events.PublishAsync(RunPhase.Planning, RunEventLevel.Warning, "orchestrator",
                        $"O plano ainda tem {blocking.Count} problema(s) bloqueante(s) após {revision} revisão(ões). " +
                        "Ele vai para aprovação humana assim mesmo, com os problemas visíveis.",
                        cancellationToken: cancellationToken);
                }

                await events.PublishAsync(RunPhase.Planning, RunEventLevel.Milestone, "architect",
                    $"Plano com {plan.Items.Count} work item(s) em {plan.Waves().Count} onda(s).",
                    cancellationToken: cancellationToken);

                return plan;
            }

            await events.PublishAsync(RunPhase.Planning, RunEventLevel.Info, "architect",
                $"Revisando o plano (rodada {revision + 1})…", cancellationToken: cancellationToken);

            plan = await ReviseAsync(blocking, critique, session, cancellationToken);
        }

        return plan;
    }

    private async Task<PlanCritique> CritiqueAsync(
        DeliveryPlan plan,
        RequirementsDocument requirements,
        IReadOnlyList<PlanIssue> planIssues,
        CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("## Requisitos aprovados").AppendLine();
        prompt.AppendLine(SquadAgentSet.Untrusted("REQUIREMENTS", PlanFormatter.Requirements(requirements))).AppendLine();
        prompt.AppendLine("## Plano proposto").AppendLine();
        prompt.AppendLine(SquadAgentSet.Untrusted("PLAN", PlanFormatter.Plan(plan))).AppendLine();
        prompt.AppendLine("## O que o validador determinístico já encontrou").AppendLine();

        if (planIssues.Count == 0)
        {
            prompt.AppendLine("Nada. Não repita esse tipo de verificação — procure o que um programa não vê.");
        }
        else
        {
            foreach (PlanIssue issue in planIssues)
            {
                prompt.Append("- [").Append(issue.Severity).Append("] ").Append(issue.Code).Append(": ")
                      .AppendLine(issue.Message);
            }

            prompt.AppendLine().AppendLine("Esses já estão cobertos. Procure o que um programa não vê.");
        }

        return await _agents.RunStructuredAsync<PlanCritique>(
            _agents.PlanCritic, prompt.ToString(), session: null, cancellationToken);
    }

    private async Task<DeliveryPlan> ReviseAsync(
        IReadOnlyList<PlanIssue> blocking,
        PlanCritique critique,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Seu plano foi reprovado. Corrija-o e devolva o plano completo revisado.").AppendLine();

        if (blocking.Count > 0)
        {
            prompt.AppendLine("### Problemas bloqueantes encontrados pelo validador determinístico").AppendLine();

            foreach (PlanIssue issue in blocking)
            {
                prompt.Append("- **").Append(issue.Code).Append("** ").Append(issue.Message);

                if (issue.ItemKeys.Count > 0)
                {
                    prompt.Append(" (itens: ").Append(string.Join(", ", issue.ItemKeys)).Append(')');
                }

                prompt.AppendLine();
            }

            prompt.AppendLine();
            prompt.AppendLine("Lembre-se: dois itens na mesma onda nunca podem declarar o mesmo arquivo. Sequencie,");
            prompt.AppendLine("extraia uma costura antes, ou funda os itens.").AppendLine();
        }

        if (critique.Problems.Count > 0)
        {
            prompt.AppendLine("### Apontamentos do revisor de plano").AppendLine();

            foreach (string problem in critique.Problems)
            {
                prompt.Append("- ").AppendLine(problem);
            }

            prompt.AppendLine();
        }

        if (critique.MissingWork.Count > 0)
        {
            prompt.AppendLine("### Trabalho faltando").AppendLine();

            foreach (string missing in critique.MissingWork)
            {
                prompt.Append("- ").AppendLine(missing);
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("Mantenha o que estava bom. Não reescreva o plano inteiro por causa de um item.");

        return await _agents.RunStructuredAsync<DeliveryPlan>(
            _agents.Architect, prompt.ToString(), session, cancellationToken);
    }

    private async Task<PlanDecision> ApprovePlanAsync(
        DeliveryPlan plan,
        RequirementsDocument requirements,
        SquadRunRequest request,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, [.. requirements.Requirements.Select(r => r.Id)]);

        if (!_options.RequirePlanApproval || request.Unattended)
        {
            await events.PublishAsync(RunPhase.PlanApproval, RunEventLevel.Warning, "orchestrator",
                "Aprovação humana do plano desativada nesta execução.", cancellationToken: cancellationToken);

            return new PlanDecision(PlanVerdict.Approve, null, "unattended", _timeProvider.GetUtcNow());
        }

        await events.PublishAsync(RunPhase.PlanApproval, RunEventLevel.Milestone, "orchestrator",
            "Aguardando aprovação humana do plano…", cancellationToken: cancellationToken);

        PlanDecision decision = await _approvals.ApprovePlanAsync(plan, issues, cancellationToken);

        await events.PublishAsync(RunPhase.PlanApproval, RunEventLevel.Milestone, "orchestrator",
            $"Plano {decision.Verdict} por {decision.DecidedBy}.", cancellationToken: cancellationToken);

        return decision;
    }

    // =====================================================================================
    // Publishing
    // =====================================================================================

    private async Task<IReadOnlyList<TrackedIssue>> PublishIssuesAsync(
        DeliveryPlan plan,
        RunId runId,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        await events.PublishAsync(RunPhase.Publishing, RunEventLevel.Info, "orchestrator",
            "Criando labels, milestone e issues no GitHub…", cancellationToken: cancellationToken);

        await _issueTracker.EnsureLabelsAsync(SquadLabels.For(plan), cancellationToken);

        int milestone = await _issueTracker.EnsureMilestoneAsync(
            plan.Title,
            $"Entrega planejada automaticamente pelo Agent Squad na execução {runId}.",
            cancellationToken);

        IReadOnlyList<TrackedIssue> issues = await _issueTracker.CreateIssuesAsync(
            plan.Items,
            milestone,
            item => IssueBodyRenderer.Render(item, plan.Title, runId.Value),
            cancellationToken);

        foreach (TrackedIssue issue in issues)
        {
            await events.PublishAsync(RunPhase.Publishing, RunEventLevel.Info, "issue-publisher",
                $"Issue #{issue.Number}: {issue.Title}", issue.Number,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = issue.Url },
                cancellationToken);
        }

        await events.PublishAsync(RunPhase.Publishing, RunEventLevel.Milestone, "issue-publisher",
            $"{issues.Count} issue(s) criada(s) no milestone \"{plan.Title}\".", cancellationToken: cancellationToken);

        return issues;
    }

    // =====================================================================================
    // Implementation
    // =====================================================================================

    private async Task<(IReadOnlyList<ImplementationOutcome> Outcomes, IReadOnlyList<PullRequestRef> PullRequests)>
        ImplementAllWavesAsync(
            DeliveryPlan plan,
            IReadOnlyList<TrackedIssue> issues,
            RunId runId,
            IRunEventPublisher events,
            CancellationToken cancellationToken)
    {
        var byKey = issues.ToDictionary(
            i => i.WorkItemKey, StringComparer.OrdinalIgnoreCase);

        var allOutcomes = new List<ImplementationOutcome>();
        var allPullRequests = new List<PullRequestRef>();
        IReadOnlyList<IReadOnlyList<WorkItem>> waves = plan.Waves();

        for (int index = 0; index < waves.Count; index++)
        {
            IReadOnlyList<WorkItem> wave = waves[index];
            int waveNumber = index + 1;

            await events.PublishAsync(RunPhase.Implementation, RunEventLevel.Milestone, "orchestrator",
                $"Onda {waveNumber}/{waves.Count}: {wave.Count} agente(s) em paralelo " +
                $"(máximo {_options.MaxParallelAgents} simultâneos).", cancellationToken: cancellationToken);

            var waveOutcomes = new ConcurrentBag<(ImplementationOutcome Outcome, PullRequestRef? PullRequest)>();

            await Parallel.ForEachAsync(
                wave,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxParallelAgents,
                    CancellationToken = cancellationToken,
                },
                async (item, token) =>
                {
                    if (!byKey.TryGetValue(item.Key, out TrackedIssue? issue))
                    {
                        return;
                    }

                    (ImplementationOutcome Outcome, PullRequestRef? PullRequest) result = await ImplementOneAsync(item, issue, plan, runId, events, token);
                    waveOutcomes.Add(result);
                });

            foreach ((ImplementationOutcome outcome, PullRequestRef? pullRequest) in waveOutcomes)
            {
                allOutcomes.Add(outcome);

                if (pullRequest is not null)
                {
                    allPullRequests.Add(pullRequest);
                }
            }

            int succeeded = waveOutcomes.Count(o => o.Outcome.Validation.Passed);

            await events.PublishAsync(RunPhase.Implementation, RunEventLevel.Milestone, "orchestrator",
                $"Onda {waveNumber} concluída: {succeeded}/{wave.Count} aprovada(s) no gate.",
                cancellationToken: cancellationToken);
        }

        return (allOutcomes, allPullRequests);
    }

    private async Task<(ImplementationOutcome Outcome, PullRequestRef? PullRequest)> ImplementOneAsync(
        WorkItem item,
        TrackedIssue issue,
        DeliveryPlan plan,
        RunId runId,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string branch = item.BranchName(issue.Number);
        string baseRef = $"origin/{_gitHub.BaseBranch}";

        AgentWorktree worktree = await _worktrees.CreateAsync(issue.Number, branch, baseRef, cancellationToken);

        await events.PublishAsync(RunPhase.Implementation, RunEventLevel.Info, "orchestrator",
            $"Worktree criado em `{worktree.Path}` no branch `{branch}`.", issue.Number, cancellationToken: cancellationToken);

        await using ICodingAgent agent = await _codingAgents.CreateAsync(worktree, cancellationToken);

        // One session id across every repair attempt, so the agent keeps its own context
        // from the previous round. It must be a real UUID: the runtime rejects anything else.
        var sessionId = Guid.NewGuid();
        string issueBody = IssueBodyRenderer.Render(item, plan.Title, runId.Value);
        string transcriptPath = Path.Combine(_options.RunsDirectory, runId.Value, $"issue-{issue.Number}.log");

        CodingAttempt attempt = null!;
        ValidationReport validation = null!;
        ReviewResult? review = null;
        int attemptNumber = 0;

        for (attemptNumber = 1; attemptNumber <= _options.MaxRepairAttempts + 1; attemptNumber++)
        {
            await events.PublishAsync(RunPhase.Implementation, RunEventLevel.Info, $"implementer-{issue.Number}",
                attemptNumber == 1
                    ? $"Implementando: {item.Title}"
                    : $"Tentativa de correção {attemptNumber - 1}…",
                issue.Number, cancellationToken: cancellationToken);

            var assignment = new CodingAssignment(
                worktree, item, issue.Number, issueBody, attemptNumber,
                attemptNumber == 1 ? null : validation,
                review?.Blocking ?? [],
                sessionId);

            attempt = await agent.ImplementAsync(assignment, _ => ValueTask.CompletedTask, cancellationToken);

            await AppendTranscriptAsync(transcriptPath, attemptNumber, attempt, cancellationToken);

            foreach (string denial in attempt.DeniedActions)
            {
                await events.PublishAsync(RunPhase.Implementation, RunEventLevel.Warning, "permission-policy",
                    denial, issue.Number, cancellationToken: cancellationToken);
            }

            await events.PublishAsync(RunPhase.Validation, RunEventLevel.Info, "validation-gate",
                "Rodando format, build com warnings-as-errors, testes e varredura de vulnerabilidades…",
                issue.Number, cancellationToken: cancellationToken);

            validation = await _validationGate.ValidateAsync(worktree, item, baseRef, attemptNumber, cancellationToken);

            await events.PublishAsync(
                RunPhase.Validation,
                validation.Passed ? RunEventLevel.Info : RunEventLevel.Warning,
                "validation-gate",
                validation.Passed
                    ? "Gate aprovado."
                    : $"Gate reprovado em: {string.Join(", ", validation.Failures.Select(f => f.Kind))}.",
                issue.Number, cancellationToken: cancellationToken);

            if (!validation.Passed)
            {
                review = null;
                continue;
            }

            review = await ReviewAsync(item, worktree, baseRef, validation, issue.Number, events, cancellationToken);

            if (review.IsClean)
            {
                break;
            }

            await events.PublishAsync(RunPhase.Review, RunEventLevel.Warning, "code-reviewer",
                $"{review.Blocking.Count} apontamento(s) bloqueante(s); devolvendo ao agente.",
                issue.Number, cancellationToken: cancellationToken);
        }

        attemptNumber = Math.Min(attemptNumber, _options.MaxRepairAttempts + 1);
        string diff = await _gitClient.GetDiffAsync(worktree.Path, baseRef, cancellationToken);

        var outcome = new ImplementationOutcome(
            issue.Number, item.Key, branch, worktree.Path, attempt.SessionId, attempt.Model,
            attemptNumber, attempt.Report, validation, diff, stopwatch.Elapsed, transcriptPath);

        PullRequestRef? pullRequest = await DeliverAsync(outcome, item, review, runId, worktree, events, cancellationToken);

        return (outcome, pullRequest);
    }

    private async Task<ReviewResult> ReviewAsync(
        WorkItem item,
        AgentWorktree worktree,
        string baseRef,
        ValidationReport validation,
        int issueNumber,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        await events.PublishAsync(RunPhase.Review, RunEventLevel.Info, "code-reviewer",
            "Revisando o diff contra as regras de engenharia…", issueNumber, cancellationToken: cancellationToken);

        string diff = await _gitClient.GetDiffAsync(worktree.Path, baseRef, cancellationToken);

        if (string.IsNullOrWhiteSpace(diff))
        {
            return new ReviewResult("O diff está vazio; não há nada a revisar.", [], false, item.AcceptanceCriteria);
        }

        const int MaxDiffCharacters = 120_000;
        string bounded = diff.Length <= MaxDiffCharacters
            ? diff
            : diff[..MaxDiffCharacters] + "\n... (diff truncado)";

        var prompt = new StringBuilder();
        prompt.AppendLine("## Issue implementada").AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"**{item.Title}** — {item.Goal}").AppendLine();
        prompt.AppendLine("### Critérios de aceite").AppendLine();

        foreach (string criterion in item.AcceptanceCriteria)
        {
            prompt.Append("- ").AppendLine(criterion);
        }

        prompt.AppendLine().AppendLine("## Resultado do gate determinístico (já executado — não repita)").AppendLine();
        prompt.AppendLine(validation.ToMarkdownTable()).AppendLine();
        prompt.AppendLine("## Diff sob revisão").AppendLine();
        prompt.AppendLine(SquadAgentSet.Untrusted("DIFF", bounded));

        ReviewResult result = await _agents.RunStructuredAsync<ReviewResult>(
            _agents.CodeReviewer, prompt.ToString(), session: null, cancellationToken);

        await events.PublishAsync(
            RunPhase.Review,
            result.IsClean ? RunEventLevel.Info : RunEventLevel.Warning,
            "code-reviewer",
            result.IsClean
                ? "Revisão limpa."
                : $"{result.Findings.Count} apontamento(s), {result.Blocking.Count} bloqueante(s).",
            issueNumber, cancellationToken: cancellationToken);

        return result;
    }

    private async Task<PullRequestRef?> DeliverAsync(
        ImplementationOutcome outcome,
        WorkItem item,
        ReviewResult? review,
        RunId runId,
        AgentWorktree worktree,
        IRunEventPublisher events,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outcome.Diff))
        {
            await events.PublishAsync(RunPhase.Delivery, RunEventLevel.Error, "orchestrator",
                "Nenhuma alteração foi produzida; nada a entregar.", outcome.IssueNumber, cancellationToken: cancellationToken);

            await _issueTracker.CommentAsync(
                outcome.IssueNumber,
                BuildFailureComment(outcome, runId),
                cancellationToken);

            return null;
        }

        // Commit anything the agent left uncommitted. Agents are told to commit, and most
        // do; this makes the pipeline tolerant of the ones that forget.
        await _gitClient.CommitAllAsync(
            worktree.Path,
            $"chore(agent): work in progress for issue #{outcome.IssueNumber}",
            cancellationToken);

        await _gitClient.PushAsync(worktree.Path, outcome.BranchName, cancellationToken);

        string body = PullRequestBodyRenderer.Render(outcome, item, review, runId.Value, _agents.ReviewModel);

        var labels = new List<string> { "agent-generated", $"area:{item.Area}" };

        if (!outcome.Validation.Passed)
        {
            labels.Add("needs-human");
        }

        PullRequestRef pullRequest = await _issueTracker.CreatePullRequestAsync(
            new PullRequestRequest(
                Title: $"{ConventionalPrefix(item.Area)}: {item.Title}",
                Body: body,
                HeadBranch: outcome.BranchName,
                BaseBranch: _gitHub.BaseBranch,
                IssueNumber: outcome.IssueNumber,
                Labels: labels,

                // Draft unless everything is clean. A pull request that failed the gate must
                // not look ready to merge.
                Draft: _options.OpenPullRequestsAsDraft || !outcome.Validation.Passed),
            cancellationToken);

        await events.PublishAsync(RunPhase.Delivery, RunEventLevel.Milestone, "pr-author",
            $"Pull request #{pullRequest.Number} aberto: {pullRequest.Url}", outcome.IssueNumber,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = pullRequest.Url },
            cancellationToken);

        return pullRequest;
    }

    private static string ConventionalPrefix(string area) => area.ToLowerInvariant() switch
    {
        "docs" => "docs",
        "tests" => "test",
        "infra" => "build",
        _ => "feat",
    };

    private static string BuildFailureComment(ImplementationOutcome outcome, RunId runId)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## 🤖 O agente não conseguiu entregar esta issue").AppendLine();
        builder.Append("**Status reportado:** `").Append(outcome.Report.Status).AppendLine("`").AppendLine();
        builder.AppendLine(outcome.Report.Summary).AppendLine();

        if (outcome.Report.BlockedReason is { } reason)
        {
            builder.AppendLine("**Motivo do bloqueio:**").AppendLine().AppendLine(reason).AppendLine();
        }

        if (!outcome.Validation.Passed)
        {
            builder.AppendLine("### Gate de validação").AppendLine();
            builder.AppendLine(outcome.Validation.ToMarkdownTable()).AppendLine();
            builder.AppendLine("<details><summary>Diagnóstico</summary>").AppendLine();
            builder.AppendLine(outcome.Validation.ToRepairBrief(maxLinesPerStep: 25));
            builder.AppendLine("</details>").AppendLine();
        }

        builder.Append("<sub>Execução `").Append(runId.Value).Append("`, ")
               .Append(outcome.Attempts.ToString(CultureInfo.InvariantCulture))
               .AppendLine(" tentativa(s). Nenhum pull request foi aberto.</sub>");

        return builder.ToString();
    }

    private static async Task AppendTranscriptAsync(
        string path,
        int attempt,
        CodingAttempt result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"===== Tentativa {attempt} =====");
        builder.AppendLine(result.RawOutput);
        builder.AppendLine();

        await File.AppendAllTextAsync(path, builder.ToString(), cancellationToken);
    }

    private static string BuildSummary(
        IReadOnlyList<ImplementationOutcome> outcomes,
        IReadOnlyList<PullRequestRef> pullRequests)
    {
        int passed = outcomes.Count(o => o.Validation.Passed);

        return $"Execução concluída: {passed}/{outcomes.Count} work item(s) aprovado(s) no gate, " +
               $"{pullRequests.Count} pull request(s) aberto(s) para revisão humana.";
    }

    private static async Task SafePublishAsync(
        IRunEventPublisher events,
        RunPhase phase,
        RunEventLevel level,
        string message)
    {
        try
        {
            await events.PublishAsync(phase, level, "orchestrator", message, cancellationToken: CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // The bus is already shutting down. Losing a final event is acceptable;
            // masking the original failure with this one is not.
        }
    }

    private SquadRunResult Finish(
        RunId runId,
        RunOutcome outcome,
        ProjectContext? context,
        RequirementsDocument? requirements,
        DeliveryPlan? plan,
        IReadOnlyList<TrackedIssue> issues,
        IReadOnlyList<ImplementationOutcome> outcomes,
        IReadOnlyList<PullRequestRef> pullRequests,
        TimeSpan duration,
        string message)
    {
        LogRunFinished(runId.Value, outcome, duration.TotalMinutes);

        return new SquadRunResult(runId, outcome, context, requirements, plan, issues, outcomes, pullRequests, duration, message);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started against {Repository}")]
    private partial void LogRunStarted(string runId, string repository);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} finished as {Outcome} after {Minutes:F1} minutes")]
    private partial void LogRunFinished(string runId, RunOutcome outcome, double minutes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} failed")]
    private partial void LogRunFailed(Exception exception, string runId);
}

