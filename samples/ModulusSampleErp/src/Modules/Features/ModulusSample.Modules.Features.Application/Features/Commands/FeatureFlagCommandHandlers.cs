using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Features.Application.Features.Dtos;
using ModulusSample.Modules.Features.Domain.Constants;
using ModulusSample.Modules.Features.Domain.Entities;
using ModulusSample.Modules.Features.Domain.Repositories;
using ModulusSample.Modules.Features.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Modules.Features.Application.Features.Commands;

public sealed class CreateFeatureFlagHandler(
    IFeatureFlagRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CreateFeatureFlagCommand, Result<CreateFeatureFlagResponse>>
{
    public async Task<Result<CreateFeatureFlagResponse>> HandleAsync(CreateFeatureFlagCommand request, CancellationToken ct)
    {
        Result<FeatureKey> keyResult = FeatureKey.Create(request.Key);
        if (keyResult.IsFailure)
        {
            return Result.Failure<CreateFeatureFlagResponse>(keyResult.Error);
        }

        if (await repository.ExistsByKeyAsync(keyResult.Value, request.TenantId, ct))
        {
            return Result.Failure<CreateFeatureFlagResponse>(FeatureErrors.DuplicateKey);
        }

        Result<FeatureFlag> featureResult = FeatureFlag.Create(
            FeatureFlagId.Create(),
            keyResult.Value,
            request.Name,
            request.Description,
            request.IsEnabled,
            request.TenantId,
            currentUser.UserId?.ToString());

        if (featureResult.IsFailure)
        {
            return Result.Failure<CreateFeatureFlagResponse>(featureResult.Error);
        }

        await repository.AddAsync(featureResult.Value, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new CreateFeatureFlagResponse(
            featureResult.Value.Id.Value,
            featureResult.Value.Key.Value,
            featureResult.Value.Name));
    }
}

public sealed class UpdateFeatureFlagHandler(
    IFeatureFlagRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<UpdateFeatureFlagCommand, Result<UpdateFeatureFlagResponse>>
{
    public async Task<Result<UpdateFeatureFlagResponse>> HandleAsync(UpdateFeatureFlagCommand request, CancellationToken ct)
    {
        FeatureFlag? feature = await repository.GetByIdAsync(FeatureFlagId.From(request.FeatureFlagId), ct);
        if (feature is null)
        {
            return Result.Failure<UpdateFeatureFlagResponse>(FeatureErrors.NotFound);
        }

        Result updateResult = feature.Update(request.Name, request.Description, currentUser.UserId?.ToString() ?? "system");
        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateFeatureFlagResponse>(updateResult.Error);
        }

        await repository.UpdateAsync(feature, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new UpdateFeatureFlagResponse(
            feature.Id.Value,
            feature.Key.Value,
            feature.Name,
            feature.IsEnabled,
            feature.LastModifiedAt));
    }
}

public sealed class ToggleFeatureFlagHandler(
    IFeatureFlagRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ToggleFeatureFlagCommand, Result<UpdateFeatureFlagResponse>>
{
    public async Task<Result<UpdateFeatureFlagResponse>> HandleAsync(ToggleFeatureFlagCommand request, CancellationToken ct)
    {
        FeatureFlag? feature = await repository.GetByIdAsync(FeatureFlagId.From(request.FeatureFlagId), ct);
        if (feature is null)
        {
            return Result.Failure<UpdateFeatureFlagResponse>(FeatureErrors.NotFound);
        }

        Result toggleResult = feature.Toggle(request.IsEnabled, currentUser.UserId?.ToString() ?? "system");
        if (toggleResult.IsFailure)
        {
            return Result.Failure<UpdateFeatureFlagResponse>(toggleResult.Error);
        }

        await repository.UpdateAsync(feature, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new UpdateFeatureFlagResponse(
            feature.Id.Value,
            feature.Key.Value,
            feature.Name,
            feature.IsEnabled,
            feature.LastModifiedAt));
    }
}

public sealed class DeleteFeatureFlagHandler(
    IFeatureFlagRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<DeleteFeatureFlagCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteFeatureFlagCommand request, CancellationToken ct)
    {
        FeatureFlag? feature = await repository.GetByIdAsync(FeatureFlagId.From(request.FeatureFlagId), ct);
        if (feature is null)
        {
            return Result.Failure(FeatureErrors.NotFound);
        }

        feature.Delete(currentUser.UserId?.ToString() ?? "system");
        await repository.DeleteAsync(feature, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success();
    }
}
