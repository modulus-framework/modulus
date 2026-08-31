using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Errors;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class RejectVendorBankAccountCommandValidator : AbstractValidator<RejectVendorBankAccountCommand>
{
    public RejectVendorBankAccountCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.BankAccountId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectVendorBankAccountCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RejectVendorBankAccountCommand, Result>
{
    public async Task<Result> HandleAsync(RejectVendorBankAccountCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure(VendorErrors.NotFound(request.VendorId));

        Result reject = vendor.RejectBankAccount(request.BankAccountId, request.Reason, currentUser.UserName ?? "system");
        if (reject.IsFailure)
            return reject;

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}
