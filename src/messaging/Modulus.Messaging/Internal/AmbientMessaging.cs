namespace Modulus.Messaging.Internal;

/// <summary>Wraps the scoped bus so that each publish or send runs under the caller's ambient state (FR-21).</summary>
internal sealed class AmbientBus(IBus inner, IServiceProvider scope, IMessageAmbientContext ambient) : IBus
{
    public async Task Publish<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        using var entered = ambient.Enter(scope);
        await inner.Publish(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task Send<TMessage>(Uri address, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        using var entered = ambient.Enter(scope);
        await inner.Send(address, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendRequest<TMessage>(Uri address, TMessage message, Guid requestId, string replyEndpoint, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        using var entered = ambient.Enter(scope);
        await inner.SendRequest(address, message, requestId, replyEndpoint, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Wraps the scoped scheduler the same way, so a scheduled message carries the caller's ambient state.</summary>
internal sealed class AmbientScheduler(IMessageScheduler inner, IServiceProvider scope, IMessageAmbientContext ambient) : IMessageScheduler
{
    public Task<Guid> ScheduleSend<TMessage>(Uri address, DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class => Under(() => inner.ScheduleSend(address, deliverAt, message, cancellationToken));

    public Task<Guid> SchedulePublish<TMessage>(DateTimeOffset deliverAt, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class => Under(() => inner.SchedulePublish(deliverAt, message, cancellationToken));

    public Task<Guid> ScheduleSend<TMessage>(Uri address, TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class => Under(() => inner.ScheduleSend(address, delay, message, cancellationToken));

    public Task<Guid> SchedulePublish<TMessage>(TimeSpan delay, TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class => Under(() => inner.SchedulePublish(delay, message, cancellationToken));

    public Task<bool> CancelScheduled(Guid token, CancellationToken cancellationToken = default) => inner.CancelScheduled(token, cancellationToken);

    /// <summary>The schedule reads the ambient state when it is called, so it runs inside the scope.</summary>
    private Task<Guid> Under(Func<Task<Guid>> schedule)
    {
        using var entered = ambient.Enter(scope);
        return schedule();
    }
}
