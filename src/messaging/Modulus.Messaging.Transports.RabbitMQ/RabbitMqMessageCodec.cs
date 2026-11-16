using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Modulus.Messaging.Transports.RabbitMQ;

/// <summary>
/// Maps an envelope to an AMQP message and back. The body is JSON (readable by non-.NET services); the metadata
/// travels as headers (<c>mod-*</c>). The message type is a header, rebuilt with <see cref="Type.GetType(string)"/>.
/// </summary>
internal static class RabbitMqMessageCodec
{
    internal const string MessageTypeHeader = "mod-message-type";
    internal const string QuorumDeliveryCountHeader = "x-delivery-count";
    internal const string ConsumerKeyHeader = "mod-consumer-key";
    internal const string ErrorTypeHeader = "mod-error-type";
    internal const string ErrorMessageHeader = "mod-error-message";
    internal const string EndpointHeader = "mod-endpoint";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (byte[] Body, BasicProperties Properties) Encode(MessageEnvelope envelope)
    {
        var type = envelope.Message.GetType();
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in envelope.Headers)
        {
            headers[key] = value;
        }

        headers[MessageTypeHeader] = MessageUrn.Of(type);
        if (envelope.ConsumerKey is not null)
        {
            headers[ConsumerKeyHeader] = envelope.ConsumerKey;
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = envelope.MessageId.ToString("D"),
            Headers = headers,
        };
        return (JsonSerializer.SerializeToUtf8Bytes(envelope.Message, type, Json), properties);
    }

    /// <summary>Rebuilds the envelope, or returns the reason it cannot be rebuilt (unknown type, bad body, bad id).</summary>
    public static bool TryDecode(
        string endpoint,
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties properties,
        out MessageEnvelope? envelope,
        out string? reason,
        MessageTypeAllowList? allowList = null)
    {
        envelope = null;
        reason = null;
        var headers = StringHeaders(properties.Headers);

        // NFR-10: the type name comes off the wire, so it is resolved only against the registered message types.
        if (!headers.TryGetValue(MessageTypeHeader, out var typeName) ||
            MessageTypeAllowList.ResolveWireType(typeName, allowList) is not { } type)
        {
            reason = $"Unknown message type '{typeName ?? "(missing)"}'.";
            return false;
        }

        if (!Guid.TryParse(properties.MessageId, out var messageId))
        {
            reason = "Missing or invalid message id.";
            return false;
        }

        object? message;
        try
        {
            message = JsonSerializer.Deserialize(body.Span, type, Json);
        }
        catch (JsonException exception)
        {
            reason = $"Body is not valid JSON for '{type.FullName}': {exception.Message}";
            return false;
        }

        if (message is null)
        {
            reason = "Body is empty.";
            return false;
        }

        // A quorum queue counts the deliveries returned before this one in x-delivery-count (D3): this delivery is one more.
        if (headers.TryGetValue(QuorumDeliveryCountHeader, out var returned) &&
            long.TryParse(returned, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
        {
            headers[MessagingHeaders.DeliveryCount] = (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        headers.Remove(MessageTypeHeader);
        headers.TryGetValue(ConsumerKeyHeader, out var consumerKey);
        headers.Remove(ConsumerKeyHeader);
        envelope = new MessageEnvelope(messageId, endpoint, message, headers, consumerKey);
        return true;
    }

    /// <summary>
    /// True when a bus-hosted receiver has no consumer for the delivery's type (NFR-10): the message is unhandled and goes
    /// to _skipped without being decoded. Always false for a standalone transport (no registered types).
    /// </summary>
    internal static bool NotRegistered(IReadOnlyBasicProperties properties, MessageTypeAllowList? types)
    {
        if (types is null)
        {
            return false;
        }

        var headers = StringHeaders(properties.Headers);
        return headers.TryGetValue(MessageTypeHeader, out var name) && types.Resolve(name) is null;
    }

    internal static Dictionary<string, string> StringHeaders(IDictionary<string, object?>? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (headers is null)
        {
            return result;
        }

        foreach (var (key, value) in headers)
        {
            result[key] = value switch
            {
                null => string.Empty,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => value.ToString() ?? string.Empty,
            };
        }

        return result;
    }
}
