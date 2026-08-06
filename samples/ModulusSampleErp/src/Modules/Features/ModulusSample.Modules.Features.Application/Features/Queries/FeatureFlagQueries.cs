using ModulusSample.Modules.Features.Application.Features.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Features.Application.Features.Queries;

public sealed record GetAllFeatureFlagsQuery(
    bool? IsEnabled = null,
    int PageNumber = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<FeatureFlagResponse>>>;

public sealed record GetFeatureFlagByIdQuery(Guid FeatureFlagId) : Modulus.Mediator.Abstractions.IQuery<Result<FeatureFlagResponse>>;

public sealed record GetFeatureFlagByKeyQuery(string Key) : Modulus.Mediator.Abstractions.IQuery<Result<FeatureFlagResponse>>;

public sealed record GetEnabledFeatureFlagsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<FeatureFlagResponse>>>;