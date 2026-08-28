using ProcureFlow.Shared.Domain;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record ApproveVendorBankAccountCommand(
    Guid VendorId,
    Guid BankAccountId) : Modulus.Mediator.Abstractions.ICommand<Result>;
