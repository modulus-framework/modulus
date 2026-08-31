using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Abstractions.Identity;

public interface IUserIdentityService
{
    Task<Result<User>> ProvisionUserAsync(
        string email,
        string userName,
        string firstName,
        string lastName,
        bool emailVerified,
        CancellationToken ct = default);

    Task<Result<User>> ResolveUserAsync(string externalUserId, CancellationToken ct = default);
}
