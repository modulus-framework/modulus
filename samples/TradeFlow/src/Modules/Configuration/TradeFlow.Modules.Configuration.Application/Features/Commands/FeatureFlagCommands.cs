using TradeFlow.Modules.Configuration.Application.Features.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Features.Commands;

public sealed record CreateFeatureFlagCommand(
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<CreateFeatureFlagResponse>>;

public sealed record UpdateFeatureFlagCommand(
    Guid FeatureFlagId,
    string Name,
    string? Description) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateFeatureFlagResponse>>;

public sealed record ToggleFeatureFlagCommand(
    Guid FeatureFlagId,
    bool IsEnabled) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateFeatureFlagResponse>>;

public sealed record DeleteFeatureFlagCommand(Guid FeatureFlagId) : Modulus.Mediator.Abstractions.ICommand<Result>;
