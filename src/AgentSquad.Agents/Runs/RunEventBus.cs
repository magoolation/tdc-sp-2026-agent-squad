using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Events;
using AgentSquad.Core.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentSquad.Agents.Runs;

/// <summary>
/// The single event stream every observer of a run shares.
/// </summary>
/// <remarks>
/// <para>
/// The console renderer, the Blazor dashboard and the on-disk journal all read the same
/// sequence, so the terminal and the web UI can never disagree about what happened.
/// </para>
/// <para>
/// Late subscribers are replayed the events they missed before following live. That is what
/// makes the dashboard usable: opening it two minutes into a run shows the whole run, not
/// just the tail.
/// </para>
/// </remarks>
public sealed partial class RunEventBus : IRunEventStream, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JournalJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly ConcurrentDictionary<string, RunChannel> _runs = new(StringComparer.Ordinal);
    private readonly SquadOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RunEventBus> _logger;

    /// <summary>Initializes a new instance of the <see cref="RunEventBus"/> class.</summary>
    /// <param name="options">Squad options.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="logger">Logger.</param>
    public RunEventBus(IOptions<SquadOptions> options, TimeProvider timeProvider, ILogger<RunEventBus> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Creates a publisher scoped to one run.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <returns>A publisher that stamps every event with this run and a monotonic sequence.</returns>
    public IRunEventPublisher PublisherFor(RunId runId)
    {
        RunChannel channel = _runs.GetOrAdd(runId.Value, id => new RunChannel(id, JournalPath(id)));

        return new ScopedPublisher(this, runId, channel);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> SubscribeAsync(
        RunId runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RunChannel channel = _runs.GetOrAdd(runId.Value, id => new RunChannel(id, JournalPath(id)));

        var subscriber = Channel.CreateUnbounded<RunEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        // Replay under the lock so no event can slip between the snapshot and the
        // subscription, which would otherwise show up as a silent gap in the dashboard.
        RunEvent[] backlog = channel.Subscribe(subscriber.Writer);

        try
        {
            foreach (RunEvent replayed in backlog)
            {
                yield return replayed;
            }

            await foreach (RunEvent live in subscriber.Reader.ReadAllAsync(cancellationToken))
            {
                yield return live;
            }
        }
        finally
        {
            channel.Unsubscribe(subscriber.Writer);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RunId> KnownRuns() =>
        [.. _runs.Keys.OrderByDescending(k => k, StringComparer.Ordinal).Select(RunId.Parse)];

    /// <summary>Signals that a run has ended, completing every subscriber's stream.</summary>
    /// <param name="runId">The run.</param>
    public void Complete(RunId runId)
    {
        if (_runs.TryGetValue(runId.Value, out RunChannel? channel))
        {
            channel.Complete();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (RunChannel channel in _runs.Values)
        {
            channel.Complete();
            await channel.DisposeAsync();
        }

        _runs.Clear();
    }

    private string JournalPath(string runId)
    {
        string directory = Path.Combine(_options.RunsDirectory, runId);
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "events.jsonl");
    }

    private void Publish(RunChannel channel, RunEvent runEvent)
    {
        channel.Publish(runEvent, JournalJson);

        if (runEvent.Level >= RunEventLevel.Warning)
        {
            LogNotableEvent(runEvent.Level, runEvent.Source, runEvent.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[{Level}] {Source}: {Message}")]
    private partial void LogNotableEvent(RunEventLevel level, string source, string message);

    private sealed class ScopedPublisher(RunEventBus bus, RunId runId, RunChannel channel) : IRunEventPublisher
    {
        public ValueTask PublishAsync(
            RunPhase phase,
            RunEventLevel level,
            string source,
            string message,
            int? issueNumber = null,
            IReadOnlyDictionary<string, string>? data = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runEvent = new RunEvent(
                runId.Value,
                channel.NextSequence(),
                bus._timeProvider.GetUtcNow(),
                phase,
                level,
                source,
                message,
                issueNumber,
                data);

            bus.Publish(channel, runEvent);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// The fan-out point for one run: a replay buffer, the live subscribers, and the journal.
    /// </summary>
    private sealed class RunChannel(string runId, string journalPath) : IAsyncDisposable
    {
        private readonly List<RunEvent> _history = [];
        private readonly List<ChannelWriter<RunEvent>> _subscribers = [];
        private readonly Lock _guard = new();
        private readonly StreamWriter _journal = new(journalPath, append: true) { AutoFlush = true };
        private long _sequence;
        private bool _completed;

        public string RunId { get; } = runId;

        public long NextSequence() => Interlocked.Increment(ref _sequence);

        public RunEvent[] Subscribe(ChannelWriter<RunEvent> writer)
        {
            lock (_guard)
            {
                if (_completed)
                {
                    writer.TryComplete();
                    return [.. _history];
                }

                _subscribers.Add(writer);
                return [.. _history];
            }
        }

        public void Unsubscribe(ChannelWriter<RunEvent> writer)
        {
            lock (_guard)
            {
                _subscribers.Remove(writer);
            }
        }

        public void Publish(RunEvent runEvent, JsonSerializerOptions options)
        {
            lock (_guard)
            {
                _history.Add(runEvent);

                foreach (ChannelWriter<RunEvent> subscriber in _subscribers)
                {
                    // Unbounded channels, so this only fails on a completed writer.
                    subscriber.TryWrite(runEvent);
                }

                // The journal is the audit trail AI-006 requires; it is written
                // synchronously so a crashed run still leaves a usable record.
                _journal.WriteLine(JsonSerializer.Serialize(runEvent, options));
            }
        }

        public void Complete()
        {
            lock (_guard)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;

                foreach (ChannelWriter<RunEvent> subscriber in _subscribers)
                {
                    subscriber.TryComplete();
                }

                _subscribers.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _journal.DisposeAsync();
        }
    }
}
