using System.Diagnostics;
using System.Text;
using AgentSquad.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentSquad.Tools.Processes;

/// <summary>
/// Runs external processes with argument-list safety, a hard timeout and full output capture.
/// </summary>
/// <remarks>
/// <para>
/// Arguments are always passed through <see cref="ProcessStartInfo.ArgumentList"/>, never
/// as a concatenated command line (SEC-004). That is not a style preference: a branch name
/// or an issue title flows from a language model into these calls, and a joined command
/// line would make that a command-injection sink.
/// </para>
/// <para>
/// Every invocation has a timeout (ENG-046). A coding agent that hangs must not hang the run.
/// </para>
/// </remarks>
public sealed partial class ProcessRunner(ILogger<ProcessRunner> logger, TimeProvider timeProvider) : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        // On Windows several tools ship as .cmd shims that Process.Start cannot launch
        // directly; the resolver turns those into a cmd.exe invocation without ever
        // building a concatenated command line.
        LaunchPlan plan = ExecutableResolver.Resolve(fileName);

        var startInfo = new ProcessStartInfo(plan.FileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string prefix in plan.PrefixArguments)
        {
            startInfo.ArgumentList.Add(prefix);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        long startTimestamp = _timeProvider.GetTimestamp();
        LogStarting(fileName, arguments.Count, workingDirectory);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var stdoutDone = new SemaphoreSlim(0, 1);
        using var stderrDone = new SemaphoreSlim(0, 1);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.Release();
            }
            else
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.Release();
            }
            else
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start the process '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Two sources rather than one so the caller's cancellation and the timeout stay
        // distinguishable: a timeout is a reportable outcome, a cancellation propagates.
        using var timeoutSource = new CancellationTokenSource(timeout, _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        bool timedOut = false;

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);

            // Drain the asynchronous readers so no output is lost.
            await stdoutDone.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await stderrDone.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            KillTree(process, fileName);
        }
        catch (OperationCanceledException)
        {
            KillTree(process, fileName);
            throw;
        }

        TimeSpan duration = _timeProvider.GetElapsedTime(startTimestamp);
        int exitCode = timedOut ? -1 : process.ExitCode;

        var result = new ProcessResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString(),
            duration,
            timedOut);

        if (timedOut)
        {
            LogTimedOut(fileName, timeout.TotalSeconds);
        }
        else if (exitCode != 0)
        {
            LogFailed(fileName, exitCode, duration.TotalSeconds);
        }
        else
        {
            LogSucceeded(fileName, duration.TotalSeconds);
        }

        return result;
    }

    private void KillTree(Process process, string fileName)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill. Nothing to do.
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LogKillFailed(ex, fileName);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting {FileName} with {ArgumentCount} arguments in {WorkingDirectory}")]
    private partial void LogStarting(string fileName, int argumentCount, string workingDirectory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{FileName} exited successfully after {Seconds:F1}s")]
    private partial void LogSucceeded(string fileName, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{FileName} exited with code {ExitCode} after {Seconds:F1}s")]
    private partial void LogFailed(string fileName, int exitCode, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "{FileName} exceeded its {TimeoutSeconds:F0}s budget and was terminated")]
    private partial void LogTimedOut(string fileName, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not terminate {FileName}")]
    private partial void LogKillFailed(Exception exception, string fileName);
}
