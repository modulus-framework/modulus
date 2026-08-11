namespace ModulusSample.Modules.Media.Domain.Repositories;

using Modulus.Data.Abstractions;
using ModulusSample.Modules.Media.Domain.Entities;

public interface IMediaFolderRepository : IRepository<MediaFolder>
{
    Task<MediaFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaFolder>> GetByParentFolderIdAsync(Guid? parentFolderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaFolder>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<string> GenerateUniquePathAsync(string name, Guid? parentFolderId, CancellationToken cancellationToken = default);
}
