namespace Modulus.Media.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The durable <see cref="IMediaStore"/>. Every query names the company, so a row of another company is never returned, even when the
/// ambient context is missing.
/// </summary>
public sealed class EfMediaStore(ModulusMediaDbContext db) : IMediaStore
{
    public async Task AddAsync(MediaItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        db.MediaItems.Add(MediaRow.From(item));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<MediaItem?> GetAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
    {
        var row = await db.MediaItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        return row?.ToItem();
    }

    public async Task<MediaPage> QueryAsync(Guid? tenantId, MediaQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = db.MediaItems.AsNoTracking().Where(m => m.TenantId == tenantId);

        if (query.Kind is { } kind)
            rows = rows.Where(m => m.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Lower-cased on both sides: providers differ in whether LIKE and instr ignore case, and the in-memory store ignores it.
            var term = query.Search.ToLowerInvariant();
            rows = rows.Where(m => m.FileName.ToLower().Contains(term));
        }

        var total = await rows.CountAsync(ct).ConfigureAwait(false);
        var pageSize = Math.Clamp(query.PageSize, 1, MediaQuery.MaxPageSize);
        var page = Math.Max(1, query.Page);

        var items = await rows
            .OrderByDescending(m => m.UploadedAt)
            .ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new MediaPage(items.Select(r => r.ToItem()).ToList(), total);
    }

    public async Task<bool> RemoveAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
    {
        var deleted = await db.MediaItems
            .Where(m => m.Id == id && m.TenantId == tenantId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        return deleted > 0;
    }
}
