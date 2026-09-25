using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Tools.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AgentSquad.Tools.Tests.Git;

/// <summary>
/// Tests for the git command lines the orchestrator depends on.
/// </summary>
/// <remarks>
/// The clone under <c>WorkRoot</c> is reused across runs, which makes its checked-out state
/// shared mutable state between runs that never see each other. A real run read the previous
/// run's integration branch off disk and reported a repository holding two files as an
/// existing .NET 8 catalog API — so the intake's first decision, greenfield or brownfield,
/// came from a branch nobody had merged. These tests pin the reset down.
/// </remarks>
public sealed class GitClientTests
{
    [Fact]
    public async Task CheckoutBase_Should_ResetToTheRemoteTip_NotJustSwitchBranch()
    {
        // Arrange
        (GitClient git, IProcessRunner runner) = CreateClient();

        // Act
        await git.CheckoutBaseAsync(@"C:\squad\repos\demo", "main", TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> checkout = CapturedArguments(runner, "checkout");

        checkout.Should().Contain("--force", "a previous run may have left the working tree dirty");
        checkout.Should().Contain("-B", "the local branch may not exist, or may be an old run's tip");
        checkout.Should().ContainInOrder("-B", "main", "origin/main");
    }

    [Fact]
    public async Task CheckoutBase_Should_RemoveFilesLeftByThePreviousRun()
    {
        // Arrange
        (GitClient git, IProcessRunner runner) = CreateClient();

        // Act
        await git.CheckoutBaseAsync(@"C:\squad\repos\demo", "main", TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> clean = CapturedArguments(runner, "clean");

        clean.Should().Contain("-fdx", "untracked build output would otherwise land in the inventory");
    }

    [Fact]
    public async Task CheckoutBase_Should_RunInTheCloneNotTheCurrentDirectory()
    {
        // Arrange
        (GitClient git, IProcessRunner runner) = CreateClient();

        // Act
        await git.CheckoutBaseAsync(@"C:\squad\repos\demo", "main", TestContext.Current.CancellationToken);

        // Assert
        await runner.Received().RunAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>>(a => a.Count > 0 && a[0] == "checkout"),
            @"C:\squad\repos\demo",
            Arg.Any<TimeSpan>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckoutBase_Should_RejectAnEmptyBranchName()
    {
        // Arrange
        (GitClient git, _) = CreateClient();

        // Act
        Func<Task> act = () => git.CheckoutBaseAsync(
            @"C:\squad\repos\demo", "  ", TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static (GitClient Client, IProcessRunner Runner) CreateClient()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
                Arg.Any<TimeSpan>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false));

        var client = new GitClient(
            runner,
            Options.Create(new SquadOptions { WorkRoot = Path.Combine(Path.GetTempPath(), "squad-tests") }),
            NullLogger<GitClient>.Instance);

        return (client, runner);
    }

    private static IReadOnlyList<string> CapturedArguments(IProcessRunner runner, string first)
    {
        foreach (NSubstitute.Core.ICall call in runner.ReceivedCalls())
        {
            object?[] args = call.GetArguments();

            if (args.Length > 1 && args[1] is IReadOnlyList<string> list &&
                list.Count > 0 && string.Equals(list[0], first, StringComparison.Ordinal))
            {
                return list;
            }
        }

        return [];
    }
}
