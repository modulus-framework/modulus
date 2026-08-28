using ProcureFlow.Shared.Domain;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record AddVendorBankAccountCommand(
    Guid VendorId,
    string BankName,
    string AccountName,
    string AccountNumber,
    string Branch,
    string SwiftCode) : Modulus.Mediator.Abstractions.ICommand<Result>;
