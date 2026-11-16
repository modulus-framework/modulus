namespace Modulus.Messaging;

/// <summary>
/// Keeps a consumer's endpoint and settings in one class (FR-03): the endpoint name and the options that apply to it.
/// Register it with <see cref="MessagingBusBuilder.AddConsumer{TConsumer, TMessage}(ConsumerDefinition{TConsumer})"/>.
/// The settings are the same as the ones <c>AddConsumer</c> takes; a definition is one way to write them down.
/// </summary>
public abstract class ConsumerDefinition<TConsumer>
    where TConsumer : class
{
    /// <summary>
    /// The endpoint the consumer listens on. Null uses the consumer's name in kebab-case, as <c>AddConsumer</c> does
    /// without an endpoint name.
    /// </summary>
    public virtual string? EndpointName => null;

    /// <summary>Sets retry, redelivery, timeout or other per-consumer settings. The default sets nothing.</summary>
    public virtual void Configure(ConsumerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
    }
}
