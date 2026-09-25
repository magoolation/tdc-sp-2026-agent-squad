using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Validation;
using AgentSquad.Tools.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AgentSquad.Tools.Tests.Validation;

/// <summary>
/// Tests for the commands the validation gate runs.
/// </summary>
/// <remarks>
/// <para>
/// Like the GitHub CLI tests, these assert on <b>arguments</b>. The bug that motivated them
/// was not a logic error: <c>--artifacts-path</c> relocates <c>obj/</c> as well as
/// <c>bin/</c>, so passing it to <c>build</c> but not to <c>restore</c> makes the build
/// fail with <c>NETSDK1004: Assets file ... not found</c>.
/// </para>
/// <para>
/// What made it worth a permanent test is who paid for it. The failure looked exactly like
/// broken code, so it was reported against the coding agent, which then spent its repair
/// attempts fixing something that was never wrong. A gate that blames the wrong party is
/// worse than no gate.
/// </para>
/// </remarks>
public sealed class DotNetValidationGateTests
{
    private const string WorktreePath = @"C:\squad\wt\i42";
    private const string ArtifactsPath = @"C:\squad\bld\i42";

    [Fact]
    public async Task Validate_Should_PassArtifactsPathToEveryStepThatSupportsIt()
    {
        // Arrange
        (DotNetValidationGate gate, IProcessRunner runner, string worktree) = CreateGate();

        // Act
        await gate.ValidateAsync(
            Worktree(worktree), Item(), "origin/main", attempt: 1, TestContext.Current.CancellationToken);

        // Assert
        foreach (string verb in (string[])["restore", "build", "test"])
        {
            IReadOnlyList<string> arguments = Captured(runner, verb);

            arguments.Should().NotBeEmpty($"the gate must run 'dotnet {verb}'");
            arguments.Should().Contain("--artifacts-path",
                $"'dotnet {verb}' must agree with the others about where obj/ lives");

            int index = IndexOf(arguments, "--artifacts-path");
            arguments[index + 1].Should().Be(ArtifactsPath);
        }
    }

