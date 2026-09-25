using System.Collections.Concurrent;
using AgentSquad.Core.Configuration;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace AgentSquad.Agents.Implementation;

/// <summary>
/// Decides, per request, what a coding agent is allowed to do inside its worktree.
/// </summary>
/// <remarks>
/// <para>
/// This is the concrete implementation of AI-004 (excessive agency). The coding agent is
/// autonomous within a sandbox and powerless outside it: it may read, write and build
/// inside its own worktree, but it cannot push a branch, open a pull request, reach an
/// arbitrary host, or write outside the directory it was given.
/// </para>
/// <para>
/// Refusals are recorded rather than merely returned. A policy that silently blocks things
/// produces an agent that fails for reasons nobody can see, so every denial is surfaced in
/// the run journal and in the pull-request body.
/// </para>
/// </remarks>
public sealed partial class CopilotPermissionPolicy(
    CopilotOptions options,
    string worktreeRoot,
    string artifactsRoot,
    ILogger logger)
{
    private readonly CopilotOptions _options = options;
    private readonly string _worktreeRoot = Path.GetFullPath(worktreeRoot);
    private readonly string _artifactsRoot = Path.GetFullPath(artifactsRoot);
    private readonly ILogger _logger = logger;
    private readonly ConcurrentQueue<string> _denials = new();

    /// <summary>Gets every action the policy refused, for the audit trail.</summary>
    public IReadOnlyList<string> Denials => [.. _denials];

    /// <summary>
    /// Evaluates one permission request.
    /// </summary>
    /// <param name="request">What the agent wants to do.</param>
    /// <param name="invocation">
    /// Invocation context supplied by the runtime. Unused: every input this policy needs is
    /// on the request itself. The parameter is required by the SDK's callback signature.
    /// </param>
    /// <returns>The decision.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style", "IDE0060:Remove unused parameter",
        Justification = "The signature is fixed by SessionConfig.OnPermissionRequest.")]
    public Task<PermissionDecision> EvaluateAsync(PermissionRequest request, PermissionInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Managed policy can mark a request as requiring a real human. Auto-approving it
        // here would override an organizational control, so the request is declined and
        // left for another connected client to answer.
        if (request.ManagedApprovalRequired == true)
        {
            return Deny(request, "Managed policy requires a human decision.", reject: false);
        }

        return request switch
        {
            PermissionRequestShell shell => EvaluateShell(shell),
            PermissionRequestWrite write => EvaluateWrite(write),
            PermissionRequestRead read => EvaluateRead(read),
            PermissionRequestUrl url => EvaluateUrl(url),
            PermissionRequestMcp mcp => EvaluateMcp(mcp),
            _ => Approve(),
        };
    }

    private Task<PermissionDecision> EvaluateShell(PermissionRequestShell shell)
    {
        // The runtime decomposes a shell invocation into first-level command identifiers
        // ("git push", "dotnet build"), which is what makes a precise deny list possible:
        // `git commit` stays allowed while `git push` does not.
        IEnumerable<string> identifiers = shell.Commands?
            .Select(c => c.Identifier)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!) ?? [];

        foreach (string identifier in identifiers)
        {
            string? denied = _options.DeniedShellCommands.FirstOrDefault(
                rule => identifier.StartsWith(rule, StringComparison.OrdinalIgnoreCase));

            if (denied is not null)
            {
                return Deny(
                    shell,
                    $"'{denied}' is reserved for the orchestrator. " +
                    "Commit your work and report; branch publication and pull requests are not yours to perform.",
                    reject: true);
            }
        }

        // A shell redirection is a file write that bypasses the write-permission path check.
        if (shell.HasWriteFileRedirection == true)
        {
            return Deny(shell, "Shell output redirection is not allowed; use the file-editing tools.", reject: true);
        }

        // ResolvedPaths maps the paths the runtime detected in the command line to their
        // resolved absolute form; the resolved value is what the sandbox check needs.
        if (shell.ResolvedPaths is { Count: > 0 } resolved)
        {
            foreach (string path in resolved.Values)
            {
                if (!IsInsideSandbox(path))
                {
                    return Deny(shell, $"'{path}' is outside this agent's worktree.", reject: true);
                }
            }
        }

        return Approve();
    }

    private Task<PermissionDecision> EvaluateWrite(PermissionRequestWrite write)
    {
        string? path = write.ResolvedPath ?? write.FileName;

        if (string.IsNullOrWhiteSpace(path))
        {
            return Deny(write, "A write request arrived with no resolvable path.", reject: true);
        }

        return IsInsideSandbox(path)
            ? Approve()
            : Deny(write, $"Writes are confined to this agent's worktree; '{path}' is outside it.", reject: true);
    }

    private Task<PermissionDecision> EvaluateRead(PermissionRequestRead read)
    {
        string? path = read.ResolvedPath ?? read.Path;

        // Reads are treated more permissively than writes: an agent reading a NuGet package
        // or an SDK reference file is doing its job, and reads cannot damage the repository.
        // What reads can do is exfiltrate, which the URL policy below is what actually guards.
        return string.IsNullOrWhiteSpace(path) || !LooksSensitive(path)
            ? Approve()
            : Deny(read, $"'{path}' looks like a credential store and is not readable by an agent.", reject: true);
    }

    private Task<PermissionDecision> EvaluateUrl(PermissionRequestUrl url)
    {
        if (string.IsNullOrWhiteSpace(url.Url) || !Uri.TryCreate(url.Url, UriKind.Absolute, out Uri? parsed))
        {
            return Deny(url, "A fetch request arrived with an unparseable URL.", reject: true);
        }

        bool allowed = _options.AllowedHosts.Any(host =>
            parsed.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            parsed.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

        return allowed
            ? Approve()
            : Deny(url, $"'{parsed.Host}' is not on the allow-list of documentation hosts.", reject: true);
    }

    private Task<PermissionDecision> EvaluateMcp(PermissionRequestMcp mcp) =>
        mcp.ReadOnly == true
            ? Approve()
            : Deny(mcp, $"The MCP tool '{mcp.ServerName}/{mcp.ToolName}' mutates state and is not approved for agents.", reject: true);

    /// <summary>
    /// Decides whether a path lies inside the sandbox this agent may touch.
    /// </summary>
    /// <remarks>
    /// The path is normalized first and then compared with a trailing separator appended
    /// (SEC-005). Without the separator, <c>C:\squad\wt\i42-evil</c> would pass a naive
    /// prefix check against <c>C:\squad\wt\i42</c>.
    /// </remarks>
    /// <param name="path">The candidate path.</param>
    /// <returns><see langword="true"/> when the path is inside the worktree or its artifacts directory.</returns>
    private bool IsInsideSandbox(string path)
    {
        string full;

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return IsUnder(full, _worktreeRoot) || IsUnder(full, _artifactsRoot) || IsUnder(full, Path.GetTempPath());

        static bool IsUnder(string candidate, string root)
        {
            string normalized = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;

            return candidate.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool LooksSensitive(string path)
    {
        string name = Path.GetFileName(path);

        return name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            || name.Equals("id_rsa", StringComparison.OrdinalIgnoreCase)
            || name.Equals("secrets.json", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".ssh", StringComparison.OrdinalIgnoreCase)
            || path.Contains(".aws", StringComparison.OrdinalIgnoreCase)
            || path.Contains("hosts", StringComparison.OrdinalIgnoreCase) && path.Contains("System32", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<PermissionDecision> Approve() => Task.FromResult(PermissionDecision.ApproveOnce());

    private Task<PermissionDecision> Deny(PermissionRequest request, string reason, bool reject)
    {
        string record = $"{request.Kind}: {reason}";
        _denials.Enqueue(record);
        LogDenied(request.Kind ?? "unknown", reason);

        // Reject() feeds the reason back to the model, which lets the agent adapt instead of
        // retrying the same blocked action. NoResult() declines without telling it anything,
        // which is what a managed-policy request needs.
        return Task.FromResult(reject ? PermissionDecision.Reject(reason) : PermissionDecision.NoResult());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Permission denied for {Kind}: {Reason}")]
    private partial void LogDenied(string kind, string reason);
}
