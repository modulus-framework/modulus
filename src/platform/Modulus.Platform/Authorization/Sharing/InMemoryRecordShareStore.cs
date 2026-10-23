using System.Collections.Concurrent;

namespace Modulus.Authorization.Sharing;

/// <summary>Per-process <see cref="IRecordShareStore"/> for development and tests. Shares are lost on restart; production registers a durable store.</summary>
public sealed class InMemoryRecordShareStore : IRecordShareStore
{
    private readonly ConcurrentDictionary<Guid, RecordShare> _shares = new();

    public Task AddAsync(RecordShare share, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(share);
        return _shares.TryAdd(share.Id, share)
            ? Task.CompletedTask
            : throw new InvalidOperationException($"A share with id '{share.Id}' already exists.");
    }

    public Task<RecordShare?> FindAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_shares.TryGetValue(id, out var share) ? share : null);

    public Task<IReadOnlyList<RecordShare>> ForRecordAsync(Guid? tenantId, string resourceType, string resourceId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecordShare>>(_shares.Values
            .Where(s => s.TenantId == tenantId && s.ResourceType == resourceType && s.ResourceId == resourceId)
            .ToList());

    public Task UpdateAsync(RecordShare share, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(share);
        _shares[share.Id] = share;
        return Task.CompletedTask;
    }
}
