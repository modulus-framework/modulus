using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

internal sealed class UpdateProfileCommandHandler(
    IUserRepository userRepository,
    ICacheService cacheService,
    IUnitOfWork unitOfWork,
    ILogger<UpdateProfileCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<UpdateProfileCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateProfileCommand request,
        CancellationToken cancellationToken)
    {
        var userId = UserId.Create(request.UserId);

        User? user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.User.NotFound);
        }

        PhoneNumber? phoneNumber = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            Result<PhoneNumber> phoneResult = PhoneNumber.Create(request.PhoneNumber);
            if (phoneResult.IsFailure)
            {
                return Result.Failure(phoneResult.Error);
            }

            phoneNumber = phoneResult.Value;
        }

        user.UpdateProfile(
            request.FirstName,
            request.LastName,
            phoneNumber,
            request.ProfileImageUrl);

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.User.UserProfile(request.UserId), cancellationToken);

        logger.LogInformation("User profile updated - UserId: {UserId}", request.UserId);

        return Result.Success();
    }
}
