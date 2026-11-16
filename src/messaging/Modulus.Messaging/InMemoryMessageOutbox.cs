namespace Modulus.Messaging;

/// <summary>
/// An outbox that holds a scope's outgoing messages in memory until the scope commits (FR-10). Commit sends each one
/// once; rollback, or disposing the scope without a commit, sends nothing (UC-01).
/// </summary>
/// <remarks>
/// Nothing here is durable: a process that exits between a commit and the send loses the messages still pending. It is
/// for tests, development and workloads that accept that loss. Use the EF Core or Npgsql outbox where it matters.
/// It does not store scheduled messages (<see cref="IMessageOutbox.AddScheduled"/> is unsupported).
/// </remarks>
public interface IInMemoryMessageOutbox : IMessageOutbox
{
    /// <summary>The messages waiting for <see cref="CommitAsync"/>.</summary>
    int Pending { get; }

    /// <summary>
    /// Sends the pending messages in the order they were added. A message leaves the pending list only after it is sent,
    /// so when a send fails the rest stay pending and a later commit sends them.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards the pending messages without sending them.</summary>
    void Rollback();
}

internal sealed class InMemoryMessageOutbox(MessagingBus bus) : IInMemoryMessageOutbox, IDisposable
{
    private readonly List<MessageEnvelope> _pending = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _commits = new(1, 1);

    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public Task Add(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_gate)
        {
            _pending.Add(envelope);
        }

        return Task.CompletedTask;
    }

    public Task AddScheduled(MessageEnvelope envelope, DateTimeOffset deliverAt, Guid scheduleToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The in-memory outbox does not store scheduled messages. Use the EF Core or Npgsql outbox to schedule inside a scope.");

    public Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The in-memory outbox does not store scheduled messages, so there is nothing to cancel here.");

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        // One commit at a time: two concurrent commits would otherwise both send the head of the list.
        await _commits.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                MessageEnvelope next;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        return;
                    }

                    next = _pending[0];
                }

                await bus.Transport.Send(next, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _pending.RemoveAt(0);
                }
            }
        }
        finally
        {
            _commits.Release();
        }
    }

    public void Rollback()
    {
        lock (_gate)
        {
            _pending.Clear();
        }
    }

    /// <summary>Ending the scope without a commit is a rollback: nothing pending is sent.</summary>
    public void Dispose() => Rollback();
}
