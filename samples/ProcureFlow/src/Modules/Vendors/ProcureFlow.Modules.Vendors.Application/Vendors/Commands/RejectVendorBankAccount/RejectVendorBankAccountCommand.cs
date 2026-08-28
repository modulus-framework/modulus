using ProcureFlow.Shared.Domain;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record RejectVendorBankAccountCommand(
    Guid VendorId,
    Guid BankAccountId,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result>;
