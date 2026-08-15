namespace ModulusSample.Modules.Purchasing.Application.Permissions;

public static class PurchasingPermissions
{
    public const string Module = "Purchasing";

    public static class PurchaseOrders
    {
        public const string Create = $"{Module}.PurchaseOrders.Create";
        public const string View = $"{Module}.PurchaseOrders.View";
        public const string Edit = $"{Module}.PurchaseOrders.Edit";
        public const string Delete = $"{Module}.PurchaseOrders.Delete";
        public const string Submit = $"{Module}.PurchaseOrders.Submit";
        public const string Approve = $"{Module}.PurchaseOrders.Approve";
        public const string Reject = $"{Module}.PurchaseOrders.Reject";
        public const string Receive = $"{Module}.PurchaseOrders.Receive";
    }

    public static class Requisitions
    {
        public const string Create = $"{Module}.Requisitions.Create";
        public const string View = $"{Module}.Requisitions.View";
        public const string Edit = $"{Module}.Requisitions.Edit";
        public const string Delete = $"{Module}.Requisitions.Delete";
        public const string Submit = $"{Module}.Requisitions.Submit";
        public const string Process = $"{Module}.Requisitions.Process";
    }

    public static class Suppliers
    {
        public const string Create = $"{Module}.Suppliers.Create";
        public const string View = $"{Module}.Suppliers.View";
        public const string Edit = $"{Module}.Suppliers.Edit";
        public const string Delete = $"{Module}.Suppliers.Delete";
    }

    public static class Reports
    {
        public const string View = $"{Module}.Reports.View";
        public const string Export = $"{Module}.Reports.Export";
    }

    public static class AllPermissions
    {
        public const string CreatePurchaseOrders = PurchaseOrders.Create;
        public const string ViewPurchaseOrders = PurchaseOrders.View;
        public const string EditPurchaseOrders = PurchaseOrders.Edit;
        public const string DeletePurchaseOrders = PurchaseOrders.Delete;
        public const string SubmitPurchaseOrders = PurchaseOrders.Submit;
        public const string ApprovePurchaseOrders = PurchaseOrders.Approve;
        public const string RejectPurchaseOrders = PurchaseOrders.Reject;
        public const string ReceivePurchaseOrders = PurchaseOrders.Receive;
        public const string CreateRequisitions = Requisitions.Create;
        public const string ViewRequisitions = Requisitions.View;
        public const string EditRequisitions = Requisitions.Edit;
        public const string DeleteRequisitions = Requisitions.Delete;
        public const string SubmitRequisitions = Requisitions.Submit;
        public const string ProcessRequisitions = Requisitions.Process;
        public const string CreateSuppliers = Suppliers.Create;
        public const string ViewSuppliers = Suppliers.View;
        public const string EditSuppliers = Suppliers.Edit;
        public const string DeleteSuppliers = Suppliers.Delete;
        public const string ViewReports = Reports.View;
        public const string ExportReports = Reports.Export;

        public static readonly string[] Values = new[]
        {
            CreatePurchaseOrders, ViewPurchaseOrders, EditPurchaseOrders, DeletePurchaseOrders, SubmitPurchaseOrders, ApprovePurchaseOrders, RejectPurchaseOrders, ReceivePurchaseOrders,
            CreateRequisitions, ViewRequisitions, EditRequisitions, DeleteRequisitions, SubmitRequisitions, ProcessRequisitions,
            CreateSuppliers, ViewSuppliers, EditSuppliers, DeleteSuppliers,
            ViewReports, ExportReports
        };
    }
}