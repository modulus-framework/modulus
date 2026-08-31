using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record UpdateVendorCommand(
    Guid VendorId,
    string? Name = null,
    string? LegalName = null,
    string? Country = null,
    VendorType? VendorType = null,
    string? Tin = null,
    string? Bin = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null) : Modulus.Mediator.Abstractions.ICommand<Result>;
