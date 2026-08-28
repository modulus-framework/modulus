using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Shared.Application.Authorization;

public interface IPermissionService
{
    Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId, CancellationToken ct = default);
}
