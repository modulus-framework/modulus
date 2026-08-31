using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

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
