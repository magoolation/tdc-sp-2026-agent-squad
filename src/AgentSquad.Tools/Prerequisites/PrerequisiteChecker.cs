using System.Globalization;
using System.Text.RegularExpressions;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using Microsoft.Extensions.Options;

namespace AgentSquad.Tools.Prerequisites;

/// <summary>How important a prerequisite is.</summary>
public enum PrerequisiteSeverity
{
    /// <summary>The run cannot start without it.</summary>
    Required = 0,

    /// <summary>The run works without it, but something degrades.</summary>
    Recommended = 1,
}

/// <summary>
/// The result of checking one prerequisite.
/// </summary>
/// <param name="Name">What was checked.</param>
/// <param name="Severity">Whether it blocks the run.</param>
/// <param name="Satisfied">Whether the check passed.</param>
/// <param name="Detail">What was found, such as a version string.</param>
/// <param name="Remedy">The exact command to run to fix it, when it failed.</param>
public sealed record PrerequisiteResult(
    string Name,
    PrerequisiteSeverity Severity,
    bool Satisfied,
    string Detail,
    string? Remedy);

/// <summary>
/// Verifies that everything the factory depends on is present and correctly configured.
/// </summary>
/// <remarks>
/// <para>
/// This runs before any model is called and before anything is written to GitHub. Every
/// failure it can diagnose here is a failure that would otherwise surface twenty minutes
/// into a run — or, worse, on stage.
/// </para>
/// <para>
/// Each failed check carries the literal command that fixes it, so the operator never has
/// to guess what "gh is not authenticated" is asking for.
/// </para>
/// </remarks>
public sealed partial class PrerequisiteChecker(
    IProcessRunner processRunner,
    IOptions<SquadOptions> squadOptions,
    IOptions<GitHubOptions> gitHubOptions,
    IOptions<FoundryOptions> foundryOptions)
{
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly SquadOptions _squad = squadOptions.Value;
    private readonly GitHubOptions _gitHub = gitHubOptions.Value;
    private readonly FoundryOptions _foundry = foundryOptions.Value;

    /// <summary>Minimum .NET SDK feature band the solution targets.</summary>
    public const string MinimumDotNetVersion = "10.0.100";

    /// <summary>Minimum GitHub Copilot CLI version.</summary>
    public const string MinimumCopilotVersion = "1.0.80";

    /// <summary>Minimum git version.</summary>
    public const string MinimumGitVersion = "2.45.0";

    /// <summary>
    /// Runs every check.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per prerequisite, in the order they were checked.</returns>
    public async Task<IReadOnlyList<PrerequisiteResult>> CheckAllAsync(CancellationToken cancellationToken)
    {
        List<PrerequisiteResult> results =
        [
            await CheckDotNetAsync(cancellationToken),
            await CheckGitAsync(cancellationToken),
            await CheckGitIdentityAsync(cancellationToken),
            await CheckGitHubCliAsync(cancellationToken),
            await CheckGitHubAuthAsync(cancellationToken),
            await CheckGitHubScopesAsync(cancellationToken),
            await CheckRepositoryAccessAsync(cancellationToken),
            await CheckCopilotCliAsync(cancellationToken),
            await CheckCopilotAuthAsync(cancellationToken),
            await CheckAzureCliAsync(cancellationToken),
            CheckFoundryEndpoint(),
            CheckWorkRoot(),
            CheckPathBudget(),
        ];

        return results;
    }

    private async Task<PrerequisiteResult> CheckDotNetAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("dotnet", ["--version"], cancellationToken);

        if (!result.Succeeded)
        {
            return Fail(".NET SDK", "dotnet was not found on PATH.", "https://dotnet.microsoft.com/download/dotnet/10.0");
        }

        string version = result.StandardOutput.Trim();
        bool ok = CompareVersions(version, MinimumDotNetVersion) >= 0;

        return new PrerequisiteResult(
            ".NET SDK",
            PrerequisiteSeverity.Required,
            ok,
            version,
            ok ? null : $"Install .NET SDK {MinimumDotNetVersion} or newer: https://dotnet.microsoft.com/download/dotnet/10.0");
    }

    private async Task<PrerequisiteResult> CheckGitAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("git", ["--version"], cancellationToken);

        if (!result.Succeeded)
        {
            return Fail("git", "git was not found on PATH.", "winget install Git.Git");
        }

        Match match = VersionRegex().Match(result.StandardOutput);
        string version = match.Success ? match.Value : result.StandardOutput.Trim();
        bool ok = !match.Success || CompareVersions(version, MinimumGitVersion) >= 0;

        return new PrerequisiteResult(
            "git",
            PrerequisiteSeverity.Required,
            ok,
            version,
            ok ? null : $"Update git to {MinimumGitVersion} or newer: winget upgrade Git.Git");
    }

    private async Task<PrerequisiteResult> CheckGitIdentityAsync(CancellationToken cancellationToken)
    {
        ProcessResult name = await RunAsync("git", ["config", "--get", "user.name"], cancellationToken);
        ProcessResult email = await RunAsync("git", ["config", "--get", "user.email"], cancellationToken);

        bool ok = name.Succeeded && email.Succeeded &&
                  !string.IsNullOrWhiteSpace(name.StandardOutput) &&
                  !string.IsNullOrWhiteSpace(email.StandardOutput);

        return new PrerequisiteResult(
            "git identity",
            PrerequisiteSeverity.Required,
            ok,
            ok ? $"{name.StandardOutput.Trim()} <{email.StandardOutput.Trim()}>" : "user.name or user.email is not set",
            ok ? null : "git config --global user.name \"Your Name\" && git config --global user.email \"you@example.com\"");
    }

    private async Task<PrerequisiteResult> CheckGitHubCliAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("gh", ["--version"], cancellationToken);

        if (!result.Succeeded)
        {
            return Fail("GitHub CLI", "gh was not found on PATH.", "winget install GitHub.cli");
        }

        Match match = VersionRegex().Match(result.StandardOutput);
        string version = match.Success ? match.Value : result.StandardOutput.Split('\n')[0].Trim();

        // Native sub-issues and issue dependencies (--parent, --add-sub-issue, --blocked-by)
        // arrived in 2.101. Older builds fail with an argument-parse error rather than a
        // message that explains anything, so the version is checked here instead.
        bool ok = !match.Success || CompareVersions(version, _gitHub.MinimumCliVersion) >= 0;

        return new PrerequisiteResult(
            "GitHub CLI",
            PrerequisiteSeverity.Required,
            ok,
            version,
            ok ? null : $"Update gh to {_gitHub.MinimumCliVersion} or newer: winget upgrade GitHub.cli");
    }

    private async Task<PrerequisiteResult> CheckGitHubAuthAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("gh", ["auth", "status"], cancellationToken);

        // gh writes the status to stderr even on success.
        string output = result.StandardOutput + result.StandardError;
        bool ok = result.Succeeded && output.Contains("Logged in", StringComparison.OrdinalIgnoreCase);

        Match account = AccountRegex().Match(output);

        return new PrerequisiteResult(
            "GitHub authentication",
            PrerequisiteSeverity.Required,
            ok,
            ok && account.Success ? account.Groups["login"].Value : "not authenticated",
            ok ? null : "gh auth login");
    }

    private async Task<PrerequisiteResult> CheckGitHubScopesAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("gh", ["auth", "status"], cancellationToken);
        string output = result.StandardOutput + result.StandardError;

        Match scopes = ScopesRegex().Match(output);
        string found = scopes.Success ? scopes.Groups["scopes"].Value : string.Empty;

        bool Has(string scope) => found.Contains($"'{scope}'", StringComparison.OrdinalIgnoreCase);

        // `repo` is the one that genuinely blocks: without it there are no issues and no
        // pull requests.
        //
        // `workflow` is only Recommended. It is required to push to .github/workflows
        // through some credential types, but not all — a push of a workflow file succeeded
        // here on a token without it. Treating it as blocking would stop a run that would
        // have worked, so it is reported and left to the operator.
        bool hasRepo = Has("repo");
        bool hasWorkflow = Has("workflow");

        if (!hasRepo)
        {
            return new PrerequisiteResult(
                "GitHub token scopes",
                PrerequisiteSeverity.Required,
                false,
                found.Length > 0 ? found : "unknown",
                "gh auth refresh -h github.com -s repo");
        }

        return new PrerequisiteResult(
            "GitHub token scopes",
            PrerequisiteSeverity.Recommended,
            hasWorkflow,
            found.Length > 0 ? found : "unknown",
            hasWorkflow
                ? null
                : "gh auth refresh -h github.com -s workflow   " +
                  "(só necessário se um agente for alterar .github/workflows; dependendo da credencial, o push funciona sem ele)");
    }

    private async Task<PrerequisiteResult> CheckRepositoryAccessAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_gitHub.Owner) || string.IsNullOrWhiteSpace(_gitHub.Repository))
        {
            return Fail(
                "Target repository",
                "GitHub:Owner and GitHub:Repository are not configured.",
                "Set them in appsettings.json or pass --owner and --repo.");
        }

        ProcessResult result = await RunAsync(
            "gh",
            ["repo", "view", _gitHub.Slug, "--json", "name,viewerPermission", "--jq", ".viewerPermission"],
            cancellationToken);

        if (!result.Succeeded)
        {
            return Fail(
                "Target repository",
                $"{_gitHub.Slug} is not reachable.",
                $"gh repo create {_gitHub.Slug} --private --clone");
        }

        string permission = result.StandardOutput.Trim();
        bool ok = permission is "ADMIN" or "MAINTAIN" or "WRITE";

        return new PrerequisiteResult(
            "Target repository",
            PrerequisiteSeverity.Required,
            ok,
            $"{_gitHub.Slug} ({permission})",
            ok ? null : $"Write access to {_gitHub.Slug} is required; the token currently has {permission}.");
    }

    private async Task<PrerequisiteResult> CheckCopilotCliAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("copilot", ["--version"], cancellationToken);

        if (!result.Succeeded)
        {
            return new PrerequisiteResult(
                "GitHub Copilot CLI",
                PrerequisiteSeverity.Recommended,
                false,
                "copilot was not found on PATH",
                "npm install -g @github/copilot   (optional: the SDK ships its own pinned runtime)");
        }

        Match match = VersionRegex().Match(result.StandardOutput);
        string version = match.Success ? match.Value : result.StandardOutput.Trim();
        bool ok = !match.Success || CompareVersions(version, MinimumCopilotVersion) >= 0;

        return new PrerequisiteResult(
            "GitHub Copilot CLI",
            PrerequisiteSeverity.Recommended,
            ok,
            version,
            ok ? null : $"copilot update   (found {version}, want {MinimumCopilotVersion}+)");
    }

    private async Task<PrerequisiteResult> CheckCopilotAuthAsync(CancellationToken cancellationToken)
    {
        bool hasToken =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GH_TOKEN")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

        if (hasToken)
        {
            return new PrerequisiteResult(
                "Copilot entitlement",
                PrerequisiteSeverity.Required,
                true,
                "token supplied through the environment",
                null);
        }

        // No explicit token, so the CLI falls back to its own stored credentials.
        ProcessResult result = await RunAsync("gh", ["auth", "status"], cancellationToken);
        bool ok = result.Succeeded;

        return new PrerequisiteResult(
            "Copilot entitlement",
            PrerequisiteSeverity.Required,
            ok,
            ok ? "using the signed-in GitHub account" : "no Copilot credential found",
            ok ? null : "copilot login   (or export GH_TOKEN with the 'Copilot Requests' permission)");
    }

    private async Task<PrerequisiteResult> CheckAzureCliAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync("az", ["account", "show", "--query", "name", "-o", "tsv"], cancellationToken);
        bool ok = result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput);

        return new PrerequisiteResult(
            "Azure sign-in",
            PrerequisiteSeverity.Required,
            ok,
            ok ? result.StandardOutput.Trim() : "not signed in",
            ok ? null : "az login   (DefaultAzureCredential reads this session)");
    }

    private PrerequisiteResult CheckFoundryEndpoint()
    {
        bool configured = !string.IsNullOrWhiteSpace(_foundry.ProjectEndpoint);
        bool wellFormed = configured &&
            Uri.TryCreate(_foundry.ProjectEndpoint, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.AbsolutePath.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase);

        return new PrerequisiteResult(
            "Microsoft Foundry endpoint",
            PrerequisiteSeverity.Required,
            wellFormed,
            configured ? _foundry.ProjectEndpoint : "not configured",
            wellFormed
                ? null
                : "Set Foundry:ProjectEndpoint to https://<account>.services.ai.azure.com/api/projects/<project> (azd up prints it).");
    }

    private PrerequisiteResult CheckWorkRoot()
    {
        try
        {
            Directory.CreateDirectory(_squad.WorkRoot);
            string probe = Path.Combine(_squad.WorkRoot, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            return new PrerequisiteResult("Work directory", PrerequisiteSeverity.Required, true, _squad.WorkRoot, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("Work directory", $"{_squad.WorkRoot} is not writable: {ex.Message}", $"Choose a writable Squad:WorkRoot.");
        }
    }

    /// <summary>
    /// Checks that the worktree root leaves enough room for real project paths.
    /// </summary>
    /// <remarks>
    /// <c>core.longpaths</c> lifts the 260-character limit but not a second ceiling near
    /// 320 characters. A real ASP.NET project contributes about 110 characters of tracked
    /// path, and MSBuild adds 60 to 180 more, so the worktree root has to stay short.
    /// </remarks>
    /// <returns>The check result.</returns>
    private PrerequisiteResult CheckPathBudget()
    {
        const int Budget = 80;
        int length = _squad.WorktreesDirectory.Length + "/i9999".Length;
        bool ok = length <= Budget;

        return new PrerequisiteResult(
            "Worktree path budget",
            PrerequisiteSeverity.Recommended,
            ok,
            $"{length.ToString(CultureInfo.InvariantCulture)} characters (budget {Budget})",
            ok ? null : $"Shorten Squad:WorkRoot — deep project paths will exceed the Windows limit. Current root: {_squad.WorkRoot}");
    }

    private async Task<ProcessResult> RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(
                fileName,
                arguments,
                Directory.GetCurrentDirectory(),
                TimeSpan.FromSeconds(30),
                environment: null,
                cancellationToken);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The executable is not on PATH. That is a check result, not an error.
            return new ProcessResult(-1, string.Empty, ex.Message, TimeSpan.Zero, TimedOut: false);
        }
    }

    private static PrerequisiteResult Fail(string name, string detail, string remedy) =>
        new(name, PrerequisiteSeverity.Required, false, detail, remedy);

    private static int CompareVersions(string left, string right)
    {
        Match leftMatch = VersionRegex().Match(left);
        Match rightMatch = VersionRegex().Match(right);

        if (!leftMatch.Success || !rightMatch.Success)
        {
            return 0;
        }

        return Version.TryParse(leftMatch.Value, out Version? l) && Version.TryParse(rightMatch.Value, out Version? r)
            ? l.CompareTo(r)
            : 0;
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"Logged in to \S+ account (?<login>\S+)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex AccountRegex();

    [GeneratedRegex(@"Token scopes:\s*(?<scopes>.+)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex ScopesRegex();
}
