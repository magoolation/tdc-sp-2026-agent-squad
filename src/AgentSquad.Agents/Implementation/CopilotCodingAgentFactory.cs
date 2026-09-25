using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Agents.Implementation;

/// <summary>
/// Creates one Copilot coding agent per worktree, each with its own isolated configuration home.
/// </summary>
public sealed class CopilotCodingAgentFactory(
    IOptions<CopilotOptions> copilotOptions,
    IOptions<SquadOptions> squadOptions,
    ILoggerFactory loggerFactory) : ICodingAgentFactory
{
    private readonly CopilotOptions _copilot = copilotOptions.Value;
    private readonly SquadOptions _squad = squadOptions.Value;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    /// <inheritdoc />
    public async Task<ICodingAgent> CreateAsync(AgentWorktree worktree, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        // A private COPILOT_HOME per agent. Sharing one would mean sharing the session
        // store, the persisted permission approvals and the log directory across every
        // concurrent agent, which makes their state interfere and their logs unreadable.
        string copilotHome = Path.Combine(_squad.WorkRoot, "copilot-home", $"i{worktree.IssueNumber}");

        return await CopilotCodingAgent.StartAsync(
            worktree,
            _copilot,
            copilotHome,
            _squad.AgentTimeout,
            _loggerFactory,
            cancellationToken);
    }
}
