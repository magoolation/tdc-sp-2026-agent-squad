using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Planning;
using AgentSquad.Tools.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AgentSquad.Tools.Tests.GitHub;

/// <summary>
/// Tests for the command lines handed to the GitHub CLI.
/// </summary>
/// <remarks>
/// <para>
/// These assert on the <b>arguments</b> rather than on the outcome, which is unusual and
/// deliberate. The failures this type produces are not logic errors: they are
/// misunderstandings of what a flag means, and they surface only against the real CLI —
/// after the planning phase has already been paid for, ten minutes into a run.
/// </para>
/// <para>
/// The first of these tests exists because of exactly that. <c>gh issue create --milestone</c>
/// matches by <b>name</b>; the implementation passed the number, and every issue creation
/// failed with <c>could not add to milestone '1': '1' not found</c>.
/// </para>
/// </remarks>
public sealed class GitHubCliIssueTrackerTests
{
    [Fact]
    public async Task CreateIssues_Should_PassTheMilestoneByTitle_NotByNumber()
    {
        // Arrange
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker();

        // Act
        await tracker.CreateIssuesAsync(
            [Item("W-01")],
            milestoneTitle: "product catalog rest api",
            bodyRenderer: _ => "corpo",
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> arguments = CapturedArguments(runner, "issue", "create");
        int index = IndexOf(arguments, "--milestone");

        index.Should().BeGreaterThanOrEqualTo(0, "the milestone must be sent");
        arguments[index + 1].Should().Be("product catalog rest api");
        arguments[index + 1].Should().NotBe("1", "gh matches milestones by name, never by number");
    }

    [Fact]
    public async Task CreateIssues_Should_OmitTheMilestone_When_NoneIsGiven()
    {
        // Arrange
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker();

        // Act
        await tracker.CreateIssuesAsync(
            [Item("W-01")],
            milestoneTitle: null,
            bodyRenderer: _ => "corpo",
            TestContext.Current.CancellationToken);

        // Assert
        CapturedArguments(runner, "issue", "create").Should().NotContain("--milestone");
    }

    [Fact]
    public async Task CreateIssues_Should_LabelByWaveAreaAndSize()
    {
        // Arrange — the labels are how a human reads the parallelism plan off the issue list.
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker();

        // Act
        await tracker.CreateIssuesAsync(
            [Item("W-01", wave: 2, area: "web", size: WorkItemSize.Large)],
            milestoneTitle: null,
            bodyRenderer: _ => "corpo",
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> arguments = CapturedArguments(runner, "issue", "create");

        arguments.Should().Contain("agent-task");
        arguments.Should().Contain("wave:2");
        arguments.Should().Contain("area:web");
        arguments.Should().Contain("size:l");
    }

    [Fact]
    public async Task CreateIssues_Should_AddNeedsHuman_When_TheItemRequiresADecision()
    {
        // Arrange
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker();

        // Act
        await tracker.CreateIssuesAsync(
            [Item("W-01", needsHuman: true)],
            milestoneTitle: null,
            bodyRenderer: _ => "corpo",
            TestContext.Current.CancellationToken);

        // Assert
        CapturedArguments(runner, "issue", "create").Should().Contain("needs-human");
    }

    [Fact]
    public async Task CreateIssues_Should_SendTheBodyAsAFile_NotOnTheCommandLine()
    {
        // Arrange — issue bodies routinely exceed the Windows command-line length limit,
        // and a file also sidesteps every quoting question.
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker();

        // Act
        await tracker.CreateIssuesAsync(
            [Item("W-01")],
            milestoneTitle: null,
            bodyRenderer: _ => new string('x', 40_000),
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> arguments = CapturedArguments(runner, "issue", "create");

        arguments.Should().Contain("--body-file");
        arguments.Should().NotContain("--body");
        arguments.Should().NotContain(a => a.Length > 1_000);
    }

    [Fact]
    public async Task CreatePullRequest_Should_AlwaysPassHeadExplicitly()
    {
        // Arrange — without --head, gh tries to infer the branch and can prompt about
        // pushing or forking, which deadlocks a redirected process with no terminal.
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker(
            stdout: "https://github.com/owner/repo/pull/7");

        // Act
        await tracker.CreatePullRequestAsync(
            new PullRequestRequest(
                "feat: algo",
                "corpo",
                HeadBranch: "agent/issue-42-algo",
                BaseBranch: "main",
                IssueNumber: 42,
                Labels: ["agent-generated"],
                Draft: true),
            TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> arguments = CapturedArguments(runner, "pr", "create");
        int index = IndexOf(arguments, "--head");

        index.Should().BeGreaterThanOrEqualTo(0);
        arguments[index + 1].Should().Be("agent/issue-42-algo");
        arguments.Should().Contain("--draft");
        arguments.Should().Contain("agent-generated");
    }

    [Fact]
    public async Task CreatePullRequest_Should_ReturnTheNumberParsedFromTheUrl()
    {
        // Arrange — `gh pr create` has no --json flag; it prints the URL.
        (GitHubCliIssueTracker tracker, _) = CreateTracker(
            stdout: "Creating pull request for agent/x into main\nhttps://github.com/owner/repo/pull/123");

        // Act
        PullRequestRef result = await tracker.CreatePullRequestAsync(
            new PullRequestRequest("t", "b", "agent/x", "main", 1, [], false),
            TestContext.Current.CancellationToken);

        // Assert
        result.Number.Should().Be(123);
        result.Url.Should().EndWith("/pull/123");
    }

    [Fact]
    public async Task EnsureLabels_Should_UseForce_SoReRunsAreIdempotent()
    {
        // Arrange
        (GitHubCliIssueTracker tracker, IProcessRunner runner) = CreateTracker();

        // Act
        await tracker.EnsureLabelsAsync(
            [new LabelDefinition("agent-task", "5319E7", "descrição")],
            TestContext.Current.CancellationToken);

        // Assert — --force upserts, which keeps a second run from failing on existing labels.
        CapturedArguments(runner, "label", "create").Should().Contain("--force");
    }

    [Fact]
    public async Task GhFailure_Should_ThrowWithTheCommandAndTheCliMessage()
    {
        // Arrange
        (GitHubCliIssueTracker tracker, _) = CreateTracker(
            exitCode: 1, stderr: "could not add to milestone '1': '1' not found");

        // Act
        Func<Task> act = () => tracker.CreateIssuesAsync(
            [Item("W-01")], null, _ => "corpo", TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<GitHubCliException>()
                 .Where(e => e.Message.Contains("issue create", StringComparison.Ordinal)
                          && e.Message.Contains("milestone", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------------------------------

    private static (GitHubCliIssueTracker Tracker, IProcessRunner Runner) CreateTracker(
        int exitCode = 0,
        string stdout = "https://github.com/owner/repo/issues/1",
        string stderr = "")
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
                Arg.Any<TimeSpan>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, stdout, stderr, TimeSpan.Zero, TimedOut: false));

        var tracker = new GitHubCliIssueTracker(
            runner,
            Options.Create(new GitHubOptions { Owner = "owner", Repository = "repo" }),
            Options.Create(new SquadOptions { WorkRoot = Path.Combine(Path.GetTempPath(), "squad-tests") }),
            NullLogger<GitHubCliIssueTracker>.Instance);

        return (tracker, runner);
    }

    /// <summary>Finds the position of a flag in a captured argument list.</summary>
    /// <param name="arguments">The captured arguments.</param>
    /// <param name="value">The flag to locate.</param>
    /// <returns>The index, or -1 when absent.</returns>
    private static int IndexOf(IReadOnlyList<string> arguments, string value)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the arguments of the first gh invocation matching a subcommand.</summary>
    /// <param name="runner">The substituted runner.</param>
    /// <param name="first">First argument, such as <c>issue</c>.</param>
    /// <param name="second">Second argument, such as <c>create</c>.</param>
    /// <returns>The captured argument list.</returns>
    private static IReadOnlyList<string> CapturedArguments(IProcessRunner runner, string first, string second)
    {
        foreach (NSubstitute.Core.ICall call in runner.ReceivedCalls())
        {
            object?[] args = call.GetArguments();

            if (args.Length > 1 && args[1] is IReadOnlyList<string> list &&
                list.Count > 1 && list[0] == first && list[1] == second)
            {
                return list;
            }
        }

        return [];
    }

    private static WorkItem Item(
        string key,
        int wave = 1,
        string area = "core",
        WorkItemSize size = WorkItemSize.Small,
        bool needsHuman = false) =>
        new(
            Key: key,
            Title: $"item {key}",
            Goal: "objetivo",
            Context: "contexto",
            Files: [new PlannedFile("src/A.cs", FileAction.Create)],
            OutOfScope: [],
            AcceptanceCriteria: ["Dado X, quando Y, então Z"],
            ImplementationNotes: [],
            RequirementIds: [],
            DependsOn: [],
            Wave: wave,
            Area: area,
            Size: size,
            NeedsHuman: needsHuman);
}

