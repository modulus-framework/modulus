using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Domain.Errors;

/// <summary>Error factory for the Vendors module — one place per module.</summary>
public static class VendorErrors
{
    public static Error NotFound(Guid vendorId) =>
        Error.NotFound("Vendor.NotFound", $"Vendor '{vendorId}' was not found");

    public static readonly Error Duplicate = Error.Conflict(
        "Vendor.Duplicate",
        "A vendor with the same TIN, BIN or name+country already exists (BR-VEN-02)");
}
