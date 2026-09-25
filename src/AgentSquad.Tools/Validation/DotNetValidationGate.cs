using System.Globalization;
using System.Text.RegularExpressions;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Planning;
using AgentSquad.Core.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Tools.Validation;

/// <summary>
/// Runs the deterministic .NET quality gate over one agent worktree.
/// </summary>
/// <remarks>
/// <para>
/// This type holds the authority in the factory. A coding agent reports what it believes
/// it did; this gate establishes what is actually true (AI-008). No pull request is opened
/// unless it passes.
/// </para>
/// <para>
/// Steps run in dependency order and stop at the first blocking failure, because a broken
/// build makes every later step meaningless — and because a failing agent should get back
/// the first real error, not a cascade of consequences.
/// </para>
/// </remarks>
public sealed partial class DotNetValidationGate(
    IProcessRunner processRunner,
    IGitClient gitClient,
    IOptions<SquadOptions> options,
    TimeProvider timeProvider,
    ILogger<DotNetValidationGate> logger) : IValidationGate
{
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly IGitClient _gitClient = gitClient;
    private readonly SquadOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<DotNetValidationGate> _logger = logger;

    /// <inheritdoc />
    public async Task<ValidationReport> ValidateAsync(
        AgentWorktree worktree,
        WorkItem item,
        string baseRef,
        int attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        ArgumentNullException.ThrowIfNull(item);

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        long startTimestamp = _timeProvider.GetTimestamp();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.ValidationTimeout);

        var steps = new List<ValidationStep>();

        try
        {
            string? solution = FindSolution(worktree.Path);

            if (solution is null)
            {
                steps.Add(Skipped(ValidationStepKind.Restore, "No solution or project file was found."));
            }
            else
            {
                await RunDotNetStepsAsync(worktree, solution, steps, budget.Token);
            }

            // These two are cheap, never depend on a successful build, and catch the two
            // failure modes that matter most for an autonomous agent: scope creep and secrets.
            steps.Add(await CheckScopeAsync(worktree, item, baseRef, budget.Token));
            steps.Add(await ScanForSecretsAsync(worktree, baseRef, budget.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            steps.Add(new ValidationStep(
                ValidationStepKind.Build,
                ValidationOutcome.TimedOut,
                _timeProvider.GetElapsedTime(startTimestamp),
                null,
                $"The validation gate exceeded its {_options.ValidationTimeout.TotalMinutes:F0}-minute budget.",
                []));
        }

        var report = new ValidationReport(
            worktree.IssueNumber,
            attempt,
            steps,
            startedAt,
            _timeProvider.GetElapsedTime(startTimestamp));

        LogCompleted(worktree.IssueNumber, attempt, report.Passed, report.Failures.Count);

        return report;
    }

    private async Task RunDotNetStepsAsync(
        AgentWorktree worktree,
        string solution,
        List<ValidationStep> steps,
        CancellationToken cancellationToken)
    {
        // --artifacts-path on restore, not only on build.
        //
        // It relocates obj/ as well as bin/, so a restore without it writes the assets file
        // to the default location while the build with it looks somewhere else, and the
        // build fails with NETSDK1004 "Assets file ... not found". The failure is silent
        // about its cause and lands on the coding agent, which then burns its repair
        // attempts trying to fix code that was never broken. Every step that takes the flag
        // has to agree on it.
        ValidationStep restore = await RunStepAsync(
            ValidationStepKind.Restore,
            worktree,
            ["restore", solution, "--artifacts-path", worktree.ArtifactsPath],
            cancellationToken);

        steps.Add(restore);

        if (restore.IsBlocking)
        {
            return;
        }

        steps.Add(await RunStepAsync(
            ValidationStepKind.Format,
            worktree,
            ["format", solution, "--verify-no-changes", "--severity", "info", "--no-restore"],
            cancellationToken));

        ValidationStep build = await RunStepAsync(
            ValidationStepKind.Build,
            worktree,
            [
                "build", solution,
                "--no-restore",
                "-c", "Release",
                "-warnaserror",
                $"-m:{_options.MsBuildNodesPerAgent.ToString(CultureInfo.InvariantCulture)}",
                "-nr:false",
                "--artifacts-path", worktree.ArtifactsPath,
            ],
            cancellationToken);

        steps.Add(build);

        if (build.IsBlocking)
        {
            // A failing build makes the test run meaningless and its output would bury
            // the compiler errors the agent actually needs to see.
            steps.Add(Skipped(ValidationStepKind.Test, "Skipped because the build failed."));
            steps.Add(Skipped(ValidationStepKind.Vulnerabilities, "Skipped because the build failed."));
            return;
        }

        steps.Add(await RunTestsAsync(worktree, solution, cancellationToken));

        steps.Add(await RunStepAsync(
            ValidationStepKind.Vulnerabilities,
            worktree,
            ["list", solution, "package", "--vulnerable", "--include-transitive"],
            cancellationToken));
    }

    /// <summary>
    /// Runs the test suite, working around a known <c>dotnet test</c> defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the .NET 10 SDK, <c>dotnet test</c> driving Microsoft.Testing.Platform can report
    /// "Zero tests ran" (exit code 5) for a suite that runs perfectly when its executable is
    /// launched directly. Reproduced here against a pristine, minimal xUnit v3 project, so
    /// it is an SDK-level defect rather than anything about this repository
    /// (dotnet/sdk#51283 and neighbours).
    /// </para>
    /// <para>
    /// Silently accepting that would be the worst possible outcome: the gate would pass a
    /// pull request whose tests never executed. So a zero-test result is treated as
    /// suspicious and retried by running each test project directly — every
    /// Microsoft.Testing.Platform test project is an executable, so
    /// <c>dotnet run --project</c> runs the same suite through the same runner without the
    /// broken orchestration layer.
    /// </para>
    /// <para>
    /// If the direct run also finds nothing, the step fails: a repository with test projects
    /// and no executing tests is a real problem, not a tooling quirk.
    /// </para>
    /// </remarks>
    /// <param name="worktree">The worktree under validation.</param>
    /// <param name="solution">Solution or project path, relative to the worktree.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The test step result.</returns>
    private async Task<ValidationStep> RunTestsAsync(
        AgentWorktree worktree,
        string solution,
        CancellationToken cancellationToken)
    {
        ValidationStep step = await RunStepAsync(
            ValidationStepKind.Test,
            worktree,
            [
                "test", solution,
                "--no-build",
                "-c", "Release",
                "--artifacts-path", worktree.ArtifactsPath,
            ],
            cancellationToken);

        bool reportedZeroTests = step.Diagnostics.Any(d =>
            d.Contains("Zero tests ran", StringComparison.OrdinalIgnoreCase));

        if (!reportedZeroTests)
        {
            return step;
        }

        string[] testProjects = FindTestProjects(worktree.Path);

        if (testProjects.Length == 0)
        {
            // No test projects at all: nothing was skipped, so the zero-test result is honest.
            return step with
            {
                Outcome = ValidationOutcome.Skipped,
                Summary = "No test project was found in the worktree.",
            };
        }

        LogTestFallback(worktree.IssueNumber, testProjects.Length);

        var diagnostics = new List<string>();
        long start = _timeProvider.GetTimestamp();
        bool allPassed = true;
        int projectsRun = 0;

        foreach (string project in testProjects)
        {
            ProcessResult result = await _processRunner.RunAsync(
                "dotnet",
                ["run", "--project", project, "--no-build", "-c", "Release"],
                worktree.Path,
                _options.ProcessTimeout,
                BuildEnvironment(),
                cancellationToken);

            projectsRun++;

            if (!result.Succeeded)
            {
                allPassed = false;
                diagnostics.Add($"--- {project} ---");
                diagnostics.AddRange(ExtractDiagnostics(ValidationStepKind.Test, result));
            }
        }

        TimeSpan duration = _timeProvider.GetElapsedTime(start);

        return new ValidationStep(
            ValidationStepKind.Test,
            allPassed ? ValidationOutcome.Passed : ValidationOutcome.Failed,
            duration,
            allPassed ? 0 : 1,
            allPassed
                ? $"OK ({projectsRun} test project(s) run directly; 'dotnet test' reported zero)"
                : $"{projectsRun} test project(s) run directly; at least one failed.",
            diagnostics);
    }

    private static string[] FindTestProjects(string worktreePath)
    {
        // Convention over configuration: a test project lives under tests/ or ends in
        // .Tests.csproj. Reading every project file to check IsTestProject would be more
        // precise and far slower, and this gate runs on every repair attempt.
        return
        [
            .. Directory
                .EnumerateFiles(worktreePath, "*.csproj", SearchOption.AllDirectories)
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(worktreePath, path))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private async Task<ValidationStep> RunStepAsync(
        ValidationStepKind kind,
        AgentWorktree worktree,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        long start = _timeProvider.GetTimestamp();

        ProcessResult result = await _processRunner.RunAsync(
            "dotnet",
            arguments,
            worktree.Path,
            _options.ProcessTimeout,
            BuildEnvironment(),
            cancellationToken);

        TimeSpan duration = _timeProvider.GetElapsedTime(start);

        // `dotnet list package --vulnerable` exits 0 even when it finds something, so the
        // outcome has to be read out of the text rather than the exit code.
        bool failed = kind == ValidationStepKind.Vulnerabilities
            ? HasHighSeverityVulnerability(result.StandardOutput)
            : !result.Succeeded;

        ValidationOutcome outcome = result.TimedOut
            ? ValidationOutcome.TimedOut
            : failed ? ValidationOutcome.Failed : ValidationOutcome.Passed;

        IReadOnlyList<string> diagnostics = outcome == ValidationOutcome.Passed
            ? []
            : ExtractDiagnostics(kind, result);

        string summary = outcome switch
        {
            ValidationOutcome.Passed => "OK",
            ValidationOutcome.TimedOut => $"Exceeded {_options.ProcessTimeout.TotalMinutes:F0} minutes.",
            _ => diagnostics.Count > 0 ? diagnostics[0] : $"Exit code {result.ExitCode}.",
        };

        return new ValidationStep(kind, outcome, duration, result.ExitCode, Truncate(summary, 200), diagnostics);
    }

    /// <summary>
    /// Builds the environment every validation build shares.
    /// </summary>
    /// <remarks>
    /// <c>NUGET_SCRATCH</c> is the important one and the least obvious. NuGet's
    /// cross-process extraction locks live there, not in the packages folder. With a
    /// shared packages folder but a per-worker scratch directory, twenty-four concurrent
    /// restores failed twenty-four times and left the shared cache <i>permanently</i>
    /// corrupted: every package carried the <c>.nupkg.metadata</c> completion marker while
    /// missing its <c>.nuspec</c>, so even a later serial restore failed with NU5037 until
    /// the cache was purged by hand. One scratch directory for every worker is not a
    /// tuning choice.
    /// </remarks>
    /// <returns>Environment variables for the child process.</returns>
    private Dictionary<string, string> BuildEnvironment() => new(StringComparer.Ordinal)
    {
        ["NUGET_PACKAGES"] = _options.NuGetPackagesDirectory,
        ["NUGET_SCRATCH"] = _options.NuGetScratchDirectory,

        // Idle MSBuild nodes linger for fifteen minutes holding analyzer assemblies,
        // which later blocks worktree deletion.
        ["MSBUILDDISABLENODEREUSE"] = "1",
        ["MSBUILDTERMINALLOGGER"] = "off",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",
    };

    private async Task<ValidationStep> CheckScopeAsync(
        AgentWorktree worktree,
        WorkItem item,
        string baseRef,
        CancellationToken cancellationToken)
    {
        long start = _timeProvider.GetTimestamp();

        IReadOnlyList<string> changed = await _gitClient.GetChangedFilesAsync(worktree.Path, baseRef, cancellationToken);
        HashSet<string> declared = new(item.Paths.Select(Normalize), StringComparer.OrdinalIgnoreCase);

        List<string> unexpected =
        [
            .. changed.Select(Normalize)
                      .Where(path => !declared.Contains(path) && !IsAlwaysAllowed(path)),
        ];

        TimeSpan duration = _timeProvider.GetElapsedTime(start);

        if (changed.Count == 0)
        {
            return new ValidationStep(
                ValidationStepKind.ScopeCompliance,
                ValidationOutcome.Failed,
                duration,
                null,
                "The agent produced no changes at all.",
                ["No files differ from the base branch. The work item was not implemented."]);
        }

        if (unexpected.Count == 0)
        {
            return new ValidationStep(
                ValidationStepKind.ScopeCompliance,
                ValidationOutcome.Passed,
                duration,
                null,
                $"{changed.Count} file(s), all within the declared scope.",
                []);
        }

        // A warning, not a hard failure: an agent that also touched an adjacent file is
        // usually right, and a human reviews every pull request anyway. What matters is
        // that the deviation is visible rather than silent.
        return new ValidationStep(
            ValidationStepKind.ScopeCompliance,
            ValidationOutcome.Passed,
            duration,
            null,
            $"{unexpected.Count} file(s) outside the declared scope — see the pull-request body.",
            [.. unexpected.Select(p => $"Outside the declared scope: {p}")]);
    }

    private async Task<ValidationStep> ScanForSecretsAsync(
        AgentWorktree worktree,
        string baseRef,
        CancellationToken cancellationToken)
    {
        long start = _timeProvider.GetTimestamp();

        string diff = await _gitClient.GetDiffAsync(worktree.Path, baseRef, cancellationToken);
        var hits = new List<string>();

        foreach (string line in diff.Split('\n'))
        {
            // Added lines only: an agent removing an existing secret is a good thing.
            if (line.Length < 2 || line[0] != '+' || line.StartsWith("+++", StringComparison.Ordinal))
            {
                continue;
            }

            foreach ((string name, Regex pattern) in SecretPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    hits.Add($"{name}: {Truncate(line.TrimStart('+').Trim(), 120)}");
                    break;
                }
            }
        }

        TimeSpan duration = _timeProvider.GetElapsedTime(start);

        return hits.Count == 0
            ? new ValidationStep(ValidationStepKind.SecretScan, ValidationOutcome.Passed, duration, null, "No secrets detected.", [])
            : new ValidationStep(
                ValidationStepKind.SecretScan,
                ValidationOutcome.Failed,
                duration,
                null,
                $"{hits.Count} possible secret(s) in the diff (SEC-001).",
                hits);
    }

    private static readonly (string Name, Regex Pattern)[] SecretPatterns =
    [
        ("GitHub token", GitHubTokenRegex()),
        ("Azure storage key", AzureStorageKeyRegex()),
        ("Connection string with password", ConnectionStringRegex()),
        ("Private key block", PrivateKeyRegex()),
        ("Hard-coded API key", ApiKeyAssignmentRegex()),
        ("JSON Web Token", JwtRegex()),
    ];

    [GeneratedRegex(@"\b(gh[pousr]|github_pat)_[A-Za-z0-9_]{20,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"AccountKey\s*=\s*[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AzureStorageKeyRegex();

    [GeneratedRegex(@"(Password|Pwd)\s*=\s*[^;""'\s]{6,}", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConnectionStringRegex();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(
        @"(api[_-]?key|secret|client[_-]?secret|access[_-]?token)\s*[:=]\s*[""'][^""'\s]{16,}[""']",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ApiKeyAssignmentRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JwtRegex();

    private static bool IsAlwaysAllowed(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("Directory.Packages.props", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static string? FindSolution(string worktreePath)
    {
        string[] solutions = [.. Directory.EnumerateFiles(worktreePath, "*.slnx"), .. Directory.EnumerateFiles(worktreePath, "*.sln")];

        if (solutions.Length > 0)
        {
            return Path.GetFileName(solutions[0]);
        }

        string[] projects = [.. Directory.EnumerateFiles(worktreePath, "*.csproj", SearchOption.AllDirectories).Take(1)];

        return projects.Length > 0 ? Path.GetRelativePath(worktreePath, projects[0]) : null;
    }

    private static bool HasHighSeverityVulnerability(string output) =>
        output.Contains("High", StringComparison.Ordinal) ||
        output.Contains("Critical", StringComparison.Ordinal);

    private static IReadOnlyList<string> ExtractDiagnostics(ValidationStepKind kind, ProcessResult result)
    {
        IReadOnlyList<string> all = result.AllLines();

        // Keep the lines that name a real problem. The agent needs the compiler's own
        // text verbatim; restating it in our own words loses the information it needs.
        List<string> relevant =
        [
            .. all.Where(line =>
                line.Contains(": error ", StringComparison.Ordinal) ||
                line.Contains(": warning ", StringComparison.Ordinal) ||
                line.Contains("Assert.", StringComparison.Ordinal) ||
                line.Contains("failed:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("[xUnit.net", StringComparison.Ordinal)),
        ];

        if (relevant.Count > 0)
        {
            return [.. relevant.Distinct(StringComparer.Ordinal).Take(80)];
        }

        // Nothing matched the known shapes, so fall back to the tail of the output,
        // which is where a process that failed without a structured message explains itself.
        return kind == ValidationStepKind.Vulnerabilities
            ? [.. all.Take(40)]
            : [.. all.TakeLast(40)];
    }

    private static ValidationStep Skipped(ValidationStepKind kind, string reason) =>
        new(kind, ValidationOutcome.Skipped, TimeSpan.Zero, null, reason, []);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Validation of issue {IssueNumber} attempt {Attempt}: passed={Passed}, failures={FailureCount}")]
    private partial void LogCompleted(int issueNumber, int attempt, bool passed, int failureCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "'dotnet test' reported zero tests for issue {IssueNumber}; running {ProjectCount} test project(s) directly instead")]
    private partial void LogTestFallback(int issueNumber, int projectCount);
}
