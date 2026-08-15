using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Domain.Constants;

public static class Schemas
{
    public const string Sales = "sales";
}

public static class OrderStatuses
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Processing = "processing";
    public const string Shipped = "shipped";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
    public const string Returned = "returned";
    public const string Refunded = "refunded";
}

public static class PaymentTerms
{
    public const string Net30 = "net_30";
    public const string Net60 = "net_60";
    public const string Immediate = "immediate";
    public const string CashOnDelivery = "cash_on_delivery";
}

public static class ShippingMethods
{
    public const string Standard = "standard";
    public const string Express = "express";
    public const string Overnight = "overnight";
    public const string Pickup = "pickup";
    public const string Freight = "freight";
}

public static class OrderErrors
{
    public static readonly Error NotFound = Error.NotFound("Order.NotFound", "Order not found");
    public static readonly Error DuplicateNumber = Error.Conflict("Order.DuplicateNumber", "An order with this number already exists");
    public static readonly Error InvalidStatus = Error.Validation("Order.InvalidStatus", "Invalid order status");
    public static readonly Error CannotDeleteConfirmedOrder = Error.BusinessRule("Order.CannotDeleteConfirmedOrder", "Cannot delete a confirmed order");
    public static readonly Error CannotCancelShippedOrder = Error.BusinessRule("Order.CannotCancelShippedOrder", "Cannot cancel a shipped order");
    public static readonly Error EmptyCustomer = Error.Validation("Order.EmptyCustomer", "Customer cannot be empty");
    public static readonly Error InvalidOrderDate = Error.Validation("Order.InvalidOrderDate", "Order date cannot be in the future");
    public static readonly Error EmptyShippingAddress = Error.Validation("Order.EmptyShippingAddress", "Shipping address cannot be empty");
    public static readonly Error EmptyPaymentMethod = Error.Validation("Order.EmptyPaymentMethod", "Payment method cannot be specified");
    public static readonly Error CannotModifyCompletedOrder = Error.BusinessRule("Order.CannotModifyCompletedOrder", "Cannot modify completed order");
    public static readonly Error InsufficientStock = Error.BusinessRule("Order.InsufficientStock", "Insufficient stock for order items");
    public static readonly Error InvalidTotalAmount = Error.Validation("Order.InvalidTotalAmount", "Total amount cannot be negative");
}

public static class OrderItemErrors
{
    public static readonly Error EmptyProduct = Error.Validation("OrderItem.EmptyProduct", "Product cannot be empty");
    public static readonly Error InvalidQuantity = Error.Validation("OrderItem.InvalidQuantity", "Quantity must be positive");
    public static readonly Error InvalidPrice = Error.Validation("OrderItem.InvalidPrice", "Price must be positive");
    public static readonly Error CannotModifyShippedItem = Error.BusinessRule("OrderItem.CannotModifyShippedItem", "Cannot modify shipped order item");
    public static readonly Error CannotDeleteShippedItem = Error.BusinessRule("OrderItem.CannotDeleteShippedItem", "Cannot delete shipped order item");
}