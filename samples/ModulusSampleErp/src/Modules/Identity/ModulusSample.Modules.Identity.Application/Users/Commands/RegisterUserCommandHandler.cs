using ModulusSample.Modules.Identity.Application.Abstractions.Data;
using ModulusSample.Modules.Identity.Application.Abstractions.Identity;
using ModulusSample.Modules.Identity.Application.IntegrationEvents;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using Modulus.Events.Abstractions;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using DomainUserType = ModulusSample.Modules.Identity.Domain.Enums.UserType;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

internal sealed class RegisterUserCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IEmailVerificationTokenService emailVerificationTokenService,
    IPasswordHasher passwordHasher,
    IModuleBus moduleBus,
    IUnitOfWork unitOfWork,
    ILogger<RegisterUserCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<RegisterUserCommand, Result<RegisterUserResponse>>
{
    public async Task<Result<RegisterUserResponse>> HandleAsync(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        // Validate email
        Result<Email> emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<RegisterUserResponse>(emailResult.Error);
        }

        // Check uniqueness
        if (await userRepository.ExistsByEmailAsync(emailResult.Value, cancellationToken))
        {
            return Result.Failure<RegisterUserResponse>(IdentityErrors.User.EmailAlreadyExists);
        }

        if (await userRepository.ExistsByUserNameAsync(UserName.Create(request.UserName), cancellationToken))
        {
            return Result.Failure<RegisterUserResponse>(IdentityErrors.User.UserNameAlreadyExists);
        }

        // Validate phone
        PhoneNumber? phoneNumber = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            Result<PhoneNumber> phoneResult = PhoneNumber.Create(request.PhoneNumber);
            if (phoneResult.IsFailure)
            {
                return Result.Failure<RegisterUserResponse>(phoneResult.Error);
            }

            phoneNumber = phoneResult.Value;
        }

        // Assign default role
        string roleName = "User";
        Role? role = await roleRepository.GetByNameAsync(roleName, cancellationToken);

        // Create user locally
        User user = User.Create(
            UserId.Create(),
            emailResult.Value,
            UserName.Create(request.UserName),
            request.FirstName,
            request.LastName,
            DomainUserType.User);

        user.PasswordHash = passwordHasher.Hash(request.Password);

        if (phoneNumber is not null)
        {
            user.UpdateProfile(request.FirstName, request.LastName, phoneNumber, null);
        }

        // Assign role
        if (role is not null)
        {
            user.AddRole(role.Id);
        }

        try
        {
            await userRepository.AddAsync(user, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Generate email verification token
            Result<string> tokenResult = await emailVerificationTokenService.GenerateTokenAsync(
                user.Id.Value,
                cancellationToken);

            if (tokenResult.IsFailure)
            {
                logger.LogError(
                    "Failed to generate email verification token for user {UserId}: {Error}",
                    user.Id.Value,
                    tokenResult.Error.Message);
            }
            else
            {
                logger.LogInformation(
                    "Generated email verification token for user: {UserId}",
                    user.Id.Value);
            }

            // Publish integration event for notification module
            string integrationEventType = user.UserType switch
            {
                DomainUserType.Admin => nameof(DomainUserType.Admin),
                DomainUserType.User => nameof(DomainUserType.User),
                _ => nameof(DomainUserType.User)
            };

            UserRegisteredIntegrationEvent integrationEvent = new(
                user.Id.Value,
                user.Email.Value,
                integrationEventType);

            await moduleBus.PublishAsync(integrationEvent, cancellationToken);

            logger.LogInformation(
                "Published UserRegisteredIntegrationEvent for user: {UserId}",
                user.Id.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save user to database");

            return Result.Failure<RegisterUserResponse>(
                Error.Failure("User.CreationFailed", "Failed to create user in database"));
        }

        return Result.Success(new RegisterUserResponse(
            user!.Id.Value,
            request.Email,
            request.UserName,
            user.Status.ToString(),
            "Registration successful. Please verify your email."));
    }
}
