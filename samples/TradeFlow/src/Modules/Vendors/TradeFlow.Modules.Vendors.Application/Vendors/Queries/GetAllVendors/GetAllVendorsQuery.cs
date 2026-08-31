using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed record GetAllVendorsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<VendorResponse>>>;
