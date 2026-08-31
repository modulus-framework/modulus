using TradeFlow.Shared.Domain;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record SubmitVendorCommand(Guid VendorId) : Modulus.Mediator.Abstractions.ICommand<Result<VendorStatusResponse>>;
