namespace ModulusSample.Modules.Sales.Application.Permissions;

public static class SalesPermissions
{
    public const string Module = "Sales";

    public static class Orders
    {
        public const string Create = $"{Module}.Orders.Create";
        public const string View = $"{Module}.Orders.View";
        public const string Edit = $"{Module}.Orders.Edit";
        public const string Delete = $"{Module}.Orders.Delete";
        public const string Confirm = $"{Module}.Orders.Confirm";
        public const string Ship = $"{Module}.Orders.Ship";
        public const string Cancel = $"{Module}.Orders.Cancel";
        public const string Refund = $"{Module}.Orders.Refund";
    }

    public static class Returns
    {
        public const string Create = $"{Module}.Returns.Create";
        public const string View = $"{Module}.Returns.View";
        public const string Process = $"{Module}.Returns.Process";
        public const string Approve = $"{Module}.Returns.Approve";
        public const string Reject = $"{Module}.Returns.Reject";
    }

    public static class Quotes
    {
        public const string Create = $"{Module}.Quotes.Create";
        public const string View = $"{Module}.Quotes.View";
        public const string Convert = $"{Module}.Quotes.Convert";
    }

    public static class Reports
    {
        public const string View = $"{Module}.Reports.View";
        public const string Export = $"{Module}.Reports.Export";
    }

    public static class AllPermissions
    {
        public const string CreateOrders = Orders.Create;
        public const string ViewOrders = Orders.View;
        public const string EditOrders = Orders.Edit;
        public const string DeleteOrders = Orders.Delete;
        public const string ConfirmOrders = Orders.Confirm;
        public const string ShipOrders = Orders.Ship;
        public const string CancelOrders = Orders.Cancel;
        public const string RefundOrders = Orders.Refund;
        public const string CreateReturns = Returns.Create;
        public const string ViewReturns = Returns.View;
        public const string ProcessReturns = Returns.Process;
        public const string ApproveReturns = Returns.Approve;
        public const string RejectReturns = Returns.Reject;
        public const string CreateQuotes = Quotes.Create;
        public const string ViewQuotes = Quotes.View;
        public const string ConvertQuotes = Quotes.Convert;
        public const string ViewReports = Reports.View;
        public const string ExportReports = Reports.Export;

        public static readonly string[] Values = new[]
        {
            CreateOrders, ViewOrders, EditOrders, DeleteOrders, ConfirmOrders, ShipOrders, CancelOrders, RefundOrders,
            CreateReturns, ViewReturns, ProcessReturns, ApproveReturns, RejectReturns,
            CreateQuotes, ViewQuotes, ConvertQuotes,
            ViewReports, ExportReports
        };
    }
}