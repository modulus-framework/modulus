namespace Modulus.AuditLogging;

using Modulus.Core.Abstractions.Common;

/// <summary>
/// Append/query store for <see cref="AuditLogEntry"/> rows.
/// Rows are tenant-tagged at write time; callers pass the tenant filter
/// explicitly so host operators can query across tenants.
/// </summary>
public interface IAuditLogStore
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default);

    Task<PagedList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default);

    Task<AuditLogEntry?> GetOrNullAsync(Guid id, CancellationToken ct = default);
}
