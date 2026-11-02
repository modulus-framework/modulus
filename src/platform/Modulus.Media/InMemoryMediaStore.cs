namespace Modulus.Media;

using System.Collections.Concurrent;

/// <summary>
/// Metadata held in memory. Suits a single node, development and tests. Production needs a durable store (an EF Core module store, to be
/// added); the table would be tenant-owned and carry the tenant filter.
/// </summary>
public sealed class InMemoryMediaStore : IMediaStore
{
    private readonly ConcurrentDictionary<Guid, MediaItem> _items = new();

    public Task AddAsync(MediaItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_items.TryAdd(item.Id, item))
            throw new InvalidOperationException($"Media {item.Id:N} already exists.");
        return Task.CompletedTask;
    }

    public Task<MediaItem?> GetAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(id, out var item) && item.TenantId == tenantId ? item : null);

    public Task<MediaPage> QueryAsync(Guid? tenantId, MediaQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matching = _items.Values
            .Where(i => i.TenantId == tenantId)
            .Where(i => query.Kind is null || i.Kind == query.Kind)
            .Where(i => string.IsNullOrWhiteSpace(query.Search) || i.FileName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.UploadedAt)
            .ThenBy(i => i.Id)
            .ToList();

        var pageSize = Math.Clamp(query.PageSize, 1, MediaQuery.MaxPageSize);
        var page = Math.Max(1, query.Page);
        var items = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new MediaPage(items, matching.Count));
    }

    public Task<bool> RemoveAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
    {
        if (_items.TryGetValue(id, out var item) && item.TenantId == tenantId)
            return Task.FromResult(_items.TryRemove(id, out _));
        return Task.FromResult(false);
    }
}
