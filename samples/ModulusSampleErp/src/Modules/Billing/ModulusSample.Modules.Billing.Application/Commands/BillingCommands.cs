using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.Commands;

public sealed record CreateInvoiceCommand(
    string InvoiceNumber,
    Guid SalesOrderId,
    Guid CustomerId,
    string Currency = "USD") : ICommand<Result<Guid>>;

public sealed record AddInvoiceLineCommand(
    Guid InvoiceId,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxRate = 0.1m) : ICommand<Result>;

public sealed record SendInvoiceCommand(
    Guid InvoiceId) : ICommand<Result>;

public sealed record MarkInvoiceAsPaidCommand(
    Guid InvoiceId) : ICommand<Result>;

public sealed record MarkInvoiceAsOverdueCommand(
    Guid InvoiceId) : ICommand<Result>;

public sealed record CreatePaymentCommand(
    string PaymentNumber,
    Guid InvoiceId,
    decimal Amount,
    string PaymentMethod) : ICommand<Result<Guid>>;

public sealed record ConfirmPaymentCommand(
    Guid PaymentId,
    string? ReferenceNumber = null) : ICommand<Result>;

public sealed record CreateCreditNoteCommand(
    string CreditNoteNumber,
    Guid InvoiceId,
    decimal Amount,
    string Reason) : ICommand<Result<Guid>>;

public sealed record IssueCreditNoteCommand(
    Guid CreditNoteId) : ICommand<Result>;

public sealed record ApplyCreditNoteCommand(
    Guid CreditNoteId) : ICommand<Result>;
