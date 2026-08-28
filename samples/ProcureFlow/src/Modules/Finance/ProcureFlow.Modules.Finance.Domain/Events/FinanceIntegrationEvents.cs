using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Finance.Domain.Events;

public sealed record ApInvoiceSubmittedDomainEvent(
    Guid EventId,
    Guid InvoiceId,
    Guid TenantId,
    string InvoiceNumber,
    Guid VendorId,
    decimal TotalAmount,
    DateTime OccurredAt
) : IDomainEvent;

[IntegrationEventName("Finance.ApInvoiceApproved.v1")]
public sealed record ApInvoiceApprovedDomainEvent(
    Guid EventId,
    Guid InvoiceId,
    Guid TenantId,
    string InvoiceNumber,
    Guid VendorId,
    decimal TotalAmount,
    DateOnly DueDate,
    DateTime OccurredAt
) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Finance.ApInvoiceApproved.v1";
}

public sealed record ApInvoiceCancelledDomainEvent(
    Guid EventId,
    Guid InvoiceId,
    Guid TenantId,
    string InvoiceNumber,
    Guid VendorId,
    string Reason,
    DateTime OccurredAt
) : IDomainEvent;

public sealed record ApInvoicePaidDomainEvent(
    Guid EventId,
    Guid InvoiceId,
    Guid TenantId,
    string InvoiceNumber,
    Guid VendorId,
    decimal TotalAmount,
    DateTime OccurredAt
) : IDomainEvent;

public sealed record PaymentProposalCreatedDomainEvent(
    Guid EventId,
    Guid ProposalId,
    Guid TenantId,
    DateOnly PaymentDate,
    int InvoiceCount,
    decimal TotalAmount,
    DateTime OccurredAt
) : IDomainEvent;

[IntegrationEventName("Finance.PaymentSettled.v1")]
public sealed record PaymentSettledDomainEvent(
    Guid EventId,
    Guid PaymentId,
    Guid TenantId,
    Guid VendorId,
    decimal Amount,
    string ReferenceNumber,
    DateTime OccurredAt
) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Finance.PaymentSettled.v1";
}

[IntegrationEventName("Finance.JournalPosted.v1")]
public sealed record JournalPostedDomainEvent(
    Guid EventId,
    Guid JournalId,
    Guid TenantId,
    string JournalNumber,
    string Description,
    decimal TotalDebit,
    decimal TotalCredit,
    DateTime OccurredAt
) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Finance.JournalPosted.v1";
}