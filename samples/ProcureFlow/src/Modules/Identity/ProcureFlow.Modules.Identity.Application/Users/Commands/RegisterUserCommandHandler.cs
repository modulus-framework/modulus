using Modulus.EntityFrameworkCore.Abstractions;
using ProcureFlow.Modules.Identity.Application.Abstractions.Identity;
using ProcureFlow.Modules.Identity.Application.Users.Dtos;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using DomainUserType = ProcureFlow.Modules.Identity.Domain.Enums.UserType;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

internal sealed class RegisterUserCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IEmailVerificationTokenService emailVerificationTokenService,
    IPasswordHasher passwordHasher,
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
            await unitOfWork.CommitAsync(cancellationToken);

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

            // The UserRegistered integration event flows transactionally via the
            // outbox: User.Create raised UserCreatedDomainEvent (an IIntegrationEvent),
            // which ModuleDbContext enqueued before committing.
            logger.LogInformation(
                "Registered user {UserId}; UserRegistered.v1 enqueued via outbox",
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
