using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Domain.Constants;

public static class Schemas
{
    public const string Billing = "billing";
}

public static class InvoiceStatuses
{
    public const string Draft = "draft";
    public const string Issued = "issued";
    public const string Sent = "sent";
    public const string Paid = "paid";
    public const string Overdue = "overdue";
    public const string Cancelled = "cancelled";
    public const string Void = "void";
}

public static class PaymentMethods
{
    public const string CreditCard = "credit_card";
    public const string BankTransfer = "bank_transfer";
    public const string Cash = "cash";
    public const string Check = "check";
    public const string OnlinePayment = "online_payment";
}

public static class CreditNoteReasons
{
    public const string Return = "return";
    public const string Discount = "discount";
    public const string PriceAdjustment = "price_adjustment";
    public const string DamagedGoods = "damaged_goods";
    public const string Other = "other";
}

public static class InvoiceErrors
{
    public static readonly Error NotFound = Error.NotFound("Invoice.NotFound", "Invoice not found");
    public static readonly Error DuplicateNumber = Error.Conflict("Invoice.DuplicateNumber", "An invoice with this number already exists");
    public static readonly Error InvalidStatus = Error.Validation("Invoice.InvalidStatus", "Invalid invoice status");
    public static readonly Error CannotDeletePaidInvoice = Error.BusinessRule("Invoice.CannotDeletePaidInvoice", "Cannot delete a paid invoice");
    public static readonly Error CannotCancelPaidInvoice = Error.BusinessRule("Invoice.CannotCancelPaidInvoice", "Cannot cancel a paid invoice");
    public static readonly Error NegativeAmount = Error.Validation("Invoice.NegativeAmount", "Invoice amount cannot be negative");
    public static readonly Error EmptyCustomer = Error.Validation("Invoice.EmptyCustomer", "Customer cannot be empty");
    public static readonly Error InvalidDueDate = Error.Validation("Invoice.InvalidDueDate", "Due date must be in the future");
    public static readonly Error AlreadyPaid = Error.BusinessRule("Invoice.AlreadyPaid", "Invoice is already paid");
    public static readonly Error PaymentExceedsAmount = Error.BusinessRule("Invoice.PaymentExceedsAmount", "Payment amount exceeds invoice balance");
}

public static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound("Payment.NotFound", "Payment not found");
    public static readonly Error InvalidAmount = Error.Validation("Payment.InvalidAmount", "Payment amount must be positive");
    public static readonly Error CannotRefundCancelledPayment = Error.BusinessRule("Payment.CannotRefundCancelledPayment", "Cannot refund a cancelled payment");
    public static readonly Error InvoiceNotSpecified = Error.Validation("Payment.InvoiceNotSpecified", "Invoice must be specified");
    public static readonly Error AlreadyRefunded = Error.BusinessRule("Payment.AlreadyRefunded", "Payment is already refunded");
    public static readonly Error CannotProcessInvoicePaidInFull = Error.BusinessRule("Payment.CannotProcessInvoicePaidInFull", "Cannot process payment for an invoice paid in full");
}

public static class CreditNoteErrors
{
    public static readonly Error NotFound = Error.NotFound("CreditNote.NotFound", "Credit note not found");
    public static readonly Error InvalidAmount = Error.Validation("CreditNote.InvalidAmount", "Credit note amount must be positive");
    public static readonly Error InvoiceNotSpecified = Error.Validation("CreditNote.InvoiceNotSpecified", "Invoice must be specified");
    public static readonly Error CannotExceedInvoiceAmount = Error.BusinessRule("CreditNote.CannotExceedInvoiceAmount", "Credit note amount cannot exceed invoice total");
    public static readonly Error AlreadyApplied = Error.BusinessRule("CreditNote.AlreadyApplied", "Credit note is already applied");
    public static readonly Error InvalidReason = Error.Validation("CreditNote.InvalidReason", "Invalid credit note reason");
}