using TradeFlow.Shared.Domain;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record RejectVendorCommand(
    Guid VendorId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<VendorStatusResponse>>;
