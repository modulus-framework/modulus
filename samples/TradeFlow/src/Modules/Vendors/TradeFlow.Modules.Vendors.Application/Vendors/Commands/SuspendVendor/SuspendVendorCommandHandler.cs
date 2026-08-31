using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Errors;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Mediator.Abstractions;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class SuspendVendorCommandValidator : AbstractValidator<SuspendVendorCommand>
{
    public SuspendVendorCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class SuspendVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SuspendVendorCommand, Result<VendorStatusResponse>>
{
    public async Task<Result<VendorStatusResponse>> HandleAsync(SuspendVendorCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorStatusResponse>(VendorErrors.NotFound(request.VendorId));

        Result suspend = vendor.Suspend(request.Reason);
        if (suspend.IsFailure)
            return Result.Failure<VendorStatusResponse>(suspend.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new VendorStatusResponse(vendor.Id, vendor.Status));
    }
}
