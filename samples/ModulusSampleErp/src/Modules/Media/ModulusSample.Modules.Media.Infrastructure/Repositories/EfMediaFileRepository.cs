using Microsoft.EntityFrameworkCore;
using Modulus.EntityFrameworkCore;
using ModulusSample.Modules.Media.Infrastructure.Database;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Enums;
using ModulusSample.Modules.Media.Domain.Repositories;

namespace ModulusSample.Modules.Media.Infrastructure.Repositories;

public sealed class EfMediaFileRepository : EfRepository<MediaFile>, IMediaFileRepository
{
    private readonly MediaDbContext _mediaDbContext;

    public EfMediaFileRepository(MediaDbContext mediaDbContext, IServiceProvider sp) : base(sp)
    {
        _mediaDbContext = mediaDbContext;
    }

    public async Task<IReadOnlyList<MediaFile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFiles
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<MediaFile?> GetByStoragePathAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFiles
            .FirstOrDefaultAsync(f => f.StoragePath == storagePath, cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFile>> GetByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFiles
            .Where(f => f.FolderId == folderId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFile>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFiles
            .Where(f => f.TenantId == tenantId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFile>> GetByTypeAsync(MediaFileType fileType, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFiles
            .Where(f => f.FileType == fileType)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFile>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var searchTerm = query.ToLowerInvariant();

        return await _mediaDbContext.MediaFiles
            .Where(f =>
                f.FileName.ToLower().Contains(searchTerm) ||
                f.OriginalFileName.ToLower().Contains(searchTerm) ||
                (f.AltText != null && f.AltText.ToLower().Contains(searchTerm)) ||
                (f.Description != null && f.Description.ToLower().Contains(searchTerm)))
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
