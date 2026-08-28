using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Features.Dtos;
using ProcureFlow.Modules.Configuration.Domain.Constants;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Domain.Repositories;
using ProcureFlow.Modules.Configuration.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Features.Queries;

public sealed class GetAllFeatureFlagsHandler(
    IFeatureFlagRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetAllFeatureFlagsQuery, Result<PagedResult<FeatureFlagResponse>>>
{
    public async Task<Result<PagedResult<FeatureFlagResponse>>> HandleAsync(GetAllFeatureFlagsQuery request, CancellationToken ct)
    {
        var paged = await repository.GetPagedAsync(
            currentTenant.TenantId ?? Guid.Empty,
            null,
            request.IsEnabled,
            request.PageNumber,
            request.PageSize,
            ct);

        var responses = paged.Items.Select(f => new FeatureFlagResponse(
            f.Id.Value,
            f.Key.Value,
            f.Name,
            f.Description,
            f.IsEnabled,
            f.TenantId,
            f.CreatedAt,
            f.CreatedBy,
            f.LastModifiedAt,
            f.LastModifiedBy)).ToList();

        return Result.Success(new PagedResult<FeatureFlagResponse>(
            responses,
            paged.TotalCount,
            request.PageNumber,
            request.PageSize));
    }
}

public sealed class GetFeatureFlagByIdHandler(
    IFeatureFlagRepository repository) : IQueryHandler<GetFeatureFlagByIdQuery, Result<FeatureFlagResponse>>
{
    public async Task<Result<FeatureFlagResponse>> HandleAsync(GetFeatureFlagByIdQuery request, CancellationToken ct)
    {
        FeatureFlag? feature = await repository.GetByIdAsync(FeatureFlagId.From(request.FeatureFlagId), ct);
        if (feature is null)
        {
            return Result.Failure<FeatureFlagResponse>(FeatureErrors.NotFound);
        }

        return Result.Success(ToResponse(feature));
    }

    private static FeatureFlagResponse ToResponse(FeatureFlag f) => new(
        f.Id.Value,
        f.Key.Value,
        f.Name,
        f.Description,
        f.IsEnabled,
        f.TenantId,
        f.CreatedAt,
        f.CreatedBy,
        f.LastModifiedAt,
        f.LastModifiedBy);
}

public sealed class GetFeatureFlagByKeyHandler(
    IFeatureFlagRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetFeatureFlagByKeyQuery, Result<FeatureFlagResponse>>
{
    public async Task<Result<FeatureFlagResponse>> HandleAsync(GetFeatureFlagByKeyQuery request, CancellationToken ct)
    {
        Result<FeatureKey> keyResult = FeatureKey.Create(request.Key);
        if (keyResult.IsFailure)
        {
            return Result.Failure<FeatureFlagResponse>(keyResult.Error);
        }

        FeatureFlag? feature = await repository.GetByKeyAsync(keyResult.Value, currentTenant.TenantId ?? Guid.Empty, ct);
        if (feature is null)
        {
            return Result.Failure<FeatureFlagResponse>(FeatureErrors.NotFound);
        }

        return Result.Success(new FeatureFlagResponse(
            feature.Id.Value,
            feature.Key.Value,
            feature.Name,
            feature.Description,
            feature.IsEnabled,
            feature.TenantId,
            feature.CreatedAt,
            feature.CreatedBy,
            feature.LastModifiedAt,
            feature.LastModifiedBy));
    }
}

public sealed class GetEnabledFeatureFlagsHandler(
    IFeatureFlagRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetEnabledFeatureFlagsQuery, Result<IReadOnlyList<FeatureFlagResponse>>>
{
    public async Task<Result<IReadOnlyList<FeatureFlagResponse>>> HandleAsync(GetEnabledFeatureFlagsQuery request, CancellationToken ct)
    {
        IReadOnlyList<FeatureFlag> features = await repository.GetEnabledAsync(currentTenant.TenantId ?? Guid.Empty, ct);
        IReadOnlyList<FeatureFlagResponse> responses = features.Select(f => new FeatureFlagResponse(
            f.Id.Value,
            f.Key.Value,
            f.Name,
            f.Description,
            f.IsEnabled,
            f.TenantId,
            f.CreatedAt,
            f.CreatedBy,
            f.LastModifiedAt,
            f.LastModifiedBy)).ToList();

        return Result.Success(responses);
    }
}
