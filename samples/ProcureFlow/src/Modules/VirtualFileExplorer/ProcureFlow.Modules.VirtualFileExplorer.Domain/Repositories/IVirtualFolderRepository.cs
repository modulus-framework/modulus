using ProcureFlow.Modules.VirtualFileExplorer.Domain.Entities;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

namespace ProcureFlow.Modules.VirtualFileExplorer.Domain.Repositories;

public interface IVirtualFolderRepository
{
    Task<VirtualFolder?> GetByIdAsync(VirtualFolderId id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<VirtualFolder>> GetByParentAsync(VirtualFolderId? parentFolderId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<VirtualFolder>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string name, VirtualFolderId? parentFolderId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(VirtualFolder folder, CancellationToken ct = default);
    Task UpdateAsync(VirtualFolder folder, CancellationToken ct = default);
    Task DeleteAsync(VirtualFolder folder, CancellationToken ct = default);
}
