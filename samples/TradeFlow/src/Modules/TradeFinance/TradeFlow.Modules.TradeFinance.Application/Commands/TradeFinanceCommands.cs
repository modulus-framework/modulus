using TradeFlow.Modules.TradeFinance.Application.Dtos;
using TradeFlow.Modules.TradeFinance.Domain.Entities;
using TradeFlow.Modules.TradeFinance.Domain.Rules;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.TradeFinance.Application.Commands;

public sealed record CreateLcCommand(
    Guid? FileId,
    Guid? PoId,
    string LcNumber,
    LcType Type,
    string Currency,
    decimal Amount,
    decimal TolerancePct,
    Guid ApplicantCompanyId,
    Guid BeneficiaryVendorId,
    string BeneficiaryName,
    Guid IssuingBankId,
    DateOnly LatestShipmentDate,
    DateOnly ExpiryDate,
    string Incoterm,
    string PortOfLoading,
    string PortOfDischarge,
    bool PartialShipmentAllowed,
    bool TransshipmentAllowed,
    decimal MarginPct,
    decimal BookingFxRate) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record SubmitLcApplicationCommand(
    Guid LcId,
    LcPrerequisiteInput Prerequisites,
    LcTermConsistencyInput Terms) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record ApproveLcApplicationCommand(Guid LcId) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record IssueLcCommand(
    Guid LcId,
    decimal FacilityAvailable,
    bool FacilityOverride) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record RequestLcAmendmentCommand(
    Guid LcId,
    decimal? ValueDelta,
    bool TenorIncreasing,
    string ReasonCode,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record ApproveLcAmendmentCommand(
    Guid LcId,
    Guid AmendmentId) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record PresentLcCommand(
    Guid LcId,
    string PresentationNo,
    IReadOnlyList<string> DocumentRefs) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record LogLcDiscrepancyCommand(
    Guid LcId,
    Guid PresentationId,
    string Code,
    string Description) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record AcceptLcPresentationCommand(
    Guid LcId,
    Guid PresentationId,
    DateOnly AcceptanceDate) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record RefuseLcPresentationCommand(
    Guid LcId,
    Guid PresentationId) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record RetireLcCommand(
    Guid LcId,
    decimal? RealizedFxRate,
    string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record CloseExpiredLcCommand(
    Guid LcId,
    DateOnly AsOfDate) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record AddLcChargeCommand(
    Guid LcId,
    LcChargeType Type,
    decimal Amount,
    string Currency,
    string? RefDoc) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record CancelLcCommand(Guid LcId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<LetterOfCreditResponse>>;

public sealed record CreateTtCommand(
    Guid? FileId,
    Guid? PoId,
    string TtNumber,
    Guid VendorId,
    string BeneficiaryName,
    string Currency,
    decimal Amount,
    TtScheduleType ScheduleType,
    string BankRef) : Modulus.Mediator.Abstractions.ICommand<Result<TtPaymentResponse>>;

public sealed record ExecuteTtCommand(
    Guid TtId,
    DateOnly ValueDate,
    decimal FxRate,
    decimal Charges) : Modulus.Mediator.Abstractions.ICommand<Result<TtPaymentResponse>>;

public sealed record MatchShipmentCommand(Guid TtId) : Modulus.Mediator.Abstractions.ICommand<Result<TtPaymentResponse>>;

public sealed record CancelTtCommand(Guid TtId, string Reason) : Modulus.Mediator.Abstractions.ICommand<Result<TtPaymentResponse>>;

public sealed record RegisterSwiftMessageCommand(
    string MtType,
    string Reference,
    string Direction,
    Guid? LinkedLcId,
    Guid? LinkedTtId,
    string? ContentRef) : Modulus.Mediator.Abstractions.ICommand<Result<SwiftMessageResponse>>;

public sealed record CreateBankFacilityCommand(
    Guid BankId,
    decimal LimitAmount,
    string Currency) : Modulus.Mediator.Abstractions.ICommand<Result<BankFacilityResponse>>;

public sealed record MarkObligationsOverdueCommand(
    DateOnly AsOfDate) : Modulus.Mediator.Abstractions.ICommand<Result>;