namespace ModulusSample.Modules.Billing.Application.Permissions;

public static class BillingPermissions
{
    public const string Module = "Billing";

    public static class Invoices
    {
        public const string Create = $"{Module}.Invoices.Create";
        public const string View = $"{Module}.Invoices.View";
        public const string Edit = $"{Module}.Invoices.Edit";
        public const string Delete = $"{Module}.Invoices.Delete";
        public const string Issue = $"{Module}.Invoices.Issue";
        public const string Send = $"{Module}.Invoices.Send";
        public const string Cancel = $"{Module}.Invoices.Cancel";
    }

    public static class Payments
    {
        public const string Create = $"{Module}.Payments.Create";
        public const string View = $"{Module}.Payments.View";
        public const string Process = $"{Module}.Payments.Process";
        public const string Refund = $"{Module}.Payments.Refund";
    }

    public static class CreditNotes
    {
        public const string Create = $"{Module}.CreditNotes.Create";
        public const string View = $"{Module}.CreditNotes.View";
        public const string Issue = $"{Module}.CreditNotes.Issue";
    }

    public static class Reports
    {
        public const string View = $"{Module}.Reports.View";
        public const string Export = $"{Module}.Reports.Export";
    }

    public static class AllPermissions
    {
        public const string CreateInvoices = Invoices.Create;
        public const string ViewInvoices = Invoices.View;
        public const string EditInvoices = Invoices.Edit;
        public const string DeleteInvoices = Invoices.Delete;
        public const string IssueInvoices = Invoices.Issue;
        public const string SendInvoices = Invoices.Send;
        public const string CancelInvoices = Invoices.Cancel;
        public const string CreatePayments = Payments.Create;
        public const string ViewPayments = Payments.View;
        public const string ProcessPayments = Payments.Process;
        public const string RefundPayments = Payments.Refund;
        public const string CreateCreditNotes = CreditNotes.Create;
        public const string ViewCreditNotes = CreditNotes.View;
        public const string IssueCreditNotes = CreditNotes.Issue;
        public const string ViewReports = Reports.View;
        public const string ExportReports = Reports.Export;

        public static readonly string[] Values = new[]
        {
            CreateInvoices, ViewInvoices, EditInvoices, DeleteInvoices, IssueInvoices, SendInvoices, CancelInvoices,
            CreatePayments, ViewPayments, ProcessPayments, RefundPayments,
            CreateCreditNotes, ViewCreditNotes, IssueCreditNotes,
            ViewReports, ExportReports
        };
    }
}