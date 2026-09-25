using System.Text.Json.Serialization;
using AgentSquad.Core.Abstractions;

namespace AgentSquad.Tools.GitHub;

/// <summary>
/// One label as <c>gh issue list --json labels</c> returns it.
/// </summary>
/// <param name="Name">Label name.</param>
/// <param name="Color">Six-digit hex colour, without a leading hash.</param>
/// <param name="Description">What the label means.</param>
public sealed record IssueLabel(string Name, string? Color, string? Description);

/// <summary>
/// An issue as read back from <c>gh issue list --json</c>.
/// </summary>
/// <remarks>
/// The shapes in that payload are inconsistent — <c>labels</c> is a flat array while
/// <c>subIssues</c> is a connection with a <c>nodes</c> wrapper — so only the fields the
/// orchestrator actually needs are modelled here.
/// </remarks>
/// <param name="Number">Issue number.</param>
/// <param name="Title">Issue title.</param>
/// <param name="Url">Web URL.</param>
/// <param name="State"><c>OPEN</c> or <c>CLOSED</c>.</param>
/// <param name="Body">Markdown body.</param>
/// <param name="Labels">Labels applied.</param>
public sealed record IssueSnapshot(
    int Number,
    string Title,
    string Url,
    string State,
    string? Body,
    IReadOnlyList<IssueLabel> Labels);

/// <summary>
/// Source-generated JSON contracts for the GitHub CLI payloads (ENG-029).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(IssueSnapshot))]
[JsonSerializable(typeof(IssueSnapshot[]))]
[JsonSerializable(typeof(IssueLabel))]
[JsonSerializable(typeof(TrackedIssue))]
[JsonSerializable(typeof(PullRequestRef))]
public sealed partial class GitHubJsonContext : JsonSerializerContext;
