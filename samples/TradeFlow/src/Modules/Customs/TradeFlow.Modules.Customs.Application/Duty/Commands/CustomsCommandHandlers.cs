using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Customs.Application.Duty.Commands;
using TradeFlow.Modules.Customs.Application.Duty.Dtos;
using TradeFlow.Modules.Customs.Domain.Duty;
using TradeFlow.Modules.Customs.Domain.Entities;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Application.Duty.Commands;

public sealed class CreateHsCodeHandler(
    IHsCodeRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateHsCodeCommand, Result<HsCodeResponse>>
{
    public async Task<Result<HsCodeResponse>> HandleAsync(CreateHsCodeCommand request, CancellationToken ct)
    {
        HsCode? existing = await repository.GetEffectiveAsync(request.Code, request.EffectiveFrom, ct);
        if (existing is not null)
            return Result.Failure<HsCodeResponse>(Error.Conflict(
                "HsCode.Overlap",
                "An effective HS code already exists for this code and date (BR-HS-01)"));

        HsCode hsCode = HsCode.Create(request.Code, request.Description, request.EffectiveFrom, request.EffectiveTo);
        await repository.AddAsync(hsCode, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new HsCodeResponse(hsCode.Id, hsCode.Code, hsCode.Description, hsCode.EffectiveFrom, hsCode.EffectiveTo));
    }
}

public sealed class CreateDutyRateHandler(
    IDutyRateRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CreateDutyRateCommand, Result<DutyRateResponse>>
{
    public async Task<Result<DutyRateResponse>> HandleAsync(CreateDutyRateCommand request, CancellationToken ct)
    {
        var candidate = DutyRate.Create(request.HsCode, request.Component, request.Rate, request.EffectiveFrom,
            request.EffectiveTo, request.Source, currentUser.UserName ?? "system", request.SpecificRate,
            request.Uom, request.RefDoc);

        if (await repository.HasOverlappingAsync(candidate, ct))
            return Result.Failure<DutyRateResponse>(Error.Conflict(
                "DutyRate.Overlap",
                $"Overlapping duty-rate period for HS {request.HsCode} component {request.Component} (BR-DS-01)"));

        await repository.AddAsync(candidate, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(DutyResponseFactory.ToResponse(candidate));
    }
}

public sealed class ApproveDutyRateHandler(
    IDutyRateRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ApproveDutyRateCommand, Result<DutyRateResponse>>
{
    public async Task<Result<DutyRateResponse>> HandleAsync(ApproveDutyRateCommand request, CancellationToken ct)
    {
        DutyRate? rate = await repository.GetByIdAsync(request.RateId, ct);
        if (rate is null)
            return Result.Failure<DutyRateResponse>(Error.NotFound("DutyRate.NotFound", "Duty rate not found"));

        rate.Approve(currentUser.UserName ?? "system");
        await unitOfWork.CommitAsync(ct);

        return Result.Success(DutyResponseFactory.ToResponse(rate));
    }
}

public sealed class RejectDutyRateHandler(
    IDutyRateRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RejectDutyRateCommand, Result>
{
    public async Task<Result> HandleAsync(RejectDutyRateCommand request, CancellationToken ct)
    {
        DutyRate? rate = await repository.GetByIdAsync(request.RateId, ct);
        if (rate is null)
            return Result.Failure(Error.NotFound("DutyRate.NotFound", "Duty rate not found"));

        rate.Reject(currentUser.UserName ?? "system", request.Reason);
        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}

public sealed class CreateSroBenefitHandler(
    ISroBenefitRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateSroBenefitCommand, Result<SroBenefitResponse>>
{
    public async Task<Result<SroBenefitResponse>> HandleAsync(CreateSroBenefitCommand request, CancellationToken ct)
    {
        var benefit = SroBenefit.Create(request.Name, request.HsCodePrefix, request.Type, request.EffectiveFrom,
            request.OverrideRate, request.CapPercent, request.Conditions, request.EffectiveTo);

        await repository.AddAsync(benefit, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new SroBenefitResponse(benefit.Id, benefit.Name, benefit.HsCodePrefix, benefit.Type,
            benefit.OverrideRate, benefit.CapPercent, benefit.Conditions, benefit.EffectiveFrom, benefit.EffectiveTo));
    }
}

public sealed class CreateBoeHandler(
    IBoeRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateBoeCommand, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(CreateBoeCommand request, CancellationToken ct)
    {
        if (request.Lines.Count == 0)
            return Result.Failure<BoeResponse>(Error.Validation("Boe.Empty", "A BoE requires at least one line"));

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var boe = BillOfEntry.Create(tenantId, request.FileId, request.BoeNo, request.BoeDate, request.OfficeCode,
            request.DeclarantAin);

        foreach (BoeLineInput line in request.Lines)
        {
            boe.AddLine(new BoeLine(Guid.NewGuid(), line.CiLineId, line.HsCode, line.Description, line.Quantity,
                line.Uom, line.DeclaredAvFcy, line.CustomsExchangeRate, line.LandingChargePct));
        }

        await repository.AddAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class AssessBoeHandler(
    IBoeRepository repository,
    IDutyRateRepository rateRepository,
    ISroBenefitRepository sroRepository,
    IAitAtLedgerRepository ledgerRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<AssessBoeCommand, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(AssessBoeCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "BoE not found"));

        var assessedByLine = (request.AssessedLines ?? Array.Empty<AssessedLineInput>())
            .ToDictionary(x => x.LineId);

        foreach (BoeLine line in boe.Lines)
        {
            IReadOnlyDictionary<DutyComponent, DutyRateRow> rates =
                await rateRepository.GetEffectiveRatesAsync(line.HsCode, boe.BoeDate, ct);
            IReadOnlyList<SroBenefitApplication> sro =
                await sroRepository.GetActiveForAsync(line.HsCode, boe.TenantId, boe.BoeDate, ct);

            DutyCalculationResult calc = DutyCascadeCalculator.Calculate(
                line.Quantity, line.DeclaredAvFcy, 0m, 0m, line.CustomsExchangeRate,
                line.LandingChargePct, line.TariffValueBdt, rates, sro);

            line.RecordComputed(calc.Tti, calc.Components.Select(c => new RateLineageRow(
                c.Component.ToString(), c.RateRowId, c.Rate)));

            if (assessedByLine.TryGetValue(line.Id, out AssessedLineInput? assessed))
            {
                line.Assess(assessed.AssessedTtiBdt, assessed.AssessedDutyLines.Select(d => new AssessedDutyLine(d.Component, d.Amount)));
            }
            else
            {
                // No external assessment supplied → assessed equals system computation.
                line.Assess(calc.Tti, calc.Components.Select(c => new AssessedDutyLine(c.Component.ToString(), c.Amount)));
            }

            // BR-CUS-07: AIT/AT advance-tax additions per consignment at assessment.
            if (calc.GetComponentAmount(DutyComponent.Ait) > 0m)
                await ledgerRepository.AddAsync(AitAtLedgerEntry.Create(
                    request.CompanyId, boe.BoeDate.Year, DutyComponent.Ait, calc.GetComponentAmount(DutyComponent.Ait),
                    AitAtEntryType.Addition, boe.FileId, boe.Id, boe.BoeDate), ct);
            if (calc.GetComponentAmount(DutyComponent.At) > 0m)
                await ledgerRepository.AddAsync(AitAtLedgerEntry.Create(
                    request.CompanyId, boe.BoeDate.Year, DutyComponent.At, calc.GetComponentAmount(DutyComponent.At),
                    AitAtEntryType.Addition, boe.FileId, boe.Id, boe.BoeDate), ct);
        }

        boe.Assess(request.TolerancePct);
        boe.RecordVarianceDisputes(DisputeResolutionType.QueryResponse, null);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class RegisterChallanHandler(
    IBoeRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RegisterChallanCommand, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(RegisterChallanCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "BoE not found"));

        boe.RegisterChallan(new Challan(Guid.NewGuid(), request.ChallanNo, request.Amount, request.PaidAtUtc, request.EvidenceRef));
        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class ExamineBoeHandler(
    IBoeRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ExamineBoeCommand, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(ExamineBoeCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "BoE not found"));

        boe.Examine(request.Lane);
        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class ReleaseBoeHandler(
    IBoeRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReleaseBoeCommand, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(ReleaseBoeCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "BoE not found"));

        boe.Release();
        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

public sealed class AccrueDemurrageHandler(
    IDemurrageRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<AccrueDemurrageCommand, Result<DemurrageResponse>>
{
    public async Task<Result<DemurrageResponse>> HandleAsync(AccrueDemurrageCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var accrual = DemurrageAccrual.Create(tenantId, request.FileId, request.ContainerRef, request.PortCode,
            request.LandingDate, request.FreeDays, request.DailyRateBdt);
        accrual.Accrue(request.AsOfDate);

        await repository.AddAsync(accrual, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new DemurrageResponse(accrual.Id, tenantId, request.FileId, request.ContainerRef,
            request.PortCode, request.LandingDate, request.FreeDays, request.DailyRateBdt,
            accrual.AccruedDays, accrual.AccruedAmountBdt));
    }
}

public sealed class EstimateDutyHandler(
    IDutyRateRepository rateRepository,
    ISroBenefitRepository sroRepository,
    ICurrentTenant currentTenant) : ICommandHandler<EstimateDutyCommand, Result<DutyEstimateResponse>>
{
    public async Task<Result<DutyEstimateResponse>> HandleAsync(EstimateDutyCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyDictionary<DutyComponent, DutyRateRow> rates =
            await rateRepository.GetEffectiveRatesAsync(request.HsCode, request.AssessmentDate, ct);
        IReadOnlyList<SroBenefitApplication> sro =
            await sroRepository.GetActiveForAsync(request.HsCode, tenantId, request.AssessmentDate, ct);

        DutyCalculationResult calc = DutyCascadeCalculator.Calculate(
            request.Quantity, request.UnitPriceFcy, request.FreightShareFcy, request.InsuranceShareFcy,
            request.CustomsExchangeRate, DutyCascadeCalculator.DefaultLandingChargePct, null, rates, sro);

        return Result.Success(new DutyEstimateResponse(
            calc.CifFcy,
            calc.AvEffective,
            calc.Tti,
            calc.UsedTariffValue,
            calc.Components.Select(c => new DutyComponentEstimateResponse(c.Component, c.RateDescription, c.BaseAmount, c.Amount)).ToList()));
    }
}

public sealed class ResolveDisputeHandler(
    IBoeRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ResolveDisputeCommand, Result<BoeResponse>>
{
    public async Task<Result<BoeResponse>> HandleAsync(ResolveDisputeCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BoeResponse>(Error.NotFound("Boe.NotFound", "BoE not found"));

        boe.ResolveDispute(request.DisputeId, request.ResolutionType, request.Notes);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(boe));
    }
}

// ── Item-HS Mapping Handlers (BR-HS-02..03) ────────────────────────

public sealed class CreateItemHsMappingHandler(
    IItemHsMappingRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateItemHsMappingCommand, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(CreateItemHsMappingCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var mapping = ItemHsMapping.Create(tenantId, request.ItemId, request.HsCode,
            request.Confidence, request.Notes, request.IsConsignmentOverride, request.FileId);

        await repository.AddAsync(mapping, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}

public sealed class UpdateItemHsMappingHandler(
    IItemHsMappingRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateItemHsMappingCommand, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(UpdateItemHsMappingCommand request, CancellationToken ct)
    {
        ItemHsMapping? mapping = await repository.GetByIdAsync(request.MappingId, ct);
        if (mapping is null)
            return Result.Failure<ItemHsMappingResponse>(Error.NotFound("HsMapping.NotFound", "HS mapping not found"));

        mapping.UpdateHsCode(request.HsCode, request.Confidence, request.Notes);

        await repository.SaveAsync(mapping, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}

public sealed class SubmitItemHsMappingHandler(
    IItemHsMappingRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitItemHsMappingCommand, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(SubmitItemHsMappingCommand request, CancellationToken ct)
    {
        ItemHsMapping? mapping = await repository.GetByIdAsync(request.MappingId, ct);
        if (mapping is null)
            return Result.Failure<ItemHsMappingResponse>(Error.NotFound("HsMapping.NotFound", "HS mapping not found"));

        Result result = mapping.Submit();
        if (result.IsFailure)
            return Result.Failure<ItemHsMappingResponse>(result.Error);

        await repository.SaveAsync(mapping, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}

public sealed class ApproveItemHsMappingHandler(
    IItemHsMappingRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<ApproveItemHsMappingCommand, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(ApproveItemHsMappingCommand request, CancellationToken ct)
    {
        ItemHsMapping? mapping = await repository.GetByIdAsync(request.MappingId, ct);
        if (mapping is null)
            return Result.Failure<ItemHsMappingResponse>(Error.NotFound("HsMapping.NotFound", "HS mapping not found"));

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        Result result = mapping.Approve(tenantId);
        if (result.IsFailure)
            return Result.Failure<ItemHsMappingResponse>(result.Error);

        await repository.SaveAsync(mapping, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}

public sealed class RejectItemHsMappingHandler(
    IItemHsMappingRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectItemHsMappingCommand, Result<ItemHsMappingResponse>>
{
    public async Task<Result<ItemHsMappingResponse>> HandleAsync(RejectItemHsMappingCommand request, CancellationToken ct)
    {
        ItemHsMapping? mapping = await repository.GetByIdAsync(request.MappingId, ct);
        if (mapping is null)
            return Result.Failure<ItemHsMappingResponse>(Error.NotFound("HsMapping.NotFound", "HS mapping not found"));

        Result result = mapping.Reject(request.Reason);
        if (result.IsFailure)
            return Result.Failure<ItemHsMappingResponse>(result.Error);

        await repository.SaveAsync(mapping, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(DutyResponseFactory.ToResponse(mapping));
    }
}