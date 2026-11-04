namespace Modulus.Analytics;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// Turns a record's values into JSON and back, using the dataset's declared field types, so a queued record comes back with the same
/// CLR types it had when it was captured (a <see cref="Guid"/> is not returned as text). Only declared fields are kept.
/// </summary>
public static class AnalyticsValueSerializer
{
    /// <summary>Converts <paramref name="values"/> to JSON according to <paramref name="dataset"/>.</summary>
    /// <param name="dataset">The dataset version the values follow.</param>
    /// <param name="values">The values by field name.</param>
    /// <exception cref="InvalidOperationException">A required field is missing or null, or a value cannot be read as its declared type.</exception>
    public static string Serialize(AnalyticsDatasetDescriptor dataset, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(values);

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var field in dataset.Fields)
            {
                values.TryGetValue(field.Name, out var value);
                if (value is null)
                {
                    if (!field.IsNullable && field.EffectiveMasking != AnalyticsMaskingAction.Drop)
                    {
                        throw new InvalidOperationException($"Field '{field.Name}' of '{dataset.Key}' is required.");
                    }

                    if (values.ContainsKey(field.Name) || field.IsNullable)
                    {
                        w.WriteNull(field.Name);
                    }

                    continue;
                }

                try
                {
                    Write(w, field, value);
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    throw new InvalidOperationException(
                        $"Field '{field.Name}' of '{dataset.Key}' is {field.Type}, but the value is a {value.GetType().Name}.", ex);
                }
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Returns <paramref name="values"/> with each declared field coerced to its declared CLR type (string, long, decimal, double, bool,
    /// <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>, <see cref="Guid"/>) and undeclared fields removed. Sinks can rely on these types.
    /// </summary>
    /// <param name="dataset">The dataset version the values follow.</param>
    /// <param name="values">The values by field name.</param>
    /// <exception cref="InvalidOperationException">A required field is missing or null, or a value cannot be read as its declared type.</exception>
    public static IReadOnlyDictionary<string, object?> Normalize(AnalyticsDatasetDescriptor dataset, IReadOnlyDictionary<string, object?> values)
        => Deserialize(dataset, Serialize(dataset, values));

    /// <summary>Reads JSON written by <see cref="Serialize"/> back into typed values.</summary>
    /// <param name="dataset">The dataset version the JSON follows.</param>
    /// <param name="json">The JSON object.</param>
    /// <exception cref="InvalidOperationException">The JSON does not match the dataset.</exception>
    public static IReadOnlyDictionary<string, object?> Deserialize(AnalyticsDatasetDescriptor dataset, string json)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in dataset.Fields)
            {
                if (!doc.RootElement.TryGetProperty(field.Name, out var element))
                {
                    continue;
                }

                result[field.Name] = element.ValueKind == JsonValueKind.Null ? null : Read(field.Type, element);
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new InvalidOperationException($"Stored values do not match dataset '{dataset.Key}': {ex.Message}", ex);
        }
    }

    private static void Write(Utf8JsonWriter w, AnalyticsFieldDescriptor field, object value)
    {
        var culture = CultureInfo.InvariantCulture;
        switch (field.Type)
        {
            case AnalyticsFieldType.String:
                w.WriteString(field.Name, Convert.ToString(value, culture));
                break;
            case AnalyticsFieldType.Int64:
                w.WriteNumber(field.Name, Convert.ToInt64(value, culture));
                break;
            case AnalyticsFieldType.Decimal:
                w.WriteNumber(field.Name, Convert.ToDecimal(value, culture));
                break;
            case AnalyticsFieldType.Double:
                w.WriteNumber(field.Name, Convert.ToDouble(value, culture));
                break;
            case AnalyticsFieldType.Boolean:
                w.WriteBoolean(field.Name, value is bool b ? b : throw new InvalidCastException());
                break;
            case AnalyticsFieldType.Timestamp:
                w.WriteString(field.Name, value switch
                {
                    DateTimeOffset o => o.ToUniversalTime().ToString("O", culture),
                    DateTime d => new DateTimeOffset(d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d).ToUniversalTime().ToString("O", culture),
                    _ => throw new InvalidCastException(),
                });
                break;
            case AnalyticsFieldType.Date:
                w.WriteString(field.Name, value switch
                {
                    DateOnly d => d.ToString("O", culture),
                    DateTime dt => DateOnly.FromDateTime(dt).ToString("O", culture),
                    _ => throw new InvalidCastException(),
                });
                break;
            default:
                w.WriteString(field.Name, value switch
                {
                    Guid g => g.ToString("D", culture),
                    string s => Guid.Parse(s).ToString("D", culture),
                    _ => throw new InvalidCastException(),
                });
                break;
        }
    }

    private static object Read(AnalyticsFieldType type, JsonElement element) => type switch
    {
        AnalyticsFieldType.String => element.GetString()!,
        AnalyticsFieldType.Int64 => element.GetInt64(),
        AnalyticsFieldType.Decimal => element.GetDecimal(),
        AnalyticsFieldType.Double => element.GetDouble(),
        AnalyticsFieldType.Boolean => element.GetBoolean(),
        AnalyticsFieldType.Timestamp => DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        AnalyticsFieldType.Date => DateOnly.ParseExact(element.GetString()!, "O", CultureInfo.InvariantCulture),
        _ => element.GetGuid(),
    };
}
