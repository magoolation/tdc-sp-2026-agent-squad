using System.Globalization;
using System.Text;
using AgentSquad.Core.Planning;

namespace AgentSquad.Agents.Rendering;

/// <summary>
/// Renders a work item as a GitHub issue body.
/// </summary>
/// <remarks>
/// The body is the coding agent's entire specification. It has to be self-contained:
/// the agent that reads it has the repository, <c>AGENTS.md</c>, the engineering rules and
/// this text, and no conversation to fall back on.
/// </remarks>
public static class IssueBodyRenderer
{
    /// <summary>
    /// Renders one work item.
    /// </summary>
    /// <param name="item">The work item.</param>
    /// <param name="planTitle">The delivery this item belongs to.</param>
    /// <param name="runId">The run that produced it, for traceability.</param>
    /// <returns>The Markdown body.</returns>
    public static string Render(WorkItem item, string planTitle, string runId)
    {
        ArgumentNullException.ThrowIfNull(item);

        var builder = new StringBuilder();

        builder.AppendLine("## Objetivo").AppendLine();
        builder.AppendLine(item.Goal).AppendLine();

        builder.AppendLine("## Contexto").AppendLine();
        builder.AppendLine(item.Context).AppendLine();

        builder.AppendLine("## Escopo — arquivos previstos").AppendLine();
        builder.AppendLine("| Arquivo | Ação |");
        builder.AppendLine("|---|---|");

        foreach (PlannedFile file in item.Files)
        {
            string action = file.Action switch
            {
                FileAction.Create => "criar",
                FileAction.Modify => "alterar",
                _ => "remover",
            };

            builder.Append("| `").Append(file.Path).Append("` | ").Append(action).AppendLine(" |");
        }

        builder.AppendLine();

        if (item.OutOfScope.Count > 0)
        {
            builder.Append("**Fora de escopo:** ").AppendLine(string.Join("; ", item.OutOfScope)).AppendLine();
        }

        builder.AppendLine("## Critérios de aceite").AppendLine();

        foreach (string criterion in item.AcceptanceCriteria)
        {
            builder.Append("- [ ] ").AppendLine(criterion);
        }

        builder.AppendLine("- [ ] `dotnet format --verify-no-changes`, `dotnet build -warnaserror` e `dotnet test` verdes");
        builder.AppendLine();

        if (item.ImplementationNotes.Count > 0)
        {
            builder.AppendLine("## Notas de implementação").AppendLine();

            foreach (string note in item.ImplementationNotes)
            {
                builder.Append("- ").AppendLine(note);
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Definição de pronto").AppendLine();
        builder.AppendLine("PR aberto, gate de validação verde, revisão automatizada sem apontamento bloqueante,");
        builder.AppendLine(CultureInfo.InvariantCulture, $"e o corpo do PR fechando esta issue com `Closes #<n>`.");
        builder.AppendLine();

        // A machine-readable block so the orchestrator can rebuild the dependency graph and
        // re-check the file-conflict rule from the issues alone, without the original plan.
        builder.AppendLine("<!-- squad:meta");
        builder.Append("plan: ").AppendLine(planTitle);
        builder.Append("run: ").AppendLine(runId);
        builder.Append("key: ").AppendLine(item.Key);
        builder.Append("wave: ").AppendLine(item.Wave.ToString(CultureInfo.InvariantCulture));
        builder.Append("area: ").AppendLine(item.Area);
        builder.Append("size: ").AppendLine(item.Size.ToString().ToLowerInvariant());
        builder.Append("dependsOn: [").Append(string.Join(", ", item.DependsOn)).AppendLine("]");
        builder.Append("requirements: [").Append(string.Join(", ", item.RequirementIds)).AppendLine("]");
        builder.AppendLine("files:");

        foreach (PlannedFile file in item.Files)
        {
            builder.Append("  - ").AppendLine(file.Path);
        }

        builder.AppendLine("-->");
        builder.AppendLine();

        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"> 🤖 Issue gerada automaticamente pelo **Agent Squad** na execução `{runId}`, a partir do plano _{planTitle}_.");
        builder.AppendLine("> Este trabalho será implementado por um agente autônomo em um `git worktree` isolado.");

        return builder.ToString();
    }
}
