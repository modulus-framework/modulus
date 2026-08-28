using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.Entities;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.Repositories;
using ProcureFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ProcureFlow.Modules.VirtualFileExplorer.Infrastructure.Database;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.VirtualFileExplorer.Infrastructure.Repositories;

public sealed class EfVirtualFileRepository(VirtualFileExplorerDbContext context) : IVirtualFileRepository
{
    public async Task<VirtualFile?> GetByIdAsync(VirtualFileId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFiles
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId, ct);
    }

    public async Task<VirtualFile?> GetByNameAsync(string name, VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFiles
            .FirstOrDefaultAsync(f => f.Name == name.Trim() && f.FolderId == folderId && f.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<VirtualFile>> GetByFolderAsync(VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFiles
            .Where(f => f.FolderId == folderId && f.TenantId == tenantId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<VirtualFile>> GetPagedAsync(
        VirtualFolderId folderId,
        Guid tenantId,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        IQueryable<VirtualFile> query = context.VirtualFiles
            .Where(f => f.FolderId == folderId && f.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(f => f.Name.Contains(searchTerm));
        }

        int totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(f => f.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<VirtualFile>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<long> CountInFolderAsync(VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFiles
            .LongCountAsync(f => f.FolderId == folderId && f.TenantId == tenantId, ct);
    }

    public async Task<bool> ExistsAsync(string name, VirtualFolderId folderId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.VirtualFiles
            .AnyAsync(f => f.Name == name.Trim() && f.FolderId == folderId && f.TenantId == tenantId, ct);
    }

    public async Task AddAsync(VirtualFile file, CancellationToken ct = default)
    {
        await context.VirtualFiles.AddAsync(file, ct);
    }

    public async Task UpdateAsync(VirtualFile file, CancellationToken ct = default)
    {
        context.VirtualFiles.Update(file);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(VirtualFile file, CancellationToken ct = default)
    {
        context.VirtualFiles.Remove(file);
        await Task.CompletedTask;
    }
}
