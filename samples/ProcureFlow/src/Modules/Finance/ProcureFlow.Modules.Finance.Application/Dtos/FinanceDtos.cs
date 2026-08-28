using ProcureFlow.Modules.Finance.Domain.Entities;

namespace ProcureFlow.Modules.Finance.Application.Dtos;

public sealed record ApInvoiceDto(
    Guid Id,
    Guid TenantId,
    string InvoiceNumber,
    Guid VendorId,
    DateOnly InvoiceDate,
    DateOnly? ReceivedDate,
    DateOnly DueDate,
    string Currency,
    decimal TotalAmount,
    ApInvoiceSource Source,
    bool IsCreditNote,
    ApInvoiceStatus Status,
    string? CancelReason,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string CreatedBy,
    DateTime CreatedAtUtc,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    List<InvoiceLineDto> Lines
);

public sealed record InvoiceLineDto(
    Guid Id,
    Guid PoLineId,
    Guid? GrnLineId,
    string Description,
    decimal Quantity,
    string Uom,
    decimal UnitPrice,
    decimal LineTotal,
    InvoiceLineMatchStatus MatchStatus,
    string? MatchReason
);

public sealed record PaymentProposalDto(
    Guid Id,
    Guid TenantId,
    string ProposalNumber,
    DateOnly PaymentDate,
    string Currency,
    decimal TotalAmount,
    PaymentProposalStatus Status,
    string CreatedBy,
    DateTime CreatedAtUtc,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    List<Guid> InvoiceIds
);

public sealed record ApPaymentDto(
    Guid Id,
    Guid InvoiceId,
    decimal Amount,
    DateOnly PaymentDate,
    string ReferenceNumber,
    PaymentStatus Status,
    DateTime CreatedAtUtc,
    string? BankReference,
    DateTime? ClearedAtUtc
);

public sealed record JournalBatchDto(
    Guid Id,
    Guid TenantId,
    string JournalNumber,
    DateOnly PostingDate,
    string Description,
    string Currency,
    JournalStatus Status,
    decimal TotalDebit,
    decimal TotalCredit,
    string CreatedBy,
    DateTime CreatedAtUtc,
    string? PostedBy,
    DateTime? PostedAtUtc,
    List<JournalLineDto> Lines
);

public sealed record JournalLineDto(
    Guid Id,
    string AccountCode,
    string AccountName,
    string Description,
    decimal Debit,
    decimal Credit,
    Guid? CostCenterId
);

public sealed record FxRateDto(
    Guid Id,
    Guid TenantId,
    DateOnly EffectiveDate,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    FxSource Source,
    string? SourceReference,
    DateTime UploadedAtUtc
);

public sealed record CostCenterDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    Guid? ParentId,
    bool IsActive
);

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

public sealed record MatchExceptionDto(
    Guid Id,
    Guid InvoiceId,
    Guid InvoiceLineId,
    MatchExceptionType Type,
    decimal InvoiceQty,
    decimal MatchedQty,
    decimal InvoicePrice,
    decimal MatchedPrice,
    string Description,
    MatchExceptionStatus Status,
    string? Resolution,
    string? ResolvedBy,
    DateTime? ResolvedAtUtc,
    DateTime CreatedAtUtc
);

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────

public sealed record GrIrAccrualDto(
    Guid Id,
    Guid GrnId,
    Guid? PoId,
    Guid VendorId,
    string GrnNumber,
    DateOnly ReceivedOn,
    decimal Amount,
    string Currency,
    GrIrAccrualStatus Status,
    Guid? InvoiceId,
    DateOnly? ClearedOn,
    string? ClearedBy,
    string CreatedBy,
    DateTime CreatedAtUtc
);