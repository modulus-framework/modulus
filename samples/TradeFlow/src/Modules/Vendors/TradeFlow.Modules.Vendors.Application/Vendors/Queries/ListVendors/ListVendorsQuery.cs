using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed record ListVendorsQuery(
    VendorStatus? Status = null,
    string? Country = null,
    VendorType? VendorType = null,
    string? SearchTerm = null) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<VendorResponse>>>;
