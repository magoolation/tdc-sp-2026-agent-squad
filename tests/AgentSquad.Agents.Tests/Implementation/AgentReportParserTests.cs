using AgentSquad.Agents.Implementation;
using AgentSquad.Core.Implementation;

namespace AgentSquad.Agents.Tests.Implementation;

/// <summary>
/// Tests for reading the report a coding agent prints at the end of its run.
/// </summary>
/// <remarks>
/// Two properties matter here and they pull in opposite directions. The parser must be
/// forgiving, because a model asked for a fenced JSON block will sometimes decorate it —
/// and failing a run that produced good code over a stray backtick would be absurd. But it
/// must never invent a success: when nothing can be read, the outcome has to be something
/// the pipeline treats with suspicion, not <c>Completed</c>.
/// </remarks>
public sealed class AgentReportParserTests
{
    [Fact]
    public void Parse_Should_ReadTheReport_When_TheContractIsFollowedExactly()
    {
        // Arrange
        const string Output = """
            I implemented the wave planner and its tests.

            ```json AGENT_REPORT
            {
              "issue": 42,
              "status": "completed",
              "summary": "Adiciona o planejador de ondas com ordenação por dependência.",
              "filesChanged": ["src/Core/WavePlanner.cs", "tests/Core/WavePlannerTests.cs"],
              "testsAdded": ["WavePlannerTests.Plan_Should_OrderByDependency"],
              "outOfScopeNeeded": [],
              "risks": ["A ordenação assume que o grafo é acíclico."],
              "blockedReason": null
            }
            ```
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 42);

        // Assert
        report.Issue.Should().Be(42);
        report.Status.Should().Be(ImplementationStatus.Completed);
        report.Summary.Should().StartWith("Adiciona o planejador");
        report.FilesChanged.Should().HaveCount(2);
        report.TestsAdded.Should().ContainSingle();
        report.Risks.Should().ContainSingle();
        report.BlockedReason.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_ReadTheReport_When_TheFenceHasNoAgentReportTag()
    {
        // Arrange — a common, harmless deviation from the contract.
        const string Output = """
            Done.

            ```json
            {
              "issue": 7,
              "status": "blocked",
              "summary": "Não consegui prosseguir.",
              "blockedReason": "O teste existente em OrderTests.cs contradiz o critério de aceite."
            }
            ```
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 7);

        // Assert
        report.Status.Should().Be(ImplementationStatus.Blocked);
        report.BlockedReason.Should().Contain("contradiz");
    }

    [Fact]
    public void Parse_Should_UseTheLastReport_When_SeveralArePresent()
    {
        // Arrange — an agent that retried may print an early report and then a final one.
        const string Output = """
            ```json AGENT_REPORT
            { "issue": 1, "status": "partial", "summary": "primeira tentativa" }
            ```

            Depois de corrigir o build:

            ```json AGENT_REPORT
            { "issue": 1, "status": "completed", "summary": "versão final" }
            ```
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 1);

        // Assert
        report.Status.Should().Be(ImplementationStatus.Completed);
        report.Summary.Should().Be("versão final");
    }

    [Fact]
    public void Parse_Should_FallBackToTheIssueNumber_When_TheReportOmitsIt()
    {
        // Arrange
        const string Output = """
            ```json AGENT_REPORT
            { "status": "completed", "summary": "feito" }
            ```
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 99);

        // Assert
        report.Issue.Should().Be(99);
    }

    [Fact]
    public void Parse_Should_DefaultToPartial_When_NoReportCanBeRead()
    {
        // Arrange — the critical case. The agent did work, but said nothing structured,
        // so the pipeline must not be told the work is complete.
        const string Output = "Editei alguns arquivos e o build passou. Acho que está pronto.";

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 5);

        // Assert
        report.Status.Should().Be(ImplementationStatus.Partial);
        report.Status.Should().NotBe(ImplementationStatus.Completed);
        report.Risks.Should().ContainSingle().Which.Should().Contain("AGENT_REPORT");
    }

    [Fact]
    public void Parse_Should_ReportFailure_When_TheAgentProducedNoOutput()
    {
        // Act
        AgentReport report = AgentReportParser.Parse(string.Empty, 3);

        // Assert
        report.Status.Should().Be(ImplementationStatus.Failed);
        report.BlockedReason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("completed", ImplementationStatus.Completed)]
    [InlineData("COMPLETED", ImplementationStatus.Completed)]
    [InlineData("done", ImplementationStatus.Completed)]
    [InlineData("partial", ImplementationStatus.Partial)]
    [InlineData("blocked", ImplementationStatus.Blocked)]
    [InlineData("failed", ImplementationStatus.Failed)]
    public void Parse_Should_MapKnownStatusValues(string value, ImplementationStatus expected)
    {
        // Arrange
        string output = $$"""
            ```json AGENT_REPORT
            { "issue": 1, "status": "{{value}}", "summary": "x" }
            ```
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(output, 1);

        // Assert
        report.Status.Should().Be(expected);
    }

    [Fact]
    public void Parse_Should_NotTreatAnUnknownStatusAsSuccess()
    {
        // Arrange — an invented status must degrade safely, never upward.
        const string Output = """
            ```json AGENT_REPORT
            { "issue": 1, "status": "mostly-fine", "summary": "x" }
            ```
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 1);

        // Assert
        report.Status.Should().Be(ImplementationStatus.Partial);
    }

    [Fact]
    public void Parse_Should_ToleratePlainJson_When_ThereIsNoFence()
    {
        // Arrange
        const string Output = """
            Resultado:
            { "issue": 12, "status": "completed", "summary": "entregue sem cercas" }
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 12);

        // Assert
        report.Status.Should().Be(ImplementationStatus.Completed);
        report.Summary.Should().Be("entregue sem cercas");
    }

    [Fact]
    public void Parse_Should_NotThrow_When_TheJsonIsMalformed()
    {
        // Arrange — truncation mid-object is a realistic failure of a streamed response.
        const string Output = """
            ```json AGENT_REPORT
            { "issue": 1, "status": "completed", "summary": "trunc
            """;

        // Act
        AgentReport report = AgentReportParser.Parse(Output, 1);

        // Assert
        report.Should().NotBeNull();
        report.Status.Should().NotBe(ImplementationStatus.Completed);
    }
}
