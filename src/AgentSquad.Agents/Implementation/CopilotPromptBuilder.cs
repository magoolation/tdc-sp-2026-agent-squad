using System.Globalization;
using System.Text;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Review;

namespace AgentSquad.Agents.Implementation;

/// <summary>
/// Builds the prompt handed to a Copilot coding agent.
/// </summary>
/// <remarks>
/// The prompt deliberately restates very little. <c>AGENTS.md</c>,
/// <c>.github/copilot-instructions.md</c>, <c>.github/instructions/*.instructions.md</c>
/// and <c>.github/skills/</c> are all loaded by the runtime from the worktree itself, so
/// duplicating them here would only create two sources of truth that drift apart.
/// What the prompt supplies is the part the repository cannot know: this issue, this
/// attempt, and what failed last time.
/// </remarks>
public static class CopilotPromptBuilder
{
    /// <summary>
    /// Instructions injected as organization-level guidance, above the repository's own files.
    /// </summary>
    public const string OrganizationInstructions = """
        You are an implementer in an autonomous software factory. You implement exactly one
        GitHub issue, inside one git worktree, and you do not publish anything.

        Absolute rules:
        - Never run `git push`, `git checkout`, `git switch`, `git merge`, `git rebase`,
          `git reset`, `git worktree`, or any `gh pr` command. The orchestrator owns branch
          publication and pull requests. Attempting them will be refused.
        - Never touch files outside the scope your issue declares. If you find that something
          outside that scope must change, stop and record it under `outOfScopeNeeded`.
        - Never commit a secret, a token, a connection string, or a `.env` file.
        - Never suppress an analyzer to make the build pass. Fix the code.
        - Never weaken, skip or delete a test to make it green.
        - Every behaviour you add comes with a test.

        Read `AGENTS.md` and `docs/engineering-rules.md` in the worktree before you write
        code; they are the authority on style, security and testing here.

        Finish by running the full local gate and printing the AGENT_REPORT block.
        """;

    /// <summary>
    /// Builds the prompt for one attempt.
    /// </summary>
    /// <param name="assignment">The assignment.</param>
    /// <returns>The prompt text.</returns>
    public static string Build(CodingAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var builder = new StringBuilder();

        if (assignment.Attempt == 1)
        {
            AppendInitial(builder, assignment);
        }
        else
        {
            AppendRepair(builder, assignment);
        }

        AppendGate(builder);
        AppendReportContract(builder, assignment.IssueNumber);

        return builder.ToString();
    }

    private static void AppendInitial(StringBuilder builder, CodingAssignment assignment)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Implemente a issue #{assignment.IssueNumber}");
        builder.AppendLine();
        builder.AppendLine("A especificação completa está abaixo. Ela é a sua fonte da verdade.");
        builder.AppendLine();

        // The issue body is machine-generated here, but the same pipeline accepts issues a
        // human wrote. Delimiting it keeps the boundary between data and instruction
        // explicit either way (AI-001).
        builder.AppendLine("<<<UNTRUSTED ISSUE");
        builder.AppendLine(assignment.IssueBody);
        builder.AppendLine("UNTRUSTED>>>");
        builder.AppendLine();

