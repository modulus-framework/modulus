namespace Modulus.Analytics.Messaging;

/// <summary>Registers the messaging sink.</summary>
public static class AnalyticsMessagingExtensions
{
    /// <summary>
    /// Adds the messaging sink. The module bus (<c>AddRabbitMqEventBus</c> or <c>AddKafkaEventBus</c>) must be registered as well.
    /// </summary>
    /// <param name="builder">The analytics builder.</param>
    public static AnalyticsBuilder AddMessaging(this AnalyticsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSink<MessagingAnalyticsSink>();
    }
}
