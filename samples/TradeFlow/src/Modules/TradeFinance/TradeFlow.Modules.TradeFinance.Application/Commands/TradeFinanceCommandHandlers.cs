using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.TradeFinance.Application.Commands;
using TradeFlow.Modules.TradeFinance.Application.Dtos;
using TradeFlow.Modules.TradeFinance.Domain.Entities;
using TradeFlow.Modules.TradeFinance.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.TradeFinance.Application.Commands;

public sealed class CreateLcHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateLcCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(CreateLcCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        if (await repository.ExistsByNumberAsync(tenantId, request.LcNumber, ct))
            return Result.Failure<LetterOfCreditResponse>(Error.Conflict("Lc.Duplicate", "LC number already exists"));

        var lc = LetterOfCredit.Create(tenantId, request.FileId, request.PoId, request.LcNumber, request.Type,
            request.Currency, request.Amount, request.TolerancePct, request.ApplicantCompanyId,
            request.BeneficiaryVendorId, request.BeneficiaryName, request.IssuingBankId,
            request.LatestShipmentDate, request.ExpiryDate, request.Incoterm, request.PortOfLoading,
            request.PortOfDischarge, request.PartialShipmentAllowed, request.TransshipmentAllowed,
            request.MarginPct, request.BookingFxRate, currentUser.UserName ?? "system");

        await repository.AddAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class SubmitLcApplicationHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitLcApplicationCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(SubmitLcApplicationCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result prerequisites = lc.CheckPrerequisites(request.Prerequisites);
        if (prerequisites.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(prerequisites.Error);

        Result terms = lc.ValidateTerms(request.Terms);
        if (terms.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(terms.Error);

        Result submit = lc.SubmitForApproval();
        if (submit.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(submit.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class ApproveLcApplicationHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ApproveLcApplicationCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(ApproveLcApplicationCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result approve = lc.ApproveApplication(currentUser.UserName ?? "system");
        if (approve.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(approve.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class IssueLcHandler(
    ILcRepository repository,
    IBankFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<IssueLcCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(IssueLcCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        BankFacility? facility = await facilityRepository.GetByBankAsync(lc.TenantId, lc.IssuingBankId, ct);
        decimal available = facility?.Available ?? request.FacilityAvailable;

        Result issue = lc.Issue(currentUser.UserName ?? "system", available, request.FacilityOverride);
        if (issue.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(issue.Error);

        // BR-LC-05: reserve facility exposure for the opened LC.
        if (facility is not null)
        {
            Result reserve = facility.Reserve(lc.Amount, lc.Id, lc.LcNumber, "LC opening exposure (BR-LC-05)");
            if (reserve.IsFailure)
                return Result.Failure<LetterOfCreditResponse>(reserve.Error);
            await facilityRepository.SaveAsync(facility, ct);
        }

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class RequestLcAmendmentHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RequestLcAmendmentCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(RequestLcAmendmentCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result amendment = lc.RequestAmendment(request.ValueDelta, request.TenorIncreasing,
            request.ReasonCode, request.Reason, currentUser.UserName ?? "system");
        if (amendment.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(amendment.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class ApproveLcAmendmentHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<ApproveLcAmendmentCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(ApproveLcAmendmentCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result approve = lc.ApproveAmendment(request.AmendmentId, currentUser.UserName ?? "system");
        if (approve.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(approve.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class PresentLcHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<PresentLcCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(PresentLcCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result present = lc.Present(request.PresentationNo, request.DocumentRefs, currentUser.UserName ?? "system");
        if (present.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(present.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class LogLcDiscrepancyHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<LogLcDiscrepancyCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(LogLcDiscrepancyCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result discrepancy = lc.LogDiscrepancy(request.PresentationId, request.Code, request.Description);
        if (discrepancy.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(discrepancy.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class AcceptLcPresentationHandler(
    ILcRepository repository,
    IPaymentObligationRepository obligationRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<AcceptLcPresentationCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(AcceptLcPresentationCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result accept = lc.AcceptPresentation(request.PresentationId, request.AcceptanceDate);
        if (accept.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(accept.Error);

        // BR-OBL-01: acceptance creates a maturity obligation on the calendar.
        MaturityObligation maturity = lc.Maturities.Single(m => m.Status == MaturityStatus.Open);
        await obligationRepository.AddAsync(PaymentObligation.Create(
            lc.TenantId, "LcMaturity", lc.Id, lc.LcNumber, maturity.DueDate, maturity.Amount, lc.Currency), ct);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class RefuseLcPresentationHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RefuseLcPresentationCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(RefuseLcPresentationCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result refuse = lc.RefusePresentation(request.PresentationId);
        if (refuse.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(refuse.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class RetireLcHandler(
    ILcRepository repository,
    IBankFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RetireLcCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(RetireLcCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result retire = lc.Retire(request.RealizedFxRate, request.Reason, currentUser.UserName ?? "system");
        if (retire.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(retire.Error);

        BankFacility? facility = await facilityRepository.GetByBankAsync(lc.TenantId, lc.IssuingBankId, ct);
        if (facility is not null)
        {
            Result release = facility.Release(lc.Amount, lc.Id, "LC retirement releases exposure (BR-LC-05)");
            if (release.IsFailure)
                return Result.Failure<LetterOfCreditResponse>(release.Error);
            await facilityRepository.SaveAsync(facility, ct);
        }

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class CloseExpiredLcHandler(
    ILcRepository repository,
    IBankFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<CloseExpiredLcCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(CloseExpiredLcCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result close = lc.CloseExpired(request.AsOfDate);
        if (close.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(close.Error);

        // BR-LC-09: expired-undrawn closure releases facility exposure.
        BankFacility? facility = await facilityRepository.GetByBankAsync(lc.TenantId, lc.IssuingBankId, ct);
        if (facility is not null)
        {
            Result release = facility.Release(lc.Amount, lc.Id, "Expired-undrawn LC closure (BR-LC-09)");
            if (release.IsFailure)
                return Result.Failure<LetterOfCreditResponse>(release.Error);
            await facilityRepository.SaveAsync(facility, ct);
        }

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class AddLcChargeHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddLcChargeCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(AddLcChargeCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        lc.AddCharge(request.Type, request.Amount, request.Currency, request.RefDoc);
        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class CancelLcHandler(
    ILcRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CancelLcCommand, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(CancelLcCommand request, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(request.LcId, ct);
        if (lc is null)
            return Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"));

        Result cancel = lc.Cancel(request.Reason);
        if (cancel.IsFailure)
            return Result.Failure<LetterOfCreditResponse>(cancel.Error);

        await repository.SaveAsync(lc, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class CreateTtHandler(
    ITtRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateTtCommand, Result<TtPaymentResponse>>
{
    public async Task<Result<TtPaymentResponse>> HandleAsync(CreateTtCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        if (await repository.ExistsByNumberAsync(tenantId, request.TtNumber, ct))
            return Result.Failure<TtPaymentResponse>(Error.Conflict("Tt.Duplicate", "TT number already exists"));

        var tt = TtPayment.Create(tenantId, request.FileId, request.PoId, request.TtNumber, request.VendorId,
            request.BeneficiaryName, request.Currency, request.Amount, request.ScheduleType, request.BankRef,
            currentUser.UserName ?? "system");

        await repository.AddAsync(tt, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToTtResponse(tt));
    }
}

public sealed class ExecuteTtHandler(
    ITtRepository repository,
    IPaymentObligationRepository obligationRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<ExecuteTtCommand, Result<TtPaymentResponse>>
{
    public async Task<Result<TtPaymentResponse>> HandleAsync(ExecuteTtCommand request, CancellationToken ct)
    {
        TtPayment? tt = await repository.GetByIdAsync(request.TtId, ct);
        if (tt is null)
            return Result.Failure<TtPaymentResponse>(Error.NotFound("Tt.NotFound", "TT not found"));

        Result execute = tt.Execute(request.ValueDate, request.FxRate, request.Charges);
        if (execute.IsFailure)
            return Result.Failure<TtPaymentResponse>(execute.Error);

        // BR-OBL-01: scheduled TT lines become calendar obligations.
        await obligationRepository.AddAsync(PaymentObligation.Create(
            tt.TenantId, "TtSchedule", tt.Id, tt.TtNumber, request.ValueDate, tt.Amount, tt.Currency), ct);

        await repository.SaveAsync(tt, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToTtResponse(tt));
    }
}

public sealed class MatchShipmentHandler(
    ITtRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<MatchShipmentCommand, Result<TtPaymentResponse>>
{
    public async Task<Result<TtPaymentResponse>> HandleAsync(MatchShipmentCommand request, CancellationToken ct)
    {
        TtPayment? tt = await repository.GetByIdAsync(request.TtId, ct);
        if (tt is null)
            return Result.Failure<TtPaymentResponse>(Error.NotFound("Tt.NotFound", "TT not found"));

        Result match = tt.MatchShipment();
        if (match.IsFailure)
            return Result.Failure<TtPaymentResponse>(match.Error);

        await repository.SaveAsync(tt, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToTtResponse(tt));
    }
}

public sealed class CancelTtHandler(
    ITtRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CancelTtCommand, Result<TtPaymentResponse>>
{
    public async Task<Result<TtPaymentResponse>> HandleAsync(CancelTtCommand request, CancellationToken ct)
    {
        TtPayment? tt = await repository.GetByIdAsync(request.TtId, ct);
        if (tt is null)
            return Result.Failure<TtPaymentResponse>(Error.NotFound("Tt.NotFound", "TT not found"));

        Result cancel = tt.Cancel(request.Reason);
        if (cancel.IsFailure)
            return Result.Failure<TtPaymentResponse>(cancel.Error);

        await repository.SaveAsync(tt, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(TradeFinanceResponseFactory.ToTtResponse(tt));
    }
}

public sealed class RegisterSwiftMessageHandler(
    ISwiftMessageRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<RegisterSwiftMessageCommand, Result<SwiftMessageResponse>>
{
    public async Task<Result<SwiftMessageResponse>> HandleAsync(RegisterSwiftMessageCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var message = SwiftMessage.Create(tenantId, request.MtType, request.Reference, request.Direction,
            request.LinkedLcId, request.LinkedTtId, request.ContentRef);

        await repository.AddAsync(message, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new SwiftMessageResponse(message.Id, message.TenantId, message.MtType,
            message.Reference, message.Direction, message.LinkedLcId, message.LinkedTtId,
            message.ContentRef, message.IsMatched));
    }
}

public sealed class CreateBankFacilityHandler(
    IBankFacilityRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateBankFacilityCommand, Result<BankFacilityResponse>>
{
    public async Task<Result<BankFacilityResponse>> HandleAsync(CreateBankFacilityCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var facility = BankFacility.Create(tenantId, request.BankId, request.LimitAmount, request.Currency);

        await repository.AddAsync(facility, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new BankFacilityResponse(facility.Id, facility.TenantId, facility.BankId,
            facility.LimitAmount, facility.Currency, facility.Outstanding, facility.Available));
    }
}

public sealed class MarkObligationsOverdueHandler(
    IPaymentObligationRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<MarkObligationsOverdueCommand, Result>
{
    public async Task<Result> HandleAsync(MarkObligationsOverdueCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<PaymentObligation> overdue = await repository.GetOverdueAsync(tenantId, request.AsOfDate, ct);

        foreach (PaymentObligation obligation in overdue)
        {
            obligation.MarkOverdue(request.AsOfDate);
            int days = request.AsOfDate.DayNumber - obligation.DueDate.DayNumber;
            obligation.Notify(days);
        }

        await unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}