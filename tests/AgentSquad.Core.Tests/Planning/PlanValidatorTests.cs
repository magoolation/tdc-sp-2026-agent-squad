using AgentSquad.Core.Planning;

namespace AgentSquad.Core.Tests.Planning;

/// <summary>
/// Tests for the rule that makes parallel agents safe.
/// </summary>
/// <remarks>
/// A language model produces the plan; this validator decides whether it may run. These
/// tests are therefore the guard on the single most consequential piece of judgement in
/// the factory, and they are deliberately written against observable behaviour — the
/// issue codes and the items each issue names — rather than internals.
/// </remarks>
public sealed class PlanValidatorTests
{
    [Fact]
    public void Validate_Should_ReturnNoIssues_When_PlanIsWellFormed()
    {
        // Arrange
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/Domain/Order.cs", "tests/Domain/OrderTests.cs"], requirements: ["RF-01"]),
            Item("W-02", wave: 2, files: ["src/Api/OrdersEndpoint.cs", "tests/Api/OrdersEndpointTests.cs"],
                 dependsOn: ["W-01"], requirements: ["RF-02"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, ["RF-01", "RF-02"]);

        // Assert
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Should_ReportFileConflict_When_TwoItemsInTheSameWaveClaimOneFile()
    {
        // Arrange — the failure mode the whole design exists to prevent.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/OrderService.cs", "tests/OrderServiceTests.cs"]),
            Item("W-02", wave: 1, files: ["src/OrderService.cs", "tests/OrderServiceExtraTests.cs"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        PlanIssue conflict = issues.Should().ContainSingle(i => i.Code == PlanValidator.Codes.FileConflict).Subject;
        conflict.Severity.Should().Be(PlanIssueSeverity.Error);
        conflict.ItemKeys.Should().BeEquivalentTo(["W-01", "W-02"]);
        conflict.Message.Should().Contain("src/OrderService.cs");
    }

    [Fact]
    public void Validate_Should_NormalizeSeparators_When_DetectingFileConflicts()
    {
        // Arrange — a model may emit either separator; they must still collide.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: [@"src\Domain\Order.cs"]),
            Item("W-02", wave: 1, files: ["src/Domain/Order.cs"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.FileConflict);
    }

    [Fact]
    public void Validate_Should_AllowSameFile_When_ItemsAreInDifferentWaves()
    {
        // Arrange — sequencing is the sanctioned way to resolve a conflict.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/OrderService.cs", "tests/OrderServiceTests.cs"]),
            Item("W-02", wave: 2, files: ["src/OrderService.cs", "tests/OrderServiceMoreTests.cs"],
                 dependsOn: ["W-01"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().NotContain(i => i.Code == PlanValidator.Codes.FileConflict);
    }

    [Fact]
    public void Validate_Should_ReportWaveOrdering_When_DependencyIsInTheSameWave()
    {
        // Arrange
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"]),
            Item("W-02", wave: 1, files: ["src/B.cs", "tests/BTests.cs"], dependsOn: ["W-01"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.WaveOrdering);
    }

    [Fact]
    public void Validate_Should_ReportDependencyCycle_When_ItemsDependOnEachOther()
    {
        // Arrange — a cycle deadlocks the factory, so it must never reach execution.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"], dependsOn: ["W-02"]),
            Item("W-02", wave: 1, files: ["src/B.cs", "tests/BTests.cs"], dependsOn: ["W-01"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.DependencyCycle);
    }

    [Fact]
    public void Validate_Should_ReportUnknownDependency_When_DependencyKeyIsNotInThePlan()
    {
        // Arrange
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"], dependsOn: ["W-99"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().ContainSingle(i => i.Code == PlanValidator.Codes.UnknownDependency)
              .Which.Message.Should().Contain("W-99");
    }

    [Fact]
    public void Validate_Should_ReportNoFiles_When_AnItemDeclaresNothing()
    {
        // Arrange — without a file list the conflict rule cannot be enforced at all.
        DeliveryPlan plan = Plan(Item("W-01", wave: 1, files: []));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.NoFiles && i.Severity == PlanIssueSeverity.Error);
    }

    [Fact]
    public void Validate_Should_ReportItemTooLarge_When_AnItemExceedsTheFileLimit()
    {
        // Arrange
        string[] files = [.. Enumerable.Range(0, PlanValidator.MaxFilesPerItem + 1).Select(i => $"src/File{i}.cs")];
        DeliveryPlan plan = Plan(Item("W-01", wave: 1, files: files));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.ItemTooLarge);
    }

    [Fact]
    public void Validate_Should_ReportNoAcceptanceCriteria_When_AnItemHasNone()
    {
        // Arrange — the gate cannot decide "done" without a verifiable criterion.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"], acceptanceCriteria: []));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.NoAcceptanceCriteria);
    }

    [Fact]
    public void Validate_Should_WarnAboutMissingTests_When_ProductionCodeHasNoTestFile()
    {
        // Arrange
        DeliveryPlan plan = Plan(Item("W-01", wave: 1, files: ["src/Domain/Order.cs"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert — a warning, not an error: it informs the human without blocking the run.
        issues.Should().ContainSingle(i => i.Code == PlanValidator.Codes.NoTests)
              .Which.Severity.Should().Be(PlanIssueSeverity.Warning);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Validate_Should_ReportWaveNumbering_When_WavesDoNotStartAtOne(int firstWave)
    {
        // Arrange
        DeliveryPlan plan = Plan(Item("W-01", wave: firstWave, files: ["src/A.cs", "tests/ATests.cs"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.WaveNumbering);
    }

    [Fact]
    public void Validate_Should_ReportWaveNumbering_When_WaveNumbersSkip()
    {
        // Arrange
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"]),
            Item("W-02", wave: 3, files: ["src/B.cs", "tests/BTests.cs"], dependsOn: ["W-01"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.WaveNumbering);
    }

    [Fact]
    public void Validate_Should_ReportDuplicateKey_When_TwoItemsShareAKey()
    {
        // Arrange — duplicate keys would make the issue-to-item mapping ambiguous.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"]),
            Item("W-01", wave: 1, files: ["src/B.cs", "tests/BTests.cs"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().Contain(i => i.Code == PlanValidator.Codes.DuplicateKey);
    }

    [Fact]
    public void Validate_Should_ReportUncoveredRequirement_When_NoItemReferencesIt()
    {
        // Arrange
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"], requirements: ["RF-01"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, ["RF-01", "RF-02"]);

        // Assert
        issues.Should().ContainSingle(i => i.Code == PlanValidator.Codes.UncoveredRequirement)
              .Which.Message.Should().Contain("RF-02");
    }

    [Fact]
    public void Validate_Should_OrderErrorsBeforeWarnings()
    {
        // Arrange — the orchestrator shows the first issues to the planner, so the ones
        // that block execution have to come first.
        DeliveryPlan plan = Plan(
            Item("W-01", wave: 1, files: ["src/Shared.cs"]),
            Item("W-02", wave: 1, files: ["src/Shared.cs"]));

        // Act
        IReadOnlyList<PlanIssue> issues = PlanValidator.Validate(plan, []);

        // Assert
        issues.Should().NotBeEmpty();
        issues[0].Severity.Should().Be(PlanIssueSeverity.Error);
        issues.Should().BeInDescendingOrder(i => i.Severity);
    }

    [Fact]
    public void Validate_Should_ThrowArgumentNullException_When_PlanIsNull()
    {
        // Act
        Action act = () => PlanValidator.Validate(null!, []);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Waves_Should_GroupItemsInAscendingWaveOrder()
    {
        // Arrange
        DeliveryPlan plan = Plan(
            Item("W-03", wave: 2, files: ["src/C.cs", "tests/CTests.cs"]),
            Item("W-01", wave: 1, files: ["src/A.cs", "tests/ATests.cs"]),
            Item("W-02", wave: 1, files: ["src/B.cs", "tests/BTests.cs"]));

        // Act
        IReadOnlyList<IReadOnlyList<WorkItem>> waves = plan.Waves();

        // Assert
        waves.Should().HaveCount(2);
        waves[0].Should().HaveCount(2);
        waves[1].Should().ContainSingle().Which.Key.Should().Be("W-03");
    }

    // -------------------------------------------------------------------------------------

    private static DeliveryPlan Plan(params WorkItem[] items) =>
        new("Entrega de teste", "Visão geral.", [], items, [], []);

    private static WorkItem Item(
        string key,
        int wave,
        IReadOnlyList<string> files,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<string>? requirements = null,
        IReadOnlyList<string>? acceptanceCriteria = null) =>
        new(
            Key: key,
            Title: $"item {key}",
            Goal: "Objetivo de teste.",
            Context: "Contexto de teste.",
            Files: [.. files.Select(f => new PlannedFile(f, FileAction.Create))],
            OutOfScope: [],
            AcceptanceCriteria: acceptanceCriteria ?? ["Dado X, quando Y, então Z"],
            ImplementationNotes: [],
            RequirementIds: requirements ?? [],
            DependsOn: dependsOn ?? [],
            Wave: wave,
            Area: "core",
            Size: WorkItemSize.Small,
            NeedsHuman: false);
}
