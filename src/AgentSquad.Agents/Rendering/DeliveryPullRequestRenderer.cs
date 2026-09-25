using System.Globalization;
using System.Text;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Planning;

namespace AgentSquad.Agents.Rendering;

/// <summary>
/// Renders the body of the pull request that delivers a whole run.
/// </summary>
/// <remarks>
/// Written for the person who has to decide whether a run's worth of agent-produced code
/// enters the branch the team ships from. It answers, in order: what this contains, what
/// was verified mechanically, what did <b>not</b> pass, and what each individual change was.
/// </remarks>
public static class DeliveryPullRequestRenderer
{
    /// <summary>
    /// Renders the delivery pull-request body.
    /// </summary>
    /// <param name="plan">The delivery plan.</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="integrationBranch">The branch holding the run's work.</param>
    /// <param name="baseBranch">The branch being merged into.</param>
    /// <param name="outcomes">Every implementation outcome.</param>
    /// <param name="itemPullRequests">The per-item pull requests.</param>
    /// <returns>The Markdown body.</returns>
    public static string Render(
        DeliveryPlan plan,
        string runId,
        string integrationBranch,
        string baseBranch,
        IReadOnlyList<ImplementationOutcome> outcomes,
        IReadOnlyList<PullRequestRef> itemPullRequests)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(itemPullRequests);

        IReadOnlyList<ImplementationOutcome> passed = [.. outcomes.Where(o => o.Validation.Passed)];
        IReadOnlyList<ImplementationOutcome> failed = [.. outcomes.Where(o => !o.Validation.Passed)];

        var byIssue = itemPullRequests
            .GroupBy(p => p.IssueNumber)
            .ToDictionary(g => g.Key, g => g.First());

        var builder = new StringBuilder();

        builder.AppendLine("## A decisão é esta").AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Este pull request leva **{passed.Count} de {outcomes.Count}** work items de `{integrationBranch}` " +
            $"para `{baseBranch}`.");
        builder.AppendLine();
        builder.AppendLine("Os pull requests individuais abaixo são as **unidades de revisão** — leia-os para " +
                           "entender cada mudança isoladamente. **Este aqui é o portão**: é a única coisa na " +
                           "execução que propõe alterar a branch da qual o time entrega.");
        builder.AppendLine();

        builder.AppendLine("## O que foi entregue").AppendLine();
        builder.AppendLine(plan.Overview).AppendLine();

        builder.AppendLine("| Issue | Item | Área | Gate | Tentativas | Pull request |");
        builder.AppendLine("|---|---|---|---|---|---|");

        foreach (ImplementationOutcome outcome in outcomes.OrderBy(o => o.IssueNumber))
        {
            WorkItem? item = plan.Items.FirstOrDefault(i =>
                string.Equals(i.Key, outcome.WorkItemKey, StringComparison.OrdinalIgnoreCase));

            string pullRequest = byIssue.TryGetValue(outcome.IssueNumber, out PullRequestRef? reference)
                ? $"#{reference.Number.ToString(CultureInfo.InvariantCulture)}"
                : "—";

            builder.Append("| #").Append(outcome.IssueNumber.ToString(CultureInfo.InvariantCulture))
                   .Append(" | ").Append(item?.Title ?? outcome.WorkItemKey)
                   .Append(" | `").Append(item?.Area ?? "—")
                   .Append("` | ").Append(outcome.Validation.Passed ? "✅" : "❌")
                   .Append(" | ").Append(outcome.Attempts.ToString(CultureInfo.InvariantCulture))
                   .Append(" | ").Append(pullRequest)
                   .AppendLine(" |");
        }

        builder.AppendLine();

        if (failed.Count > 0)
        {
            builder.AppendLine("## ⚠️ O que não entrou").AppendLine();
            builder.AppendLine("Estes work items reprovaram no gate e **não estão** neste branch. As issues " +
                               "continuam abertas e os pull requests deles seguem marcados `needs-human`.")
                   .AppendLine();

            foreach (ImplementationOutcome outcome in failed)
            {
                builder.Append("- **#").Append(outcome.IssueNumber.ToString(CultureInfo.InvariantCulture))
                       .Append("** — ").Append(outcome.Report.Summary)
                       .Append(" _(reprovou em: ")
                       .Append(string.Join(", ", outcome.Validation.Failures.Select(f => f.Kind)))
                       .AppendLine(")_");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Antes de aprovar").AppendLine();
        builder.AppendLine("O gate garante que o código compila sem warnings, está formatado, passa nos testes " +
                           "e não traz pacote vulnerável nem segredo. Ele **não** garante que resolve o problema " +
                           "certo. Vale olhar:").AppendLine();

        List<string> attention =
        [
            .. outcomes.SelectMany(o => o.Report.Risks).Distinct(StringComparer.Ordinal),
            .. outcomes.SelectMany(o => o.Report.OutOfScopeNeeded)
                       .Distinct(StringComparer.Ordinal)
                       .Select(o => $"Necessário mas deixado fora de escopo: {o}"),
        ];

        if (attention.Count == 0)
        {
            builder.AppendLine("- Nenhum risco foi declarado pelos agentes. Leia o diff mesmo assim.");
        }
        else
        {
            foreach (string note in attention.Take(15))
            {
                builder.Append("- ").AppendLine(note);
            }
        }

        builder.AppendLine();

        if (plan.DefaultDecisions.Count > 0)
        {
            builder.AppendLine("<details><summary>Decisões que a fábrica tomou sem perguntar</summary>").AppendLine();

            foreach (string decision in plan.DefaultDecisions)
            {
                builder.Append("- ").AppendLine(decision);
            }

            builder.AppendLine().AppendLine("</details>").AppendLine();
        }

        double totalMinutes = outcomes.Sum(o => o.Duration.TotalMinutes);

        builder.AppendLine("---").AppendLine();
        builder.AppendLine("| | |");
        builder.AppendLine("|---|---|");
        builder.Append("| Execução | `").Append(runId).AppendLine("` |");
        builder.Append("| Branch de integração | `").Append(integrationBranch).AppendLine("` |");
        builder.Append("| Work items | ").Append(passed.Count.ToString(CultureInfo.InvariantCulture))
               .Append('/').Append(outcomes.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" aprovados no gate |");
        builder.Append("| Tempo somado dos agentes | ")
               .Append(totalMinutes.ToString("F1", CultureInfo.InvariantCulture)).AppendLine(" min |");
        builder.AppendLine();

        builder.AppendLine("> 🤖 **Entrega produzida por agentes autônomos.**");
        builder.AppendLine("> Cada mudança passou pelo mesmo gate exigido de um pull request humano.");
        builder.AppendLine("> **O merge é seu.**");

        return builder.ToString();
    }
}
