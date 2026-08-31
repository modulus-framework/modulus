namespace Modulus.Events;

using System.Text.Json;
using System.Text.Json.Serialization;
using Modulus.Events.Abstractions;

/// <summary>
/// <see cref="IMessageSerializer"/> implementation using System.Text.Json with
/// opinionated defaults: camelCase property names, case-insensitive deserialization
/// (for compatibility with other stacks), and string enum values.
/// </summary>
public sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Serialize(object payload, Type type)
        => JsonSerializer.Serialize(payload, type, s_options);

    public object? Deserialize(string json, Type type)
    {
        try
        {
            return JsonSerializer.Deserialize(json, type, s_options);
        }
        catch (JsonException)
        {
            // Deserialization failed — return null rather than propagating.
            // Callers decide how to handle: log, dead-letter, etc.
            return null;
        }
    }
}
