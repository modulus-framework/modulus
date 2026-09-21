namespace Modulus.AuditLogging;

/// <summary>
/// One queryable audit row: "who did what to which resource, when".
/// Complements the field-level <c>EntityChange</c> history (EF Core) with an
/// action-level log that management UIs can list and filter.
/// </summary>
public sealed record AuditLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? UserId { get; init; }

    public string? UserName { get; init; }

    /// <summary>What happened, e.g. <c>Grant</c>, <c>Login</c>, <c>OrderPlaced</c>.</summary>
    public required string Action { get; init; }

    /// <summary>What kind of thing was acted on, e.g. <c>Permission</c>, <c>Order</c>.</summary>
    public string? Resource { get; init; }

    public string? ResourceId { get; init; }

    public string? Detail { get; init; }
}
