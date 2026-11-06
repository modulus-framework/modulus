namespace Modulus.Analytics.Management;

/// <summary>
/// A dead-lettered analytics record as management sees it. It carries no record values on purpose: queued rows hold values before
/// masking, so restricted fields must not leave through an operations API.
/// </summary>
/// <param name="Id">The queue row id.</param>
/// <param name="TenantId">The record's tenant.</param>
/// <param name="EventId">The record id (the integration event id).</param>
/// <param name="Dataset">The dataset name.</param>
/// <param name="SchemaVersion">The dataset version.</param>
/// <param name="OccurredAt">When the business fact happened (UTC).</param>
/// <param name="CreatedAt">When the record was queued (UTC).</param>
/// <param name="DeadLetteredAt">When it was given up on (UTC).</param>
/// <param name="AttemptCount">Failed attempts before it was given up on.</param>
/// <param name="LastError">Why the last attempt failed.</param>
public sealed record AnalyticsDeadLetter(
    Guid Id,
    Guid TenantId,
    Guid EventId,
    string Dataset,
    int SchemaVersion,
    DateTime OccurredAt,
    DateTime CreatedAt,
    DateTime? DeadLetteredAt,
    int AttemptCount,
    string? LastError);

/// <summary>How the delivery queue stands.</summary>
/// <param name="Pending">Records waiting for a first attempt or a retry.</param>
/// <param name="Delivered">Delivered records not yet purged.</param>
/// <param name="DeadLettered">Records given up on.</param>
/// <param name="OldestPendingAt">When the oldest pending record was queued (UTC), or <see langword="null"/> when none is pending.</param>
/// <param name="OldestPendingAgeSeconds">How long the oldest pending record has waited. A rising value means delivery is falling behind.</param>
public sealed record AnalyticsQueueStatus(
    int Pending,
    int Delivered,
    int DeadLettered,
    DateTime? OldestPendingAt,
    double? OldestPendingAgeSeconds);

/// <summary>Request to replay dead-lettered records.</summary>
/// <param name="Ids">The queue row ids, at most 1000.</param>
public sealed record AnalyticsReplayRequest(Guid[] Ids);

/// <summary>Response from a replay.</summary>
/// <param name="Replayed">Rows put back in the queue.</param>
/// <param name="NotFound">Ids that are not dead-lettered rows (unknown, or not failed).</param>
public sealed record AnalyticsReplayResponse(int Replayed, int NotFound);

/// <summary>Response from a purge.</summary>
/// <param name="Purged">Dead letters deleted.</param>
public sealed record AnalyticsPurgeResponse(int Purged);

/// <summary>A page of results.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">This page.</param>
/// <param name="Total">All matches.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">Items per page.</param>
public sealed record AnalyticsPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Request to start backfill runs.</summary>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="TenantIds">The tenants to cover (at most 1000), or <see langword="null"/> for every active tenant.</param>
/// <param name="Restart">Whether to restart runs that already finished, failed or were cancelled.</param>
/// <param name="Incremental">
/// <see langword="false"/> (the default) reads the whole table once. <see langword="true"/> starts a run that keeps the dataset up to date by reading what changed,
/// and polls until it is cancelled.
/// </param>
public sealed record AnalyticsBackfillRequest(string Dataset, int Version, Guid[]? TenantIds = null, bool Restart = false, bool Incremental = false);

/// <summary>
/// A backfill run as management sees it. The cursor is left out: it is a position in the source table and means nothing to an operator.
/// </summary>
/// <param name="Id">The run id.</param>
/// <param name="Dataset">The dataset name.</param>
/// <param name="SchemaVersion">The dataset version.</param>
/// <param name="TenantId">The tenant being read.</param>
/// <param name="Kind">Backfill or Incremental.</param>
/// <param name="Status">Pending, Running, Completed, Failed or Cancelled.</param>
/// <param name="RowsQueued">Records queued so far.</param>
/// <param name="AttemptCount">Consecutive failed cycles.</param>
/// <param name="CreatedAt">When the run was created (UTC).</param>
/// <param name="UpdatedAt">When it last made progress (UTC).</param>
/// <param name="CompletedAt">When it completed (UTC).</param>
/// <param name="NextAttemptAt">When the next attempt is due after a failure (UTC).</param>
/// <param name="LastError">What went wrong last.</param>
/// <param name="RequestedBy">Who started it.</param>
public sealed record AnalyticsBackfillRunInfo(
    Guid Id,
    string Dataset,
    int SchemaVersion,
    Guid TenantId,
    string Kind,
    string Status,
    long RowsQueued,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    DateTime? NextAttemptAt,
    string? LastError,
    string? RequestedBy);

/// <summary>Request to reconcile a dataset version against its sinks.</summary>
/// <param name="Dataset">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="TenantIds">The tenants to check (at most 1000), or <see langword="null"/> for every active tenant.</param>
public sealed record AnalyticsReconcileRequest(string Dataset, int Version, Guid[]? TenantIds = null);

/// <summary>A data-quality check of one dataset version, for some or all companies.</summary>
public sealed record AnalyticsQualityRequest(string Dataset, int Version, Guid[]? TenantIds = null);

/// <summary>A registered dataset version, as the catalog shows it: its owner, rules and fields. It never carries record values.</summary>
/// <param name="Name">The dataset name.</param>
/// <param name="Version">The dataset version.</param>
/// <param name="Owner">The team that owns the dataset.</param>
/// <param name="Description">What the dataset holds, if the owner wrote it down.</param>
/// <param name="FreshnessSla">How far behind the source the dataset may fall, or <see langword="null"/> when there is no promise.</param>
/// <param name="RetentionDays">How long the warehouse keeps the rows, or <see langword="null"/> for indefinitely.</param>
/// <param name="RequiredFeature">The plan feature a company needs to receive the dataset, or <see langword="null"/> when every company does.</param>
/// <param name="Fields">The fields, in declaration order.</param>
public sealed record AnalyticsDatasetInfo(
    string Name,
    int Version,
    string Owner,
    string? Description,
    TimeSpan? FreshnessSla,
    int? RetentionDays,
    string? RequiredFeature,
    IReadOnlyList<AnalyticsFieldInfo> Fields);

/// <summary>One field of a catalogued dataset.</summary>
/// <param name="Name">The field name.</param>
/// <param name="Type">The logical type.</param>
/// <param name="IsNullable">Whether the value may be absent.</param>
/// <param name="Classification">How sensitive the field is.</param>
/// <param name="Masking">What is done to the value before delivery.</param>
/// <param name="MaxNullRate">The largest share of nulls allowed by the data-quality rule, or <see langword="null"/> when there is no rule.</param>
public sealed record AnalyticsFieldInfo(
    string Name,
    string Type,
    bool IsNullable,
    string Classification,
    string Masking,
    double? MaxNullRate);
