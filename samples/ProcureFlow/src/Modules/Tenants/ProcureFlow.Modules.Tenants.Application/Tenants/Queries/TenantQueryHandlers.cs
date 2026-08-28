using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Tenants.Application.Tenants.Dtos;
using ProcureFlow.Modules.Tenants.Domain.Constants;
using ProcureFlow.Modules.Tenants.Domain.Entities;
using ProcureFlow.Modules.Tenants.Domain.Repositories;
using ProcureFlow.Modules.Tenants.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Tenants.Application.Tenants.Queries;

public sealed class GetTenantByIdHandler(
    ITenantRepository tenantRepository) : IQueryHandler<GetTenantByIdQuery, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> HandleAsync(GetTenantByIdQuery request, CancellationToken ct)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(TenantId.From(request.TenantId), ct);
        if (tenant is null)
        {
            return Result.Failure<TenantDto>(TenantErrors.NotFound);
        }

        return Result.Success(TenantDtoMapper.ToDto(tenant));
    }
}

public sealed class GetTenantBySubdomainHandler(
    ITenantRepository tenantRepository) : IQueryHandler<GetTenantBySubdomainQuery, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> HandleAsync(GetTenantBySubdomainQuery request, CancellationToken ct)
    {
        Result<Subdomain> subdomainResult = Subdomain.Create(request.Subdomain);
        if (subdomainResult.IsFailure)
        {
            return Result.Failure<TenantDto>(subdomainResult.Error);
        }

        Tenant? tenant = await tenantRepository.GetBySubdomainAsync(subdomainResult.Value, ct);
        if (tenant is null)
        {
            return Result.Failure<TenantDto>(TenantErrors.NotFound);
        }

        return Result.Success(TenantDtoMapper.ToDto(tenant));
    }
}

public sealed class GetAllTenantsHandler(
    ITenantRepository tenantRepository) : IQueryHandler<GetAllTenantsQuery, Result<PagedResult<TenantDto>>>
{
    public async Task<Result<PagedResult<TenantDto>>> HandleAsync(GetAllTenantsQuery request, CancellationToken ct)
    {
        IReadOnlyList<Tenant> allTenants = await tenantRepository.GetAllAsync(ct);

        IEnumerable<Tenant> filteredTenants = allTenants;
        if (request.IsActive.HasValue)
        {
            filteredTenants = filteredTenants.Where(t => t.IsActive == request.IsActive.Value);
        }

        var list = filteredTenants.ToList();
        int totalCount = list.Count;
        var pagedTenants = list
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(TenantDtoMapper.ToDto)
            .ToList();

        return Result.Success(new PagedResult<TenantDto>(
            pagedTenants,
            totalCount,
            request.Page,
            request.PageSize));
    }
}

public sealed class GetActiveTenantsHandler(
    ITenantRepository tenantRepository) : IQueryHandler<GetActiveTenantsQuery, Result<IReadOnlyList<TenantDto>>>
{
    public async Task<Result<IReadOnlyList<TenantDto>>> HandleAsync(GetActiveTenantsQuery request, CancellationToken ct)
    {
        IReadOnlyList<Tenant> tenants = await tenantRepository.GetActiveTenantsAsync(ct);
        IReadOnlyList<TenantDto> dtos = tenants.Select(TenantDtoMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

public sealed class GetInactiveTenantsHandler(
    ITenantRepository tenantRepository) : IQueryHandler<GetInactiveTenantsQuery, Result<IReadOnlyList<TenantDto>>>
{
    public async Task<Result<IReadOnlyList<TenantDto>>> HandleAsync(GetInactiveTenantsQuery request, CancellationToken ct)
    {
        IReadOnlyList<Tenant> tenants = await tenantRepository.GetInactiveTenantsAsync(ct);
        IReadOnlyList<TenantDto> dtos = tenants.Select(TenantDtoMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

public sealed class SearchTenantsHandler(
    ITenantRepository tenantRepository) : IQueryHandler<SearchTenantsQuery, Result<PagedResult<TenantDto>>>
{
    public async Task<Result<PagedResult<TenantDto>>> HandleAsync(SearchTenantsQuery request, CancellationToken ct)
    {
        IReadOnlyList<Tenant> allTenants = await tenantRepository.GetAllAsync(ct);

        var searchTerm = request.SearchTerm.ToLowerInvariant();
        var filteredTenants = allTenants
            .Where(t => t.Name.ToLowerInvariant().Contains(searchTerm) ||
                       t.Subdomain.Value.ToLowerInvariant().Contains(searchTerm))
            .ToList();

        int totalCount = filteredTenants.Count;
        var pagedTenants = filteredTenants
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(TenantDtoMapper.ToDto)
            .ToList();

        return Result.Success(new PagedResult<TenantDto>(
            pagedTenants,
            totalCount,
            request.Page,
            request.PageSize));
    }
}

internal static class TenantDtoMapper
{
    public static TenantDto ToDto(Tenant tenant) => new(
        tenant.Id.Value,
        tenant.Name,
        tenant.Subdomain.Value,
        tenant.IsActive,
        tenant.Features,
        tenant.Settings,
        tenant.CreatedAtUtc,
        tenant.CreatedBy,
        tenant.LastModifiedAtUtc,
        tenant.LastModifiedBy,
        tenant.IsDeleted,
        tenant.DeletedAtUtc,
        tenant.DeletedBy,
        tenant.Version);
}
