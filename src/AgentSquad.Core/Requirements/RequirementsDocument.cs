using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgentSquad.Core.Requirements;

/// <summary>How a requirement constrains the delivery.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequirementKind>))]
public enum RequirementKind
{
    /// <summary>Observable behaviour the system must exhibit.</summary>
    Functional = 0,

    /// <summary>A quality attribute: performance, availability, security, accessibility.</summary>
    NonFunctional = 1,

    /// <summary>A technology or process decision imposed from outside.</summary>
    Constraint = 2,

    /// <summary>Something the factory decided on its own because nobody said otherwise.</summary>
    Assumption = 3,
}

/// <summary>How firmly a requirement is grounded in what the human actually said.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequirementConfidence>))]
public enum RequirementConfidence
{
    /// <summary>Stated explicitly by a human.</summary>
    Stated = 0,

    /// <summary>Strongly implied by what was said or by the existing code.</summary>
    Implied = 1,

    /// <summary>Assumed by the factory. Must surface as an open question or a declared default.</summary>
    Assumed = 2,
}

/// <summary>
/// A single requirement, traceable back to where it came from.
/// </summary>
/// <param name="Id">Stable identifier such as <c>RF-01</c>, used by work items and issues.</param>
/// <param name="Kind">How this requirement constrains the delivery.</param>
/// <param name="Statement">Observable behaviour, not implementation.</param>
/// <param name="Rationale">Why it matters to the user.</param>
/// <param name="Source">Where it came from: <c>transcript:14:32</c>, <c>user-request</c>, <c>inferred-from-code:src/Foo.cs</c>.</param>
/// <param name="Confidence">How firmly it is grounded.</param>
/// <param name="AcceptanceCriteria">Given/when/then statements a machine can verify.</param>
public sealed record Requirement(
    [property: Description("Stable identifier, e.g. RF-01.")] string Id,
    [property: Description("functional | nonfunctional | constraint | assumption")] RequirementKind Kind,
    [property: Description("The requirement, phrased as observable behaviour.")] string Statement,
    [property: Description("Why this matters to the user.")] string Rationale,
    [property: Description("Traceable origin, e.g. transcript:14:32 or inferred-from-code:src/Foo.cs.")] string Source,
    [property: Description("stated | implied | assumed")] RequirementConfidence Confidence,
    [property: Description("Machine-verifiable given/when/then criteria.")] IReadOnlyList<string> AcceptanceCriteria);

/// <summary>
/// One concrete option offered to the human when answering an open question.
/// </summary>
/// <param name="Label">Short label shown in the console or the web UI.</param>
/// <param name="Impact">What choosing this option does to the plan, in concrete terms.</param>
public sealed record QuestionOption(
    [property: Description("Short label for the option.")] string Label,
    [property: Description("Concrete effect on the plan: issues added, time, architecture.")] string Impact);

/// <summary>
/// An ambiguity the factory cannot resolve on its own.
/// </summary>
/// <remarks>
/// Every question carries a <see cref="DefaultIfUnanswered"/> so the factory can always
/// proceed without a human. Only genuinely architecture-changing questions may be
/// <see cref="Blocking"/>, and at most two per round.
/// </remarks>
/// <param name="Id">Stable identifier such as <c>Q-01</c>.</param>
/// <param name="Question">The question, phrased so it can be answered by picking an option.</param>
/// <param name="Why">What decision in the plan depends on the answer.</param>
/// <param name="Options">Concrete alternatives. Never an open-ended prompt.</param>
/// <param name="DefaultIfUnanswered">The option label used when nobody answers.</param>
/// <param name="Blocking">Whether the run must stop until this is answered.</param>
public sealed record OpenQuestion(
    [property: Description("Stable identifier, e.g. Q-01.")] string Id,
    [property: Description("The question to ask the human.")] string Question,
    [property: Description("Which planning decision depends on the answer.")] string Why,
    [property: Description("Two to four concrete options.")] IReadOnlyList<QuestionOption> Options,
    [property: Description("Label of the option to use if nobody answers.")] string DefaultIfUnanswered,
    [property: Description("True only for genuinely architecture-changing questions.")] bool Blocking);

/// <summary>
/// The human's answer to an <see cref="OpenQuestion"/>.
/// </summary>
/// <param name="QuestionId">Identifier of the question being answered.</param>
/// <param name="Answer">The chosen option label, or free text the human typed instead.</param>
/// <param name="AnsweredBy">Who answered, for the audit trail (AI-006).</param>
/// <param name="AnsweredAt">When the answer was given.</param>
public sealed record QuestionAnswer(
    string QuestionId,
    string Answer,
    string AnsweredBy,
    DateTimeOffset AnsweredAt);

/// <summary>
/// The full output of the requirements phase.
/// </summary>
/// <param name="Title">A short name for the delivery.</param>
/// <param name="Summary">One paragraph a stakeholder can read.</param>
/// <param name="Requirements">Every requirement, including assumptions.</param>
/// <param name="OpenQuestions">Ambiguities that still need a human.</param>
/// <param name="OutOfScope">What the factory explicitly decided not to build.</param>
public sealed record RequirementsDocument(
    [property: Description("Short name for the delivery.")] string Title,
    [property: Description("One paragraph summary for a stakeholder.")] string Summary,
    [property: Description("All requirements, including assumptions.")] IReadOnlyList<Requirement> Requirements,
    [property: Description("Ambiguities that still need a human decision.")] IReadOnlyList<OpenQuestion> OpenQuestions,
    [property: Description("What is explicitly not being built.")] IReadOnlyList<string> OutOfScope)
{
    /// <summary>Gets the questions that must be answered before planning can continue.</summary>
    [JsonIgnore]
    public IReadOnlyList<OpenQuestion> BlockingQuestions =>
        [.. OpenQuestions.Where(q => q.Blocking)];

    /// <summary>Gets the requirements the factory assumed rather than heard.</summary>
    [JsonIgnore]
    public IReadOnlyList<Requirement> Assumptions =>
        [.. Requirements.Where(r => r.Confidence == RequirementConfidence.Assumed)];
}
