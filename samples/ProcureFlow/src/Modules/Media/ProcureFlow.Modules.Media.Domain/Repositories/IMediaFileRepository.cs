namespace ModulusSample.Modules.Media.Domain.Repositories;

using Modulus.Data.Abstractions;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Enums;

public interface IMediaFileRepository : IRepository<MediaFile>
{
    Task<IReadOnlyList<MediaFile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MediaFile?> GetByStoragePathAsync(string storagePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaFile>> GetByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaFile>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaFile>> GetByTypeAsync(MediaFileType fileType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaFile>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
