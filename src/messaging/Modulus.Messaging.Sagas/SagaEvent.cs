namespace Modulus.Messaging.Sagas;

/// <summary>A message type a saga handles. <see cref="CorrelateBy"/> says which instance it belongs to.</summary>
public sealed class SagaEvent<TMessage> where TMessage : class
{
    private readonly Action<Func<object, Guid>> _register;

    internal SagaEvent(Action<Func<object, Guid>> register) => _register = register;

    /// <summary>Gives the correlation key of each message. Events with the same key belong to the same saga instance.</summary>
    public SagaEvent<TMessage> CorrelateBy(Func<TMessage, Guid> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _register(message => key((TMessage)message));
        return this;
    }
}
