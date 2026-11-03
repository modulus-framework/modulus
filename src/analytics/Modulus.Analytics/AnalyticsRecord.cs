namespace Modulus.Analytics;

/// <summary>One row headed for an analytics provider.</summary>
/// <param name="EventId">
/// A stable id for the row. Redelivery of the same row carries the same id, so a provider can deduplicate on
/// (<paramref name="TenantId"/>, dataset, <paramref name="EventId"/>).
/// </param>
/// <param name="Dataset">The dataset name.</param>
/// <param name="SchemaVersion">The dataset version the values follow.</param>
/// <param name="TenantId">The tenant the row belongs to. Never empty: analytics output is tenant-scoped.</param>
/// <param name="OccurredAt">When the business fact happened.</param>
/// <param name="Values">The field values by field name.</param>
public sealed record AnalyticsRecord(
    Guid EventId,
    string Dataset,
    int SchemaVersion,
    Guid TenantId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Values);
