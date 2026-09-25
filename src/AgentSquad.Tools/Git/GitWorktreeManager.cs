using System.Globalization;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Tools.Git;

/// <summary>
/// Creates and destroys the isolated worktrees that make parallel coding agents safe.
/// </summary>
/// <remarks>
/// <para>
/// Three measured Windows behaviours shape this implementation, and each one produced a
/// real failure before it was handled:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <b>Creation races on the shared config.</b> <c>git worktree add -b X &lt;path&gt; origin/main</c>
///     writes <c>branch.X.remote</c> and <c>branch.X.merge</c> into the repository-wide
///     <c>.git/config</c>, taking <c>config.lock</c>. Under sixteen-way concurrency that failed
///     roughly four percent of the time, leaving a created branch beside a half-written config
///     section. Both fixes are applied here: creation is serialized behind a mutex, and the
///     branch is created with <c>--no-track</c> so nothing is written to the shared config at all.
///     Upstream is set later by <c>git push --set-upstream</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Removal is not atomic.</b> When <c>git worktree remove</c> cannot delete the directory —
///     and a process merely holding the worktree as its current directory is enough — it has
///     <i>already</i> deleted the tracked files and deregistered the worktree. Retrying gives
///     "is not a working tree" and <c>prune</c> finds nothing to clean. Recovery is therefore a
///     filesystem concern, handled by the retry loop below.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>MSBuild node reuse blocks deletion.</b> Idle build nodes linger for fifteen minutes
///     holding loaded analyzer and task assemblies. <c>dotnet build-server shutdown</c> runs
///     before every removal.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed partial class GitWorktreeManager : IWorktreeManager, IDisposable
{
    private readonly IProcessRunner _processRunner;
    private readonly SquadOptions _options;
    private readonly ILogger<GitWorktreeManager> _logger;

    /// <summary>
    /// Serializes worktree creation. Creation takes about a hundred milliseconds, so the
    /// throughput cost of serializing is negligible next to the cost of a torn config.
    /// </summary>
    private readonly SemaphoreSlim _creationLock = new(1, 1);

    private readonly List<AgentWorktree> _created = [];
    private readonly Lock _createdGuard = new();

    /// <summary>Initializes a new instance of the <see cref="GitWorktreeManager"/> class.</summary>
    /// <param name="processRunner">Process runner.</param>
    /// <param name="options">Squad options.</param>
    /// <param name="logger">Logger.</param>
    public GitWorktreeManager(
        IProcessRunner processRunner,
        IOptions<SquadOptions> options,
        ILogger<GitWorktreeManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Gets or sets the repository the manager operates on.</summary>
    public string RepositoryPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public async Task PrepareRepositoryAsync(CancellationToken cancellationToken)
    {
        EnsureRepositoryConfigured();

        // core.longpaths lifts the 260-character limit; it does not lift a second ceiling
        // near 320 characters, which is why worktree roots are kept short by construction.
        await ConfigAsync("core.longpaths", "true", cancellationToken);

        // Without this, `git config --worktree` silently writes to the shared config and
        // leaks settings such as core.autocrlf into every sibling worktree.
        await ConfigAsync("extensions.worktreeConfig", "true", cancellationToken);

        // Background maintenance can prune objects a sibling worktree still needs.
        await ConfigAsync("maintenance.auto", "false", cancellationToken);
        await ConfigAsync("gc.auto", "0", cancellationToken);

        // Give contended ref locks time to clear instead of failing outright.
        await ConfigAsync("core.filesRefLockTimeout", "5000", cancellationToken);
        await ConfigAsync("core.packedRefsTimeout", "5000", cancellationToken);

        Directory.CreateDirectory(_options.WorktreesDirectory);
        Directory.CreateDirectory(_options.ArtifactsDirectory);
        Directory.CreateDirectory(_options.NuGetScratchDirectory);
        Directory.CreateDirectory(_options.NuGetPackagesDirectory);

        LogPrepared(RepositoryPath);
    }

    /// <inheritdoc />
    public async Task<AgentWorktree> CreateAsync(
        int issueNumber,
        string branch,
        string baseRef,
        CancellationToken cancellationToken)
    {
        EnsureRepositoryConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);

        // Short, predictable directory name. The branch slug is not reused here because
        // the path budget matters more than the readability of the folder name.
        string folder = string.Create(CultureInfo.InvariantCulture, $"i{issueNumber}");
        string path = Path.Combine(_options.WorktreesDirectory, folder);
        string artifacts = Path.Combine(_options.ArtifactsDirectory, folder);

        await _creationLock.WaitAsync(cancellationToken);

        try
        {
            // Make the operation idempotent: a previous failed attempt can leave a branch
            // behind even when no worktree exists.
            await CleanupResidueAsync(path, branch, cancellationToken);

            ProcessResult result = await GitAsync(
                RepositoryPath,
                cancellationToken,
                "worktree", "add", "--no-track", "-b", branch, path, baseRef);

            if (!result.Succeeded)
            {
                throw new GitCommandException("worktree add", result);
            }
        }
        finally
        {
            _creationLock.Release();
        }

        Directory.CreateDirectory(artifacts);

        var worktree = new AgentWorktree(issueNumber, branch, path, artifacts);

        lock (_createdGuard)
        {
            _created.Add(worktree);
        }

        LogCreated(issueNumber, branch, path);

        return worktree;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(AgentWorktree worktree, bool deleteBranch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        EnsureRepositoryConfigured();

        // Build servers hold analyzer assemblies for fifteen minutes and will block deletion.
        await ShutdownBuildServersAsync(worktree.Path, cancellationToken);

        // Two -f flags: one forces past untracked files, the second past a git-level lock.
        await GitAsync(RepositoryPath, cancellationToken, "worktree", "remove", "--force", "--force", worktree.Path);

        // git may have failed after already deleting tracked files and deregistering the
        // worktree, so the directory is now purely a filesystem problem.
        bool removed = await TryDeleteDirectoryAsync(worktree.Path, cancellationToken);

        await GitAsync(RepositoryPath, cancellationToken, "worktree", "prune");

        if (deleteBranch)
        {
            // The branch survives `worktree remove`; it has to go separately.
            await GitAsync(RepositoryPath, cancellationToken, "branch", "-D", worktree.Branch);
        }

        if (removed)
        {
            LogRemoved(worktree.IssueNumber, worktree.Path);
        }
        else
        {
            LogRemovalIncomplete(worktree.Path);
        }

        lock (_createdGuard)
        {
            _created.RemoveAll(w => string.Equals(w.Path, worktree.Path, StringComparison.OrdinalIgnoreCase));
        }

        return removed;
    }

    /// <inheritdoc />
    public async Task<int> CleanupAllAsync(CancellationToken cancellationToken)
    {
        AgentWorktree[] snapshot;

        lock (_createdGuard)
        {
            snapshot = [.. _created];
        }

        int removed = 0;

        foreach (AgentWorktree worktree in snapshot)
        {
            try
            {
                if (await RemoveAsync(worktree, deleteBranch: false, cancellationToken))
                {
                    removed++;
                }
            }
            catch (GitCommandException ex)
            {
                LogCleanupFailed(ex, worktree.Path);
            }
        }

        return removed;
    }

    /// <inheritdoc />
    public void Dispose() => _creationLock.Dispose();

    private async Task CleanupResidueAsync(string path, string branch, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            await GitAsync(RepositoryPath, cancellationToken, "worktree", "remove", "--force", "--force", path);
            await TryDeleteDirectoryAsync(path, cancellationToken);
            await GitAsync(RepositoryPath, cancellationToken, "worktree", "prune");
        }

        // A losing creation race leaves the branch behind and, sometimes, a half-written
        // tracking config section. Remove both before retrying.
        await GitAsync(RepositoryPath, cancellationToken, "branch", "-D", branch);
        await GitAsync(RepositoryPath, cancellationToken, "config", "--remove-section", $"branch.{branch}");
    }

    private async Task<bool> TryDeleteDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        const int MaxAttempts = 5;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == MaxAttempts)
                {
                    LogDeleteFailed(ex, path, attempt);
                    return false;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), cancellationToken);
            }
        }

        return !Directory.Exists(path);
    }

    private Task<ProcessResult> ShutdownBuildServersAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        string directory = Directory.Exists(workingDirectory) ? workingDirectory : RepositoryPath;

        return _processRunner.RunAsync(
            "dotnet",
            ["build-server", "shutdown"],
            directory,
            TimeSpan.FromMinutes(2),
            environment: null,
            cancellationToken);
    }

    private Task<ProcessResult> ConfigAsync(string key, string value, CancellationToken cancellationToken) =>
        GitAsync(RepositoryPath, cancellationToken, "config", key, value);

    private Task<ProcessResult> GitAsync(
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

    private void EnsureRepositoryConfigured()
    {
        if (string.IsNullOrWhiteSpace(RepositoryPath))
        {
            throw new InvalidOperationException(
                $"{nameof(RepositoryPath)} must be set before using the worktree manager.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Repository {RepositoryPath} prepared for parallel worktrees")]
    private partial void LogPrepared(string repositoryPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worktree for issue {IssueNumber} created on {Branch} at {Path}")]
    private partial void LogCreated(int issueNumber, string branch, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worktree for issue {IssueNumber} removed from {Path}")]
    private partial void LogRemoved(int issueNumber, string path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "git deregistered the worktree at {Path} but the directory could not be deleted; it is now orphaned")]
    private partial void LogRemovalIncomplete(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete {Path} after {Attempts} attempts")]
    private partial void LogDeleteFailed(Exception exception, string path, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup of {Path} failed")]
    private partial void LogCleanupFailed(Exception exception, string path);
}
