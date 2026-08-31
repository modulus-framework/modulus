using TradeFlow.Modules.Vendors.Application.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed class GetAllVendorsQueryHandler(
    IVendorRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetAllVendorsQuery, Result<IReadOnlyList<VendorResponse>>>
{
    public async Task<Result<IReadOnlyList<VendorResponse>>> HandleAsync(GetAllVendorsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<Vendor> vendors = await repository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<VendorResponse>>(
            vendors.Select(GetVendorByIdQueryHandler.ToResponse).ToList());
    }
}
