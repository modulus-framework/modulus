using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Modulus.Messaging.Internal;

/// <summary>
/// Meter "Modulus.Messaging" (FR-24, NFR-17). Names follow the OpenTelemetry messaging conventions where one exists.
/// Every instrument is tagged with the destination endpoint, so a dashboard can split by queue.
/// </summary>
internal static class MessagingMetrics
{
    public const string MeterName = "Modulus.Messaging";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Deliveries skipped because the consumer already handled the message (FR-11).</summary>
    public static readonly Counter<long> InboxDuplicates = Meter.CreateCounter<long>("messaging.inbox.duplicates");

    /// <summary>Messages handed to a transport by publish or send.</summary>
    public static readonly Counter<long> Sent = Meter.CreateCounter<long>("messaging.client.sent.messages", "{message}");

    /// <summary>Messages a consumer handled successfully (the attempt committed).</summary>
    public static readonly Counter<long> Consumed = Meter.CreateCounter<long>("messaging.client.consumed.messages", "{message}");

    /// <summary>Time of each consume attempt, including the commit. Tagged with <c>error.type</c> when the attempt failed.</summary>
    public static readonly Histogram<double> ProcessDuration = Meter.CreateHistogram<double>("messaging.process.duration", "s");

    /// <summary>Messages moved out of the normal path: <c>reason</c> is <c>error</c>, <c>expired</c>, <c>no-tenant</c> or <c>unhandled</c>.</summary>
    public static readonly Counter<long> Parked = Meter.CreateCounter<long>("messaging.parked.messages", "{message}");

    internal static KeyValuePair<string, object?> Destination(string endpoint) => new("messaging.destination.name", endpoint);

    internal static void RecordProcess(string endpoint, long startedTimestamp, string? errorType)
    {
        var tags = new TagList { Destination(endpoint) };
        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        ProcessDuration.Record(Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds, tags);
    }
}
