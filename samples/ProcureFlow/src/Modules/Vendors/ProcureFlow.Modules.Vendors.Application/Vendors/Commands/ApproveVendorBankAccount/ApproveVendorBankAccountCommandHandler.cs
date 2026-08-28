using ProcureFlow.Modules.Vendors.Application.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Errors;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class ApproveVendorBankAccountCommandValidator : AbstractValidator<ApproveVendorBankAccountCommand>
{
    public ApproveVendorBankAccountCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.BankAccountId).NotEmpty();
    }
}

public sealed class ApproveVendorBankAccountCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ApproveVendorBankAccountCommand, Result>
{
    public async Task<Result> HandleAsync(ApproveVendorBankAccountCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure(VendorErrors.NotFound(request.VendorId));

        Result approve = vendor.ApproveBankAccount(request.BankAccountId, currentUser.UserName ?? "system");
        if (approve.IsFailure)
            return approve;

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}
