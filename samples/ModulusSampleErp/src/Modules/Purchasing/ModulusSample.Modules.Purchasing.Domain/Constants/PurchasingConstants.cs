using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Domain.Constants;

public static class Schemas
{
    public const string Purchasing = "purchasing";
}

public static class PurchaseOrderStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Received = "received";
    public const string PartiallyReceived = "partially_received";
    public const string Cancelled = "cancelled";
    public const string Rejected = "rejected";
}

public static class RequisitionStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Processed = "processed";
}

public static class PurchaseOrderErrors
{
    public static readonly Error NotFound = Error.NotFound("PurchaseOrder.NotFound", "Purchase order not found");
    public static readonly Error DuplicateNumber = Error.Conflict("PurchaseOrder.DuplicateNumber", "A purchase order with this number already exists");
    public static readonly Error InvalidStatus = Error.Validation("PurchaseOrder.InvalidStatus", "Invalid purchase order status");
    public static readonly Error CannotDeleteApprovedOrder = Error.BusinessRule("PurchaseOrder.CannotDeleteApprovedOrder", "Cannot delete an approved purchase order");
    public static readonly Error EmptySupplier = Error.Validation("PurchaseOrder.EmptySupplier", "Supplier cannot be empty");
    public static readonly Error InvalidOrderDate = Error.Validation("PurchaseOrder.InvalidOrderDate", "Order date cannot be in the future");
    public static readonly Error CannotCancelReceivedOrder = Error.BusinessRule("PurchaseOrder.CannotCancelReceivedOrder", "Cannot cancel a received purchase order");
    public static readonly Error InvalidTotalAmount = Error.Validation("PurchaseOrder.InvalidTotalAmount", "Total amount cannot be negative");
    public static readonly Error CannotModifyProcessedOrder = Error.BusinessRule("PurchaseOrder.CannotModifyProcessedOrder", "Cannot modify processed purchase order");
}

public static class RequisitionErrors
{
    public static readonly Error NotFound = Error.NotFound("Requisition.NotFound", "Requisition not found");
    public static readonly Error InvalidStatus = Error.Validation("Requisition.InvalidStatus", "Invalid requisition status");
    public static readonly Error CannotDeleteProcessedRequisition = Error.BusinessRule("Requisition.CannotDeleteProcessedRequisition", "Cannot delete a processed requisition");
    public static readonly Error EmptyRequester = Error.Validation("Requisition.EmptyRequester", "Requester cannot be empty");
    public static readonly Error EmptyDepartment = Error.Validation("Requisition.EmptyDepartment", "Department cannot be empty");
}