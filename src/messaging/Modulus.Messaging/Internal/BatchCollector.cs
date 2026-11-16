using System.Collections.Concurrent;

namespace Modulus.Messaging.Internal;

/// <summary>
/// Gathers the deliveries of one batch consumer into batches (FR-04). Messages are grouped by tenant, because a batch runs
/// in one tenant's scope. A group runs when it reaches the batch size, or when the window has passed since its first message.
/// Every message waits for its batch: <see cref="AddAsync"/> returns when the batch succeeded and throws when it failed,
/// after the retries of the batch.
/// </summary>
internal sealed class BatchCollector
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _open = new(StringComparer.Ordinal);
    private readonly int _size;
    private readonly TimeSpan _window;
    private readonly RetryPolicy? _retry;
    private readonly TimeProvider _time;
    private readonly Func<IReadOnlyList<MessageEnvelope>, Task> _attempt;

    internal BatchCollector(int size, TimeSpan window, RetryPolicy? retry, TimeProvider time, Func<IReadOnlyList<MessageEnvelope>, Task> attempt)
    {
        _size = size;
        _window = window;
        _retry = retry;
        _time = time;
        _attempt = attempt;
    }

    /// <summary>Adds the message to its tenant's batch and waits until that batch has run.</summary>
    internal async Task AddAsync(MessageEnvelope envelope, string tenantKey, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Item>? full = null;
        lock (_gate)
        {
            if (!_open.TryGetValue(tenantKey, out var pending))
            {
                pending = new Pending();
                _open[tenantKey] = pending;
                StartWindow(tenantKey, pending);
            }

            pending.Items.Add(new Item(envelope, done));
            if (pending.Items.Count >= _size)
            {
                _open.Remove(tenantKey);
                full = pending.Items;
            }
        }

        if (full is not null)
        {
            await ExecuteAsync(full).ConfigureAwait(false);
        }

        await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the group's batch when its window passes, unless it already ran because it was full.</summary>
    private void StartWindow(string tenantKey, Pending pending)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(_window, _time, CancellationToken.None).ConfigureAwait(false);
            List<Item>? due = null;
            lock (_gate)
            {
                if (_open.TryGetValue(tenantKey, out var current) && ReferenceEquals(current, pending))
                {
                    _open.Remove(tenantKey);
                    due = pending.Items;
                }
            }

            if (due is not null)
            {
                await ExecuteAsync(due).ConfigureAwait(false);
            }
        }, CancellationToken.None);
    }

    /// <summary>Runs the batch, retrying it as a whole, and completes every waiting message with the outcome.</summary>
    private async Task ExecuteAsync(List<Item> items)
    {
        var envelopes = items.Select(i => i.Envelope).ToArray();
        Exception? failure = null;
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                await _attempt(envelopes).ConfigureAwait(false);
                failure = null;
                break;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure is MessageParkException || _retry is null || _retry.IsIgnored(failure) || attempts - 1 >= _retry.Retries)
            {
                break;
            }

            await Task.Delay(_retry.DelayFor(attempts), _time, CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var item in items)
        {
            if (failure is null)
            {
                item.Done.TrySetResult();
            }
            else
            {
                item.Done.TrySetException(failure);
            }
        }
    }

    private sealed class Pending
    {
        public List<Item> Items { get; } = [];
    }

    private sealed record Item(MessageEnvelope Envelope, TaskCompletionSource Done);
}
