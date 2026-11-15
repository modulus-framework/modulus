namespace Modulus.Messaging;

/// <summary>Handles one message type. A new DI scope is created for every message (RULE-07).</summary>
public interface IConsumer<TMessage> where TMessage : class
{
    Task Consume(ConsumeContext<TMessage> context);
}
