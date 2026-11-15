using System.Diagnostics;

namespace Modulus.Messaging.Internal;

/// <summary>
/// Traces for publish, send and consume (FR-24, NFR-17). The producer's W3C trace context travels in the
/// <see cref="MessagingHeaders.TraceParent"/> header, so a consumer's span joins the producer's trace, also when the
/// message waited in an outbox or a queue. Without a listener, no span is created and no header is stamped.
/// </summary>
internal static class MessagingTelemetry
{
    public const string SourceName = "Modulus.Messaging";

    private static readonly ActivitySource Source = new(SourceName);

    internal static Activity? StartProducer(string operation, string destination, string system)
    {
        var activity = Source.StartActivity(operation, ActivityKind.Producer);
        activity?.SetTag("messaging.system", system);
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.destination.name", destination);
        return activity;
    }

    internal static Activity? StartConsumer(MessageEnvelope envelope, string system)
    {
        var parent = default(ActivityContext);
        if (envelope.Headers.TryGetValue(MessagingHeaders.TraceParent, out var traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState: null, out parent);
        }

        var activity = Source.StartActivity($"process {envelope.Endpoint}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", system);
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", envelope.Endpoint);
        activity?.SetTag("messaging.message.id", envelope.MessageId.ToString());
        return activity;
    }

    /// <summary>
    /// The trace id and span id carried by a message's <see cref="MessagingHeaders.TraceParent"/> header, or null when it has
    /// none. Works without a listener, so a log line can name the trace even when no span is recorded.
    /// </summary>
    internal static (string TraceId, string SpanId)? TraceOf(MessageEnvelope envelope)
    {
        if (!envelope.Headers.TryGetValue(MessagingHeaders.TraceParent, out var traceParent))
        {
            return null;
        }

        // W3C: version-traceid-spanid-flags, with a 32-digit trace id and a 16-digit span id.
        var parts = traceParent.Split('-');
        return parts.Length == 4 && parts[1].Length == 32 && parts[2].Length == 16 ? (parts[1], parts[2]) : null;
    }

    /// <summary>The W3C trace context of the current span, or null when there is none to carry.</summary>
    internal static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: { } id } ? id : null;
}
