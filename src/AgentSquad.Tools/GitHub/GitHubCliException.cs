using AgentSquad.Core.Abstractions;

namespace AgentSquad.Tools.GitHub;

/// <summary>
/// Thrown when a <c>gh</c> command fails.
/// </summary>
public sealed class GitHubCliException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="GitHubCliException"/> class.</summary>
    public GitHubCliException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GitHubCliException"/> class.</summary>
    /// <param name="message">The message.</param>
    public GitHubCliException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GitHubCliException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public GitHubCliException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GitHubCliException"/> class.</summary>
    /// <param name="command">The gh command that failed.</param>
    /// <param name="result">The captured process result.</param>
    public GitHubCliException(string command, ProcessResult result)
        : base(BuildMessage(command, result))
    {
        Command = command;
        ExitCode = result?.ExitCode ?? -1;
    }

    /// <summary>Gets the gh command that failed.</summary>
    public string? Command { get; }

    /// <summary>Gets the process exit code.</summary>
    public int ExitCode { get; }

    private static string BuildMessage(string command, ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return result.TimedOut
            ? $"'gh {command}' exceeded its time budget."
            : $"'gh {command}' failed with exit code {result.ExitCode}: {detail.Trim()}";
    }
}
