using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Repositories;

public interface IVirtualFileRepository
{
    Task<VirtualFile?> GetByIdAsync(VirtualFileId id, Guid tenantId, CancellationToken ct = default);
    Task<VirtualFile?> GetByNameAsync(string name, VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<VirtualFile>> GetByFolderAsync(VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default);
    Task<PagedResult<VirtualFile>> GetPagedAsync(VirtualFolderId folderId, Guid tenantId, string? searchTerm, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<long> CountInFolderAsync(VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string name, VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(VirtualFile file, CancellationToken ct = default);
    Task UpdateAsync(VirtualFile file, CancellationToken ct = default);
    Task DeleteAsync(VirtualFile file, CancellationToken ct = default);
}
