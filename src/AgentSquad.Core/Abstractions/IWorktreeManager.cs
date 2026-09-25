namespace AgentSquad.Core.Abstractions;

/// <summary>
/// An isolated checkout in which exactly one coding agent works.
/// </summary>
/// <param name="IssueNumber">The issue being implemented here.</param>
/// <param name="Branch">The branch checked out.</param>
/// <param name="Path">Absolute path of the worktree root.</param>
/// <param name="ArtifactsPath">Absolute path where MSBuild writes <c>obj</c>/<c>bin</c> for this worktree.</param>
public sealed record AgentWorktree(
    int IssueNumber,
    string Branch,
    string Path,
    string ArtifactsPath);

/// <summary>
/// Creates and destroys the isolated checkouts that make parallel agents safe.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must account for three verified Windows behaviours:
/// </para>
/// <list type="number">
///   <item>
///     Creating a worktree with a tracking branch writes to the shared <c>.git/config</c>,
///     which races under concurrency. Creation is therefore serialized, and branches are
///     created untracked; upstream is set later by the push.
///   </item>
///   <item>
///     <c>git worktree remove</c> is <b>not atomic</b>: when deletion fails it has already
///     deregistered the worktree and deleted the tracked files, so recovery is a filesystem
///     concern. Removal must retry with backoff and then prune.
///   </item>
///   <item>
///     MSBuild node reuse holds analyzer assemblies for fifteen minutes, which blocks
///     directory deletion. Build servers must be shut down before removal.
///   </item>
/// </list>
/// <para>See <c>.github/skills/worktree-hygiene/SKILL.md</c> for the agent-facing rules.</para>
/// </remarks>
public interface IWorktreeManager
{
    /// <summary>
    /// Prepares the repository for parallel worktrees.
    /// </summary>
    /// <remarks>
    /// Sets <c>core.longpaths</c>, enables per-worktree config, and disables automatic
    /// maintenance so background garbage collection cannot prune objects a sibling needs.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the repository is configured.</returns>
    Task PrepareRepositoryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates an isolated worktree for one issue.
    /// </summary>
    /// <param name="issueNumber">The issue being implemented.</param>
    /// <param name="branch">The branch name to create.</param>
    /// <param name="baseRef">The commit-ish to branch from, typically <c>origin/main</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created worktree.</returns>
    Task<AgentWorktree> CreateAsync(int issueNumber, string branch, string baseRef, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a worktree and, optionally, its branch.
    /// </summary>
    /// <param name="worktree">The worktree to remove.</param>
    /// <param name="deleteBranch">Whether to delete the branch too. A branch survives worktree removal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the directory is gone; otherwise <see langword="false"/>.</returns>
    Task<bool> RemoveAsync(AgentWorktree worktree, bool deleteBranch, CancellationToken cancellationToken);

    /// <summary>Removes every worktree the factory created, best effort.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of worktrees removed.</returns>
    Task<int> CleanupAllAsync(CancellationToken cancellationToken);
}
