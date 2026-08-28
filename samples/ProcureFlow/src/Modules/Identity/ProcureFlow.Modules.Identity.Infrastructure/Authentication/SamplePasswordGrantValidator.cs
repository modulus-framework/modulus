using Modulus.Identity.Abstractions;
using ProcureFlow.Modules.Identity.Application.Abstractions.Identity;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Modules.Identity.Infrastructure.Authentication;

internal sealed class SamplePasswordGrantValidator(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    ILogger<SamplePasswordGrantValidator> logger)
    : IPasswordGrantCredentialValidator
{
    public async Task<PasswordGrantResult> ValidateAsync(
        string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return PasswordGrantResult.Denied();

        var userName = UserName.Create(username);
        var user = await userRepository.GetByUserNameAsync(userName, ct);

        if (user is null)
        {
            var emailResult = Email.Create(username);
            if (emailResult.IsSuccess)
            {
                user = await userRepository.GetByEmailAsync(emailResult.Value, ct);
            }
        }

        if (user is null)
        {
            logger.LogWarning("Password grant: no user found for {Username}", username);
            return PasswordGrantResult.Denied();
        }

        if (user.IsDeleted || user.Status == Domain.Enums.UserStatus.Suspended || user.Status == Domain.Enums.UserStatus.Deleted)
        {
            logger.LogWarning("Password grant: user {UserId} is not active ({Status})", user.Id.Value, user.Status);
            return PasswordGrantResult.Denied("account_disabled");
        }

        if (user.PasswordHash == null || !passwordHasher.Verify(password, user.PasswordHash))
        {
            logger.LogWarning("Password grant: invalid password for user {UserId}", user.Id.Value);
            return PasswordGrantResult.Denied();
        }

        var roles = await roleRepository.GetByUserIdAsync(user.Id, ct);
        var permissions = await userRepository.GetUserPermissionCodesAsync(user.Id, ct);

        logger.LogInformation("Password grant: successful login for user {UserId}", user.Id.Value);

        return new PasswordGrantResult
        {
            Success = true,
            Subject = user.Id.Value.ToString(),
            UserName = user.FullName,
            Email = user.Email.Value,
            Roles = roles.Select(r => r.Name).ToList(),
        };
    }
}
