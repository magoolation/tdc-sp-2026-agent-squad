using System.ComponentModel.DataAnnotations;

namespace AgentSquad.Core.Configuration;

/// <summary>
/// How the GitHub Copilot coding agents are configured and constrained.
/// </summary>
/// <remarks>
/// <para>
/// The deny list here is the practical expression of AI-004 (excessive agency): the
/// coding agent may write code and run builds and tests, but it may not publish anything.
/// Branch pushes, pull requests and merges belong to the orchestrator, which only acts
/// after the deterministic gate has passed.
/// </para>
/// <para>
/// Denials take precedence over every allow rule in the Copilot permission model,
/// including blanket approval, so this list holds even when tools are auto-approved.
/// </para>
/// </remarks>
public sealed class CopilotOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Copilot";

    /// <summary>Gets or sets the model the coding agents use.</summary>
    [Required]
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>
    /// Gets or sets the reasoning effort for the coding agents.
    /// </summary>
    /// <remarks>Valid values: <c>none</c>, <c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c>, <c>max</c>.</remarks>
    public string ReasoningEffort { get; set; } = "high";

    /// <summary>
    /// Gets or sets an explicit path to the Copilot runtime, overriding the bundled binary.
    /// </summary>
    /// <remarks>
    /// Leave empty to use the runtime the SDK downloads and version-pins at build time.
    /// Set it to reuse an existing installation, or for an air-gapped machine.
    /// </remarks>
    public string? RuntimePath { get; set; }

    /// <summary>
    /// Gets or sets the shell command prefixes the agent is never allowed to run.
    /// </summary>
    /// <remarks>
    /// Matched against the first-level command identifier reported by the permission
    /// request, so <c>git push</c> is blocked while <c>git commit</c> is not.
    /// </remarks>
    public IList<string> DeniedShellCommands { get; } =
    [
        "git push",
        "git worktree",
        "git checkout",
        "git switch",
        "git rebase",
        "git merge",
        "git reset",
        "git stash",
        "git gc",
        "git prune",
        "git config",
        "gh pr",
        "gh repo",
        "gh release",
        "gh auth",
        "gh secret",
        "npm publish",
        "dotnet nuget push",
        "az",
        "azd",
        "rm",
        "rmdir",
        "del",
        "format",
        "shutdown",
        "reg",
    ];

    /// <summary>
    /// Gets or sets the hosts the agent may fetch from.
    /// </summary>
    /// <remarks>
    /// An empty list denies all network fetches. Documentation lookups are useful to a
    /// coding agent; arbitrary egress is an exfiltration path (AI-003).
    /// </remarks>
    public IList<string> AllowedHosts { get; } =
    [
        "learn.microsoft.com",
        "docs.microsoft.com",
        "github.com",
        "raw.githubusercontent.com",
        "api.nuget.org",
        "www.nuget.org",
        "devblogs.microsoft.com",
    ];

    /// <summary>
    /// Gets or sets a value indicating whether the agent may ask the human questions mid-run.
    /// </summary>
    /// <remarks>
    /// Off: a parallel wave of agents blocking on interactive questions would stall the
    /// whole run. An agent that needs an answer must stop and report instead (AGENTS.md §3.10).
    /// </remarks>
    public bool AllowAskUser { get; set; }

    /// <summary>Gets or sets a value indicating whether agent output is streamed for live display.</summary>
    public bool Streaming { get; set; } = true;

    /// <summary>
    /// Gets or sets the repository-relative directories holding skills the agents may load.
    /// </summary>
    public IList<string> SkillDirectories { get; } = [".github/skills"];

    /// <summary>
    /// Gets or sets the repository-relative directories holding path-scoped instructions.
    /// </summary>
    public IList<string> InstructionDirectories { get; } = [".github/instructions"];
}
