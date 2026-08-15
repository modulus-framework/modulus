namespace ModulusSample.Modules.Inventory.Application.Permissions;

public static class InventoryPermissions
{
    public const string Module = "Inventory";

    public static class Stock
    {
        public const string Add = $"{Module}.Stock.Add";
        public const string Remove = $"{Module}.Stock.Remove";
        public const string Transfer = $"{Module}.Stock.Transfer";
        public const string Adjust = $"{Module}.Stock.Adjust";
        public const string View = $"{Module}.Stock.View";
    }

    public static class Reservations
    {
        public const string Create = $"{Module}.Reservations.Create";
        public const string View = $"{Module}.Reservations.View";
        public const string Release = $"{Module}.Reservations.Release";
    }

    public static class Locations
    {
        public const string Create = $"{Module}.Locations.Create";
        public const string View = $"{Module}.Locations.View";
        public const string Edit = $"{Module}.Locations.Edit";
        public const string Delete = $"{Module}.Locations.Delete";
    }

    public static class Reports
    {
        public const string View = $"{Module}.Reports.View";
        public const string Export = $"{Module}.Reports.Export";
    }

    public static class AllPermissions
    {
        public const string AddStock = Stock.Add;
        public const string RemoveStock = Stock.Remove;
        public const string TransferStock = Stock.Transfer;
        public const string AdjustStock = Stock.Adjust;
        public const string ViewStock = Stock.View;
        public const string CreateReservations = Reservations.Create;
        public const string ViewReservations = Reservations.View;
        public const string ReleaseReservations = Reservations.Release;
        public const string CreateLocations = Locations.Create;
        public const string ViewLocations = Locations.View;
        public const string EditLocations = Locations.Edit;
        public const string DeleteLocations = Locations.Delete;
        public const string ViewReports = Reports.View;
        public const string ExportReports = Reports.Export;

        public static readonly string[] Values = new[]
        {
            AddStock, RemoveStock, TransferStock, AdjustStock, ViewStock,
            CreateReservations, ViewReservations, ReleaseReservations,
            CreateLocations, ViewLocations, EditLocations, DeleteLocations,
            ViewReports, ExportReports
        };
    }
}