using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Abstractions.Identity;

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
