using Microsoft.EntityFrameworkCore;
using Modulus.EntityFrameworkCore;
using ModulusSample.Modules.Media.Infrastructure.Database;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Repositories;

namespace ModulusSample.Modules.Media.Infrastructure.Repositories;

public sealed class EfMediaFolderRepository : EfRepository<MediaFolder>, IMediaFolderRepository
{
    private readonly MediaDbContext _mediaDbContext;

    public EfMediaFolderRepository(MediaDbContext mediaDbContext, IServiceProvider sp) : base(sp)
    {
        _mediaDbContext = mediaDbContext;
    }

    public async Task<MediaFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFolders
            .FirstOrDefaultAsync(f => f.Path == path, cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFolder>> GetByParentFolderIdAsync(Guid? parentFolderId, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFolders
            .Where(f => f.ParentFolderId == parentFolderId)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaFolder>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _mediaDbContext.MediaFolders
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<string> GenerateUniquePathAsync(string name, Guid? parentFolderId, CancellationToken cancellationToken = default)
    {
        var sanitizedName = SanitizeFolderName(name);
        var basePath = parentFolderId.HasValue
            ? (await GetByIdAsync(parentFolderId.Value, cancellationToken))?.Path ?? "root"
            : "root";

        var path = $"{basePath}/{sanitizedName}";
        var counter = 1;
        var originalPath = path;

        while (await GetByPathAsync(path, cancellationToken) != null)
        {
            path = $"{originalPath}-{counter}";
            counter++;
        }

        return path;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(Path.GetInvalidPathChars())
            .Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
            .Distinct()
            .ToArray();

        var sanitizedName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitizedName) ? "unnamed" : sanitizedName.Trim();
    }
}