        builder.AppendLine("## Procedimento");
        builder.AppendLine();
        builder.AppendLine("1. Leia `AGENTS.md` e `docs/engineering-rules.md` neste worktree.");
        builder.AppendLine("2. Explore o código vizinho e siga os padrões que já existem.");
        builder.AppendLine("3. Confirme que todos os arquivos que pretende tocar estão na tabela de escopo da issue.");
        builder.AppendLine("4. Escreva o teste primeiro quando o comportamento for testável.");
        builder.AppendLine("5. Implemente.");
        builder.AppendLine("6. Rode o gate local completo até ficar 100% verde.");
        builder.AppendLine("7. Faça `git add` dos arquivos e `git commit` em Conventional Commits, com `Refs: #" +
                           assignment.IssueNumber.ToString(CultureInfo.InvariantCulture) + "`.");
        builder.AppendLine();
    }

    private static void AppendRepair(StringBuilder builder, CodingAssignment assignment)
    {
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"# Correção — issue #{assignment.IssueNumber}, tentativa {assignment.Attempt}");
        builder.AppendLine();
        builder.AppendLine("Sua implementação anterior está no worktree, mas foi reprovada. Corrija-a.");
        builder.AppendLine();
        builder.AppendLine("**Não recomece do zero.** Leia o diagnóstico, encontre a causa e faça a menor correção que a resolve.");
        builder.AppendLine();

        if (assignment.PreviousFailure is { } failure)
        {
            builder.AppendLine("## O que o gate de validação reprovou");
            builder.AppendLine();
            builder.AppendLine(failure.ToRepairBrief());
        }

        if (assignment.PreviousFindings.Count > 0)
        {
            builder.AppendLine("## Apontamentos bloqueantes da revisão de código");
            builder.AppendLine();

            foreach (ReviewFinding finding in assignment.PreviousFindings)
            {
                builder.Append("- **").Append(finding.Rule).Append("** `").Append(finding.File);

                if (finding.Line is { } line)
                {
                    builder.Append(':').Append(line.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append("` — ").Append(finding.Finding)
                       .Append(" **Correção esperada:** ").AppendLine(finding.Fix);
            }

            builder.AppendLine();
        }

        builder.AppendLine("Se o diagnóstico estiver errado — por exemplo, se o teste que falhou é que está incorreto —");
        builder.AppendLine("diga isso explicitamente em `risks` e explique o porquê. Não force a passagem do gate.");
        builder.AppendLine();
    }

    private static void AppendGate(StringBuilder builder)
    {
        builder.AppendLine("## Gate local obrigatório");
        builder.AppendLine();
        builder.AppendLine("Rode na raiz do worktree, nesta ordem. Todos precisam sair com código 0:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine("dotnet restore");
        builder.AppendLine("dotnet format --severity info");
        builder.AppendLine("dotnet format --verify-no-changes --severity info");
        builder.AppendLine("dotnet build --no-restore -c Release -warnaserror");
        builder.AppendLine("dotnet test --no-build -c Release");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Se falhar, corrija o código — nunca o analisador, nunca o teste.");
        builder.AppendLine("Depois de 3 tentativas sem sucesso, pare e reporte `blocked`.");
        builder.AppendLine();
    }

    private static void AppendReportContract(StringBuilder builder, int issueNumber)
    {
        builder.AppendLine("## Relatório final (obrigatório)");
        builder.AppendLine();
        builder.AppendLine("Encerre imprimindo **exatamente** este bloco. O orquestrador faz parse dele:");
        builder.AppendLine();
        builder.AppendLine("````markdown");
        builder.AppendLine("```json AGENT_REPORT");
        builder.AppendLine("{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"issue\": {issueNumber},");
        builder.AppendLine("  \"status\": \"completed\",");
        builder.AppendLine("  \"summary\": \"uma frase sobre o que foi entregue\",");
        builder.AppendLine("  \"filesChanged\": [\"src/.../Foo.cs\", \"tests/.../FooTests.cs\"],");
        builder.AppendLine("  \"testsAdded\": [\"FooTests.Method_Should_DoX_When_Y\"],");
        builder.AppendLine("  \"outOfScopeNeeded\": [],");
        builder.AppendLine("  \"risks\": [\"o que um revisor humano deveria olhar com atenção\"],");
        builder.AppendLine("  \"blockedReason\": null");
        builder.AppendLine("}");
        builder.AppendLine("```");
        builder.AppendLine("````");
        builder.AppendLine();
        builder.AppendLine("`status` é `completed`, `partial` ou `blocked`. Reportar `blocked` com um motivo honesto");
        builder.AppendLine("é um resultado válido e útil — melhor do que um PR que quase funciona.");
    }
}
