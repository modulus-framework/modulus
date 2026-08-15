using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Domain.Constants;

public static class Schemas
{
    public const string Catalog = "catalog";
}

public static class ProductStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Discontinued = "discontinued";
    public const string OutOfStock = "out_of_stock";
    public const string PreOrder = "pre_order";
}

public static class ProductErrors
{
    public static readonly Error NotFound = Error.NotFound("Product.NotFound", "Product not found");
    public static readonly Error DuplicateSku = Error.Conflict("Product.DuplicateSku", "A product with this SKU already exists");
    public static readonly Error DuplicateCode = Error.Conflict("Product.DuplicateCode", "A product with this code already exists");
    public static readonly Error InvalidStatus = Error.Validation("Product.InvalidStatus", "Invalid product status");
    public static readonly Error EmptyName = Error.Validation("Product.EmptyName", "Product name cannot be empty");
    public static readonly Error NameTooLong = Error.Validation("Product.NameTooLong", "Product name cannot exceed 200 characters");
    public static readonly Error InvalidPrice = Error.Validation("Product.InvalidPrice", "Price must be positive");
    public static readonly Error NegativeStock = Error.Validation("Product.NegativeStock", "Stock quantity cannot be negative");
    public static readonly Error CannotDeleteProductWithSales = Error.BusinessRule("Product.CannotDeleteProductWithSales", "Cannot delete product with sales history");
    public static readonly Error CannotDiscontinueActiveProduct = Error.BusinessRule("Product.CannotDiscontinueActiveProduct", "Cannot discontinue product without deactivating first");
    public static readonly Error EmptyCategory = Error.Validation("Product.EmptyCategory", "Product category cannot be empty");
}

public static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound("Category.NotFound", "Category not found");
    public static readonly Error DuplicateName = Error.Conflict("Category.DuplicateName", "A category with this name already exists");
    public static readonly Error EmptyName = Error.Validation("Category.EmptyName", "Category name cannot be empty");
    public static readonly Error HasProducts = Error.BusinessRule("Category.HasProducts", "Category has associated products");
}