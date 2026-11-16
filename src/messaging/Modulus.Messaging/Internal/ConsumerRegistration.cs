namespace Modulus.Messaging.Internal;

/// <summary>
/// One consumer for one message type on one endpoint. <c>Options</c> holds the effective settings
/// (the consumer's own values, else the bus defaults). <c>PublishFault</c> publishes
/// <c>Fault&lt;TMessage&gt;</c> when the message is parked in <c>_error</c>.
/// </summary>
internal sealed record ConsumerRegistration(
    string ConsumerKey,
    string Endpoint,
    Type MessageType,
    Func<IServiceProvider, MessageEnvelope, CancellationToken, Task> Invoke,
    ConsumerOptions Options,
    Func<IBus, MessageEnvelope, Exception, CancellationToken, Task> PublishFault,
    Func<IServiceProvider, IReadOnlyList<MessageEnvelope>, CancellationToken, Task>? InvokeBatch = null);

/// <summary>Configuration produced by <see cref="MessagingBusBuilder"/>.</summary>
internal sealed record MessagingOptions(
    IReadOnlyList<ConsumerRegistration> Consumers,
    Func<IServiceProvider, IMessageTransport> TransportFactory,
    string ReplyEndpoint,
    int MaxMessageBytes,
    IReadOnlyList<Type> Filters,
    IReadOnlyList<Type> Observers,
    bool DeployOnly,
    MessageTypeAllowList MessageTypes);
