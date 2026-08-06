using System.Text.Json;
using ModulusSample.Modules.Tenants.Application.Tenants.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Application.Tenants.Commands;

public sealed record CreateTenantCommand(
    string Name,
    string Subdomain,
    string DatabaseConnectionString,
    JsonDocument? Features,
    JsonDocument? Settings) : Modulus.Mediator.Abstractions.ICommand<Result<CreateTenantResponse>>;

public sealed record UpdateTenantCommand(
    Guid TenantId,
    string Name,
    string DatabaseConnectionString) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateTenantResponse>>;

public sealed record ActivateTenantCommand(Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<TenantStatusResponse>>;

public sealed record DeactivateTenantCommand(Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<TenantStatusResponse>>;

public sealed record DeleteTenantCommand(Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed record UpdateTenantFeaturesCommand(
    Guid TenantId,
    JsonDocument Features) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateTenantResponse>>;

public sealed record UpdateTenantSettingsCommand(
    Guid TenantId,
    JsonDocument Settings) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateTenantResponse>>;