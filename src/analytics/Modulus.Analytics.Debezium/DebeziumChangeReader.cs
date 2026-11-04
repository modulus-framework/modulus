namespace Modulus.Analytics.Debezium;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Reads Debezium change events (JSON, schemas disabled) and maps them to analytics records. Inserts, updates and snapshot reads carry the row
/// <c>after</c>; a delete carries it <c>before</c>, with the mapping's deleted marker set. Other operations and tables without a mapping are ignored.
/// </summary>
/// <remarks>
/// <para>The connector must write plain values: <c>value.converter.schemas.enable=false</c>, <c>decimal.handling.mode=string</c> and
/// <c>time.precision.mode=adaptive_time_microseconds</c> (see <c>docs/runbooks/lakehouse-cdc.md</c>).</para>
/// <para>
/// A record's revision is the source's log position (<c>source.lsn</c>) when the connector sends it, otherwise the source timestamp, so a later change
/// wins in the warehouse as it does for every other record. The record id comes from the key column, so one row is one warehouse row.
/// </para>
/// </remarks>
public sealed class DebeziumChangeReader
{
    private readonly IReadOnlyDictionary<string, DebeziumTableMapping[]> _byTable;

    /// <summary>Creates the reader. Throws when a mapping cannot work, so the mistake shows at startup, not on the first change.</summary>
    /// <param name="mappings">One mapping per source table.</param>
    /// <exception cref="ArgumentException">A mapping is invalid.</exception>
    public DebeziumChangeReader(IEnumerable<DebeziumTableMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var list = mappings.ToList();
        foreach (var mapping in list)
        {
            Validate(mapping);
        }

        _byTable = list.GroupBy(m => m.Table, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Reads a batch of change events. A message that is not valid JSON or not a change event becomes a problem, not an exception.</summary>
    /// <param name="messages">The events, as the connector wrote them.</param>
    public DebeziumReadResult Read(IEnumerable<string> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var records = new List<AnalyticsRecord>();
        var problems = new List<string>();
        var ignored = 0;
        var position = 0;
        foreach (var message in messages)
        {
            position++;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(message);
            }
            catch (JsonException)
            {
                problems.Add($"Event {position} is not JSON.");
                continue;
            }

            using (document)
            {
                var outcome = Map(document.RootElement, position, out var record, out var problem);
                switch (outcome)
                {
                    case Outcome.Record:
                        records.Add(record!);
                        break;
                    case Outcome.Ignored:
                        ignored++;
                        break;
                    default:
                        problems.Add(problem!);
                        break;
                }
            }
        }

        return new DebeziumReadResult(records, ignored, problems);
    }

    private enum Outcome
    {
        Record,
        Ignored,
        Problem,
    }

    private Outcome Map(JsonElement root, int position, out AnalyticsRecord? record, out string? problem)
    {
        record = null;
        problem = null;
        // The schemas-enabled form nests the change under "payload"; the schemas-disabled form is the change itself.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object && !root.TryGetProperty("op", out _))
        {
            root = payload;
        }

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("op", out var opElement) || opElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
        {
            problem = $"Event {position} is not a Debezium change event (no op or source).";
            return Outcome.Problem;
        }

        var op = opElement.GetString()!;
        if (op is not ("c" or "r" or "u" or "d"))
        {
            return Outcome.Ignored; // truncate, message and other operations carry no row
        }

        var table = source.TryGetProperty("table", out var tableElement) && tableElement.ValueKind == JsonValueKind.String ? tableElement.GetString() : null;
        var schema = source.TryGetProperty("schema", out var schemaElement) && schemaElement.ValueKind == JsonValueKind.String ? schemaElement.GetString() : null;
        var mapping = table is not null && _byTable.TryGetValue(table, out var candidates)
            ? candidates.FirstOrDefault(m => m.Schema is null || string.Equals(m.Schema, schema, StringComparison.Ordinal))
            : null;
        if (mapping is null)
        {
            return Outcome.Ignored;
        }

        var deleted = op == "d";
        var row = deleted ? Property(root, "before") : Property(root, "after");
        if (row is not { ValueKind: JsonValueKind.Object } rowObject)
        {
            problem = $"Event {position} ({mapping.Table}) has no row to read.";
            return Outcome.Problem;
        }

        if (!rowObject.TryGetProperty(mapping.TenantColumn, out var tenantElement) || tenantElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(tenantElement.GetString(), out var tenant) || tenant == Guid.Empty)
        {
            problem = $"Event {position} ({mapping.Table}) has no valid company id in '{mapping.TenantColumn}'.";
            return Outcome.Problem;
        }

        if (!rowObject.TryGetProperty(mapping.KeyColumn, out var keyElement) || keyElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            problem = $"Event {position} ({mapping.Table}) has no key in '{mapping.KeyColumn}'.";
            return Outcome.Problem;
        }

        var dataset = mapping.Dataset;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in dataset.Fields)
        {
            if (string.Equals(field.Name, mapping.DeletedField, StringComparison.Ordinal))
            {
                values[field.Name] = deleted;
                continue;
            }

            if (!rowObject.TryGetProperty(field.Name, out var element) || element.ValueKind == JsonValueKind.Undefined)
            {
                if (!field.IsNullable)
                {
                    problem = $"Event {position} ({mapping.Table}) has no value for the required column '{field.Name}'.";
                    return Outcome.Problem;
                }

                values[field.Name] = null;
                continue;
            }

            if (element.ValueKind == JsonValueKind.Null)
            {
                if (!field.IsNullable)
                {
                    problem = $"Event {position} ({mapping.Table}) has a null for the required column '{field.Name}'.";
                    return Outcome.Problem;
                }

                values[field.Name] = null;
                continue;
            }

            try
            {
                values[field.Name] = ReadValue(field, element);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
            {
                problem = $"Event {position} ({mapping.Table}): '{field.Name}' cannot be read as {field.Type}: {ex.Message}";
                return Outcome.Problem;
            }
        }

        IReadOnlyDictionary<string, object?> normalized;
        try
        {
            normalized = AnalyticsValueSerializer.Normalize(dataset, values);
        }
        catch (InvalidOperationException ex)
        {
            problem = $"Event {position} ({mapping.Table}) does not match '{dataset.Key}': {ex.Message}";
            return Outcome.Problem;
        }

        var eventId = AnalyticsRecordIds.Deterministic(dataset.Name, $"{tenant:D}/{KeyText(keyElement)}");
        record = new AnalyticsRecord(eventId, dataset.Name, dataset.Version, tenant, OccurredAt(root, source), normalized, Revision(root, source));
        return Outcome.Record;
    }

    private static JsonElement? Property(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value : null;

    private static string KeyText(JsonElement key) => key.ValueKind == JsonValueKind.String
        ? key.GetString()!
        : key.GetRawText();

    private static object? ReadValue(AnalyticsFieldDescriptor field, JsonElement element) => field.Type switch
    {
        AnalyticsFieldType.String => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText(),
        AnalyticsFieldType.Int64 => element.ValueKind == JsonValueKind.Number ? element.GetInt64() : long.Parse(element.GetString()!, CultureInfo.InvariantCulture),
        AnalyticsFieldType.Double => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : double.Parse(element.GetString()!, CultureInfo.InvariantCulture),
        // A decimal must arrive as a string (decimal.handling.mode=string): a binary encoding would be silent corruption.
        AnalyticsFieldType.Decimal => element.ValueKind == JsonValueKind.String
            ? decimal.Parse(element.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture)
            : element.GetDecimal(),
        AnalyticsFieldType.Boolean => element.ValueKind == JsonValueKind.True || (element.ValueKind == JsonValueKind.String && bool.Parse(element.GetString()!)),
        AnalyticsFieldType.Timestamp => element.ValueKind == JsonValueKind.Number
            // adaptive_time_microseconds: microseconds since the Unix epoch.
            ? DateTimeOffset.UnixEpoch.AddTicks(element.GetInt64() * 10)
            : DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        AnalyticsFieldType.Date => element.ValueKind == JsonValueKind.Number
            // io.debezium.time.Date: days since the Unix epoch.
            ? new DateOnly(1970, 1, 1).AddDays(element.GetInt32())
            : DateOnly.Parse(element.GetString()!, CultureInfo.InvariantCulture),
        AnalyticsFieldType.Guid => Guid.Parse(element.GetString()!),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
    };

    private static DateTimeOffset OccurredAt(JsonElement root, JsonElement source)
    {
        var millis = source.TryGetProperty("ts_ms", out var sourceTs) && sourceTs.ValueKind == JsonValueKind.Number
            ? sourceTs.GetInt64()
            : root.TryGetProperty("ts_ms", out var eventTs) && eventTs.ValueKind == JsonValueKind.Number ? eventTs.GetInt64() : 0;
        return DateTimeOffset.FromUnixTimeMilliseconds(millis);
    }

    private static long Revision(JsonElement root, JsonElement source)
    {
        if (source.TryGetProperty("lsn", out var lsn) && lsn.ValueKind == JsonValueKind.Number)
        {
            return lsn.GetInt64();
        }

        return source.TryGetProperty("ts_ms", out var ts) && ts.ValueKind == JsonValueKind.Number
            ? ts.GetInt64()
            : root.TryGetProperty("ts_ms", out var eventTs) && eventTs.ValueKind == JsonValueKind.Number ? eventTs.GetInt64() : 0;
    }

    private static void Validate(DebeziumTableMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (string.IsNullOrWhiteSpace(mapping.Table))
        {
            throw new ArgumentException("A Debezium mapping needs the source table name.", nameof(mapping));
        }

        var problems = mapping.Dataset.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException($"The dataset of '{mapping.Table}' is not valid: {string.Join(" ", problems)}", nameof(mapping));
        }

        if (string.IsNullOrWhiteSpace(mapping.KeyColumn) || string.IsNullOrWhiteSpace(mapping.TenantColumn))
        {
            throw new ArgumentException($"The mapping of '{mapping.Table}' needs a key column and a tenant column.", nameof(mapping));
        }

        if (mapping.DeletedField is not null)
        {
            var field = mapping.Dataset.Fields.FirstOrDefault(f => string.Equals(f.Name, mapping.DeletedField, StringComparison.Ordinal));
            if (field is null || field.Type != AnalyticsFieldType.Boolean)
            {
                throw new ArgumentException($"The deleted marker '{mapping.DeletedField}' of '{mapping.Table}' must be a Boolean field of the dataset.", nameof(mapping));
            }
        }
    }
}
