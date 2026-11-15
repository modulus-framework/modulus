using System.Collections.Concurrent;

namespace Modulus.Messaging.Internal;

/// <summary>The requests waiting for a reply, by request id. A reply completes its request; a timeout removes it.</summary>
internal sealed class RequestRegistry
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<object>> _pending = new();

    internal TaskCompletionSource<object> Add(Guid requestId)
    {
        var pending = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.TryAdd(requestId, pending)
            ? pending
            : throw new InvalidOperationException($"Request {requestId} is already waiting.");
    }

    internal void Remove(Guid requestId) => _pending.TryRemove(requestId, out _);

    /// <summary>Completes the waiting request. Returns false when nothing waits for it (it timed out, or it was never sent here).</summary>
    internal bool Complete(Guid requestId, object response) =>
        _pending.TryRemove(requestId, out var pending) && pending.TrySetResult(response);
}

/// <summary>Receives the replies on this process's reply endpoint for one response type (FR-06).</summary>
internal sealed class ReplyReceiver<TResponse>(RequestRegistry registry) : IConsumer<RequestReply<TResponse>>
    where TResponse : class
{
    public Task Consume(ConsumeContext<RequestReply<TResponse>> context)
    {
        // A reply whose requester already gave up finds nothing waiting. It is dropped, not parked: nobody can act on it.
        registry.Complete(context.Message.RequestId, context.Message.Response);
        return Task.CompletedTask;
    }
}

internal sealed class RequestClient(MessagingBus bus, RequestRegistry registry) : IRequestClient
{
    public async Task<TResponse> GetResponse<TRequest, TResponse>(Uri address, TRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(request);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Must be positive.");
        }

        if (!bus.HasConsumerFor(typeof(RequestReply<TResponse>)))
        {
            throw new InvalidOperationException(
                $"No reply receiver for {typeof(TResponse).Name}: call AddRequestClient<{typeof(TResponse).Name}>() when the bus is configured.");
        }

        var requestId = Guid.NewGuid();
        var pending = registry.Add(requestId);
        try
        {
            await bus.SendWithHeaders(
                address,
                request,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MessagingHeaders.RequestId] = requestId.ToString("N"),
                    [MessagingHeaders.ReplyTo] = bus.ReplyEndpoint,
                },
                cancellationToken).ConfigureAwait(false);

            var answered = await Task.WhenAny(pending.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (answered != pending.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new RequestTimeoutException(address.ToString(), timeout);
            }

            return (TResponse)await pending.Task.ConfigureAwait(false);
        }
        finally
        {
            registry.Remove(requestId);
        }
    }
}
