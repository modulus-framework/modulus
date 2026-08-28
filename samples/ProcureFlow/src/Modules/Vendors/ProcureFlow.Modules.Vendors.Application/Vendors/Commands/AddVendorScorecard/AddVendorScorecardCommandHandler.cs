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

internal sealed class AddVendorScorecardCommandValidator : AbstractValidator<AddVendorScorecardCommand>
{
    public AddVendorScorecardCommandValidator()
    {
        RuleFor(c => c.VendorId).NotEmpty();
        RuleFor(c => c.OnTimeDeliveryScore).InclusiveBetween(0m, 100m);
        RuleFor(c => c.QualityScore).InclusiveBetween(0m, 100m);
        RuleFor(c => c.PriceCompetitivenessScore).InclusiveBetween(0m, 100m);
        RuleFor(c => c.ResponsivenessScore).InclusiveBetween(0m, 100m);
        RuleFor(c => c.ComplianceScore).InclusiveBetween(0m, 100m);
    }
}

public sealed class AddVendorScorecardCommandHandler(
    IVendorRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<AddVendorScorecardCommand, Result<VendorScorecardResponse>>
{
    public async Task<Result<VendorScorecardResponse>> HandleAsync(AddVendorScorecardCommand request, CancellationToken ct)
    {
        Vendor? vendor = await repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure<VendorScorecardResponse>(VendorErrors.NotFound(request.VendorId));

        Result add = vendor.AddScorecard(
            request.Period,
            request.OnTimeDeliveryScore,
            request.QualityScore,
            request.PriceCompetitivenessScore,
            request.ResponsivenessScore,
            request.ComplianceScore,
            currentUser.UserName ?? "system");

        if (add.IsFailure)
            return Result.Failure<VendorScorecardResponse>(add.Error);

        await repository.UpdateAsync(vendor, ct);
        await unitOfWork.CommitAsync(ct);

        VendorScorecard latest = vendor.Scorecards[^1];
        return Result.Success(new VendorScorecardResponse(
            latest.Id, latest.Period, latest.OnTimeDeliveryScore, latest.QualityScore,
            latest.PriceCompetitivenessScore, latest.ResponsivenessScore,
            latest.ComplianceScore, latest.WeightedAverage, latest.Grade));
    }
}