    [Fact]
    public async Task Validate_Should_BuildWithWarningsAsErrors_AndWithoutNodeReuse()
    {
        // Arrange — node reuse keeps analyzer assemblies loaded for fifteen minutes, which
        // later blocks worktree deletion.
        (DotNetValidationGate gate, IProcessRunner runner, string worktree) = CreateGate();

        // Act
        await gate.ValidateAsync(
            Worktree(worktree), Item(), "origin/main", attempt: 1, TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyList<string> build = Captured(runner, "build");

        build.Should().Contain("-warnaserror");
        build.Should().Contain("--no-restore");
        build.Should().Contain("-nr:false");
        build.Should().Contain("-c").And.Contain("Release");
    }

    [Fact]
    public async Task Validate_Should_PinTheSharedNuGetScratchDirectory()
    {
        // Arrange — the single most destructive misconfiguration available here. NuGet's
        // cross-process extraction locks live in the scratch directory, not in the packages
        // folder; giving each concurrent worker its own corrupts the shared cache
        // permanently, and it does not self-heal.
        (DotNetValidationGate gate, IProcessRunner runner, string worktree) = CreateGate();

        // Act
        await gate.ValidateAsync(
            Worktree(worktree), Item(), "origin/main", attempt: 1, TestContext.Current.CancellationToken);

        // Assert
        IReadOnlyDictionary<string, string> environment = CapturedEnvironment(runner, "restore");

        environment.Should().ContainKey("NUGET_SCRATCH");
        environment.Should().ContainKey("NUGET_PACKAGES");
        environment["MSBUILDDISABLENODEREUSE"].Should().Be("1");
    }

    [Fact]
    public async Task Validate_Should_SkipTheTests_When_TheBuildFails()
    {
        // Arrange — a failing build makes the test run meaningless, and its output would
        // bury the compiler errors the agent actually needs to read.
        (DotNetValidationGate gate, IProcessRunner runner, string worktree) = CreateGate(
            failVerb: "build", failOutput: "Program.cs(10,5): error CS0246: type not found");

        // Act
        ValidationReport report = await gate.ValidateAsync(
            Worktree(worktree), Item(), "origin/main", attempt: 1, TestContext.Current.CancellationToken);

        // Assert
        report.Passed.Should().BeFalse();
        Captured(runner, "test").Should().BeEmpty("the tests must not run after a failed build");

        report.Steps.Should().Contain(s => s.Kind == ValidationStepKind.Test
                                        && s.Outcome == ValidationOutcome.Skipped);
    }

    [Fact]
    public async Task Validate_Should_KeepCompilerDiagnosticsVerbatim_ForTheRepairLoop()
    {
        // Arrange — the agent needs the compiler's own text. Restating it in our words
        // loses the information it would use to fix the problem.
        const string Diagnostic = "Program.cs(10,5): error CS0246: The type or namespace name 'Foo' could not be found";

        (DotNetValidationGate gate, _, string worktree) = CreateGate(failVerb: "build", failOutput: Diagnostic);

        // Act
        ValidationReport report = await gate.ValidateAsync(
            Worktree(worktree), Item(), "origin/main", attempt: 1, TestContext.Current.CancellationToken);

        // Assert
        report.ToRepairBrief().Should().Contain("CS0246").And.Contain("Program.cs(10,5)");
    }

    // -------------------------------------------------------------------------------------

    private static (DotNetValidationGate Gate, IProcessRunner Runner, string Worktree) CreateGate(
        string? failVerb = null,
        string failOutput = "")
    {
        // A real directory with a solution file, because the gate looks for one on disk.
        string worktree = Path.Combine(Path.GetTempPath(), $"squad-gate-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, "Target.slnx"), "<Solution />");

        IProcessRunner runner = Substitute.For<IProcessRunner>();

        runner.RunAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
                Arg.Any<TimeSpan>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var arguments = (IReadOnlyList<string>)call[1];
                bool shouldFail = failVerb is not null && arguments.Count > 0 && arguments[0] == failVerb;

                return Task.FromResult(new ProcessResult(
                    shouldFail ? 1 : 0,
                    shouldFail ? failOutput : "ok",
                    string.Empty,
                    TimeSpan.FromSeconds(1),
                    TimedOut: false));
            });

        IGitClient git = Substitute.For<IGitClient>();
        git.GetChangedFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<string>>(["src/A.cs"]));
        git.GetDiffAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult("+ public class A { }"));

        var gate = new DotNetValidationGate(
            runner,
            git,
            Options.Create(new SquadOptions { WorkRoot = Path.GetTempPath() }),
            TimeProvider.System,
            NullLogger<DotNetValidationGate>.Instance);

        return (gate, runner, worktree);
    }

    private static AgentWorktree Worktree(string path) => new(42, "agent/issue-42-x", path, ArtifactsPath);

    private static WorkItem Item() =>
        new("W-01", "item", "objetivo", "contexto",
            [new PlannedFile("src/A.cs", FileAction.Create)],
            [], ["Dado X, quando Y, então Z"], [], [], [], 1, "core", WorkItemSize.Small, false);

    private static IReadOnlyList<string> Captured(IProcessRunner runner, string verb)
    {
        foreach (NSubstitute.Core.ICall call in runner.ReceivedCalls())
        {
            object?[] args = call.GetArguments();

            if (args.Length > 1 && args[1] is IReadOnlyList<string> list && list.Count > 0 && list[0] == verb)
            {
                return list;
            }
        }

        return [];
    }

    private static IReadOnlyDictionary<string, string> CapturedEnvironment(IProcessRunner runner, string verb)
    {
        foreach (NSubstitute.Core.ICall call in runner.ReceivedCalls())
        {
            object?[] args = call.GetArguments();

            if (args.Length > 4 && args[1] is IReadOnlyList<string> list && list.Count > 0 && list[0] == verb &&
                args[4] is IReadOnlyDictionary<string, string> environment)
            {
                return environment;
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

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
}
