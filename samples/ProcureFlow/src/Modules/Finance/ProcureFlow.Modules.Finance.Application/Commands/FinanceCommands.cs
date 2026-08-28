using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Finance.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Finance.Application.Commands;

public sealed record CreateApInvoiceCommand(
    string InvoiceNumber,
    Guid VendorId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Currency,
    decimal TotalAmount,
    ApInvoiceSource Source,
    bool IsCreditNote,
    List<InvoiceLineDto> Lines
) : ICommand<Result<Guid>>;

public sealed record InvoiceLineDto(
    Guid PoLineId,
    Guid? GrnLineId,
    string Description,
    decimal Quantity,
    string Uom,
    decimal UnitPrice,
    decimal LineTotal
);

public sealed record SubmitApInvoiceCommand(Guid InvoiceId) : ICommand<Result>;

public sealed record ApproveApInvoiceCommand(Guid InvoiceId) : ICommand<Result>;

public sealed record CancelApInvoiceCommand(Guid InvoiceId, string Reason) : ICommand<Result>;

public sealed record CreatePaymentProposalCommand(
    string ProposalNumber,
    DateOnly PaymentDate,
    string Currency,
    decimal TotalAmount,
    List<Guid> InvoiceIds
) : ICommand<Result<Guid>>;

public sealed record ApprovePaymentProposalCommand(Guid ProposalId) : ICommand<Result>;

public sealed record ExportPaymentProposalCommand(Guid ProposalId) : ICommand<Result>;

public sealed record CreateJournalBatchCommand(
    string JournalNumber,
    DateOnly PostingDate,
    string Description,
    string Currency,
    List<JournalLineDto> Lines
) : ICommand<Result<Guid>>;

public sealed record JournalLineDto(
    string AccountCode,
    string AccountName,
    string Description,
    decimal Debit,
    decimal Credit,
    Guid? CostCenterId
);

public sealed record PostJournalBatchCommand(Guid JournalId) : ICommand<Result>;

public sealed record CreateFxRateCommand(
    DateOnly EffectiveDate,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    FxSource Source,
    string? SourceReference
) : ICommand<Result<Guid>>;

public sealed record CreateCostCenterCommand(
    string Code,
    string Name,
    Guid? ParentId,
    bool IsActive
) : ICommand<Result<Guid>>;

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

public sealed record ApproveMatchExceptionCommand(
    Guid ExceptionId,
    string? Notes
) : ICommand<Result>;

public sealed record RejectMatchExceptionCommand(
    Guid ExceptionId,
    string Reason
) : ICommand<Result>;

public sealed record OverrideMatchExceptionCommand(
    Guid ExceptionId,
    string Reason
) : ICommand<Result>;

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────

public sealed record CreateGrIrAccrualCommand(
    Guid GrnId,
    Guid? PoId,
    Guid VendorId,
    string GrnNumber,
    DateOnly ReceivedOn,
    decimal Amount,
    string Currency
) : ICommand<Result<Guid>>;

public sealed record ClearGrIrAccrualCommand(
    Guid AccrualId,
    Guid InvoiceId,
    DateOnly ClearedOn
) : ICommand<Result>;

// ── 3-Way Match (BR-FIN-12) ────────────────────────────────────────

public sealed record PerformThreeWayMatchCommand(
    Guid InvoiceId,
    List<ThreeWayMatchLineInput> LineInputs
) : ICommand<Result<ThreeWayMatchResult>>;

public sealed record ThreeWayMatchLineInput(
    Guid InvoiceLineId,
    Guid PoLineId,
    Guid? GrnLineId,
    decimal PoQuantity,
    decimal PoUnitPrice,
    decimal GrnReceivedQty
);

public sealed record ThreeWayMatchResult(
    int TotalLines,
    int MatchedLines,
    int ExceptionLines,
    IReadOnlyList<ThreeWayMatchExceptionDto> Exceptions
);

public sealed record ThreeWayMatchExceptionDto(
    Guid Id,
    Guid InvoiceLineId,
    string Type,
    decimal InvoiceQty,
    decimal MatchedQty,
    decimal InvoicePrice,
    decimal MatchedPrice,
    string Description,
    string Status
);