namespace Modulus.AuditLogging;

/// <summary>Filter + paging for <see cref="IAuditLogStore.QueryAsync"/>.</summary>
public sealed record AuditLogQuery
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? UserId { get; init; }

    /// <summary>Substring match on <see cref="AuditLogEntry.Action"/>.</summary>
    public string? Action { get; init; }

    /// <summary>Substring match on <see cref="AuditLogEntry.Resource"/>.</summary>
    public string? Resource { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
