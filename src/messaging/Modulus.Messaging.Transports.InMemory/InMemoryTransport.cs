using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Modulus.Messaging.Transports.InMemory;

/// <summary>
/// Keeps each endpoint in a channel and delivers its messages one at a time on a background pump.
/// Stopping stops taking new messages and lets in-flight ones finish; after the stop timeout the in-flight
/// message is put back in its queue. Messages are lost if the process exits: use a durable transport in production.
/// </summary>
public sealed class InMemoryTransport : IMessageTransport, IScheduledMessageTransport
{
    /// <summary>NFR-18: in-flight messages are drained within 30 seconds by default.</summary>
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Channel<MessageEnvelope>> _queues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DeadLetter>> _deadLetters = new(StringComparer.Ordinal);
    private readonly List<Task> _pumps = [];
    private readonly object _gate = new();
    private readonly object _scheduleGate = new();
    private readonly Dictionary<Guid, List<ScheduledItem>> _scheduled = new();
    private readonly TimeSpan _stopTimeout;

    private Func<MessageEnvelope, CancellationToken, Task>? _receive;
    private CancellationTokenSource? _stopping;
    private CancellationTokenSource? _abort;

    public InMemoryTransport()
        : this(DefaultStopTimeout)
    {
    }

    public InMemoryTransport(TimeSpan stopTimeout)
    {
        if (stopTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stopTimeout), "Must not be negative.");
        }

        _stopTimeout = stopTimeout;
    }

    /// <summary>Messages that failed in a consumer, in the order they were parked. Endpoint <c>x</c> parks in <c>x_error</c>.</summary>
    public IReadOnlyList<DeadLetter> Errors(string endpoint) => Snapshot(endpoint + "_error");

    /// <summary>Messages no consumer on the endpoint handled. Endpoint <c>x</c> parks in <c>x_skipped</c>.</summary>
    public IReadOnlyList<DeadLetter> Skipped(string endpoint) => Snapshot(endpoint + "_skipped");

    public Task Start(
        IReadOnlyCollection<string> endpoints,
        Func<MessageEnvelope, CancellationToken, Task> receive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(receive);
        lock (_gate)
        {
            if (_receive is not null)
            {
                throw new InvalidOperationException("The in-memory transport is already started.");
            }

            _receive = receive;
            _stopping = new CancellationTokenSource();
            _abort = new CancellationTokenSource();

            // Queues that already exist (sent before Start) get a pump now; new ones get one when created.
            foreach (var (endpoint, queue) in _queues)
            {
                _pumps.Add(Pump(endpoint, queue));
            }

            foreach (var endpoint in endpoints)
            {
                QueueFor(endpoint);
            }
        }

        return Task.CompletedTask;
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        Task[] pumps;
        CancellationTokenSource? stopping;
        CancellationTokenSource? abort;
        lock (_gate)
        {
            stopping = _stopping;
            abort = _abort;
            _stopping = null;
            _abort = null;
            pumps = _pumps.ToArray();
            _pumps.Clear();
            _receive = null;
        }

        if (stopping is null || abort is null)
        {
            return;
        }

        // Stop taking new messages; in-flight ones keep running.
        await stopping.CancelAsync().ConfigureAwait(false);
        var drained = Task.WhenAll(pumps);
        var finished = await Task.WhenAny(drained, Task.Delay(_stopTimeout, cancellationToken)).ConfigureAwait(false);
        if (finished != drained)
        {
            // Stop timeout reached: abort the in-flight messages, which go back to their queues.
            await abort.CancelAsync().ConfigureAwait(false);
        }

        await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
        stopping.Dispose();
        abort.Dispose();
    }

    public async Task Send(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var queue = QueueFor(envelope.Endpoint);
        await queue.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Must not be negative.");
        }

        // Runs off the pump so the endpoint keeps moving while the message waits.
        return Task.Run(async () =>
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await Send(envelope, CancellationToken.None).ConfigureAwait(false);
        }, CancellationToken.None);
    }

    public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Park(envelope.Endpoint + "_error", new DeadLetter(envelope, exception));
        return Task.CompletedTask;
    }

    public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Park(envelope.Endpoint + "_skipped", new DeadLetter(envelope, null));
        return Task.CompletedTask;
    }

    public Task Schedule(Guid scheduleToken, MessageEnvelope envelope, DateTimeOffset deliverAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        var item = new ScheduledItem(envelope, new CancellationTokenSource());
        lock (_scheduleGate)
        {
            if (!_scheduled.TryGetValue(scheduleToken, out var items))
            {
                _scheduled[scheduleToken] = items = [];
            }

            items.Add(item);
        }

        var delay = deliverAt - DateTimeOffset.UtcNow;
        _ = FireAsync(scheduleToken, item, delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        return Task.CompletedTask;
    }

    public async Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken)
    {
        List<ScheduledItem>? items;
        lock (_scheduleGate)
        {
            if (!_scheduled.Remove(scheduleToken, out items))
            {
                return false;
            }
        }

        // The items are no longer in the table, so none of them can be handed to a consumer after this point.
        foreach (var item in items)
        {
            await item.Cancellation.CancelAsync().ConfigureAwait(false);
            item.Cancellation.Dispose();
        }

        return true;
    }

    /// <summary>Waits for the delivery time, then hands the envelope to its endpoint unless a cancel got there first.</summary>
    private async Task FireAsync(Guid scheduleToken, ScheduledItem item, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, item.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_scheduleGate)
        {
            // Claim the item under the lock: a cancel that removed it already won.
            if (!_scheduled.TryGetValue(scheduleToken, out var items) || !items.Remove(item))
            {
                return;
            }

            if (items.Count == 0)
            {
                _scheduled.Remove(scheduleToken);
            }
        }

        item.Cancellation.Dispose();
        await Send(item.Envelope, CancellationToken.None).ConfigureAwait(false);
    }

    private sealed class ScheduledItem(MessageEnvelope envelope, CancellationTokenSource cancellation)
    {
        public MessageEnvelope Envelope { get; } = envelope;

        public CancellationTokenSource Cancellation { get; } = cancellation;
    }

    private Channel<MessageEnvelope> QueueFor(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint name must not be empty.", nameof(endpoint));
        }

        return _queues.GetOrAdd(endpoint, name =>
        {
            var queue = Channel.CreateUnbounded<MessageEnvelope>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = false,
            });

            // Endpoints first seen after Start still get a pump, so unhandled messages are parked, not stuck.
            lock (_gate)
            {
                if (_receive is not null)
                {
                    _pumps.Add(Pump(name, queue));
                }
            }

            return queue;
        });
    }

    private Task Pump(string endpoint, Channel<MessageEnvelope> queue)
    {
        var receive = _receive!;
        var stopping = _stopping!.Token;
        var abort = _abort!.Token;
        return Task.Run(async () =>
        {
            try
            {
                await foreach (var envelope in queue.Reader.ReadAllAsync(stopping).ConfigureAwait(false))
                {
                    try
                    {
                        await receive(envelope, abort).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (abort.IsCancellationRequested)
                    {
                        // Aborted at the stop timeout: put it back so a restart delivers it.
                        queue.Writer.TryWrite(envelope);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // The bus handles consumer failures; anything reaching here is a transport bug.
                        // Park it so one bad message never stops the endpoint.
                        Park(endpoint + "_error", new DeadLetter(envelope, exception));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        }, CancellationToken.None);
    }

    private void Park(string queueName, DeadLetter letter) =>
        _deadLetters.GetOrAdd(queueName, _ => new ConcurrentQueue<DeadLetter>()).Enqueue(letter);

    private IReadOnlyList<DeadLetter> Snapshot(string queueName) =>
        _deadLetters.TryGetValue(queueName, out var queue) ? queue.ToArray() : [];
}
