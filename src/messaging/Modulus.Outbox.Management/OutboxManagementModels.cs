namespace Modulus.Outbox.Management;

/// <summary>
/// Response for a dead-lettered outbox message in list operations.
/// Includes minimal details for scanning and filtering.
/// </summary>
public sealed record OutboxDeadLetterListItem(
    Guid Id,
    string MessageType,
    string ModuleName,
    Guid TenantId,
    DateTime CreatedAt,
    int RetryCount,
    string? Error);

/// <summary>
/// Full details of a dead-lettered outbox message for inspection.
/// Includes the complete payload for replay or debugging.
/// </summary>
public sealed record OutboxDeadLetterDetail(
    Guid Id,
    string MessageType,
    string Payload,
    string ModuleName,
    Guid TenantId,
    DateTime CreatedAt,
    int RetryCount,
    string? Error,
    string? CorrelationId,
    string? CausationId);

/// <summary>Request to replay one or more dead-lettered messages.</summary>
public sealed record OutboxReplayRequest(Guid[] MessageIds);

/// <summary>Response from a replay operation.</summary>
public sealed record OutboxReplayResponse(
    int ReplayedCount,
    int NotFoundCount,
    int FailedCount,
    IReadOnlyList<string> Failures);

/// <summary>Response from a purge operation.</summary>
public sealed record OutboxPurgeResponse(int PurgedCount);

/// <summary>Pagination response for dead-letter listings.</summary>
public sealed record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize);
