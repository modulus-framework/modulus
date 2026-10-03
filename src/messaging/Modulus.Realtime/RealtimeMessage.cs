namespace Modulus.Realtime;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// One message on its way to clients, as it travels through the backplane. <see cref="Data"/> is serialized once by the
/// publisher; every node delivers it to its own connections.
/// </summary>
/// <param name="Id">Unique, time-ordered id (the SSE <c>id:</c>, the <c>Last-Event-ID</c> a client resumes from).</param>
/// <param name="Type">The event name (the SSE <c>event:</c>; an integration event's <c>[IntegrationEventName]</c>).</param>
/// <param name="Data">The payload as JSON.</param>
/// <param name="TenantId">The tenant it was published in; null for the host.</param>
/// <param name="Users">Recipient user ids; null when not addressed to users.</param>
/// <param name="Topic">Recipient topic; null when not addressed to a topic.</param>
/// <param name="Permission">Permission every recipient must hold; null for none.</param>
/// <param name="Timestamp">When it was published.</param>
public sealed record RealtimeMessage(
    string Id,
    string Type,
    string Data,
    Guid? TenantId,
    IReadOnlyList<string>? Users,
    string? Topic,
    string? Permission,
    DateTimeOffset Timestamp)
{
    private JsonElement? _element;

    /// <summary>The payload parsed once for transports that send structured data (SignalR).</summary>
    [JsonIgnore]
    internal JsonElement DataElement
    {
        get
        {
            if (_element is { } cached)
                return cached;
            using var document = JsonDocument.Parse(Data);
            var element = document.RootElement.Clone();
            _element = element;
            return element;
        }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes the message for a backplane.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads a message a backplane received; null when the text is not one.</summary>
    public static RealtimeMessage? FromJson(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<RealtimeMessage>(json, JsonOptions);
            return message is { Id.Length: > 0, Type.Length: > 0, Data.Length: > 0 } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What a SignalR client receives (method <c>event</c>).</summary>
/// <param name="Id">The message id (pass it to <c>Resume</c> after a reconnect).</param>
/// <param name="Type">The event name.</param>
/// <param name="Data">The payload.</param>
/// <param name="Topic">The topic it was addressed to, if any.</param>
/// <param name="Timestamp">When it was published.</param>
public sealed record RealtimeEnvelope(string Id, string Type, JsonElement Data, string? Topic, DateTimeOffset Timestamp);
