namespace ModulusSample.Modules.Billing.Domain.Events;

public sealed record InvoiceCreatedDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, DateTime CreatedAtUtc);
public sealed record InvoiceIssuedDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, decimal Amount, DateTime IssuedAtUtc);
public sealed record InvoiceSentDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, DateTime SentAtUtc);
public sealed record InvoicePaidDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, decimal Amount, DateTime PaidAtUtc);
public sealed record InvoiceOverdueDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, DateTime DueDate, DateTime OverdueAtUtc);
public sealed record InvoiceCancelledDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, string Reason, DateTime CancelledAtUtc);
public sealed record InvoiceDeletedDomainEvent(Guid EventId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, DateTime DeletedAtUtc);
public sealed record PaymentReceivedDomainEvent(Guid EventId, Guid PaymentId, Guid InvoiceId, string InvoiceNumber, decimal Amount, DateTime ReceivedAtUtc);
public sealed record PaymentRefundedDomainEvent(Guid EventId, Guid PaymentId, Guid InvoiceId, string InvoiceNumber, decimal Amount, string Reason, DateTime RefundedAtUtc);
public sealed record CreditNoteIssuedDomainEvent(Guid EventId, Guid CreditNoteId, Guid InvoiceId, string InvoiceNumber, decimal Amount, string Reason, DateTime IssuedAtUtc);
public sealed record CreditNoteAppliedDomainEvent(Guid EventId, Guid CreditNoteId, Guid InvoiceId, string InvoiceNumber, decimal Amount, DateTime AppliedAtUtc);