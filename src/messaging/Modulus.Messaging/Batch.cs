namespace Modulus.Messaging;

/// <summary>
/// The messages a batch consumer handles together (FR-04). Each entry is the consume context of one message, so its id,
/// headers and tenant are available. The consumer is registered with <see cref="MessagingBusBuilder.AddBatchConsumer{TConsumer,TMessage}"/>.
/// </summary>
public sealed record Batch<TMessage>(IReadOnlyList<ConsumeContext<TMessage>> Messages)
    where TMessage : class;
