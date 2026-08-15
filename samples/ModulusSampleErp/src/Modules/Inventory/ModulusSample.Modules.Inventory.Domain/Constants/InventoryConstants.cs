using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Domain.Constants;

public static class Schemas
{
    public const string Inventory = "inventory";
}

public static class MovementTypes
{
    public const string StockIn = "stock_in";
    public const string StockOut = "stock_out";
    public const string Transfer = "transfer";
    public const string Adjustment = "adjustment";
    public const string Return = "return";
    public const string Sale = "sale";
}

public static class LocationTypes
{
    public const string Warehouse = "warehouse";
    public const string Store = "store";
    public const string Stockroom = "stockroom";
    public const string External = "external";
}

public static class StockErrors
{
    public static readonly Error NotFound = Error.NotFound("Stock.NotFound", "Stock not found");
    public static readonly Error InsufficientStock = Error.BusinessRule("Stock.InsufficientStock", "Insufficient stock available");
    public static readonly Error InvalidQuantity = Error.Validation("Stock.InvalidQuantity", "Quantity must be positive");
    public static readonly Error InvalidLocation = Error.Validation("Stock.InvalidLocation", "Invalid location");
    public static readonly Error NegativeStock = Error.Validation("Stock.NegativeStock", "Stock cannot be negative");
    public static readonly Error ProductNotSpecified = Error.Validation("Stock.ProductNotSpecified", "Product must be specified");
    public static readonly Error CannotReserveAlreadyReserved = Error.BusinessRule("Stock.CannotReserveAlreadyReserved", "Stock is already reserved");
    public static readonly Error CannotReleaseUnreservedStock = Error.BusinessRule("Stock.CannotReleaseUnreservedStock", "Cannot release unreserved stock");
}

public static class MovementErrors
{
    public static readonly Error NotFound = Error.NotFound("Movement.NotFound", "Movement not found");
    public static readonly Error InvalidType = Error.Validation("Movement.InvalidType", "Invalid movement type");
    public static readonly Error CannotModifyCompletedMovement = Error.BusinessRule("Movement.CannotModifyCompletedMovement", "Cannot modify completed movement");
    public static readonly Error InvalidQuantity = Error.Validation("Movement.InvalidQuantity", "Quantity must be positive");
    public static readonly Error InvalidSourceLocation = Error.Validation("Movement.InvalidSourceLocation", "Invalid source location");
    public static readonly Error InvalidDestinationLocation = Error.Validation("Movement.InvalidDestinationLocation", "Invalid destination location");
    public static readonly Error SameSourceAndDestination = Error.BusinessRule("Movement.SameSourceAndDestination", "Source and destination cannot be the same");
}