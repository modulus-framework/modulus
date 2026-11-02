namespace Modulus.Media;

/// <summary>
/// Metadata storage. Every method takes the company explicitly, so one company's rows are never returned for another's query, even when
/// the ambient context is missing (background jobs, tests).
/// </summary>
public interface IMediaStore
{
    Task AddAsync(MediaItem item, CancellationToken ct = default);

    Task<MediaItem?> GetAsync(Guid? tenantId, Guid id, CancellationToken ct = default);

    Task<MediaPage> QueryAsync(Guid? tenantId, MediaQuery query, CancellationToken ct = default);

    Task<bool> RemoveAsync(Guid? tenantId, Guid id, CancellationToken ct = default);
}
