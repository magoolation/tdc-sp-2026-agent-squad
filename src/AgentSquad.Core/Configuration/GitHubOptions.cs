using System.ComponentModel.DataAnnotations;

namespace AgentSquad.Core.Configuration;

/// <summary>
/// The GitHub repository the factory publishes work to.
/// </summary>
/// <remarks>
/// No token lives here. Authentication comes from the <c>gh</c> CLI's own credential
/// store or from <c>GH_TOKEN</c> in the environment (SEC-001).
/// </remarks>
public sealed class GitHubOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "GitHub";

    /// <summary>Gets or sets the repository owner, user or organization.</summary>
    /// <remarks>Validated by <c>PrerequisiteChecker</c> rather than by data annotations, so
    /// the <c>doctor</c> command can report it missing instead of failing to start.</remarks>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Gets or sets the repository name.</summary>
    /// <remarks>See <see cref="Owner"/> for why this is not <c>[Required]</c>.</remarks>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch pull requests target.</summary>
    [Required]
    public string BaseBranch { get; set; } = "main";

    /// <summary>
    /// Gets or sets the minimum supported <c>gh</c> version.
    /// </summary>
    /// <remarks>
    /// 2.101.0 is the floor because native sub-issues (<c>--parent</c>,
    /// <c>--add-sub-issue</c>) and issue dependencies arrived in that line. Older builds
    /// fail with an argument-parse error rather than a clear message, so the prerequisite
    /// check verifies the version up front.
    /// </remarks>
    public string MinimumCliVersion { get; set; } = "2.101.0";

    /// <summary>
    /// Gets or sets how many issue-creation calls may be in flight at once.
    /// </summary>
    /// <remarks>
    /// GitHub's secondary rate limiter penalizes rapid content creation well before the
    /// 5,000/hour primary budget matters, so this stays deliberately small.
    /// </remarks>
    [Range(1, 5)]
    public int IssueCreationConcurrency { get; set; } = 2;

    /// <summary>Gets the <c>owner/repo</c> slug.</summary>
    public string Slug => $"{Owner}/{Repository}";

    /// <summary>Gets the SSH clone URL.</summary>
    public string CloneUrl => $"https://github.com/{Owner}/{Repository}.git";
}
