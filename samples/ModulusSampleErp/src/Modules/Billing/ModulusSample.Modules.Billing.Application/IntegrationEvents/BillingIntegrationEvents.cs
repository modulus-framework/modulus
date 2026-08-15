namespace ModulusSample.Modules.Billing.Application.IntegrationEvents;

public sealed record InvoicePaidIntegrationEvent(Guid InvoiceId, string InvoiceNumber, Guid CustomerId, decimal Amount, DateTime PaidAtUtc);
public sealed record PaymentReceivedIntegrationEvent(Guid PaymentId, Guid InvoiceId, string InvoiceNumber, Guid CustomerId, decimal Amount, DateTime ReceivedAtUtc);
public sealed record InvoiceOverdueIntegrationEvent(Guid InvoiceId, string InvoiceNumber, Guid CustomerId, DateTime DueDate, DateTime OverdueAtUtc);
public sealed record CustomerCreditUsedIntegrationEvent(Guid CustomerId, decimal AmountUsed, Guid InvoiceId, DateTime UsedAtUtc);
public sealed record CustomerCreditReleasedIntegrationEvent(Guid CustomerId, decimal AmountReleased, Guid InvoiceId, DateTime ReleasedAtUtc);