using System.ComponentModel.DataAnnotations;

namespace AgentSquad.Core.Configuration;

/// <summary>
/// How the factory runs: concurrency, budgets and where state lives.
/// </summary>
/// <remarks>
/// Every limit here exists because of AI-007: an autonomous system without explicit
/// ceilings on iteration, time and spend is a system that can run away.
/// </remarks>
public sealed class SquadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Squad";

    /// <summary>
    /// Gets or sets the root directory for run state, worktrees and build artifacts.
    /// </summary>
    /// <remarks>
    /// Keep this short. Worktree paths on Windows hit a ceiling near 320 characters that
    /// <c>core.longpaths</c> does not lift, and MSBuild adds 60–180 characters of its own.
    /// </remarks>
    [Required]
    public string WorkRoot { get; set; } = @"C:\squad";

    /// <summary>
    /// Gets or sets how many coding agents may run at once.
    /// </summary>
    /// <remarks>
    /// Measured throughput on a 32-core machine flattens past twelve. Each agent's build
    /// is also capped by <see cref="MsBuildNodesPerAgent"/> so that
    /// <c>agents × nodes ≈ cores</c>.
    /// </remarks>
    [Range(1, 32)]
    public int MaxParallelAgents { get; set; } = 4;

    /// <summary>Gets or sets the <c>-m</c> value passed to each agent's build.</summary>
    [Range(1, 16)]
    public int MsBuildNodesPerAgent { get; set; } = 4;

    /// <summary>Gets or sets how many repair rounds a work item gets before it is abandoned.</summary>
    [Range(0, 5)]
    public int MaxRepairAttempts { get; set; } = 2;

    /// <summary>Gets or sets how many times the architect may revise a rejected plan.</summary>
    [Range(0, 5)]
    public int MaxPlanRevisions { get; set; } = 2;

    /// <summary>Gets or sets how many rounds of clarifying questions the human may be asked.</summary>
    [Range(0, 3)]
    public int MaxClarificationRounds { get; set; } = 2;

    /// <summary>Gets or sets the time budget for a single coding-agent attempt.</summary>
    public TimeSpan AgentTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Gets or sets the time budget for one validation gate run.</summary>
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets the time budget for a single external process.</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets the time budget for the whole run.</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromHours(3);

    /// <summary>
    /// Gets or sets a value indicating whether pull requests are opened as drafts.
    /// </summary>
    /// <remarks>Drafts are the honest default: a human still has to read the code.</remarks>
    public bool OpenPullRequestsAsDraft { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the human is asked to approve the plan.
    /// </summary>
    /// <remarks>
    /// Turning this off contradicts AI-005 and is only appropriate for a rehearsal
    /// against a scratch repository.
    /// </remarks>
    public bool RequirePlanApproval { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether worktrees are removed after a successful run.</summary>
    public bool CleanupWorktrees { get; set; } = true;

    /// <summary>Gets the directory where run journals are written.</summary>
    public string RunsDirectory => Path.Combine(WorkRoot, "runs");

    /// <summary>Gets the directory that holds the local clone of the target repository.</summary>
    public string ReposDirectory => Path.Combine(WorkRoot, "repos");

    /// <summary>Gets the directory that holds agent worktrees.</summary>
    public string WorktreesDirectory => Path.Combine(WorkRoot, "wt");

    /// <summary>Gets the directory that holds per-worktree build artifacts, kept out of the worktrees.</summary>
    public string ArtifactsDirectory => Path.Combine(WorkRoot, "bld");

    /// <summary>
    /// Gets the shared NuGet scratch directory.
    /// </summary>
    /// <remarks>
    /// This must be one path shared by every concurrent build. NuGet's cross-process
    /// extraction locks live here rather than in the packages folder, and giving each
    /// worker its own scratch directory corrupts the shared package cache permanently:
    /// packages end up with a <c>.nupkg.metadata</c> marker but no <c>.nuspec</c>, and
    /// every later restore fails with NU5037 until the cache is purged by hand.
    /// </remarks>
    public string NuGetScratchDirectory => Path.Combine(WorkRoot, "nuget", "scratch");

    /// <summary>Gets the shared NuGet global packages folder.</summary>
    public string NuGetPackagesDirectory => Path.Combine(WorkRoot, "nuget", "packages");
}
