using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record CreateVendorCommand(
    string Name,
    string LegalName,
    string Country,
    VendorType VendorType,
    string? Tin,
    string? Bin,
    string? Email,
    string? Phone,
    string? Address) : Modulus.Mediator.Abstractions.ICommand<Result<CreateVendorResponse>>;
