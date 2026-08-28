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

internal sealed class QualifyVendorCommandValidator : AbstractValidator<QualifyVendorCommand>
{
    public QualifyVendorCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.Category).NotEmpty().MaximumLength(100);
        RuleFor(c => c.CertificateNumber).NotEmpty().MaximumLength(100);
        RuleFor(c => c.ValidFrom).LessThanOrEqualTo(c => c.ValidTo);
    }
}

public sealed class QualifyVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<QualifyVendorCommand, Result<VendorStatusResponse>>
{
    public async Task<Result<VendorStatusResponse>> HandleAsync(QualifyVendorCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorStatusResponse>(VendorErrors.NotFound(request.VendorId));

        Result qualify = vendor.Qualify(
            request.Category,
            request.CertificateNumber,
            request.ValidFrom,
            request.ValidTo,
            currentUser.UserName ?? "system");

        if (qualify.IsFailure)
            return Result.Failure<VendorStatusResponse>(qualify.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new VendorStatusResponse(vendor.Id, vendor.Status));
    }
}
