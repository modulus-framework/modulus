using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Finance.Application.Dtos;
using TradeFlow.Modules.Finance.Domain.Entities;

namespace TradeFlow.Modules.Finance.Application.Queries;

public sealed record GetApInvoiceByIdQuery(Guid InvoiceId) : IQuery<ApInvoiceDto?>;

public sealed record GetApInvoicesByVendorQuery(Guid VendorId) : IQuery<IReadOnlyList<ApInvoiceDto>>;

public sealed record GetApInvoicesByStatusQuery(ApInvoiceStatus Status) : IQuery<IReadOnlyList<ApInvoiceDto>>;

public sealed record GetOverdueInvoicesQuery(DateOnly AsOfDate) : IQuery<IReadOnlyList<ApInvoiceDto>>;

public sealed record GetPaymentProposalByIdQuery(Guid ProposalId) : IQuery<PaymentProposalDto?>;

public sealed record GetFxRateQuery(string FromCurrency, string ToCurrency, DateOnly AsOfDate) : IQuery<FxRateDto?>;

public sealed record GetFxRatesQuery(DateOnly? FromDate, DateOnly? ToDate) : IQuery<IReadOnlyList<FxRateDto>>;

public sealed record GetCostCentersQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<CostCenterDto>>;

public sealed record GetJournalBatchByIdQuery(Guid JournalId) : IQuery<JournalBatchDto?>;

// ── Match Exceptions (BR-FIN-12) ────────────────────────────────────

public sealed record GetMatchExceptionByIdQuery(Guid ExceptionId) : IQuery<MatchExceptionDto?>;

public sealed record GetMatchExceptionsByInvoiceQuery(Guid InvoiceId) : IQuery<IReadOnlyList<MatchExceptionDto>>;

public sealed record GetOpenMatchExceptionsQuery() : IQuery<IReadOnlyList<MatchExceptionDto>>;

// ── GR/IR Accruals (BR-FIN-13) ──────────────────────────────────────

public sealed record GetGrIrAccrualByIdQuery(Guid AccrualId) : IQuery<GrIrAccrualDto?>;

public sealed record GetOpenGrIrAccrualsQuery() : IQuery<IReadOnlyList<GrIrAccrualDto>>;

public sealed record GetGrIrAccrualsByVendorQuery(Guid VendorId) : IQuery<IReadOnlyList<GrIrAccrualDto>>;