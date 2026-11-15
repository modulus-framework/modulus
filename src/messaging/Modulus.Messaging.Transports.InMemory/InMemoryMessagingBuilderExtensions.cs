namespace Modulus.Messaging.Transports.InMemory;

public static class InMemoryMessagingBuilderExtensions
{
    /// <summary>Uses the in-memory transport. Nothing is durable; use it for tests and single-process apps.</summary>
    public static MessagingBusBuilder UseInMemory(this MessagingBusBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseTransport(_ => new InMemoryTransport());
    }
}
