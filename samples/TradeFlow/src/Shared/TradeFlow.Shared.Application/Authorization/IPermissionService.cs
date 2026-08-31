using TradeFlow.Shared.Domain;

namespace TradeFlow.Shared.Application.Authorization;

public interface IPermissionService
{
    Task<Result<PermissionsResponse>> GetUserPermissionsAsync(string identityId, CancellationToken ct = default);
}
