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

internal sealed class BlacklistVendorCommandValidator : AbstractValidator<BlacklistVendorCommand>
{
    public BlacklistVendorCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class BlacklistVendorCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<BlacklistVendorCommand, Result<VendorStatusResponse>>
{
    public async Task<Result<VendorStatusResponse>> HandleAsync(BlacklistVendorCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorStatusResponse>(VendorErrors.NotFound(request.VendorId));

        Result blacklist = vendor.Blacklist(request.Reason, currentUser.UserName ?? "system");
        if (blacklist.IsFailure)
            return Result.Failure<VendorStatusResponse>(blacklist.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(new VendorStatusResponse(vendor.Id, vendor.Status));
    }
}
