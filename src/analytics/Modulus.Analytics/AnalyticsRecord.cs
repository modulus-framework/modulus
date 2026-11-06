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
/// <param name="Revision">
/// Which version of the record this is, as a number that grows with each change (a modification time in UTC ticks, for example). The queue keeps one row
/// per (tenant, dataset, record id, revision): the same revision twice is a redelivery and is skipped, a higher one is an update and is queued. Use
/// 0 when a record never changes.
/// </param>
public sealed record AnalyticsRecord(
    Guid EventId,
    string Dataset,
    int SchemaVersion,
    Guid TenantId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, object?> Values,
    long Revision = 0);
