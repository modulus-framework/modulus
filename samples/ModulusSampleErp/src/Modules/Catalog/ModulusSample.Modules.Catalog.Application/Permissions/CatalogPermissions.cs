namespace ModulusSample.Modules.Catalog.Application.Permissions;

public static class CatalogPermissions
{
    public const string Module = "Catalog";

    public static class Products
    {
        public const string Create = $"{Module}.Products.Create";
        public const string View = $"{Module}.Products.View";
        public const string Edit = $"{Module}.Products.Edit";
        public const string Delete = $"{Module}.Products.Delete";
        public const string ChangePrice = $"{Module}.Products.ChangePrice";
        public const string ManageStock = $"{Module}.Products.ManageStock";
        public const string Activate = $"{Module}.Products.Activate";
        public const string Deactivate = $"{Module}.Products.Deactivate";
    }

    public static class Categories
    {
        public const string Create = $"{Module}.Categories.Create";
        public const string View = $"{Module}.Categories.View";
        public const string Edit = $"{Module}.Categories.Edit";
        public const string Delete = $"{Module}.Categories.Delete";
    }

    public static class Attributes
    {
        public const string Create = $"{Module}.Attributes.Create";
        public const string View = $"{Module}.Attributes.View";
        public const string Edit = $"{Module}.Attributes.Edit";
        public const string Delete = $"{Module}.Attributes.Delete";
    }

    public static class Reports
    {
        public const string View = $"{Module}.Reports.View";
        public const string Export = $"{Module}.Reports.Export";
    }

    public static class AllPermissions
    {
        public const string CreateProducts = Products.Create;
        public const string ViewProducts = Products.View;
        public const string EditProducts = Products.Edit;
        public const string DeleteProducts = Products.Delete;
        public const string ChangeProductPrice = Products.ChangePrice;
        public const string ManageProductStock = Products.ManageStock;
        public const string ActivateProduct = Products.Activate;
        public const string DeactivateProduct = Products.Deactivate;
        public const string CreateCategories = Categories.Create;
        public const string ViewCategories = Categories.View;
        public const string EditCategories = Categories.Edit;
        public const string DeleteCategories = Categories.Delete;
        public const string CreateAttributes = Attributes.Create;
        public const string ViewAttributes = Attributes.View;
        public const string EditAttributes = Attributes.Edit;
        public const string DeleteAttributes = Attributes.Delete;
        public const string ViewReports = Reports.View;
        public const string ExportReports = Reports.Export;

        public static readonly string[] Values = new[]
        {
            CreateProducts, ViewProducts, EditProducts, DeleteProducts, ChangeProductPrice, ManageProductStock, ActivateProduct, DeactivateProduct,
            CreateCategories, ViewCategories, EditCategories, DeleteCategories,
            CreateAttributes, ViewAttributes, EditAttributes, DeleteAttributes,
            ViewReports, ExportReports
        };
    }
}