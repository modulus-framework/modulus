using TradeFlow.Shared.Application.Abstractions.Gateways;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Procurement.Application.Commands;
using TradeFlow.Modules.Procurement.Application.Dtos;
using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Modules.Procurement.Domain.Repositories;
using TradeFlow.Modules.Vendors.PublicApi;
using TradeFlow.Shared.Application.Abstractions.Import;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Application.Commands;

public sealed class CreatePrHandler(
    IPrRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreatePrCommand, Result<PurchaseRequisitionResponse>>
{
    public async Task<Result<PurchaseRequisitionResponse>> HandleAsync(CreatePrCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        if (await repository.ExistsByNumberAsync(tenantId, request.PrNumber, ct))
            return Result.Failure<PurchaseRequisitionResponse>(Error.Conflict("Pr.Duplicate", "PR number already exists"));

        var pr = PurchaseRequisition.Create(tenantId, request.PrNumber, currentUser.UserName ?? "system");
        foreach (PrLineInput line in request.Lines)
        {
            pr.AddLine(new PrLine(Guid.NewGuid(), line.ItemId, line.FreeText, line.Category, line.Quantity,
                line.Uom, line.NeedByDate, line.SuggestedVendorId, line.EstimatedUnitPrice, line.Currency, line.Notes));
        }

        await repository.AddAsync(pr, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPrResponse(pr));
    }
}

public sealed class SubmitPrHandler(
    IPrRepository repository,
    IBudgetGateway budgetGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<SubmitPrCommand, Result<PurchaseRequisitionResponse>>
{
    public async Task<Result<PurchaseRequisitionResponse>> HandleAsync(SubmitPrCommand request, CancellationToken ct)
    {
        PurchaseRequisition? pr = await repository.GetByIdAsync(request.PrId, ct);
        if (pr is null)
            return Result.Failure<PurchaseRequisitionResponse>(Error.NotFound("Pr.NotFound", "PR not found"));

        Result submit = pr.Submit(request.CategoryLeadTimeDays);
        if (submit.IsFailure)
            return Result.Failure<PurchaseRequisitionResponse>(submit.Error);

        string? category = pr.Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Category))?.Category;
        decimal amount = pr.EstimatedTotal;
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        if (category is not null && amount > 0m)
        {
            Result budget = await budgetGateway.CheckAvailabilityAsync(new BudgetCheckRequest(
                tenantId, request.FiscalYear, request.CostCenterId, category, amount, pr.Lines[0].Currency, pr.Id), ct);

            if (budget.IsFailure)
            {
                pr.MarkBudgetFailed(budget.Error.Message);
            }
        }

        await repository.SaveAsync(pr, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPrResponse(pr));
    }
}

public sealed class ApprovePrHandler(
    IPrRepository repository,
    IBudgetGateway budgetGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<ApprovePrCommand, Result<PurchaseRequisitionResponse>>
{
    public async Task<Result<PurchaseRequisitionResponse>> HandleAsync(ApprovePrCommand request, CancellationToken ct)
    {
        PurchaseRequisition? pr = await repository.GetByIdAsync(request.PrId, ct);
        if (pr is null)
            return Result.Failure<PurchaseRequisitionResponse>(Error.NotFound("Pr.NotFound", "PR not found"));

        Result approve = pr.Approve();
        if (approve.IsFailure)
            return Result.Failure<PurchaseRequisitionResponse>(approve.Error);

        string? category = pr.Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Category))?.Category;
        if (category is not null && pr.EstimatedTotal > 0m)
        {
            // BR-PR-02: reserve the budget at approval.
            Result reserve = await budgetGateway.ReserveAsync(new BudgetLedgerRequest(
                currentTenant.TenantId ?? Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow).Year,
                Guid.Empty, category, pr.EstimatedTotal, pr.Lines[0].Currency, pr.Id, pr.PrNumber,
                currentUser.UserId ?? Guid.Empty), ct);
            if (reserve.IsFailure)
                return Result.Failure<PurchaseRequisitionResponse>(reserve.Error);
        }

        await repository.SaveAsync(pr, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPrResponse(pr));
    }
}

