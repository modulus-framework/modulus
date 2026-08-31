using TradeFlow.Modules.Tenants.Application.Tenants.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Tenants.Application.Tenants.Queries;

public sealed record GetTenantByIdQuery(Guid TenantId) : Modulus.Mediator.Abstractions.IQuery<Result<TenantDto>>;

public sealed record GetTenantBySubdomainQuery(string Subdomain) : Modulus.Mediator.Abstractions.IQuery<Result<TenantDto>>;

public sealed record GetAllTenantsQuery(
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<TenantDto>>>;

public sealed record GetActiveTenantsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<TenantDto>>>;

public sealed record GetInactiveTenantsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<TenantDto>>>;

public sealed record SearchTenantsQuery(
    string SearchTerm,
    int Page = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<TenantDto>>>;
