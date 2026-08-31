namespace Modulus.Events.Abstractions;

/// <summary>
/// Pluggable seam for message serialization. All integration-event serialization
/// flows through this single point so wire formats, options (camelCase, enum
/// handling, null behavior), and versioning are centralized and consistent.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes <paramref name="payload"/> to JSON.
    /// </summary>
    /// <param name="payload">Object to serialize.</param>
    /// <param name="type">The CLR type of <paramref name="payload"/>.</param>
    /// <returns>JSON string.</returns>
    string Serialize(object payload, Type type);

    /// <summary>
    /// Deserializes JSON into an instance of <paramref name="type"/>.
    /// </summary>
    /// <param name="json">JSON string.</param>
    /// <param name="type">Target CLR type.</param>
    /// <returns>Deserialized instance, or null if deserialization failed.</returns>
    object? Deserialize(string json, Type type);
}
