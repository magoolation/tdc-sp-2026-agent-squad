namespace AgentSquad.Core.Abstractions;

/// <summary>
/// The result of running an external process.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
/// <param name="Duration">Wall-clock duration.</param>
/// <param name="TimedOut">Whether the process was killed for exceeding its budget.</param>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut)
{
    /// <summary>Gets a value indicating whether the process succeeded.</summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>Gets stdout and stderr interleaved as lines, for diagnostics.</summary>
    /// <returns>All output lines, stdout first.</returns>
    public IReadOnlyList<string> AllLines() =>
    [
        .. StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        .. StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    ];
}

/// <summary>
/// Runs external processes safely.
/// </summary>
/// <remarks>
/// Every implementation must pass arguments through <c>ProcessStartInfo.ArgumentList</c>
/// and never build a command line by concatenation (SEC-004), and must enforce a timeout
/// on every invocation (ENG-046).
/// </remarks>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a process to completion and captures its output.
    /// </summary>
    /// <param name="fileName">Executable to run.</param>
    /// <param name="arguments">Arguments, each passed separately. Never a joined command line.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="timeout">Maximum duration before the process is killed.</param>
    /// <param name="environment">Additional environment variables.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured result.</returns>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The git operations the orchestrator performs. Coding agents never call these.
/// </summary>
public interface IGitClient
{
    /// <summary>Clones a repository, or fetches if the clone already exists.</summary>
    /// <param name="cloneUrl">Remote URL.</param>
    /// <param name="destination">Local path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The absolute path of the local clone.</returns>
    Task<string> EnsureCloneAsync(string cloneUrl, string destination, CancellationToken cancellationToken);

    /// <summary>Fetches from the default remote and prunes deleted branches.</summary>
    /// <param name="repositoryPath">Local repository path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the fetch finishes.</returns>
    Task FetchAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>Stages everything and commits, if there is anything to commit.</summary>
    /// <param name="worktreePath">Worktree to commit in.</param>
    /// <param name="message">Commit message, already formatted per Conventional Commits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if a commit was created; <see langword="false"/> if the tree was clean.</returns>
    Task<bool> CommitAllAsync(string worktreePath, string message, CancellationToken cancellationToken);

    /// <summary>Gets the unified diff of a branch against its base.</summary>
    /// <param name="worktreePath">Worktree path.</param>
    /// <param name="baseRef">Base commit-ish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unified diff, possibly empty.</returns>
    Task<string> GetDiffAsync(string worktreePath, string baseRef, CancellationToken cancellationToken);

    /// <summary>Lists the repository-relative paths changed against a base.</summary>
    /// <param name="worktreePath">Worktree path.</param>
    /// <param name="baseRef">Base commit-ish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Changed paths with forward slashes.</returns>
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string worktreePath, string baseRef, CancellationToken cancellationToken);

    /// <summary>Pushes a branch and sets its upstream.</summary>
    /// <param name="worktreePath">Worktree path.</param>
    /// <param name="branch">Branch to push.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the push finishes.</returns>
    Task PushAsync(string worktreePath, string branch, CancellationToken cancellationToken);

    /// <summary>Gets the repository's default branch name from the remote HEAD.</summary>
    /// <param name="repositoryPath">Local repository path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The default branch name, for example <c>main</c>.</returns>
    Task<string> GetDefaultBranchAsync(string repositoryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the base branch exists on the remote, creating an initial commit if the
    /// repository has none.
    /// </summary>
    /// <remarks>
    /// A freshly created GitHub repository has zero commits and zero branches, so
    /// <c>origin/main</c> does not resolve and every worktree creation fails. That is the
    /// normal starting point for a greenfield delivery, not an edge case, so the factory
    /// seeds the repository rather than refusing to work with it.
    /// </remarks>
    /// <param name="repositoryPath">Local repository path.</param>
    /// <param name="branch">The base branch name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if an initial commit was created.</returns>
    Task<bool> EnsureBaseBranchAsync(string repositoryPath, string branch, CancellationToken cancellationToken);

    /// <summary>
    /// Points the clone's own checkout at the tip of the base branch on the remote.
    /// </summary>
    /// <remarks>
    /// The clone is reused across runs, so it is left on whatever branch the previous run
    /// last touched. Anything that reads the working tree — the intake inventory above all —
    /// would then describe the previous run's output instead of the branch this delivery
    /// targets, and decide greenfield or brownfield from stale local state.
    /// </remarks>
    /// <param name="repositoryPath">The clone.</param>
    /// <param name="branch">The base branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the checkout matches the remote.</returns>
    Task CheckoutBaseAsync(string repositoryPath, string branch, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the run's integration branch from the base branch and pushes it.
    /// </summary>
    /// <remarks>
    /// Waves build on each other, but the pull requests they produce are merged by a human
    /// and stay open. Branching every wave from the base branch would therefore hide wave 1's
    /// work from wave 2, so each agent would rebuild the foundation and the resulting pull
    /// requests would all conflict. The integration branch is what the waves accumulate onto.
    /// </remarks>
    /// <param name="repositoryPath">Local repository path.</param>
    /// <param name="integrationBranch">The integration branch name.</param>
    /// <param name="baseBranch">The branch to create it from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the branch exists on the remote.</returns>
    Task CreateIntegrationBranchAsync(
        string repositoryPath,
        string integrationBranch,
        string baseBranch,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fast-forwards the integration branch onto a completed work branch and pushes it.
    /// </summary>
    /// <param name="repositoryPath">Local repository path.</param>
    /// <param name="integrationBranch">The integration branch.</param>
    /// <param name="workBranch">The branch to integrate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the merge succeeded.</returns>
    Task<bool> IntegrateAsync(
        string repositoryPath,
        string integrationBranch,
        string workBranch,
        CancellationToken cancellationToken);
}
