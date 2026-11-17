using System.Text;
using System.Text.Json;

namespace Modulus.Messaging.Transports.PostgreSql;

/// <summary>
/// Maps an envelope to a queue row and back. The body is JSON (readable by non-.NET services). The message type is
/// stored as an assembly-qualified name and rebuilt with <see cref="Type.GetType(string)"/>, as the RabbitMQ transport does.
/// </summary>
internal static class PostgreSqlMessageCodec
{
    internal const string ErrorTypeHeader = "mod-error-type";
    internal const string ErrorMessageHeader = "mod-error-message";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (string MessageType, string? ConsumerKey, string Body, string HeadersJson) Encode(MessageEnvelope envelope)
    {
        var type = envelope.Message.GetType();
        var body = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(envelope.Message, type, Json));
        var headers = JsonSerializer.Serialize(envelope.Headers, Json);
        return (envelope.WireType ?? MessageUrn.Of(type), envelope.ConsumerKey, body, headers);
    }

    /// <summary>Rebuilds the envelope, or returns the reason it cannot be rebuilt.</summary>
    public static bool TryDecode(
        string endpoint,
        Guid messageId,
        string messageType,
        string? consumerKey,
        string body,
        string headersJson,
        out MessageEnvelope? envelope,
        out string? reason,
        MessageTypeAllowList? allowList = null)
    {
        envelope = null;
        reason = null;
        if (MessageTypeAllowList.ResolveWireType(messageType, allowList) is not { } type)
        {
            reason = $"Unknown message type '{messageType}'.";
            return false;
        }

        object? message;
        try
        {
            message = JsonSerializer.Deserialize(Encoding.UTF8.GetBytes(body), type, Json);
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson, Json) ?? [];
            envelope = new MessageEnvelope(messageId, endpoint, message!, headers, consumerKey);
        }
        catch (JsonException exception)
        {
            reason = $"Body is not valid JSON for '{type.FullName}': {exception.Message}";
            return false;
        }

        return true;
    }
}
