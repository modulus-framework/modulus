namespace Modulus.Analytics;

using System.Text;
using System.Text.Json;

/// <summary>
/// Publishes a dataset contract as JSON Schema (2020-12), so the people who consume the data, and tools such as schema registries, can
/// read it without Modulus. The schema describes what a provider receives <em>after</em> masking: dropped fields are absent and
/// hashed fields are strings.
/// </summary>
public static class AnalyticsDatasetSchema
{
    /// <summary>Renders <paramref name="dataset"/> as an indented JSON Schema document.</summary>
    /// <param name="dataset">The dataset contract.</param>
    public static string ToJsonSchema(AnalyticsDatasetDescriptor dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            w.WriteString("$id", $"urn:modulus:analytics:{dataset.Key}");
            w.WriteString("title", dataset.Key);
            if (dataset.Description is not null)
            {
                w.WriteString("description", dataset.Description);
            }

            w.WriteString("type", "object");
            w.WriteBoolean("additionalProperties", false);
            w.WriteString("x-owner", dataset.Owner);
            if (dataset.FreshnessSla is { } sla)
            {
                w.WriteString("x-freshness-sla", System.Xml.XmlConvert.ToString(sla));
            }

            if (dataset.RetentionDays is { } days)
            {
                w.WriteNumber("x-retention-days", days);
            }

            var delivered = dataset.Fields.Where(f => f.EffectiveMasking != AnalyticsMaskingAction.Drop).ToList();
            w.WriteStartObject("properties");
            foreach (var field in delivered)
            {
                w.WriteStartObject(field.Name);
                var hashed = field.EffectiveMasking == AnalyticsMaskingAction.Hash;
                var (type, format) = hashed ? ("string", null) : Map(field.Type);
                if (field.IsNullable)
                {
                    w.WriteStartArray("type");
                    w.WriteStringValue(type);
                    w.WriteStringValue("null");
                    w.WriteEndArray();
                }
                else
                {
                    w.WriteString("type", type);
                }

                if (format is not null)
                {
                    w.WriteString("format", format);
                }

                w.WriteString("x-classification", field.Classification.ToString());
                w.WriteString("x-masking", field.EffectiveMasking.ToString());
                w.WriteEndObject();
            }

            w.WriteEndObject();

            w.WriteStartArray("required");
            foreach (var field in delivered.Where(f => !f.IsNullable))
            {
                w.WriteStringValue(field.Name);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static (string Type, string? Format) Map(AnalyticsFieldType type) => type switch
    {
        AnalyticsFieldType.Int64 => ("integer", null),
        AnalyticsFieldType.Decimal or AnalyticsFieldType.Double => ("number", null),
        AnalyticsFieldType.Boolean => ("boolean", null),
        AnalyticsFieldType.Timestamp => ("string", "date-time"),
        AnalyticsFieldType.Date => ("string", "date"),
        AnalyticsFieldType.Guid => ("string", "uuid"),
        _ => ("string", null),
    };
}
