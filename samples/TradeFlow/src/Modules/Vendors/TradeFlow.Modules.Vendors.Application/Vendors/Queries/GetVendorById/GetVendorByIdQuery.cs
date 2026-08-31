using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed record GetVendorByIdQuery(Guid VendorId) : Modulus.Mediator.Abstractions.IQuery<Result<VendorDetailResponse>>;
