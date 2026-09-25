using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgentSquad.Core.Review;

/// <summary>How much a review finding matters.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FindingSeverity>))]
public enum FindingSeverity
{
    /// <summary>Worth mentioning; does not block the pull request.</summary>
    Nit = 0,

    /// <summary>Should be addressed, but a human may reasonably merge without it.</summary>
    Suggestion = 1,

    /// <summary>The pull request must not be merged until this is fixed.</summary>
    Blocking = 2,
}

/// <summary>
/// One problem the reviewer agent found in a diff.
/// </summary>
/// <remarks>
/// Every blocking finding must cite a rule id from <c>docs/engineering-rules.md</c>.
/// A reviewer that cannot name the rule it is enforcing is expressing taste, not policy,
/// and taste must not block an automated pipeline.
/// </remarks>
/// <param name="Rule">Rule identifier such as <c>SEC-004</c> or <c>ENG-042</c>.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="File">Repository-relative path.</param>
/// <param name="Line">One-based line number in the new file, when known.</param>
/// <param name="Finding">What is wrong, in one or two sentences.</param>
/// <param name="Fix">The concrete change that resolves it.</param>
public sealed record ReviewFinding(
    [property: Description("Rule id from docs/engineering-rules.md, e.g. SEC-004.")] string Rule,
    [property: Description("nit | suggestion | blocking")] FindingSeverity Severity,
    [property: Description("Repository-relative file path.")] string File,
    [property: Description("One-based line number, or null.")] int? Line,
    [property: Description("What is wrong.")] string Finding,
    [property: Description("The concrete change that fixes it.")] string Fix);

/// <summary>
/// The reviewer agent's verdict on one work item's diff.
/// </summary>
/// <param name="Summary">One paragraph a human reviewer can read first.</param>
/// <param name="Findings">Every finding, most severe first.</param>
/// <param name="AcceptanceCriteriaMet">Whether the diff satisfies every acceptance criterion of the issue.</param>
/// <param name="UnmetCriteria">Acceptance criteria the reviewer could not find evidence for.</param>
public sealed record ReviewResult(
    [property: Description("One paragraph for the human reviewer.")] string Summary,
    [property: Description("Findings, most severe first.")] IReadOnlyList<ReviewFinding> Findings,
    [property: Description("Whether every acceptance criterion is satisfied.")] bool AcceptanceCriteriaMet,
    [property: Description("Criteria with no supporting evidence in the diff.")] IReadOnlyList<string> UnmetCriteria)
{
    /// <summary>Gets the findings that must be fixed before a human is asked to review.</summary>
    [JsonIgnore]
    public IReadOnlyList<ReviewFinding> Blocking =>
        [.. Findings.Where(f => f.Severity == FindingSeverity.Blocking)];

    /// <summary>Gets a value indicating whether the diff may go to a human reviewer as-is.</summary>
    [JsonIgnore]
    public bool IsClean => Blocking.Count == 0 && AcceptanceCriteriaMet;

    /// <summary>Renders the findings as a Markdown list for the pull-request body.</summary>
    /// <returns>A Markdown fragment, or a short note when there are no findings.</returns>
    public string ToMarkdown()
    {
        if (Findings.Count == 0)
        {
            return "_Nenhum apontamento da revisão automatizada._";
        }

        var builder = new System.Text.StringBuilder();

        foreach (ReviewFinding finding in Findings.OrderByDescending(f => f.Severity))
        {
            string icon = finding.Severity switch
            {
                FindingSeverity.Blocking => "🔴",
                FindingSeverity.Suggestion => "🟡",
                _ => "⚪",
            };

            builder.Append("- ").Append(icon).Append(" **").Append(finding.Rule).Append("** `")
                   .Append(finding.File);

            if (finding.Line is { } line)
            {
                builder.Append(':').Append(line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            builder.Append("` — ").Append(finding.Finding)
                   .Append(" _Correção:_ ").AppendLine(finding.Fix);
        }

        return builder.ToString();
    }
}
