using System.Runtime.CompilerServices;
using Modulus.Messaging;

namespace Modulus.Messaging.Transports.InMemory;

/// <summary>
/// The messages waiting on one endpoint. A higher <c>mod-priority</c> is taken first; equal priorities keep their
/// enqueue order (FIFO), so a consumer that sets no priority sees messages in the order they were sent (FR-13).
/// </summary>
internal sealed class EndpointQueue
{
    private readonly PriorityQueue<MessageEnvelope, (long Priority, long Order)> _items = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();
    private long _order;
    private bool _completed;

    /// <summary>Adds a message. Never blocks.</summary>
    public void Enqueue(MessageEnvelope envelope)
    {
        var priority = PriorityOf(envelope);
        lock (_gate)
        {
            // A negated priority makes the highest priority the smallest key; the order breaks ties FIFO.
            _items.Enqueue(envelope, (-priority, _order++));
        }

        _signal.Release();
    }

    /// <summary>Stops the reader once the queue is empty.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
        }

        _signal.Release();
    }

    /// <summary>Yields messages highest priority first until the queue is complete or the token is cancelled.</summary>
    public async IAsyncEnumerable<MessageEnvelope> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            MessageEnvelope? next = null;
            bool done;
            lock (_gate)
            {
                if (_items.TryDequeue(out var envelope, out _))
                {
                    next = envelope;
                }

                done = _completed && _items.Count == 0;
            }

            if (next is not null)
            {
                yield return next;
            }
            else if (done)
            {
                yield break;
            }
        }
    }

    private static long PriorityOf(MessageEnvelope envelope) =>
        envelope.Headers.TryGetValue(MessagingHeaders.Priority, out var text) && long.TryParse(text, out var priority) ? priority : 0;
}
