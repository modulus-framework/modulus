using ModulusSample.Shared.Domain;

namespace ModulusSample.Shared.Application.Authorization;

public interface IPermissionService
{
    Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId, CancellationToken ct = default);
}
