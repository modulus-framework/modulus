using ProcureFlow.Modules.Vendors.Application.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Modules.Vendors.Domain.Errors;
using ProcureFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

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
