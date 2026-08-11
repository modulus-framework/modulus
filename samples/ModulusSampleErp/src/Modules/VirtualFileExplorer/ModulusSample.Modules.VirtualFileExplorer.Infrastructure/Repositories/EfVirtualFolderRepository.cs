using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Repositories;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Database;

namespace ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Repositories;

public sealed class EfVirtualFolderRepository(VirtualFileExplorerDbContext context) : IVirtualFolderRepository
{
    public async Task<VirtualFolder?> GetByIdAsync(VirtualFolderId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFolders
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<VirtualFolder>> GetByParentAsync(VirtualFolderId? parentFolderId, Guid tenantId, CancellationToken ct = default)
    {
        IQueryable<VirtualFolder> query = context.VirtualFolders.Where(f => f.TenantId == tenantId);

        if (parentFolderId.HasValue)
        {
            query = query.Where(f => f.ParentFolderId == parentFolderId.Value);
        }
        else
        {
            query = query.Where(f => f.ParentFolderId == null);
        }

        return await query.OrderBy(f => f.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VirtualFolder>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFolders
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(string name, VirtualFolderId? parentFolderId, Guid tenantId, CancellationToken ct = default)
    {
        IQueryable<VirtualFolder> query = context.VirtualFolders
            .Where(f => f.TenantId == tenantId && f.Name == name.Trim());

        if (parentFolderId.HasValue)
        {
            query = query.Where(f => f.ParentFolderId == parentFolderId.Value);
        }
        else
        {
            query = query.Where(f => f.ParentFolderId == null);
        }

        return await query.AnyAsync(ct);
    }

    public async Task AddAsync(VirtualFolder folder, CancellationToken ct = default)
    {
        await context.VirtualFolders.AddAsync(folder, ct);
    }

    public async Task UpdateAsync(VirtualFolder folder, CancellationToken ct = default)
    {
        context.VirtualFolders.Update(folder);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(VirtualFolder folder, CancellationToken ct = default)
    {
        context.VirtualFolders.Remove(folder);
        await Task.CompletedTask;
    }
}
