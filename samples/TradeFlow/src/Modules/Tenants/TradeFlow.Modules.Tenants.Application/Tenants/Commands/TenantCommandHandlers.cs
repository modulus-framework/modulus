using System.Text.Json;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Tenants.Application.Tenants.Dtos;
using TradeFlow.Modules.Tenants.Domain.Constants;
using TradeFlow.Modules.Tenants.Domain.Entities;
using TradeFlow.Modules.Tenants.Domain.Repositories;
using TradeFlow.Modules.Tenants.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Modules.Tenants.Application.Tenants.Commands;

public sealed class CreateTenantHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<CreateTenantHandler> logger) : ICommandHandler<CreateTenantCommand, Result<CreateTenantResponse>>
{
    public async Task<Result<CreateTenantResponse>> HandleAsync(CreateTenantCommand request, CancellationToken ct)
    {
        Result<Subdomain> subdomainResult = Subdomain.Create(request.Subdomain);
        if (subdomainResult.IsFailure)
        {
            return Result.Failure<CreateTenantResponse>(subdomainResult.Error);
        }

        if (await tenantRepository.ExistsByNameAsync(request.Name, ct))
        {
            return Result.Failure<CreateTenantResponse>(TenantErrors.DuplicateName);
        }

        if (await tenantRepository.ExistsBySubdomainAsync(subdomainResult.Value, ct))
        {
            return Result.Failure<CreateTenantResponse>(TenantErrors.DuplicateSubdomain);
        }

        var tenantId = TenantId.New();
        string currentUserEmail = currentUser.Email ?? "system";

        Result<Tenant> tenantResult = Tenant.Create(
            tenantId,
            request.Name,
            subdomainResult.Value,
            request.DatabaseConnectionString,
            request.Features ?? JsonDocument.Parse("{}"),
            request.Settings ?? JsonDocument.Parse("{}"),
            currentUserEmail);

        if (tenantResult.IsFailure)
        {
            return Result.Failure<CreateTenantResponse>(tenantResult.Error);
        }

        await tenantRepository.AddAsync(tenantResult.Value, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} created by {User}", tenantId.Value, currentUserEmail);

        return Result.Success(new CreateTenantResponse(tenantId.Value, request.Name, request.Subdomain));
    }
}

public sealed class UpdateTenantHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<UpdateTenantHandler> logger) : ICommandHandler<UpdateTenantCommand, Result<UpdateTenantResponse>>
{
    public async Task<Result<UpdateTenantResponse>> HandleAsync(UpdateTenantCommand request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure<UpdateTenantResponse>(TenantErrors.NotFound);
        }

        if (!tenant.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (await tenantRepository.ExistsByNameAsync(request.Name, ct))
            {
                return Result.Failure<UpdateTenantResponse>(TenantErrors.DuplicateName);
            }
        }

        string currentUserEmail = currentUser.Email ?? "system";

        Result updateResult = tenant.Update(request.Name, request.DatabaseConnectionString, currentUserEmail);
        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateTenantResponse>(updateResult.Error);
        }

        await tenantRepository.UpdateAsync(tenant, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} updated by {User}", tenant.Id.Value, currentUserEmail);

        return Result.Success(new UpdateTenantResponse(tenant.Id.Value, tenant.Name, tenant.LastModifiedAtUtc ?? DateTime.UtcNow));
    }
}

public sealed class ActivateTenantHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<ActivateTenantHandler> logger) : ICommandHandler<ActivateTenantCommand, Result<TenantStatusResponse>>
{
    public async Task<Result<TenantStatusResponse>> HandleAsync(ActivateTenantCommand request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure<TenantStatusResponse>(TenantErrors.NotFound);
        }

        string currentUserEmail = currentUser.Email ?? "system";

        Result activateResult = tenant.Activate(currentUserEmail);
        if (activateResult.IsFailure)
        {
            return Result.Failure<TenantStatusResponse>(activateResult.Error);
        }

        await tenantRepository.UpdateAsync(tenant, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} activated by {User}", tenant.Id.Value, currentUserEmail);

        return Result.Success(new TenantStatusResponse(
            tenant.Id.Value,
            tenant.Name,
            true,
            tenant.LastModifiedAtUtc ?? DateTime.UtcNow));
    }
}

public sealed class DeactivateTenantHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<DeactivateTenantHandler> logger) : ICommandHandler<DeactivateTenantCommand, Result<TenantStatusResponse>>
{
    public async Task<Result<TenantStatusResponse>> HandleAsync(DeactivateTenantCommand request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure<TenantStatusResponse>(TenantErrors.NotFound);
        }

        string currentUserEmail = currentUser.Email ?? "system";

        Result deactivateResult = tenant.Deactivate(currentUserEmail);
        if (deactivateResult.IsFailure)
        {
            return Result.Failure<TenantStatusResponse>(deactivateResult.Error);
        }

        await tenantRepository.UpdateAsync(tenant, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} deactivated by {User}", tenant.Id.Value, currentUserEmail);

        return Result.Success(new TenantStatusResponse(
            tenant.Id.Value,
            tenant.Name,
            false,
            tenant.LastModifiedAtUtc ?? DateTime.UtcNow));
    }
}

public sealed class DeleteTenantHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<DeleteTenantHandler> logger) : ICommandHandler<DeleteTenantCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteTenantCommand request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure(TenantErrors.NotFound);
        }

        string currentUserEmail = currentUser.Email ?? "system";

        Result deleteResult = tenant.Delete(currentUserEmail);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        await tenantRepository.DeleteAsync(tenant, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} deleted by {User}", tenant.Id.Value, currentUserEmail);

        return Result.Success();
    }
}

public sealed class UpdateTenantFeaturesHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<UpdateTenantFeaturesHandler> logger) : ICommandHandler<UpdateTenantFeaturesCommand, Result<UpdateTenantResponse>>
{
    public async Task<Result<UpdateTenantResponse>> HandleAsync(UpdateTenantFeaturesCommand request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure<UpdateTenantResponse>(TenantErrors.NotFound);
        }

        string currentUserEmail = currentUser.Email ?? "system";

        Result updateResult = tenant.UpdateFeatures(request.Features, currentUserEmail);
        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateTenantResponse>(updateResult.Error);
        }

        await tenantRepository.UpdateAsync(tenant, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} features updated by {User}", tenant.Id.Value, currentUserEmail);

        return Result.Success(new UpdateTenantResponse(tenant.Id.Value, tenant.Name, tenant.LastModifiedAtUtc ?? DateTime.UtcNow));
    }
}

public sealed class UpdateTenantSettingsHandler(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<UpdateTenantSettingsHandler> logger) : ICommandHandler<UpdateTenantSettingsCommand, Result<UpdateTenantResponse>>
{
    public async Task<Result<UpdateTenantResponse>> HandleAsync(UpdateTenantSettingsCommand request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure<UpdateTenantResponse>(TenantErrors.NotFound);
        }

        string currentUserEmail = currentUser.Email ?? "system";

        Result updateResult = tenant.UpdateSettings(request.Settings, currentUserEmail);
        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateTenantResponse>(updateResult.Error);
        }

        await tenantRepository.UpdateAsync(tenant, ct);
        await unitOfWork.CommitAsync(ct);

        logger.LogInformation("Tenant {TenantId} settings updated by {User}", tenant.Id.Value, currentUserEmail);

        return Result.Success(new UpdateTenantResponse(tenant.Id.Value, tenant.Name, tenant.LastModifiedAtUtc ?? DateTime.UtcNow));
    }
}
