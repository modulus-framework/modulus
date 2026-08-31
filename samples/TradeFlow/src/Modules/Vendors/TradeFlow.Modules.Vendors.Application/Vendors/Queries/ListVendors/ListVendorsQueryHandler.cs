using TradeFlow.Modules.Vendors.Application.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed class ListVendorsQueryHandler(
    IVendorRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListVendorsQuery, Result<IReadOnlyList<VendorResponse>>>
{
    public async Task<Result<IReadOnlyList<VendorResponse>>> HandleAsync(ListVendorsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var vendors = await repository.GetFilteredAsync(
            tenantId,
            status: request.Status,
            country: request.Country,
            vendorType: request.VendorType,
            searchTerm: request.SearchTerm,
            ct);

        return Result.Success<IReadOnlyList<VendorResponse>>(
            vendors.Select(GetVendorByIdQueryHandler.ToResponse).ToList());
    }
}
