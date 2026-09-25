using System.Globalization;
using System.Text;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Requirements;

namespace AgentSquad.Agents.Orchestration;

/// <summary>
/// Renders plans and requirements as Markdown, for prompts and for human review.
/// </summary>
/// <remarks>
/// The same rendering feeds the critic agent and the approval screen on purpose: the human
/// approving the plan should be looking at exactly what the critic looked at.
/// </remarks>
public static class PlanFormatter
{
    /// <summary>Renders a requirements document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>Markdown.</returns>
    public static string Requirements(RequirementsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Title).AppendLine();
        builder.AppendLine(document.Summary).AppendLine();

        builder.AppendLine("## Requisitos").AppendLine();

        foreach (Requirement requirement in document.Requirements)
        {
            builder.Append("### ").Append(requirement.Id).Append(" — ").Append(requirement.Statement).AppendLine();
            builder.AppendLine();
            builder.Append("- Tipo: `").Append(requirement.Kind).Append("` · Confiança: `")
                   .Append(requirement.Confidence).Append("` · Origem: `").Append(requirement.Source).AppendLine("`");
            builder.Append("- Motivo: ").AppendLine(requirement.Rationale);

            if (requirement.AcceptanceCriteria.Count > 0)
            {
                builder.AppendLine("- Critérios de aceite:");

                foreach (string criterion in requirement.AcceptanceCriteria)
                {
                    builder.Append("  - ").AppendLine(criterion);
                }
            }

            builder.AppendLine();
        }

        if (document.OutOfScope.Count > 0)
        {
            builder.AppendLine("## Fora de escopo").AppendLine();

            foreach (string item in document.OutOfScope)
            {
                builder.Append("- ").AppendLine(item);
            }

            builder.AppendLine();
        }

        if (document.Assumptions.Count > 0)
        {
            builder.AppendLine("## Suposições assumidas pela fábrica").AppendLine();

            foreach (Requirement assumption in document.Assumptions)
            {
                builder.Append("- **").Append(assumption.Id).Append("** ").AppendLine(assumption.Statement);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>Renders a delivery plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>Markdown.</returns>
    public static string Plan(DeliveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(plan.Title).AppendLine();
        builder.AppendLine(plan.Overview).AppendLine();

        if (plan.Architecture.Count > 0)
        {
            builder.AppendLine("## Decisões de arquitetura").AppendLine();

            foreach (string decision in plan.Architecture)
            {
                builder.Append("- ").AppendLine(decision);
            }

            builder.AppendLine();
        }

        IReadOnlyList<IReadOnlyList<WorkItem>> waves = plan.Waves();

        for (int i = 0; i < waves.Count; i++)
        {
            builder.Append("## Onda ").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                   .Append(" — ").Append(waves[i].Count.ToString(CultureInfo.InvariantCulture))
                   .AppendLine(" item(ns) em paralelo").AppendLine();

            foreach (WorkItem item in waves[i])
            {
                builder.Append("### ").Append(item.Key).Append(" — ").AppendLine(item.Title).AppendLine();
                builder.Append("**Objetivo:** ").AppendLine(item.Goal).AppendLine();
                builder.Append("**Área:** `").Append(item.Area).Append("` · **Tamanho:** `")
                       .Append(item.Size).Append('`');

                if (item.DependsOn.Count > 0)
                {
                    builder.Append(" · **Depende de:** ").Append(string.Join(", ", item.DependsOn));
                }

                if (item.RequirementIds.Count > 0)
                {
                    builder.Append(" · **Requisitos:** ").Append(string.Join(", ", item.RequirementIds));
                }

                builder.AppendLine().AppendLine();

                builder.AppendLine("**Arquivos:**").AppendLine();

                foreach (PlannedFile file in item.Files)
                {
                    builder.Append("- `").Append(file.Path).Append("` (").Append(file.Action).AppendLine(")");
                }

                builder.AppendLine();
                builder.AppendLine("**Critérios de aceite:**").AppendLine();

                foreach (string criterion in item.AcceptanceCriteria)
                {
                    builder.Append("- ").AppendLine(criterion);
                }

                builder.AppendLine();
            }
        }

        if (plan.DefaultDecisions.Count > 0)
        {
            builder.AppendLine("## Decisões tomadas por padrão (conteste se discordar)").AppendLine();

            foreach (string decision in plan.DefaultDecisions)
            {
                builder.Append("- ").AppendLine(decision);
            }

            builder.AppendLine();
        }

        if (plan.Risks.Count > 0)
        {
            builder.AppendLine("## Riscos").AppendLine();

            foreach (string risk in plan.Risks)
            {
                builder.Append("- ").AppendLine(risk);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