public sealed class RejectPrHandler(
    IPrRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectPrCommand, Result<PurchaseRequisitionResponse>>
{
    public async Task<Result<PurchaseRequisitionResponse>> HandleAsync(RejectPrCommand request, CancellationToken ct)
    {
        PurchaseRequisition? pr = await repository.GetByIdAsync(request.PrId, ct);
        if (pr is null)
            return Result.Failure<PurchaseRequisitionResponse>(Error.NotFound("Pr.NotFound", "PR not found"));

        Result reject = pr.Reject(request.Reason);
        if (reject.IsFailure)
            return Result.Failure<PurchaseRequisitionResponse>(reject.Error);

        await repository.SaveAsync(pr, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPrResponse(pr));
    }
}

public sealed class CancelPrHandler(
    IPrRepository repository,
    IBudgetGateway budgetGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CancelPrCommand, Result<PurchaseRequisitionResponse>>
{
    public async Task<Result<PurchaseRequisitionResponse>> HandleAsync(CancelPrCommand request, CancellationToken ct)
    {
        PurchaseRequisition? pr = await repository.GetByIdAsync(request.PrId, ct);
        if (pr is null)
            return Result.Failure<PurchaseRequisitionResponse>(Error.NotFound("Pr.NotFound", "PR not found"));

        Result cancel = pr.Cancel(request.Reason);
        if (cancel.IsFailure)
            return Result.Failure<PurchaseRequisitionResponse>(cancel.Error);

        // BR-PR-05: cancellation releases the reservation.
        await budgetGateway.ReleaseAsync(new BudgetReleaseRequest(
            currentTenant.TenantId ?? Guid.Empty, pr.Id, request.Reason, currentUser.UserId ?? Guid.Empty), ct);

        await repository.SaveAsync(pr, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPrResponse(pr));
    }
}

public sealed class CreateRfqHandler(
    IRfqRepository repository,
    IVendorPublicApi vendorGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateRfqCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(CreateRfqCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        if (await repository.ExistsByNumberAsync(tenantId, request.RfqNumber, ct))
            return Result.Failure<RfqResponse>(Error.Conflict("Rfq.Duplicate", "RFQ number already exists"));

        var rfq = Rfq.Create(tenantId, request.RfqNumber, request.Title, request.IsSealed,
            request.DeadlineUtc, request.MinBidders, request.Currency, currentUser.UserName ?? "system");

        foreach (RfqLineInput line in request.Lines)
        {
            rfq.AddLine(new RfqLine(Guid.NewGuid(), line.PrLineId, line.ItemId, line.FreeText, line.HsCode,
                line.Quantity, line.Uom, line.PortOfLoading, line.PortOfDischarge));
        }

        foreach (Guid vendorId in request.InvitedVendorIds.Distinct())
        {
            // BR-SRC-02: invited vendors must be qualified for the category.
            if (!await vendorGateway.IsQualifiedForCategoryAsync(vendorId, request.Lines.FirstOrDefault()?.FreeText ?? "", ct))
                return Result.Failure<RfqResponse>(Error.Validation("Rfq.Vendor.NotQualified",
                    $"Vendor {vendorId} is not qualified for the category (BR-SRC-02)"));
            rfq.Invite(vendorId);
        }

        await repository.AddAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class OpenRfqHandler(
    IRfqRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<OpenRfqCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(OpenRfqCommand request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        if (rfq is null)
            return Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"));

        Result open = rfq.Open();
        if (open.IsFailure)
            return Result.Failure<RfqResponse>(open.Error);

        await repository.SaveAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class SubmitBidHandler(
    IRfqRepository repository,
    IVendorPublicApi vendorGateway,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitBidCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(SubmitBidCommand request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        if (rfq is null)
            return Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"));

        if (!await vendorGateway.IsVendorEligibleAsync(request.VendorId, ct))
            return Result.Failure<RfqResponse>(Error.Validation("Rfq.Vendor.NotEligible",
                "Vendor is not active/eligible (BR-VEN-02)"));

        bool isLate = DateTime.UtcNow > rfq.DeadlineUtc;
        var bid = new RfqBid(Guid.NewGuid(), request.VendorId, request.BidNo, request.TotalAmountFcy,
            request.Currency, DateTime.UtcNow, isLate, request.Notes);

        Result submit = rfq.SubmitBid(bid);
        if (submit.IsFailure)
            return Result.Failure<RfqResponse>(submit.Error);

        await repository.SaveAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class ComputeRfqComparisonHandler(
    IRfqRepository repository,
    IDutyCalculationGateway dutyGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<ComputeRfqComparisonCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(ComputeRfqComparisonCommand request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        if (rfq is null)
            return Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"));

        if (rfq.Bids.Count == 0)
            return Result.Failure<RfqResponse>(Error.Validation("Rfq.NoBids", "Cannot compare before any bids are submitted"));

        var rows = new List<RfqComparisonRow>();
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        decimal totalQty = rfq.Lines.Sum(l => l.Quantity);

        foreach (RfqBid bid in rfq.Bids)
        {
            decimal freightBdt = bid.TotalAmountFcy * request.FreightPctOfFob;
            decimal handlingBdt = bid.TotalAmountFcy * request.HandlingPctOfFob;
            decimal dutyBdt = 0m;

            // BR-SRC-05: normalize imports to landed-cost basis via the duty engine.
            foreach (RfqLine line in rfq.Lines.Where(l => l.IsImport))
            {
                decimal unitPrice = totalQty > 0m ? bid.TotalAmountFcy / totalQty : 0m;
                DutyEstimateResult estimate = await dutyGateway.EstimateAsync(new DutyEstimateRequest(
                    tenantId, line.HsCode!, line.Quantity, unitPrice, bid.Currency,
                    request.CustomsFxRate, 0m, 0m, DateOnly.FromDateTime(DateTime.UtcNow)), ct);
                dutyBdt += estimate.TotalDutyBdt;
            }

            rows.Add(new RfqComparisonRow(bid.Id, bid.VendorId, bid.TotalAmountFcy, bid.Currency,
                Math.Round(freightBdt, 2), Math.Round(dutyBdt, 2), Math.Round(handlingBdt, 2),
                Math.Round(bid.TotalAmountFcy * request.CustomsFxRate + freightBdt + dutyBdt + handlingBdt, 2)));
        }

        rfq.ReplaceComparison(rows);
        await repository.SaveAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class AwardRfqHandler(
    IRfqRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<AwardRfqCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(AwardRfqCommand request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        if (rfq is null)
            return Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"));

        Result award = rfq.AwardTo(request.VendorId, request.AmountFcy, rfq.Currency, request.SplitPercent,
            request.Justification, currentUser.UserName ?? "system");
        if (award.IsFailure)
            return Result.Failure<RfqResponse>(award.Error);

        await repository.SaveAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class ApproveRfqAwardHandler(
    IRfqRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ApproveRfqAwardCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(ApproveRfqAwardCommand request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        if (rfq is null)
            return Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"));

        rfq.ApproveCfo(currentUser.UserName ?? "system");
        await repository.SaveAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class CancelRfqHandler(
    IRfqRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CancelRfqCommand, Result<RfqResponse>>
{
    public async Task<Result<RfqResponse>> HandleAsync(CancelRfqCommand request, CancellationToken ct)
    {
        Rfq? rfq = await repository.GetByIdAsync(request.RfqId, ct);
        if (rfq is null)
            return Result.Failure<RfqResponse>(Error.NotFound("Rfq.NotFound", "RFQ not found"));

        Result cancel = rfq.Cancel(request.Reason);
        if (cancel.IsFailure)
            return Result.Failure<RfqResponse>(cancel.Error);

        await repository.SaveAsync(rfq, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToRfqResponse(rfq));
    }
}

public sealed class CreatePoHandler(
    IPoRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreatePoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(CreatePoCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        if (await repository.ExistsByNumberAsync(tenantId, request.PoNumber, ct))
            return Result.Failure<PurchaseOrderResponse>(Error.Conflict("Po.Duplicate", "PO number already exists"));

        var po = PurchaseOrder.Create(tenantId, request.PoNumber, request.Source, request.VendorId,
            request.Currency, request.Incoterm, request.PaymentMode, request.LatestShipmentDate,
            request.PartialShipmentAllowed, request.TransshipmentAllowed, request.PsiRequired,
            currentUser.UserName ?? "system", request.PortOfLoading, request.PortOfDischarge,
            request.ShipmentTolerancePct, request.ReceivedTolerancePct);

        foreach (PoLineInput line in request.Lines)
        {
            po.AddLine(new PoLine(Guid.NewGuid(), line.ItemId, line.FreeText, line.HsCode, line.Quantity,
                line.Uom, line.UnitPrice, 0m, line.Notes));
        }

        await repository.AddAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class SubmitPoHandler(
    IPoRepository repository,
    IFeasibilityEngine feasibilityEngine,
    IVendorPublicApi vendorGateway,
    IBudgetGateway budgetGateway,
    IDutyCalculationGateway dutyGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<SubmitPoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(SubmitPoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result importValidation = po.ValidateImportFields();
        if (importValidation.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(importValidation.Error);

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        // BR-PO-03 feasibility gate: gather signals and snapshot the verdict.
        bool vendorEligible = await vendorGateway.IsVendorEligibleAsync(po.VendorId, ct);
        decimal budgetHeadroom = 1.0m;
        Result budget = await budgetGateway.CheckAvailabilityAsync(new BudgetCheckRequest(
            tenantId, request.BudgetFiscalYear, request.BudgetCostCenterId, request.BudgetCategory,
            po.TotalAmount, po.Currency, po.Id), ct);
        if (budget.IsFailure)
            budgetHeadroom = 0.2m;

        decimal dutyExposure = 0m;
        PoLine? firstImport = po.Lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.HsCode));
        if (firstImport is not null && po.TotalAmount > 0m)
        {
            try
            {
                DutyEstimateResult estimate = await dutyGateway.EstimateAsync(new DutyEstimateRequest(
                    tenantId, firstImport.HsCode!, firstImport.Quantity, firstImport.UnitPrice, po.Currency,
                    1.0m, 0m, 0m, DateOnly.FromDateTime(DateTime.UtcNow)), ct);
                dutyExposure = estimate.TotalDutyBdt / po.TotalAmount;
            }
            catch (Exception)
            {
                dutyExposure = 0m;
            }
        }

        // Build enhanced feasibility input per doc 07 §7.3
        var lineInputs = po.Lines.Select(l => new FeasibilityLineInput(
            l.Id, l.HsCode ?? string.Empty, l.Quantity, l.UnitPrice,
            null, null, null, null)).ToList();

        var feasibilityInput = new FeasibilityInput(
            VendorEligible: vendorEligible,
            VendorScorecardAverage: 50m,
            BudgetHeadroomRatio: budgetHeadroom,
            EstimatedDutyExposureRatio: dutyExposure,
            VendorLeadTimeDays: 30,
            LcFacilityAvailable: true,
            PoValueBdt: po.TotalAmount,
            Lines: lineInputs);

        FeasibilityResult feasibility = feasibilityEngine.Evaluate(feasibilityInput);

        // Map factor/risk/counterfactual details to domain entities for persistence
        var factorDetails = feasibility.Factors?.Select(f =>
            new FeasibilityFactorDetail(f.Name, f.RawValue, f.NormalizedScore,
                f.WeightedContribution, f.Description)).ToList();
        var riskFlagDetails = feasibility.RiskFlags?.Select(r =>
            new FeasibilityRiskFlagDetail(r.Category, r.Message, r.Severity)).ToList();
        var counterfactualDetails = feasibility.Counterfactuals?.Select(c =>
            new FeasibilityCounterfactualDetail(c.Description, c.EstimatedScoreDelta, c.EstimatedCostDelta)).ToList();

        var snapshot = new FeasibilitySnapshot(
            feasibility.Score, feasibility.Verdict.ToString(),
            feasibility.Reasons, factorDetails, riskFlagDetails,
            counterfactualDetails, feasibility.NormalizedWeights, DateTime.UtcNow);

        Result submit = po.Submit(snapshot, feasibility.RequiresCfoOverride);
        if (submit.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(submit.Error);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class RecordCfoOverrideHandler(
    IPoRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RecordCfoOverrideCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(RecordCfoOverrideCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result overrideResult = po.RecordCfoOverride(request.Reason, currentUser.UserName ?? "system");
        if (overrideResult.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(overrideResult.Error);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class ApprovePoHandler(
    IPoRepository repository,
    IBudgetGateway budgetGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<ApprovePoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(ApprovePoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result approve = po.Approve();
        if (approve.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(approve.Error);

        // BR-PO-05: budget moves reservation → commitment at approval.
        Result commit = await budgetGateway.CommitAsync(new BudgetLedgerRequest(
            currentTenant.TenantId ?? Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow).Year,
            Guid.Empty, "Procurement", po.TotalAmount, po.Currency, po.Id, po.PoNumber,
            currentUser.UserId ?? Guid.Empty), ct);
        if (commit.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(commit.Error);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class DispatchPoHandler(
    IPoRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<DispatchPoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(DispatchPoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result dispatch = po.Dispatch();
        if (dispatch.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(dispatch.Error);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class ReceivePoHandler(
    IPoRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReceivePoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(ReceivePoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result receive = po.Receive(request.LineId, request.Quantity);
        if (receive.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(receive.Error);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class RevisePoHandler(
    IPoRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RevisePoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(RevisePoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result revise = po.Revise(request.NewTotalDelta, request.Reason, currentUser.UserName ?? "system");
        if (revise.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(revise.Error);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class ForceClosePoHandler(
    IPoRepository repository,
    IBudgetGateway budgetGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<ForceClosePoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(ForceClosePoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result close = po.ForceClose(request.Reason, currentUser.UserName ?? "system");
        if (close.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(close.Error);

        // BR-PO-06: force-close releases the residual commitment.
        await budgetGateway.ReleaseAsync(new BudgetReleaseRequest(
            currentTenant.TenantId ?? Guid.Empty, po.Id, request.Reason, currentUser.UserId ?? Guid.Empty), ct);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}

public sealed class CancelPoHandler(
    IPoRepository repository,
    IBudgetGateway budgetGateway,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CancelPoCommand, Result<PurchaseOrderResponse>>
{
    public async Task<Result<PurchaseOrderResponse>> HandleAsync(CancelPoCommand request, CancellationToken ct)
    {
        PurchaseOrder? po = await repository.GetByIdAsync(request.PoId, ct);
        if (po is null)
            return Result.Failure<PurchaseOrderResponse>(Error.NotFound("Po.NotFound", "PO not found"));

        Result cancel = po.Cancel(request.Reason, currentUser.UserName ?? "system");
        if (cancel.IsFailure)
            return Result.Failure<PurchaseOrderResponse>(cancel.Error);

        await budgetGateway.ReleaseAsync(new BudgetReleaseRequest(
            currentTenant.TenantId ?? Guid.Empty, po.Id, request.Reason, currentUser.UserId ?? Guid.Empty), ct);

        await repository.SaveAsync(po, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ProcurementResponseFactory.ToPoResponse(po));
    }
}