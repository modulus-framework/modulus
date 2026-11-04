namespace Modulus.Analytics.Messaging;

using Modulus.Events.Abstractions;

/// <summary>
/// One analytics record, as published by <see cref="MessagingAnalyticsSink"/>. <c>EventId</c> is the record id and <c>OccurredAt</c> the
/// business time. The values travel as JSON text, so the payload holds primitives only and any consumer can read it without this package's
/// types. Consumers deduplicate on (<see cref="TenantId"/>, <see cref="Dataset"/>, <c>EventId</c>) and keep the highest <see cref="Revision"/>.
/// </summary>
/// <param name="Dataset">The dataset name.</param>
/// <param name="SchemaVersion">The dataset version the values follow.</param>
/// <param name="TenantId">The tenant the record belongs to.</param>
/// <param name="Revision">The record revision (see <c>AnalyticsRecord.Revision</c>).</param>
/// <param name="ValuesJson">The masked field values as a JSON object.</param>
[IntegrationEvent<AnalyticsMessagingArea>]
public sealed record AnalyticsRecordPublished(
    string Dataset,
    int SchemaVersion,
    Guid TenantId,
    long Revision,
    string ValuesJson) : IntegrationEventBase;
