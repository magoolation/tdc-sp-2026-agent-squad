using System.Runtime.InteropServices;
using AgentSquad.Core.Abstractions;
using AgentSquad.Tools.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentSquad.Tools.Tests.Processes;

/// <summary>
/// Tests for the process runner that every external tool goes through.
/// </summary>
/// <remarks>
/// These touch real processes, so they are integration tests. They stay in the default run
/// because they are fast and because the behaviours they cover — argument safety, the
/// timeout, and launching a Windows batch shim — are exactly the ones that fail silently
/// and cost an afternoon to diagnose.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RunAsync_Should_CaptureOutput_And_ReportSuccess()
    {
        // Arrange
        ProcessRunner runner = CreateRunner();

        // Act
        ProcessResult result = await RunEchoAsync(runner, "hello-from-the-factory");

        // Assert
        result.Succeeded.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("hello-from-the-factory");
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_Should_PassArgumentsLiterally_When_TheyContainShellMetacharacters()
    {
        // Arrange — the SEC-004 guarantee, and the reason this project never builds a
        // command line by concatenation: an issue title or a branch name produced by a
        // model must arrive at the child process as one opaque argument. The script simply
        // echoes its first argument, so if any shell were interpreting the value, the
        // round trip would come back altered or the metacharacters would take effect.
        ProcessRunner runner = CreateRunner();
        const string Dangerous = "a && whoami | echo pwned ; rm -rf / `touch x` $(id)";

        string script = WriteEchoScript();

        try
        {
            (string file, string[] arguments) = InvokeScript(script, Dangerous);

            // Act
            ProcessResult result = await runner.RunAsync(
                file, arguments, Directory.GetCurrentDirectory(), Budget,
                environment: null, TestContext.Current.CancellationToken);

            // Assert
            result.Succeeded.Should().BeTrue(result.StandardError);
            result.StandardOutput.Trim().Should().Be(Dangerous);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task RunAsync_Should_ReportFailure_When_TheProcessExitsNonZero()
    {
        // Arrange
        ProcessRunner runner = CreateRunner();

        // Act
        ProcessResult result = await runner.RunAsync(
            "dotnet",
            ["--this-switch-does-not-exist"],
            Directory.GetCurrentDirectory(),
            Budget,
            environment: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task RunAsync_Should_TimeOut_When_TheProcessOutlivesItsBudget()
    {
        // Arrange — an agent that hangs must not hang the run (ENG-046).
        ProcessRunner runner = CreateRunner();

        (string file, string[] args) = Sleep30();

        // Act
        ProcessResult result = await runner.RunAsync(
            file,
            args,
            Directory.GetCurrentDirectory(),
            TimeSpan.FromSeconds(2),
            environment: null,
            TestContext.Current.CancellationToken);

        // Assert
        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task RunAsync_Should_ApplyEnvironmentVariables_ToTheChildProcess()
    {
        // Arrange — the validation gate relies on this to pin NUGET_SCRATCH.
        ProcessRunner runner = CreateRunner();

        (string file, string[] args) = PrintProbeVariable();

        // Act
        ProcessResult result = await runner.RunAsync(
            file,
            args,
            Directory.GetCurrentDirectory(),
            Budget,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["SQUAD_PROBE"] = "applied" },
            TestContext.Current.CancellationToken);

        // Assert
        result.StandardOutput.Should().Contain("applied");
    }

    [Fact]
    public async Task RunAsync_Should_ThrowArgumentException_When_TheTimeoutIsNotPositive()
    {
        // Arrange
        ProcessRunner runner = CreateRunner();

        // Act
        Func<Task> act = () => runner.RunAsync(
            "dotnet", ["--version"], Directory.GetCurrentDirectory(), TimeSpan.Zero,
            environment: null, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static ProcessRunner CreateRunner() =>
        new(NullLogger<ProcessRunner>.Instance, TimeProvider.System);

    // Cross-platform command builders. Declared with an explicit return type because a
    // conditional expression over two collection-expression tuples has no natural type.

    private static (string File, string[] Arguments) Sleep30() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"])
            : ("sh", ["-c", "sleep 30"]);

    private static (string File, string[] Arguments) PrintProbeVariable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell", ["-NoProfile", "-Command", "Write-Output $env:SQUAD_PROBE"])
            : ("sh", ["-c", "echo $SQUAD_PROBE"]);

    private static (string File, string[] Arguments) Echo(string value) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell", ["-NoProfile", "-Command", $"Write-Output '{value.Replace("'", "''", StringComparison.Ordinal)}'"])
            : ("echo", [value]);

    /// <summary>
    /// Writes a script that echoes its first argument verbatim.
    /// </summary>
    /// <remarks>
    /// A script rather than an inline command because neither <c>powershell -Command</c>
    /// nor <c>sh -c</c> binds positional arguments the way <c>-File</c> and <c>$1</c> do,
    /// and this test is specifically about what the child process receives in argv.
    /// </remarks>
    /// <returns>The path of the script, which the caller deletes.</returns>
    private static string WriteEchoScript()
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string path = Path.Combine(Path.GetTempPath(), $"squad-echo-{Guid.NewGuid():N}{(windows ? ".ps1" : ".sh")}");

        File.WriteAllText(path, windows
            ? "param([string]$Value)\r\n[Console]::Out.Write($Value)\r\n"
            : "#!/bin/sh\nprintf %s \"$1\"\n");

        return path;
    }

    private static (string File, string[] Arguments) InvokeScript(string script, string argument) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, argument])
            : ("sh", [script, argument]);

    private static Task<ProcessResult> RunEchoAsync(ProcessRunner runner, string value)
    {
        (string file, string[] args) = Echo(value);

        return runner.RunAsync(
            file, args, Directory.GetCurrentDirectory(), Budget,
            environment: null, TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Tests for resolving a command name to something the OS can actually start.
/// </summary>
public sealed class ExecutableResolverTests
{
    [Fact]
    public void Resolve_Should_FindANativeExecutable_And_NotWrapIt()
    {
        // Act
        LaunchPlan plan = ExecutableResolver.Resolve("dotnet");

        // Assert
        plan.PrefixArguments.Should().BeEmpty();
        plan.FileName.Should().Contain("dotnet");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Resolve_Should_WrapABatchShim_InCmd_OnWindows()
    {
        // Arrange — the Azure CLI installs as az.cmd, which Process.Start cannot launch
        // directly; without the wrapper the prerequisite check reports a working tool as
        // missing.
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only behaviour.");
        Assert.SkipUnless(ExecutableResolver.Resolve("az").FileName is not "az", "The Azure CLI is not installed here.");

        // Act
        LaunchPlan plan = ExecutableResolver.Resolve("az");

        // Assert
        plan.FileName.Should().Be("cmd.exe");

        // /d /c, deliberately without /s: with /s, cmd strips the outer quotes and a path
        // containing spaces breaks apart.
        plan.PrefixArguments.Should().HaveCount(3);
        plan.PrefixArguments[0].Should().Be("/d");
        plan.PrefixArguments[1].Should().Be("/c");
        // PATHEXT reports extensions in upper case on Windows, so the comparison must not care.
        plan.PrefixArguments[2].Should().EndWithEquivalentOf(".cmd");
    }

    [Fact]
    public void Resolve_Should_ReturnTheNameUnchanged_When_NothingCanBeFound()
    {
        // Act — Process.Start should be the one to report the error, not the resolver.
        LaunchPlan plan = ExecutableResolver.Resolve("definitely-not-a-real-command-42");

        // Assert
        plan.FileName.Should().Be("definitely-not-a-real-command-42");
        plan.PrefixArguments.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_Should_ThrowArgumentException_When_TheNameIsBlank()
    {
        // Act
        Action act = () => ExecutableResolver.Resolve("   ");

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}




