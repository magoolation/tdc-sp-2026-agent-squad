using System.Globalization;
using System.Text;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Review;
using AgentSquad.Core.Validation;

namespace AgentSquad.Agents.Rendering;

/// <summary>
/// Renders the body of a pull request opened by the factory.
/// </summary>
/// <remarks>
/// <para>
/// The body is written for the human who has to decide whether to merge. It answers three
/// questions in order: what changed, what was verified mechanically, and what deserves
/// their attention. Nothing is hidden — refused actions, files touched outside the declared
/// scope, and the agent's own reported risks all appear.
/// </para>
/// <para>
/// It also states which model produced the code, which AI-009 requires.
/// </para>
/// </remarks>
public static class PullRequestBodyRenderer
{
    /// <summary>
    /// Renders the pull-request body.
    /// </summary>
    /// <param name="outcome">The implementation outcome.</param>
    /// <param name="item">The work item that was implemented.</param>
    /// <param name="review">The automated review result, if one ran.</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="reviewModel">Model used for the automated review.</param>
    /// <returns>The Markdown body.</returns>
    public static string Render(
        ImplementationOutcome outcome,
        WorkItem item,
        ReviewResult? review,
        string runId,
        string reviewModel)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(item);

        var builder = new StringBuilder();

        builder.AppendLine(CultureInfo.InvariantCulture, $"Closes #{outcome.IssueNumber}");
        builder.AppendLine();

        builder.AppendLine("## O que mudou").AppendLine();
        builder.AppendLine(outcome.Report.Summary).AppendLine();

        AppendValidation(builder, outcome.Validation);
        AppendReview(builder, review, reviewModel);
        AppendAttention(builder, outcome, review);
        AppendFiles(builder, outcome);
        AppendTests(builder, outcome);
        AppendProvenance(builder, outcome, item, runId);

        return builder.ToString();
    }

    private static void AppendValidation(StringBuilder builder, ValidationReport validation)
    {
        builder.AppendLine("## Validação automatizada").AppendLine();
        builder.AppendLine(validation.ToMarkdownTable());
        builder.AppendLine();

        if (!validation.Passed)
        {
            builder.AppendLine("> ⚠️ **Este PR não passou no gate de validação.** Ele foi aberto para inspeção humana,");
            builder.AppendLine("> não para merge. Veja o diagnóstico abaixo.").AppendLine();
            builder.AppendLine("<details><summary>Diagnóstico</summary>").AppendLine();
            builder.AppendLine(validation.ToRepairBrief(maxLinesPerStep: 30));
            builder.AppendLine("</details>").AppendLine();
        }
    }

    private static void AppendReview(StringBuilder builder, ReviewResult? review, string reviewModel)
    {
        if (review is null)
        {
            return;
        }

        builder.AppendLine("## Revisão automatizada").AppendLine();
        builder.AppendLine(review.Summary).AppendLine();

        if (!review.AcceptanceCriteriaMet && review.UnmetCriteria.Count > 0)
        {
            builder.AppendLine("**Critérios de aceite sem evidência no diff:**").AppendLine();

            foreach (string criterion in review.UnmetCriteria)
            {
                builder.Append("- ").AppendLine(criterion);
            }

            builder.AppendLine();
        }

        builder.AppendLine(review.ToMarkdown()).AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"<sub>Revisão feita por `{reviewModel}`.</sub>").AppendLine();
    }

    private static void AppendAttention(StringBuilder builder, ImplementationOutcome outcome, ReviewResult? review)
    {
        List<string> attention =
        [
            .. outcome.Report.Risks,
            .. outcome.Report.OutOfScopeNeeded.Select(o => $"Necessário mas fora do escopo desta issue: {o}"),
            .. outcome.Validation.Steps
                     .Where(s => s.Kind == ValidationStepKind.ScopeCompliance)
                     .SelectMany(s => s.Diagnostics),
            .. review?.Blocking.Select(f => $"Apontamento bloqueante {f.Rule} em `{f.File}`") ?? [],
        ];

        if (outcome.Report.Status == ImplementationStatus.Blocked && outcome.Report.BlockedReason is { } reason)
        {
            attention.Insert(0, $"O agente parou e reportou bloqueio: {reason}");
        }

        if (attention.Count == 0)
        {
            return;
        }

        builder.AppendLine("## ⚠️ Atenção do revisor").AppendLine();

        foreach (string note in attention)
        {
            builder.Append("- ").AppendLine(note);
        }

        builder.AppendLine();
    }

    private static void AppendFiles(StringBuilder builder, ImplementationOutcome outcome)
    {
        if (outcome.Report.FilesChanged.Count == 0)
        {
            return;
        }

        builder.AppendLine("<details><summary>Arquivos alterados</summary>").AppendLine();

        foreach (string file in outcome.Report.FilesChanged)
        {
            builder.Append("- `").Append(file).AppendLine("`");
        }

        builder.AppendLine().AppendLine("</details>").AppendLine();
    }

    private static void AppendTests(StringBuilder builder, ImplementationOutcome outcome)
    {
        if (outcome.Report.TestsAdded.Count == 0)
        {
            builder.AppendLine("> ℹ️ O agente não declarou nenhum teste novo. Verifique se isso é adequado para esta mudança (TST-001).");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("<details><summary>Testes adicionados</summary>").AppendLine();

        foreach (string test in outcome.Report.TestsAdded)
        {
            builder.Append("- `").Append(test).AppendLine("`");
        }

        builder.AppendLine().AppendLine("</details>").AppendLine();
    }

    private static void AppendProvenance(
        StringBuilder builder,
        ImplementationOutcome outcome,
        WorkItem item,
        string runId)
    {
        builder.AppendLine("---").AppendLine();
        builder.AppendLine("## Procedência").AppendLine();

        builder.AppendLine("| | |");
        builder.AppendLine("|---|---|");
        builder.Append("| Execução | `").Append(runId).AppendLine("` |");
        builder.Append("| Work item | `").Append(item.Key).Append("` (onda ")
               .Append(item.Wave.ToString(CultureInfo.InvariantCulture)).AppendLine(") |");
        builder.Append("| Agente implementador | GitHub Copilot via `Microsoft.Agents.AI.GitHub.Copilot` |");
        builder.AppendLine();
        builder.Append("| Modelo | `").Append(outcome.Model).AppendLine("` |");
        builder.Append("| Tentativas | ").Append(outcome.Attempts.ToString(CultureInfo.InvariantCulture)).AppendLine(" |");
        builder.Append("| Duração | ").Append(outcome.Duration.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture)).AppendLine(" min |");
        builder.Append("| Branch | `").Append(outcome.BranchName).AppendLine("` |");

        if (outcome.SessionId is { } session)
        {
            builder.Append("| Sessão Copilot | `").Append(session).AppendLine("` |");
        }

        builder.AppendLine();

        builder.AppendLine("> 🤖 **Pull request gerado por um agente autônomo.**");
        builder.AppendLine("> O código passou pelo mesmo gate de qualidade exigido de um PR humano, mas");
        builder.AppendLine("> **o merge é uma decisão humana** — leia o diff antes de aprovar.");
    }
}
