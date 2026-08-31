using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Dtos;
using FluentValidation;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Vendors.Domain.Errors;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using Modulus.Mediator.Abstractions;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

internal sealed class SubmitVendorCommandValidator : AbstractValidator<SubmitVendorCommand>
{
    public SubmitVendorCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
    }
}

public sealed class SubmitVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitVendorCommand, Result<VendorStatusResponse>>
{
    public async Task<Result<VendorStatusResponse>> HandleAsync(SubmitVendorCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorStatusResponse>(VendorErrors.NotFound(request.VendorId));

        Result submit = vendor.Submit();
        if (submit.IsFailure)
            return Result.Failure<VendorStatusResponse>(submit.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new VendorStatusResponse(vendor.Id, vendor.Status));
    }
}
