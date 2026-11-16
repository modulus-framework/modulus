namespace Modulus.Messaging.Sagas;

/// <summary>What a handler sees: the saga instance, the event, and the messages it asks to send.</summary>
public sealed class SagaContext<TSaga, TMessage>
    where TSaga : SagaInstance
    where TMessage : class
{
    private readonly SagaEffects _effects;

    internal SagaContext(TSaga saga, TMessage message, SagaEffects effects)
    {
        Saga = saga;
        Message = message;
        _effects = effects;
    }

    public TSaga Saga { get; }

    public TMessage Message { get; }

    /// <summary>True once the handler asked for the saga to end with <see cref="Complete"/>.</summary>
    internal bool IsCompleted { get; private set; }

    /// <summary>Ends the saga once the handler returns: its instance is removed.</summary>
    public void Complete() => IsCompleted = true;

    /// <summary>Publishes a message once the saga's state is saved.</summary>
    public void Publish<T>(T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        _effects.Add((bus, _, ct) => bus.Publish(message, ct));
    }

    /// <summary>Sends a message to an address once the saga's state is saved.</summary>
    public void Send<T>(Uri address, T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(message);
        _effects.Add((bus, _, ct) => bus.Send(address, message, ct));
    }

    /// <summary>
    /// Asks another endpoint for something once the saga's state is saved. The answer comes back to the saga's endpoint as an
    /// ordinary message the saga declares with <c>Event</c>. A consumer that fails sends a <see cref="RequestFault"/> instead,
    /// which the saga declares as <c>Event&lt;RequestFault&gt;().CorrelateBy(f =&gt; f.RequestId)</c>.
    /// The request id is the saga instance's <c>CorrelationId</c>, so one request is outstanding per saga instance.
    /// </summary>
    public void Request<TRequest>(Uri address, TRequest request) where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(request);
        var replyEndpoint = _effects.ReplyEndpoint
            ?? throw new InvalidOperationException("A saga request needs a saga endpoint to answer to.");
        var requestId = Saga.CorrelationId;
        _effects.Add((bus, _, ct) => bus.SendRequest(address, request, requestId, replyEndpoint, ct));
    }

    /// <summary>
    /// Publishes a message after <paramref name="delay"/>, measured from the call. A timeout is a schedule. Its token is
    /// stored with the saga, so the saga cancels it when it completes; a save that fails cancels it too.
    /// </summary>
    public void Schedule<T>(TimeSpan delay, T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        _effects.AddSchedule((scheduler, ct) => scheduler.SchedulePublish(delay, message, ct));
    }
}
