using AgentSquad.Agents.Rendering;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Implementation;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Validation;

namespace AgentSquad.Agents.Tests.Rendering;

/// <summary>
/// The delivery pull request is the only human gate in a run, so its body has to be honest
/// about what did <b>not</b> make it in. These tests pin that honesty down.
/// </summary>
public sealed class DeliveryPullRequestRendererTests
{
    [Fact]
    public void Render_ListsEveryOutcomeWithItsPullRequest()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(),
            "20260925-101500-abcdef",
            "agent/run-20260925-101500-abcdef",
            "main",
            [Outcome(11, "W-01", passed: true), Outcome(12, "W-02", passed: true)],
            [PullRequest(31, 11), PullRequest(32, 12)]);

        body.Should().Contain("| #11 |").And.Contain("| #12 |");
        body.Should().Contain("#31").And.Contain("#32");
        body.Should().Contain("**2 de 2**");
    }

    [Fact]
    public void Render_NamesTheBranchesBeingMerged()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(),
            "20260925-101500-abcdef",
            "agent/run-20260925-101500-abcdef",
            "main",
            [Outcome(11, "W-01", passed: true)],
            [PullRequest(31, 11)]);

        body.Should().Contain("`agent/run-20260925-101500-abcdef`").And.Contain("`main`");
    }

    [Fact]
    public void Render_CallsOutWorkItemsThatFailedTheGate()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(),
            "20260925-101500-abcdef",
            "agent/run-20260925-101500-abcdef",
            "main",
            [Outcome(11, "W-01", passed: true), Outcome(12, "W-02", passed: false)],
            [PullRequest(31, 11)]);

        body.Should().Contain("O que não entrou");
        body.Should().Contain("**#12**");
        body.Should().Contain("Test");
        body.Should().Contain("**1 de 2**");
    }

    [Fact]
    public void Render_OmitsTheFailureSectionWhenEverythingPassed()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(),
            "20260925-101500-abcdef",
            "agent/run-20260925-101500-abcdef",
            "main",
            [Outcome(11, "W-01", passed: true)],
            [PullRequest(31, 11)]);

        body.Should().NotContain("O que não entrou");
    }

    [Fact]
    public void Render_SurfacesAgentDeclaredRisks()
    {
        ImplementationOutcome outcome = Outcome(11, "W-01", passed: true) with
        {
            Report = Outcome(11, "W-01", passed: true).Report with
            {
                Risks = ["O índice novo muda o plano de consulta do catálogo."],
                OutOfScopeNeeded = ["Migração dos dados legados."],
            },
        };

        string body = DeliveryPullRequestRenderer.Render(
            Plan(), "20260925-101500-abcdef", "agent/run-x", "main", [outcome], [PullRequest(31, 11)]);

        body.Should().Contain("O índice novo muda o plano de consulta do catálogo.");
        body.Should().Contain("Necessário mas deixado fora de escopo: Migração dos dados legados.");
    }

    [Fact]
    public void Render_KeepsTheMergeWithTheHuman()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(), "20260925-101500-abcdef", "agent/run-x", "main",
            [Outcome(11, "W-01", passed: true)], [PullRequest(31, 11)]);

        body.Should().Contain("**O merge é seu.**");
    }

    [Fact]
    public void Render_ShowsTheDefaultDecisionsTheFactoryTookAlone()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(), "20260925-101500-abcdef", "agent/run-x", "main",
            [Outcome(11, "W-01", passed: true)], [PullRequest(31, 11)]);

        body.Should().Contain("Paginação por cursor em vez de offset.");
    }

    [Fact]
    public void Render_FallsBackToADashWhenAnItemHasNoPullRequest()
    {
        string body = DeliveryPullRequestRenderer.Render(
            Plan(), "20260925-101500-abcdef", "agent/run-x", "main",
            [Outcome(12, "W-02", passed: false)], []);

        body.Should().Contain("| #12 |").And.Contain("| — |");
    }

    private static DeliveryPlan Plan() =>
        new(
            Title: "catálogo de produtos",
            Overview: "Um catálogo mínimo com busca paginada.",
            Architecture: ["Vertical slices por caso de uso."],
            Items:
            [
                Item("W-01", "criar o modelo de produto", "core", 1),
                Item("W-02", "expor a busca paginada", "api", 2),
            ],
            DefaultDecisions: ["Paginação por cursor em vez de offset."],
            Risks: ["Sem dados reais para calibrar o índice."]);

    private static WorkItem Item(string key, string title, string area, int wave) =>
        new(
            Key: key,
            Title: title,
            Goal: $"Entregar {title}.",
            Context: "Segue os padrões existentes.",
            Files: [new PlannedFile($"src/{key}.cs", FileAction.Create)],
            OutOfScope: [],
            AcceptanceCriteria: ["Dado X, quando Y, então Z."],
            ImplementationNotes: [],
            RequirementIds: ["R-01"],
            DependsOn: [],
            Wave: wave,
            Area: area,
            Size: WorkItemSize.Small,
            NeedsHuman: false);

    private static ImplementationOutcome Outcome(int issue, string key, bool passed) =>
        new(
            IssueNumber: issue,
            WorkItemKey: key,
            BranchName: $"agent/issue-{issue}",
            WorktreePath: $"C:/wt/{issue}",
            SessionId: "session",
            Model: "gpt-5.3-codex",
            Attempts: passed ? 1 : 3,
            Report: new AgentReport(
                Issue: issue,
                Status: passed ? ImplementationStatus.Completed : ImplementationStatus.Failed,
                Summary: $"Trabalho do item {key}.",
                FilesChanged: [$"src/{key}.cs"],
                TestsAdded: [],
                OutOfScopeNeeded: [],
                Risks: [],
                BlockedReason: null),
            Validation: new ValidationReport(
                IssueNumber: issue,
                Attempt: 1,
                Steps:
                [
                    new ValidationStep(
                        ValidationStepKind.Build, ValidationOutcome.Passed,
                        TimeSpan.FromSeconds(9), 0, "ok", []),
                    new ValidationStep(
                        ValidationStepKind.Test,
                        passed ? ValidationOutcome.Passed : ValidationOutcome.Failed,
                        TimeSpan.FromSeconds(14), passed ? 0 : 1, passed ? "ok" : "2 falhas", []),
                ],
                StartedAt: DateTimeOffset.UnixEpoch,
                Duration: TimeSpan.FromSeconds(23)),
            Diff: "diff",
            Duration: TimeSpan.FromMinutes(4.5),
            TranscriptPath: $"runs/{issue}.jsonl");

    private static PullRequestRef PullRequest(int number, int issue) =>
        new(Number: number, Url: $"https://github.com/o/r/pull/{number}", Branch: $"agent/issue-{issue}",
            IssueNumber: issue, IsDraft: true);
}
