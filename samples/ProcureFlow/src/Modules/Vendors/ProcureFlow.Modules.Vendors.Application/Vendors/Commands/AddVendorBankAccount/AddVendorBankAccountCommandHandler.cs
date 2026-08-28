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

internal sealed class AddVendorBankAccountCommandValidator : AbstractValidator<AddVendorBankAccountCommand>
{
    public AddVendorBankAccountCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.BankName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.AccountName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.AccountNumber).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Branch).NotEmpty().MaximumLength(100);
        RuleFor(c => c.SwiftCode).NotEmpty().MaximumLength(20);
    }
}

public sealed class AddVendorBankAccountCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<AddVendorBankAccountCommand, Result>
{
    public async Task<Result> HandleAsync(AddVendorBankAccountCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure(VendorErrors.NotFound(request.VendorId));

        Result add = vendor.AddBankAccount(
            Guid.NewGuid(),
            request.BankName,
            request.AccountName,
            request.AccountNumber,
            request.Branch,
            request.SwiftCode,
            currentUser.UserName ?? "system");

        if (add.IsFailure)
            return add;

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}
