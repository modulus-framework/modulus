namespace Modulus.AuditLogging;

using Modulus.Core.Abstractions.Common;

/// <summary>
/// Dependency-free <see cref="IAuditLogStore"/> default. Bounded
/// (<see cref="MaxEntries"/> newest rows kept) and single-node: replace with
/// an EF-backed store for durable, cross-instance audit history.
/// </summary>
public sealed class InMemoryAuditLogStore : IAuditLogStore
{
    public const int MaxEntries = 10_000;
    public const int MaxPageSize = 200;

    private readonly Lock _gate = new();
    private readonly List<AuditLogEntry> _entries = [];

    public Task AppendAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        return Task.CompletedTask;
    }

    public Task<PagedList<AuditLogEntry>> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        List<AuditLogEntry> snapshot;
        lock (_gate)
            snapshot = _entries.ToList();

        var filtered = snapshot
            .Where(e => query.From is null || e.OccurredAt >= query.From)
            .Where(e => query.To is null || e.OccurredAt <= query.To)
            .Where(e => query.TenantId is null || e.TenantId == query.TenantId)
            .Where(e => query.UserId is null || e.UserId == query.UserId)
            .Where(e => query.Action is null || e.Action.Contains(query.Action, StringComparison.OrdinalIgnoreCase))
            .Where(e => query.Resource is null || (e.Resource is not null && e.Resource.Contains(query.Resource, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.OccurredAt)
            .ToList();

        return Task.FromResult(new PagedList<AuditLogEntry>
        {
            Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = filtered.Count,
            Page = page,
            PageSize = pageSize,
        });
    }

    public Task<AuditLogEntry?> GetOrNullAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));
    }
}
