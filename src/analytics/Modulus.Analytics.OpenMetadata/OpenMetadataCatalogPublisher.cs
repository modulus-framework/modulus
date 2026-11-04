namespace Modulus.Analytics.OpenMetadata;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Registers each analytics dataset version as a table in OpenMetadata, with one column per field. The column type comes from the field's
/// declared type, and its description records the classification, masking and whether it may be null. Publishing creates the table or
/// updates it, so running it again after a change is safe.
/// </summary>
/// <remarks>
/// The table's database service, database and schema must already exist (see <see cref="OpenMetadataOptions"/>). Classification is written
/// as text, not as an OpenMetadata tag, because tag names depend on each organisation's taxonomy.
/// </remarks>
public sealed class OpenMetadataCatalogPublisher(HttpClient http, OpenMetadataOptions options)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The table name of a dataset version, <c>{name}_v{version}</c>.</summary>
    public static string TableNameFor(AnalyticsDatasetDescriptor dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return $"{dataset.Name}_v{dataset.Version}";
    }

    /// <summary>The OpenMetadata column type for a field type.</summary>
    public static string ColumnTypeFor(AnalyticsFieldType type) => type switch
    {
        AnalyticsFieldType.String => "VARCHAR",
        AnalyticsFieldType.Int64 => "BIGINT",
        AnalyticsFieldType.Decimal => "DECIMAL",
        AnalyticsFieldType.Double => "DOUBLE",
        AnalyticsFieldType.Boolean => "BOOLEAN",
        AnalyticsFieldType.Timestamp => "TIMESTAMP",
        AnalyticsFieldType.Date => "DATE",
        AnalyticsFieldType.Guid => "UUID",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown analytics field type."),
    };

    /// <summary>Creates or updates the table of one dataset version.</summary>
    /// <param name="dataset">The dataset version.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="HttpRequestException">The server refused the request.</exception>
    public async Task PublishAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var body = new CreateTableRequest(
            TableNameFor(dataset),
            $"{options.ServiceName}.{options.DatabaseName}.{options.SchemaName}",
            dataset.Description ?? $"Analytics dataset {dataset.Key}, owned by {dataset.Owner}.",
            "Regular",
            [.. dataset.Fields.Select(ToColumn)]);

        using var response = await http.PutAsJsonAsync("api/v1/tables", body, s_json, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"OpenMetadata could not register table '{body.Name}': HTTP {(int)response.StatusCode}. {(detail.Length > 300 ? detail[..300] : detail)}",
                null,
                response.StatusCode);
        }
    }

    /// <summary>Registers every dataset version in the catalog.</summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public async Task PublishAllAsync(IAnalyticsDatasetCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var dataset in catalog.All)
        {
            await PublishAsync(dataset, cancellationToken).ConfigureAwait(false);
        }
    }

    private static CreateColumnRequest ToColumn(AnalyticsFieldDescriptor field)
    {
        var notes = new List<string> { $"Classification: {field.Classification}.", $"Masking: {field.EffectiveMasking}." };
        notes.Add(field.IsNullable ? "May be null." : "Never null.");
        if (field.MaxNullRate is { } rate)
        {
            notes.Add($"At most {(rate * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture)}% null.");
        }

        return new CreateColumnRequest(field.Name, ColumnTypeFor(field.Type), string.Join(" ", notes));
    }

    private sealed record CreateTableRequest(string Name, string DatabaseSchema, string Description, string TableType, IReadOnlyList<CreateColumnRequest> Columns);

    private sealed record CreateColumnRequest(string Name, string DataType, string Description);
}
