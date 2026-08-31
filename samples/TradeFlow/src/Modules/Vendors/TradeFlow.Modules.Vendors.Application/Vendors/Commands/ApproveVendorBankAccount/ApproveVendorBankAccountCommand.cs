using TradeFlow.Shared.Domain;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record ApproveVendorBankAccountCommand(
    Guid VendorId,
    Guid BankAccountId) : Modulus.Mediator.Abstractions.ICommand<Result>;
