using AgentSquad.Core.Planning;

namespace AgentSquad.Core.Abstractions;

/// <summary>
/// An issue that exists in the task tracker.
/// </summary>
/// <param name="Number">Tracker-assigned number.</param>
/// <param name="Title">Issue title.</param>
/// <param name="Url">Web URL for humans.</param>
/// <param name="WorkItemKey">The plan key this issue was created from.</param>
/// <param name="Labels">Labels applied.</param>
public sealed record TrackedIssue(
    int Number,
    string Title,
    string Url,
    string WorkItemKey,
    IReadOnlyList<string> Labels);

/// <summary>
/// A pull request the factory opened.
/// </summary>
/// <param name="Number">Tracker-assigned number.</param>
/// <param name="Url">Web URL for humans.</param>
/// <param name="Branch">Head branch.</param>
/// <param name="IssueNumber">The issue this closes.</param>
/// <param name="IsDraft">Whether it was opened as a draft.</param>
public sealed record PullRequestRef(
    int Number,
    string Url,
    string Branch,
    int IssueNumber,
    bool IsDraft);

/// <summary>
/// The task repository the factory publishes work to.
/// </summary>
/// <remarks>
/// Abstracted so the domain never depends on the GitHub CLI directly (ENG-002), and so
/// the whole pipeline is testable without touching a real repository.
/// </remarks>
public interface IIssueTracker
{
    /// <summary>
    /// Ensures the labels the factory depends on exist, creating any that are missing.
    /// </summary>
    /// <param name="labels">Label names, each with a colour and description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The names of the labels that were created.</returns>
    Task<IReadOnlyList<string>> EnsureLabelsAsync(
        IReadOnlyList<LabelDefinition> labels,
        CancellationToken cancellationToken);

    /// <summary>Ensures a milestone exists and returns its number.</summary>
    /// <param name="title">Milestone title.</param>
    /// <param name="description">Milestone description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The milestone number.</returns>
    Task<int> EnsureMilestoneAsync(string title, string description, CancellationToken cancellationToken);

    /// <summary>
    /// Creates one issue per work item.
    /// </summary>
    /// <param name="items">The work items to publish.</param>
    /// <param name="milestoneTitle">
    /// Milestone to attach, by <b>title</b>, or <see langword="null"/> for none. The GitHub
    /// CLI matches milestones by name; passing the number makes it look for a milestone
    /// literally called "1".
    /// </param>
    /// <param name="bodyRenderer">Renders the issue body for a work item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created issues, in the same order as <paramref name="items"/>.</returns>
    Task<IReadOnlyList<TrackedIssue>> CreateIssuesAsync(
        IReadOnlyList<WorkItem> items,
        string? milestoneTitle,
        Func<WorkItem, string> bodyRenderer,
        CancellationToken cancellationToken);

    /// <summary>Adds a comment to an issue.</summary>
    /// <param name="issueNumber">The issue.</param>
    /// <param name="body">Markdown body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the comment is posted.</returns>
    Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken);

    /// <summary>Opens a pull request.</summary>
    /// <param name="request">What to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created pull request.</returns>
    Task<PullRequestRef> CreatePullRequestAsync(PullRequestRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A label the factory needs.
/// </summary>
/// <param name="Name">Label name.</param>
/// <param name="Color">Six-digit hex colour without the leading hash.</param>
/// <param name="Description">What the label means.</param>
public sealed record LabelDefinition(string Name, string Color, string Description);

/// <summary>
/// Everything needed to open a pull request.
/// </summary>
/// <param name="Title">Pull-request title.</param>
/// <param name="Body">Markdown body.</param>
/// <param name="HeadBranch">Branch containing the change.</param>
/// <param name="BaseBranch">Branch to merge into.</param>
/// <param name="IssueNumber">Issue the pull request closes.</param>
/// <param name="Labels">Labels to apply, including <c>agent-generated</c> (PRC-004).</param>
/// <param name="Draft">Whether to open as a draft.</param>
public sealed record PullRequestRequest(
    string Title,
    string Body,
    string HeadBranch,
    string BaseBranch,
    int IssueNumber,
    IReadOnlyList<string> Labels,
    bool Draft);
