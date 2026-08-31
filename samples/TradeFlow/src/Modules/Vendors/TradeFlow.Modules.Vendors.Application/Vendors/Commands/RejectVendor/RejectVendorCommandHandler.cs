using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Errors;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Mediator.Abstractions;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class RejectVendorCommandValidator : AbstractValidator<RejectVendorCommand>
{
    public RejectVendorCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectVendorCommand, Result<VendorStatusResponse>>
{
    public async Task<Result<VendorStatusResponse>> HandleAsync(RejectVendorCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorStatusResponse>(VendorErrors.NotFound(request.VendorId));

        Result reject = vendor.Reject(request.Reason);
        if (reject.IsFailure)
            return Result.Failure<VendorStatusResponse>(reject.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new VendorStatusResponse(vendor.Id, vendor.Status));
    }
}
