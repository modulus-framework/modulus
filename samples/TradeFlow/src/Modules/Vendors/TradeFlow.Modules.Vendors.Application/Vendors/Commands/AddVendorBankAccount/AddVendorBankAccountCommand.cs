using TradeFlow.Shared.Domain;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record AddVendorBankAccountCommand(
    Guid VendorId,
    string BankName,
    string AccountName,
    string AccountNumber,
    string Branch,
    string SwiftCode) : Modulus.Mediator.Abstractions.ICommand<Result>;
