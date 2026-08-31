using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Errors;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Mediator.Abstractions;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class ActivateVendorCommandValidator : AbstractValidator<ActivateVendorCommand>
{
    public ActivateVendorCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
    }
}

public sealed class ActivateVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ActivateVendorCommand, Result<VendorStatusResponse>>
{
    public async Task<Result<VendorStatusResponse>> HandleAsync(ActivateVendorCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorStatusResponse>(VendorErrors.NotFound(request.VendorId));

        Result activate = vendor.Activate();
        if (activate.IsFailure)
            return Result.Failure<VendorStatusResponse>(activate.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new VendorStatusResponse(vendor.Id, vendor.Status));
    }
}
