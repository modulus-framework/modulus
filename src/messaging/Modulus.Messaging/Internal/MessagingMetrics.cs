using System.Diagnostics.Metrics;

namespace Modulus.Messaging.Internal;

internal static class MessagingMetrics
{
    public const string MeterName = "Modulus.Messaging";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Deliveries skipped because the consumer already handled the message (FR-11).</summary>
    public static readonly Counter<long> InboxDuplicates = Meter.CreateCounter<long>("messaging.inbox.duplicates");
}
