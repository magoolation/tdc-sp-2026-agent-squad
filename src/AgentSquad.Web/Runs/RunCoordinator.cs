using System.Collections.Concurrent;
using AgentSquad.Agents.Orchestration;
using AgentSquad.Core.Runs;

namespace AgentSquad.Web.Runs;

/// <summary>What a run is doing right now, from the dashboard's point of view.</summary>
/// <param name="RunId">The run identifier, once the orchestrator has assigned one.</param>
/// <param name="Request">What was asked for.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="IsRunning">Whether the run is still in progress.</param>
/// <param name="Result">The result, once the run has finished.</param>
public sealed record RunHandle(
    RunId? RunId,
    string Request,
    DateTimeOffset StartedAt,
    bool IsRunning,
    SquadRunResult? Result);

/// <summary>
/// Starts runs from the web UI and keeps track of them.
/// </summary>
/// <remarks>
/// <para>
/// A run outlives the HTTP request and the Blazor circuit that started it, so it is owned
/// here rather than by a component. Closing the browser tab must not abort a wave of coding
/// agents halfway through.
/// </para>
/// <para>
/// One run at a time, deliberately. Two concurrent runs would fight over the same local
/// clone and the same worktree directory, and the failure would look like a mysterious git
/// error rather than the contention it actually is.
/// </para>
/// </remarks>
public sealed partial class RunCoordinator(
    SquadOrchestrator orchestrator,
    TimeProvider timeProvider,
    ILogger<RunCoordinator> logger) : IAsyncDisposable
{
    private readonly SquadOrchestrator _orchestrator = orchestrator;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<RunCoordinator> _logger = logger;

    private readonly ConcurrentDictionary<string, RunHandle> _runs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private CancellationTokenSource? _current;
    private Task? _currentTask;
    private string _pendingKey = string.Empty;

    /// <summary>Raised whenever a run starts, finishes or fails, so the UI can refresh.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether a run is in progress.</summary>
    public bool IsBusy => _oneAtATime.CurrentCount == 0;

    /// <summary>Gets every run this process knows about, newest first.</summary>
    /// <returns>The run handles.</returns>
    public IReadOnlyList<RunHandle> Runs() =>
        [.. _runs.Values.OrderByDescending(r => r.StartedAt)];

    /// <summary>Gets one run by identifier.</summary>
    /// <param name="runId">The identifier.</param>
    /// <returns>The handle, or <see langword="null"/> when unknown.</returns>
    public RunHandle? Find(string runId) =>
        _runs.TryGetValue(runId, out RunHandle? handle) ? handle : null;

    /// <summary>
    /// Starts a run in the background.
    /// </summary>
    /// <param name="request">What to build.</param>
    /// <returns><see langword="true"/> if the run started; <see langword="false"/> if one is already in progress.</returns>
    public bool TryStart(SquadRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_oneAtATime.Wait(0))
        {
            return false;
        }

        _current = new CancellationTokenSource();
        _pendingKey = $"pending-{Guid.NewGuid():N}";

        _runs[_pendingKey] = new RunHandle(null, Describe(request), _timeProvider.GetUtcNow(), IsRunning: true, Result: null);
        Changed?.Invoke();

        _currentTask = Task.Run(() => ExecuteAsync(request, _pendingKey, _current.Token), CancellationToken.None);

        return true;
    }

    /// <summary>Requests cancellation of the run in progress.</summary>
    public void Cancel() => _current?.Cancel();

    private async Task ExecuteAsync(SquadRunRequest request, string pendingKey, CancellationToken cancellationToken)
    {
        try
        {
            SquadRunResult result = await _orchestrator.RunAsync(request, cancellationToken);

            _runs.TryRemove(pendingKey, out RunHandle? pending);

            _runs[result.RunId.Value] = new RunHandle(
                result.RunId,
                pending?.Request ?? Describe(request),
                pending?.StartedAt ?? _timeProvider.GetUtcNow(),
                IsRunning: false,
                result);

            LogRunFinished(result.RunId.Value, result.Outcome);
        }
#pragma warning disable CA1031 // A background run must never crash the web host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRunFailed(ex);

            if (_runs.TryGetValue(pendingKey, out RunHandle? pending))
            {
                _runs[pendingKey] = pending with { IsRunning = false };
            }
        }
        finally
        {
            _oneAtATime.Release();
            Changed?.Invoke();
        }
    }

    private static string Describe(SquadRunRequest request) =>
        !string.IsNullOrWhiteSpace(request.Request)
            ? request.Request
            : $"Transcrição: {Path.GetFileName(request.TranscriptPath)}";

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_current is not null)
        {
            await _current.CancelAsync();
        }

        if (_currentTask is not null)
        {
            try
            {
                await _currentTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                LogShutdownTimeout();
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _current?.Dispose();
        _oneAtATime.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} finished as {Outcome}")]
    private partial void LogRunFinished(string runId, RunOutcome outcome);

    [LoggerMessage(Level = LogLevel.Error, Message = "A background run failed")]
    private partial void LogRunFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A run was still in progress when the host shut down")]
    private partial void LogShutdownTimeout();
}

