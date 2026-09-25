using System.Globalization;
using System.Text.Json;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Tools.GitHub;

/// <summary>
/// Publishes work to GitHub Issues and Pull Requests through the <c>gh</c> CLI.
/// </summary>
/// <remarks>
/// <para>
/// The CLI rather than a REST client, for two reasons that matter to a live demo:
/// it reuses the credentials the presenter already has, and every call it makes is a
/// command an audience member can paste into their own terminal afterwards.
/// </para>
/// <para>
/// Issue creation is throttled. GitHub's secondary rate limiter punishes rapid content
/// creation long before the 5,000-requests-per-hour primary budget is relevant, and a
/// wave of parallel issue creation is exactly the shape it targets.
/// </para>
/// </remarks>
public sealed partial class GitHubCliIssueTracker(
    IProcessRunner processRunner,
    IOptions<GitHubOptions> gitHubOptions,
    IOptions<SquadOptions> squadOptions,
    ILogger<GitHubCliIssueTracker> logger) : IIssueTracker
{
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly GitHubOptions _gitHub = gitHubOptions.Value;
    private readonly SquadOptions _squad = squadOptions.Value;
    private readonly ILogger<GitHubCliIssueTracker> _logger = logger;

    /// <summary>The exit code <c>gh</c> uses to signal that authentication is required.</summary>
    /// <remarks>Distinct from a generic failure because it is not retryable: the token must be fixed.</remarks>
    public const int AuthenticationRequiredExitCode = 4;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> EnsureLabelsAsync(
        IReadOnlyList<LabelDefinition> labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var created = new List<string>(labels.Count);

        foreach (LabelDefinition label in labels)
        {
            // --force makes this an idempotent upsert: it creates the label, or updates
            // the colour and description if it already exists. That removes a
            // list-then-create round trip and makes reruns safe.
            ProcessResult result = await GhAsync(
                cancellationToken,
                "label", "create", label.Name,
                "--color", label.Color,
                "--description", label.Description,
                "--force",
                "--repo", _gitHub.Slug);

            if (result.Succeeded)
            {
                created.Add(label.Name);
            }
            else
            {
                LogLabelFailed(label.Name, result.StandardError.Trim());
            }
        }

        LogLabelsEnsured(created.Count, labels.Count);

        return created;
    }

    /// <inheritdoc />
    public async Task<int> EnsureMilestoneAsync(string title, string description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        // There is no `gh milestone` command, so this goes through the REST API.
        ProcessResult existing = await GhAsync(
            cancellationToken,
            "api", $"repos/{_gitHub.Slug}/milestones",
            "--method", "GET",
            "-f", "state=all",
            "--jq", $".[] | select(.title == \"{title}\") | .number");

        if (existing.Succeeded && !string.IsNullOrWhiteSpace(existing.StandardOutput))
        {
            string first = existing.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];

            if (int.TryParse(first, CultureInfo.InvariantCulture, out int existingNumber))
            {
                LogMilestoneReused(title, existingNumber);
                return existingNumber;
            }
        }

        ProcessResult created = await GhAsync(
            cancellationToken,
            "api", $"repos/{_gitHub.Slug}/milestones",
            "--method", "POST",
            "-f", $"title={title}",
            "-f", $"description={description}",
            "--jq", ".number");

        if (!created.Succeeded)
        {
            throw new GitHubCliException("milestone create", created);
        }

        int number = int.Parse(created.StandardOutput.Trim(), CultureInfo.InvariantCulture);
        LogMilestoneCreated(title, number);

        return number;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrackedIssue>> CreateIssuesAsync(
        IReadOnlyList<WorkItem> items,
        string? milestoneTitle,
        Func<WorkItem, string> bodyRenderer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(bodyRenderer);

        var results = new TrackedIssue?[items.Count];
        using var throttle = new SemaphoreSlim(_gitHub.IssueCreationConcurrency, _gitHub.IssueCreationConcurrency);

        await Parallel.ForAsync(
            0,
            items.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _gitHub.IssueCreationConcurrency,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                await throttle.WaitAsync(token);

                try
                {
                    results[index] = await CreateSingleIssueAsync(items[index], milestoneTitle, bodyRenderer, token);
                }
                finally
                {
                    throttle.Release();
                }
            });

        var issues = new List<TrackedIssue>(items.Count);

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is { } issue)
            {
                issues.Add(issue);
            }
            else
            {
                throw new GitHubCliException($"The issue for work item '{items[i].Key}' could not be created.");
            }
        }

        return issues;
    }

    /// <inheritdoc />
    public async Task CommentAsync(int issueNumber, string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        string bodyFile = await WriteTempBodyAsync(body, cancellationToken);

        try
        {
            ProcessResult result = await GhAsync(
                cancellationToken,
                "issue", "comment", issueNumber.ToString(CultureInfo.InvariantCulture),
                "--body-file", bodyFile,
                "--repo", _gitHub.Slug);

            if (!result.Succeeded)
            {
                throw new GitHubCliException("issue comment", result);
            }
        }
        finally
        {
            TryDelete(bodyFile);
        }
    }

    /// <inheritdoc />
    public async Task<PullRequestRef> CreatePullRequestAsync(
        PullRequestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string bodyFile = await WriteTempBodyAsync(request.Body, cancellationToken);

        try
        {
            var arguments = new List<string>
            {
                "pr", "create",
                "--title", request.Title,
                "--body-file", bodyFile,
                "--base", request.BaseBranch,

                // --head must be explicit. Without it gh tries to infer the branch and may
                // prompt about pushing or forking, which deadlocks a redirected process
                // that has no terminal attached.
                "--head", request.HeadBranch,
                "--repo", _gitHub.Slug,
            };

            if (request.Draft)
            {
                arguments.Add("--draft");
            }

            foreach (string label in request.Labels)
            {
                arguments.Add("--label");
                arguments.Add(label);
            }

            ProcessResult result = await GhAsync(cancellationToken, [.. arguments]);

            if (!result.Succeeded)
            {
                throw new GitHubCliException("pr create", result);
            }

            string url = ExtractUrl(result.StandardOutput);
            int number = ExtractTrailingNumber(url);

            LogPullRequestCreated(number, request.IssueNumber, url);

            return new PullRequestRef(number, url, request.HeadBranch, request.IssueNumber, request.Draft);
        }
        finally
        {
            TryDelete(bodyFile);
        }
    }

    /// <summary>
    /// Reads issues back as structured data.
    /// </summary>
    /// <param name="labels">Labels every returned issue must carry.</param>
    /// <param name="limit">Maximum issues to return. The CLI defaults to thirty, so this is always explicit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching issues.</returns>
    public async Task<IReadOnlyList<IssueSnapshot>> ListIssuesAsync(
        IReadOnlyList<string> labels,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var arguments = new List<string>
        {
            "issue", "list",
            "--state", "all",
            "--limit", limit.ToString(CultureInfo.InvariantCulture),

            // projectItems is deliberately absent: requesting it without the `project`
            // scope fails the entire query rather than omitting that one field.
            "--json", "number,title,url,labels,state,body",
            "--repo", _gitHub.Slug,
        };

        foreach (string label in labels)
        {
            arguments.Add("--label");
            arguments.Add(label);
        }

        ProcessResult result = await GhAsync(cancellationToken, [.. arguments]);

        if (!result.Succeeded)
        {
            throw new GitHubCliException("issue list", result);
        }

        return JsonSerializer.Deserialize(result.StandardOutput, GitHubJsonContext.Default.IssueSnapshotArray) ?? [];
    }

    private async Task<TrackedIssue> CreateSingleIssueAsync(
        WorkItem item,
        string? milestoneTitle,
        Func<WorkItem, string> bodyRenderer,
        CancellationToken cancellationToken)
    {
        string bodyFile = await WriteTempBodyAsync(bodyRenderer(item), cancellationToken);

        try
        {
            string[] labels = BuildLabels(item);

            var arguments = new List<string>
            {
                "issue", "create",
                "--title", item.Title,
                "--body-file", bodyFile,
                "--repo", _gitHub.Slug,
            };

            foreach (string label in labels)
            {
                arguments.Add("--label");
                arguments.Add(label);
            }

            if (!string.IsNullOrWhiteSpace(milestoneTitle))
            {
                // By title. `gh issue create --milestone` matches on name, so passing the
                // number fails with "could not add to milestone '1': '1' not found" — and
                // it fails per issue, after the planning phase has already been paid for.
                arguments.Add("--milestone");
                arguments.Add(milestoneTitle);
            }

            ProcessResult result = await GhAsync(cancellationToken, [.. arguments]);

            if (!result.Succeeded)
            {
                throw new GitHubCliException("issue create", result);
            }

            // `gh issue create` has no --json flag; it prints the issue URL.
            string url = ExtractUrl(result.StandardOutput);
            int number = ExtractTrailingNumber(url);

            LogIssueCreated(number, item.Key, url);

            return new TrackedIssue(number, item.Title, url, item.Key, labels);
        }
        finally
        {
            TryDelete(bodyFile);
        }
    }

    private static string[] BuildLabels(WorkItem item) =>
    [
        "agent-task",
        $"wave:{item.Wave.ToString(CultureInfo.InvariantCulture)}",
        $"area:{item.Area}",
        $"size:{item.Size.ToString().ToLowerInvariant()[0]}",
        .. item.NeedsHuman ? new[] { "needs-human" } : [],
    ];

    private async Task<string> WriteTempBodyAsync(string body, CancellationToken cancellationToken)
    {
        // A body file rather than --body: issue bodies routinely exceed the Windows
        // command-line length limit, and a file sidesteps every quoting question.
        string directory = Path.Combine(_squad.WorkRoot, "tmp");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, $"body-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path, body, cancellationToken);

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Likewise.
        }
    }

    private static string ExtractUrl(string output)
    {
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return lines[i];
            }
        }

        throw new GitHubCliException($"No URL was found in the gh output: {output.Trim()}");
    }

    private static int ExtractTrailingNumber(string url)
    {
        int slash = url.LastIndexOf('/');
        string tail = slash >= 0 ? url[(slash + 1)..] : url;

        return int.TryParse(tail, CultureInfo.InvariantCulture, out int number)
            ? number
            : throw new GitHubCliException($"'{url}' does not end in an issue or pull-request number.");
    }

    private async Task<ProcessResult> GhAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        ProcessResult result = await _processRunner.RunAsync(
            "gh",
            arguments,
            _squad.WorkRoot,
            _squad.ProcessTimeout,
            environment: null,
            cancellationToken);

        if (result.ExitCode == AuthenticationRequiredExitCode)
        {
            throw new GitHubCliException(
                "The gh CLI reports that authentication is required. Run 'gh auth login', " +
                "then 'gh auth refresh -h github.com -s workflow,project'.");
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Created} of {Total} labels ensured")]
    private partial void LogLabelsEnsured(int created, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Label {Label} could not be created: {Error}")]
    private partial void LogLabelFailed(string label, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Milestone '{Title}' already existed as #{Number}")]
    private partial void LogMilestoneReused(string title, int number);

    [LoggerMessage(Level = LogLevel.Information, Message = "Milestone '{Title}' created as #{Number}")]
    private partial void LogMilestoneCreated(string title, int number);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issue #{Number} created for {WorkItemKey}: {Url}")]
    private partial void LogIssueCreated(int number, string workItemKey, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pull request #{Number} opened for issue #{IssueNumber}: {Url}")]
    private partial void LogPullRequestCreated(int number, int issueNumber, string url);
}
