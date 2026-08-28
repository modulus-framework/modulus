using ProcureFlow.Modules.Vendors.Application.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Errors;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

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
