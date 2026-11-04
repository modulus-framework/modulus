namespace Modulus.Analytics.OpenLineage;

using System.Text.Json.Serialization;
using Modulus.Analytics;

/// <summary>
/// The OpenLineage run event (spec 2-0-2), as the emitter writes it. Only the fields a backfill run needs are present; facets carry the error of a failed run.
/// </summary>
internal sealed record OpenLineageRunEvent(
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("eventTime")] DateTimeOffset EventTime,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("schemaURL")] string SchemaUrl,
    [property: JsonPropertyName("run")] OpenLineageRun Run,
    [property: JsonPropertyName("job")] OpenLineageJob Job,
    [property: JsonPropertyName("inputs")] IReadOnlyList<OpenLineageDataset> Inputs,
    [property: JsonPropertyName("outputs")] IReadOnlyList<OpenLineageDataset> Outputs);

internal sealed record OpenLineageRun(
    [property: JsonPropertyName("runId")] string RunId,
    [property: JsonPropertyName("facets"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object>? Facets);

internal sealed record OpenLineageJob(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("name")] string Name);

internal sealed record OpenLineageDataset(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("name")] string Name);

/// <summary>The error facet of a failed run.</summary>
internal sealed record OpenLineageErrorFacet(
    [property: JsonPropertyName("_producer")] string Producer,
    [property: JsonPropertyName("_schemaURL")] string SchemaUrl,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("programmingLanguage")] string ProgrammingLanguage);

/// <summary>Builds the OpenLineage run events of backfill runs.</summary>
internal static class OpenLineageEvents
{
    /// <summary>The schema of the run event.</summary>
    public const string RunEventSchema = "https://openlineage.io/spec/2-0-2/OpenLineage.json#/$defs/RunEvent";

    /// <summary>The schema of the error facet.</summary>
    public const string ErrorFacetSchema = "https://openlineage.io/spec/facets/1-0-0/ErrorMessageRunFacet.json";

    public static OpenLineageRunEvent Build(OpenLineageOptions options, AnalyticsRunLineage lineage, IEnumerable<string> sinkNames)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lineage);

        var facets = lineage.State == AnalyticsLineageState.Fail && !string.IsNullOrEmpty(lineage.Error)
            ? new Dictionary<string, object>
            {
                ["errorMessage"] = new OpenLineageErrorFacet(options.Producer, ErrorFacetSchema, lineage.Error, "C#"),
            }
            : null;

        var outputs = sinkNames
            .Select(sink => new OpenLineageDataset(options.WarehouseNamespace, $"{sink}/{lineage.Dataset}_v{lineage.Version}"))
            .ToArray();

        return new OpenLineageRunEvent(
            EventType: lineage.State switch
            {
                AnalyticsLineageState.Start => "START",
                AnalyticsLineageState.Complete => "COMPLETE",
                _ => "FAIL",
            },
            EventTime: lineage.OccurredAt,
            Producer: options.Producer,
            SchemaUrl: RunEventSchema,
            Run: new OpenLineageRun(lineage.RunId.ToString("D"), facets),
            Job: new OpenLineageJob(options.Namespace, $"analytics.backfill.{lineage.Dataset}_v{lineage.Version}"),
            Inputs: [new OpenLineageDataset(options.Namespace, lineage.Dataset)],
            Outputs: outputs);
    }
}
