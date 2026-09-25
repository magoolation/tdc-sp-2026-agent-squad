using System.Text.Json.Serialization;

namespace AgentSquad.Core.Intake;

/// <summary>
/// Whether the request creates something new or changes something that already exists.
/// </summary>
/// <remarks>
/// This is the first decision the factory makes, and it changes everything downstream:
/// a brownfield run must read the repository before asking the human anything
/// (see <c>.github/skills/requirements-elicitation/SKILL.md</c>).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryMode>))]
public enum DeliveryMode
{
    /// <summary>Not yet determined.</summary>
    Unknown = 0,

    /// <summary>A brand-new solution in an empty or non-existent repository.</summary>
    Greenfield = 1,

    /// <summary>A change to an existing codebase.</summary>
    Brownfield = 2,

    /// <summary>A self-contained new module inside an existing repository.</summary>
    BrownfieldNewModule = 3,
}

/// <summary>
/// What the factory learned about the target repository before planning anything.
/// </summary>
/// <param name="Mode">Whether this is a new solution or a change to an existing one.</param>
/// <param name="Owner">GitHub owner (user or organization) of the target repository.</param>
/// <param name="Repository">GitHub repository name.</param>
/// <param name="DefaultBranch">The branch new work branches from.</param>
/// <param name="LocalPath">Absolute path of the local clone the orchestrator owns.</param>
/// <param name="PrimaryLanguage">Dominant language detected, or <see langword="null"/> for an empty repository.</param>
/// <param name="Stack">Frameworks, test libraries and tools detected in the repository.</param>
/// <param name="SolutionFiles">Solution files found at the repository root, if any.</param>
/// <param name="Conventions">Free-form notes about the conventions the repository already follows.</param>
/// <param name="Summary">A short natural-language description for the human and for downstream prompts.</param>
public sealed record ProjectContext(
    DeliveryMode Mode,
    string Owner,
    string Repository,
    string DefaultBranch,
    string LocalPath,
    string? PrimaryLanguage,
    IReadOnlyList<string> Stack,
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> Conventions,
    string Summary)
{
    /// <summary>Gets the <c>owner/repo</c> slug used by the GitHub CLI.</summary>
    [JsonIgnore]
    public string Slug => $"{Owner}/{Repository}";

    /// <summary>Gets a value indicating whether the repository already contains code.</summary>
    [JsonIgnore]
    public bool IsExisting => Mode is DeliveryMode.Brownfield or DeliveryMode.BrownfieldNewModule;
}
