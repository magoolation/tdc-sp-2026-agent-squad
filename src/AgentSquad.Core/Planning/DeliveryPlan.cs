using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgentSquad.Core.Planning;

/// <summary>Rough size of a work item, used for scheduling and for the <c>size:</c> label.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkItemSize>))]
public enum WorkItemSize
{
    /// <summary>Under ~100 lines of useful diff.</summary>
    Small = 0,

    /// <summary>Roughly 100–250 lines of useful diff.</summary>
    Medium = 1,

    /// <summary>Roughly 250–400 lines. Anything larger must be split.</summary>
    Large = 2,
}

/// <summary>What a work item does to a file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FileAction>))]
public enum FileAction
{
    /// <summary>The file does not exist yet.</summary>
    Create = 0,

    /// <summary>The file exists and will be changed.</summary>
    Modify = 1,

    /// <summary>The file exists and will be removed.</summary>
    Delete = 2,
}

/// <summary>
/// A file a work item intends to touch.
/// </summary>
/// <remarks>
/// This is not documentation: the orchestrator uses these paths to enforce the rule that
/// no two work items in the same wave may touch the same file. That rule is what makes
/// parallel agents safe (see <c>.github/skills/github-issue-authoring/SKILL.md</c>).
/// </remarks>
/// <param name="Path">Repository-relative path, using forward slashes.</param>
/// <param name="Action">What happens to the file.</param>
public sealed record PlannedFile(
    [property: Description("Repository-relative path with forward slashes.")] string Path,
    [property: Description("create | modify | delete")] FileAction Action);

/// <summary>
/// One unit of work: exactly what a single coding agent will implement in a single worktree.
/// </summary>
/// <param name="Key">Stable plan-local key such as <c>W-03</c>. Not the GitHub issue number.</param>
/// <param name="Title">Imperative summary, reused as the issue title and the commit subject.</param>
/// <param name="Goal">One sentence: which observable behaviour starts to exist.</param>
/// <param name="Context">Why this is needed, and which existing patterns to follow.</param>
/// <param name="Files">Every file the item expects to touch.</param>
/// <param name="OutOfScope">What must explicitly not be touched.</param>
/// <param name="AcceptanceCriteria">Machine-verifiable criteria. The validation gate decides, not the model.</param>
/// <param name="ImplementationNotes">Constraints and known traps. Never the code itself.</param>
/// <param name="RequirementIds">Requirements this item satisfies, for traceability.</param>
/// <param name="DependsOn">Keys of items that must be merged first.</param>
/// <param name="Wave">One-based parallelism wave. Every dependency must sit in a lower wave.</param>
/// <param name="Area">Component label: <c>core</c>, <c>agents</c>, <c>web</c>, <c>cli</c>, <c>infra</c>, <c>docs</c>.</param>
/// <param name="Size">Rough diff size.</param>
/// <param name="NeedsHuman">Whether a human must decide something before an agent may start.</param>
public sealed record WorkItem(
    [property: Description("Plan-local key, e.g. W-03.")] string Key,
    [property: Description("Imperative title, lowercase, no trailing period.")] string Title,
    [property: Description("One sentence describing the new observable behaviour.")] string Goal,
    [property: Description("Why this is needed and which existing patterns to follow.")] string Context,
    [property: Description("Every file this item expects to touch.")] IReadOnlyList<PlannedFile> Files,
    [property: Description("What must explicitly not be touched.")] IReadOnlyList<string> OutOfScope,
    [property: Description("Machine-verifiable given/when/then criteria.")] IReadOnlyList<string> AcceptanceCriteria,
    [property: Description("Constraints and known traps, not code.")] IReadOnlyList<string> ImplementationNotes,
    [property: Description("Requirement ids this item satisfies.")] IReadOnlyList<string> RequirementIds,
    [property: Description("Keys of items that must land first.")] IReadOnlyList<string> DependsOn,
    [property: Description("One-based parallelism wave.")] int Wave,
    [property: Description("core | agents | tools | web | api | cli | infra | docs | tests")] string Area,
    [property: Description("small | medium | large")] WorkItemSize Size,
    [property: Description("True when a human must decide before an agent may start.")] bool NeedsHuman)
{
    /// <summary>Gets the paths this item touches, for conflict detection.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Paths => [.. Files.Select(f => f.Path)];

    /// <summary>Builds the git branch name the orchestrator will create for this item.</summary>
    /// <param name="issueNumber">The GitHub issue number assigned to this item.</param>
    /// <returns>A branch name of the form <c>agent/issue-42-add-wave-planner</c>.</returns>
    public string BranchName(int issueNumber) => $"agent/issue-{issueNumber}-{Slug(Title)}";

    /// <summary>Reduces a title to a short, filesystem- and git-safe slug.</summary>
    /// <param name="value">The text to slugify.</param>
    /// <returns>A lowercase, hyphen-separated slug of at most 40 characters.</returns>
    public static string Slug(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        bool lastWasHyphen = true;

        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                buffer[length++] = '-';
                lastWasHyphen = true;
            }
        }

        // Worktree paths on Windows have a hard ceiling that core.longpaths does not lift,
        // so branch and directory names stay deliberately short.
        string slug = new string(buffer[..length]).Trim('-');

        return slug.Length <= 40 ? slug : slug[..40].TrimEnd('-');
    }
}

/// <summary>
/// The approved plan: what will be built, in what order, and what may run in parallel.
/// </summary>
/// <param name="Title">Short name for the delivery.</param>
/// <param name="Overview">A paragraph explaining the approach and the main trade-off.</param>
/// <param name="Architecture">Design decisions a reviewer needs to understand the shape of the change.</param>
/// <param name="Items">Every work item.</param>
/// <param name="DefaultDecisions">Choices made without asking, so the human can contest them.</param>
/// <param name="Risks">What could go wrong and how the plan mitigates it.</param>
public sealed record DeliveryPlan(
    [property: Description("Short name for the delivery.")] string Title,
    [property: Description("One paragraph on the approach and the main trade-off.")] string Overview,
    [property: Description("Design decisions a reviewer needs.")] IReadOnlyList<string> Architecture,
    [property: Description("Every work item, ordered by wave.")] IReadOnlyList<WorkItem> Items,
    [property: Description("Choices made without asking the human.")] IReadOnlyList<string> DefaultDecisions,
    [property: Description("Risks and mitigations.")] IReadOnlyList<string> Risks)
{
    /// <summary>Gets the work items grouped into ordered parallelism waves.</summary>
    /// <returns>Waves ordered ascending; each inner list may run concurrently.</returns>
    public IReadOnlyList<IReadOnlyList<WorkItem>> Waves() =>
        [.. Items.GroupBy(i => i.Wave)
                 .OrderBy(g => g.Key)
                 .Select(IReadOnlyList<WorkItem> (g) => [.. g])];
}
