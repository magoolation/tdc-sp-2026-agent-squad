using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Tools.Git;

/// <summary>
/// The git operations the orchestrator performs, wrapping the <c>git</c> executable.
/// </summary>
/// <remarks>
/// Coding agents never reach this type: branch creation, pushes and integration are the
/// orchestrator's responsibility, and the agent permission policy denies those commands
/// outright (AI-004, <c>.github/skills/worktree-hygiene/SKILL.md</c>).
/// </remarks>
public sealed partial class GitClient(
    IProcessRunner processRunner,
    IOptions<SquadOptions> options,
    ILogger<GitClient> logger) : IGitClient
{
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly SquadOptions _options = options.Value;
    private readonly ILogger<GitClient> _logger = logger;

    /// <inheritdoc />
    public async Task<string> EnsureCloneAsync(string cloneUrl, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloneUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        if (Directory.Exists(Path.Combine(destination, ".git")))
        {
            LogReusingClone(destination);
            await FetchAsync(destination, cancellationToken);
            return destination;
        }

        string? parent = Path.GetDirectoryName(destination);

        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        LogCloning(cloneUrl, destination);

        // --filter=blob:none keeps the clone small on large repositories without the
        // broken-history problem that --depth introduces: a shallow clone cannot compute
        // a diff against its own merge base, which the agents need.
        await RunAsync(
            parent ?? Directory.GetCurrentDirectory(),
            cancellationToken,
            "clone", "--filter=blob:none", cloneUrl, destination);

        return destination;
    }

    /// <inheritdoc />
    public Task FetchAsync(string repositoryPath, CancellationToken cancellationToken) =>
        RunAsync(repositoryPath, cancellationToken, "fetch", "--prune", "origin");

    /// <inheritdoc />
    public async Task<bool> CommitAllAsync(string worktreePath, string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ProcessResult status = await RunRawAsync(worktreePath, cancellationToken, "status", "--porcelain");

        if (string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            LogNothingToCommit(worktreePath);
            return false;
        }

        await RunAsync(worktreePath, cancellationToken, "add", "--all");

        // Passing the message through ArgumentList means a title produced by a model
        // cannot break out into the command line (SEC-004).
        await RunAsync(worktreePath, cancellationToken, "commit", "--message", message);

        return true;
    }

    /// <inheritdoc />
    public async Task<string> GetDiffAsync(string worktreePath, string baseRef, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunRawAsync(
            worktreePath,
            cancellationToken,
            "diff", $"{baseRef}...HEAD", "--", ".", ":(exclude)*.lock", ":(exclude)**/bin/**", ":(exclude)**/obj/**");

        return result.StandardOutput;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string worktreePath,
        string baseRef,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunRawAsync(
            worktreePath,
            cancellationToken,
            "diff", "--name-only", $"{baseRef}...HEAD");

        return
        [
            .. result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Replace('\\', '/')),
        ];
    }

    /// <inheritdoc />
    public Task PushAsync(string worktreePath, string branch, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        // --set-upstream here rather than at worktree creation: creating a tracking branch
        // writes to the shared .git/config, which races when several worktrees are created
        // at once. Pushing is already serialized per agent, so the write is safe here.
        return RunAsync(worktreePath, cancellationToken, "push", "--set-upstream", "origin", branch);
    }

    /// <inheritdoc />
    public async Task<string> GetDefaultBranchAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunRawAsync(
            repositoryPath,
            cancellationToken,
            "symbolic-ref", "--short", "refs/remotes/origin/HEAD");

        if (result.Succeeded)
        {
            string value = result.StandardOutput.Trim();
            int slash = value.LastIndexOf('/');

            return slash >= 0 ? value[(slash + 1)..] : value;
        }

        LogDefaultBranchFallback(repositoryPath);
        return "main";
    }

    /// <inheritdoc />
    public async Task<bool> EnsureBaseBranchAsync(
        string repositoryPath,
        string branch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        // A remote-tracking ref for the branch means there is something to branch from.
        ProcessResult remote = await RunRawAsync(
            repositoryPath, cancellationToken,
            "rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{branch}");

        if (remote.Succeeded)
        {
            return false;
        }

        LogSeedingRepository(repositoryPath, branch);

        // Put something real in the initial commit. An empty commit works for git but
        // leaves the agents with no README to read and no .gitignore, and the first thing
        // a coding agent would otherwise do is commit bin/ and obj/.
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "README.md"),
            SeedReadme,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, ".gitignore"),
            SeedGitIgnore,
            cancellationToken);

        await RunAsync(repositoryPath, cancellationToken, "checkout", "-B", branch);
        await RunAsync(repositoryPath, cancellationToken, "add", "README.md", ".gitignore");
        await RunAsync(repositoryPath, cancellationToken, "commit", "--message", SeedCommitMessage);
        await RunAsync(repositoryPath, cancellationToken, "push", "--set-upstream", "origin", branch);

        // Refresh remote-tracking refs so origin/<branch> resolves for worktree creation.
        await FetchAsync(repositoryPath, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task CreateIntegrationBranchAsync(
        string repositoryPath,
        string integrationBranch,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseBranch);

        LogCreatingIntegrationBranch(integrationBranch, baseBranch);

        // The main clone's own checkout is only ever used for branch bookkeeping; the agents
        // work exclusively in worktrees, so moving HEAD here is safe.
        await RunAsync(repositoryPath, cancellationToken, "checkout", "-B", integrationBranch, $"origin/{baseBranch}");
        await RunAsync(repositoryPath, cancellationToken, "push", "--force-with-lease", "--set-upstream", "origin", integrationBranch);
        await FetchAsync(repositoryPath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IntegrateAsync(
        string repositoryPath,
        string integrationBranch,
        string workBranch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(workBranch);

        await RunAsync(repositoryPath, cancellationToken, "checkout", integrationBranch);

        ProcessResult merge = await RunRawAsync(
            repositoryPath, cancellationToken,
            "merge", "--no-edit", workBranch);

        if (!merge.Succeeded)
        {
            // A conflict here means the plan's file-conflict rule was violated in a way the
            // static check could not see — two items that touched the same behaviour through
            // different files. Abort cleanly and let the run continue; the pull request is
            // already open and a human will see the conflict on it.
            await RunRawAsync(repositoryPath, cancellationToken, "merge", "--abort");
            LogIntegrationConflict(workBranch, integrationBranch);

            return false;
        }

        await RunAsync(repositoryPath, cancellationToken, "push", "origin", integrationBranch);
        LogIntegrated(workBranch, integrationBranch);

        return true;
    }

    private const string SeedCommitMessage = """
        chore: initial commit

        Created by Agent Squad so the repository has a base branch for the
        autonomous agents to branch from.
        """;

    private const string SeedReadme = """
        # Projeto

        Repositório inicializado pelo **Agent Squad**.

        O conteúdo abaixo será substituído pela primeira entrega. Cada tarefa vira uma
        issue, é implementada por um agente autônomo em um `git worktree` isolado, passa
        por um gate de validação determinístico e chega como um pull request para revisão
        humana.

        """;

    private const string SeedGitIgnore = """
        # ---------------------------------------------------------------------------
        # .NET
        # ---------------------------------------------------------------------------
        [Bb]in/
        [Oo]bj/
        artifacts/
        *.user
        *.binlog
        [Tt]est[Rr]esults/

        # ---------------------------------------------------------------------------
        # Secrets — NEVER commit
        # ---------------------------------------------------------------------------
        .env
        .env.*
        !.env.example
        appsettings.Local.json
        secrets.json
        *.pfx
        *.pem
        *.key

        # ---------------------------------------------------------------------------
        # Tooling
        # ---------------------------------------------------------------------------
        .vs/
        .idea/
        node_modules/
        .DS_Store
        Thumbs.db

        """;

    private async Task RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        ProcessResult result = await RunRawAsync(workingDirectory, cancellationToken, arguments);

        if (!result.Succeeded)
        {
            throw new GitCommandException(arguments[0], result);
        }
    }

    private Task<ProcessResult> RunRawAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        _processRunner.RunAsync(
            "git",
            arguments,
            workingDirectory,
            _options.ProcessTimeout,
            environment: null,
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cloning {CloneUrl} into {Destination}")]
    private partial void LogCloning(string cloneUrl, string destination);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reusing the existing clone at {Destination}")]
    private partial void LogReusingClone(string destination);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nothing to commit in {WorktreePath}")]
    private partial void LogNothingToCommit(string worktreePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the default branch of {RepositoryPath}; assuming 'main'")]
    private partial void LogDefaultBranchFallback(string repositoryPath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{RepositoryPath} has no commits; creating the initial commit on {Branch} so agents have a base to branch from")]
    private partial void LogSeedingRepository(string repositoryPath, string branch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating integration branch {Branch} from origin/{BaseBranch}")]
    private partial void LogCreatingIntegrationBranch(string branch, string baseBranch);

    [LoggerMessage(Level = LogLevel.Information, Message = "Integrated {WorkBranch} into {IntegrationBranch}")]
    private partial void LogIntegrated(string workBranch, string integrationBranch);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{WorkBranch} conflicts with {IntegrationBranch}; the merge was aborted and the pull request is left for a human")]
    private partial void LogIntegrationConflict(string workBranch, string integrationBranch);
}

/// <summary>
/// Thrown when a git command fails.
/// </summary>
public sealed class GitCommandException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
    public GitCommandException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
    /// <param name="message">The message.</param>
    public GitCommandException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public GitCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
    /// <param name="subcommand">The git subcommand that failed.</param>
    /// <param name="result">The captured process result.</param>
    public GitCommandException(string subcommand, ProcessResult result)
        : base(BuildMessage(subcommand, result))
    {
        Subcommand = subcommand;
        ExitCode = result?.ExitCode ?? -1;
    }

    /// <summary>Gets the git subcommand that failed.</summary>
    public string? Subcommand { get; }

    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; }

    private static string BuildMessage(string subcommand, ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return result.TimedOut
            ? $"'git {subcommand}' exceeded its time budget."
            : $"'git {subcommand}' failed with exit code {result.ExitCode}: {detail.Trim()}";
    }
}
